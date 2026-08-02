using System;
using System.Collections.Generic;

namespace MetroDiagram.Engine
{
    // Pull the annealed in-game layout onto the eight compass headings, exactly.
    //
    // Port of MetroSvgRenderer.OctilinearSnap.cs. The annealer optimises for
    // octilinearity as one term among several, so it lands near the grid rather
    // than on it: measured over four real cities, only 62-86% of drawn in-game
    // segments were within a degree of a 45-degree multiple, against 97-100% for
    // the desktop layout that runs this pass. A reader does not see "73%
    // aligned", they see a set of arbitrary angles.
    //
    // Once an edge has an assigned heading, "this edge lies exactly on that
    // heading" is a LINEAR condition on the two station positions -- the
    // component of (pj - pi) along the heading's normal is zero. So the map
    // becomes
    //
    //     minimise    sum |p - p0|^2            stations move as little as possible
    //     subject to  (pj - pi) . n_e = 0       every assigned edge is exact
    //
    // which is convex, has one global optimum, and is automatically consistent
    // for a station shared by several lines. Solved as weighted least squares:
    // the constraints are penalties with a large weight rather than hard
    // equalities, which keeps the system positive definite and degrades
    // gracefully when a station's runs disagree.
    //
    // Headings come from RUNS, not single edges. Snapping each edge separately
    // does not remove its angular error, it folds the error into a corner: a
    // 200px edge 5 degrees off horizontal gains a 17px bulge, wider than the
    // stroke.
    //
    // Runs before FitPointsToFrame, which is a uniform scale plus a translation
    // and therefore preserves angles exactly -- the snap survives the fit.
    public sealed partial class PortableMetroSvgRenderer
    {
        /// <summary>Direction agreement, in degrees, for two edges to count as one run.</summary>
        private const double OctilinearRunToleranceDegrees = 22.0;

        /// <summary>
        /// Weight on the heading conditions relative to the unit weight on "stay
        /// put". This ratio is the design, not a knob: the heading rows are
        /// relative, so sliding a whole run sideways satisfies them exactly and
        /// costs only the anchor rows. Large enough, and the cheapest answer to a
        /// separation request is to translate a run rather than bend an edge off
        /// its heading. On the desktop corpus a ratio of 40 broke octilinearity
        /// outright while crowding improved monotonically up to 12000.
        /// </summary>
        private const double OctilinearConstraintWeight = 12000;

        private const double OctilinearSeparationWeight = 40;

        /// <summary>Minimum station spacing to restore, as a share of the median edge.</summary>
        private const double OctilinearSeparationFraction = 0.45;

        private const int OctilinearSeparationRounds = 6;

        private const double OctilinearEpsilon = 1e-9;

        private static readonly double[] OctilinearAxisX = { 1, 0.70710678118654752, 0, -0.70710678118654752 };
        private static readonly double[] OctilinearAxisY = { 0, 0.70710678118654752, 1, 0.70710678118654752 };

        private struct HeadingConstraint
        {
            public HeadingConstraint(int a, int b, int axis)
            {
                A = a;
                B = b;
                Axis = axis;
            }

            public int A { get; }

            public int B { get; }

            public int Axis { get; }
        }

        private struct SeparationRequest
        {
            public SeparationRequest(int a, int b, double x, double y, double wanted)
            {
                A = a;
                B = b;
                X = x;
                Y = y;
                Wanted = wanted;
            }

            public int A { get; }

            public int B { get; }

            public double X { get; }

            public double Y { get; }

            public double Wanted { get; }
        }

        /// <summary>
        /// Places every run exactly on a compass heading, writing back into
        /// <see cref="ArrayLayout.Positions"/>. Leaves the layout untouched when
        /// there is nothing to pin.
        /// </summary>
        private static void ApplyOctilinearSnap(ArrayLayout layout, PortableRenderOptions options)
        {
            if (!options.SnapToOctilinearGrid || layout.Positions.Length < 2)
            {
                return;
            }

            Point2[] original = (Point2[])layout.Positions.Clone();
            List<HeadingConstraint> constraints = BuildRunConstraints(layout, original);
            if (constraints.Count == 0)
            {
                return;
            }

            List<SeparationRequest> none = new List<SeparationRequest>();
            Point2[] solved = SolveOctilinear(original, constraints, none);

            double minimumSpacing = ResolveOctilinearSpacing(layout, original);
            for (int round = 0; round < OctilinearSeparationRounds && minimumSpacing > 0; round++)
            {
                List<SeparationRequest> separations = FindCrowdedPairs(solved, minimumSpacing);
                if (separations.Count == 0)
                {
                    break;
                }

                solved = SolveOctilinear(original, constraints, separations);
            }

            for (int i = 0; i < solved.Length; i++)
            {
                layout.Positions[i] = solved[i];
            }
        }

        /// <summary>
        /// One heading per edge, taken from the run it belongs to. Consecutive
        /// edges stay in the same run while they agree with its heading to within
        /// <see cref="OctilinearRunToleranceDegrees"/>; the first that does not
        /// starts a new run, and that junction is a direction change the map keeps.
        /// </summary>
        private static List<HeadingConstraint> BuildRunConstraints(ArrayLayout layout, Point2[] positions)
        {
            Dictionary<long, int> assigned = new Dictionary<long, int>();
            List<long> order = new List<long>();
            foreach (int[] chain in layout.RouteChains)
            {
                int current = -1;
                for (int i = 1; i < chain.Length; i++)
                {
                    int a = chain[i - 1];
                    int b = chain[i];
                    double dx = positions[b].X - positions[a].X;
                    double dy = positions[b].Y - positions[a].Y;
                    if ((dx * dx) + (dy * dy) < OctilinearEpsilon)
                    {
                        continue;
                    }

                    double degrees = ((((Math.Atan2(dy, dx) * 180 / Math.PI) % 180) + 180) % 180);
                    if (current < 0 || AxisErrorDegrees(degrees, current) > OctilinearRunToleranceDegrees)
                    {
                        current = NearestAxis(degrees);
                    }

                    long key = EdgePairKey(a, b);
                    // First run to claim an edge wins, so a shared trunk keeps one
                    // heading rather than flipping with whichever line is read last.
                    if (!assigned.ContainsKey(key))
                    {
                        assigned.Add(key, current);
                        order.Add(key);
                    }
                }
            }

            // Deterministic order: the solver accumulates floating-point sums, so
            // the row order has to be fixed by the data, not by hash iteration.
            order.Sort();
            List<HeadingConstraint> constraints = new List<HeadingConstraint>(order.Count);
            foreach (long key in order)
            {
                int a = (int)(key >> 32);
                int b = (int)(key & 0xFFFFFFFFL);
                constraints.Add(new HeadingConstraint(a, b, assigned[key]));
            }

            return constraints;
        }

        // ArrayLayout.PairKey is private to that nested type, so the same
        // encoding is repeated here: low index in the high word, high index in
        // the low word, which also makes a plain sort of the keys deterministic.
        private static long EdgePairKey(int a, int b)
        {
            return a <= b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        }

        private static double AxisErrorDegrees(double degrees, int axis)
        {
            double difference = Math.Abs(degrees - (axis * 45)) % 180;
            return Math.Min(difference, 180 - difference);
        }

        private static int NearestAxis(double degrees)
        {
            int best = 0;
            double bestError = double.MaxValue;
            for (int axis = 0; axis < OctilinearAxisX.Length; axis++)
            {
                double error = AxisErrorDegrees(degrees, axis);
                if (error < bestError)
                {
                    bestError = error;
                    best = axis;
                }
            }

            return best;
        }

        private static double ResolveOctilinearSpacing(ArrayLayout layout, Point2[] positions)
        {
            List<double> lengths = new List<double>(layout.EdgeA.Length);
            for (int i = 0; i < layout.EdgeA.Length; i++)
            {
                double length = Math.Sqrt(DistanceSquared(positions[layout.EdgeA[i]], positions[layout.EdgeB[i]]));
                if (length > OctilinearEpsilon)
                {
                    lengths.Add(length);
                }
            }

            if (lengths.Count == 0)
            {
                return 0;
            }

            lengths.Sort();
            return lengths[lengths.Count / 2] * OctilinearSeparationFraction;
        }

        private static List<SeparationRequest> FindCrowdedPairs(Point2[] positions, double minimumSpacing)
        {
            List<SeparationRequest> pairs = new List<SeparationRequest>();
            for (int i = 0; i < positions.Length; i++)
            {
                for (int j = i + 1; j < positions.Length; j++)
                {
                    double dx = positions[i].X - positions[j].X;
                    double dy = positions[i].Y - positions[j].Y;
                    double gap = Math.Sqrt((dx * dx) + (dy * dy));
                    if (gap >= minimumSpacing || gap < 1e-6)
                    {
                        continue;
                    }

                    pairs.Add(new SeparationRequest(i, j, dx / gap, dy / gap, minimumSpacing));
                }
            }

            return pairs;
        }

        /// <summary>
        /// Weighted least squares over the station coordinates, through the normal
        /// equations. Every row is sparse (two or four entries), so the normal
        /// matrix is accumulated directly rather than forming the design matrix.
        /// </summary>
        private static Point2[] SolveOctilinear(
            Point2[] original,
            List<HeadingConstraint> constraints,
            List<SeparationRequest> separations)
        {
            int count = original.Length;
            int size = count * 2;
            double[,] normal = new double[size, size];
            double[] rhs = new double[size];

            int[] columns = new int[4];
            double[] values = new double[4];

            // stay put
            for (int i = 0; i < count; i++)
            {
                columns[0] = 2 * i;
                values[0] = 1;
                AddNormalRow(normal, rhs, columns, values, 1, original[i].X);
                columns[0] = (2 * i) + 1;
                values[0] = 1;
                AddNormalRow(normal, rhs, columns, values, 1, original[i].Y);
            }

            // exactly on the assigned heading
            for (int i = 0; i < constraints.Count; i++)
            {
                HeadingConstraint constraint = constraints[i];
                double hx = OctilinearAxisX[constraint.Axis];
                double hy = OctilinearAxisY[constraint.Axis];
                double nx = -hy;
                double ny = hx;
                columns[0] = 2 * constraint.B;
                values[0] = nx * OctilinearConstraintWeight;
                columns[1] = (2 * constraint.B) + 1;
                values[1] = ny * OctilinearConstraintWeight;
                columns[2] = 2 * constraint.A;
                values[2] = -nx * OctilinearConstraintWeight;
                columns[3] = (2 * constraint.A) + 1;
                values[3] = -ny * OctilinearConstraintWeight;
                AddNormalRow(normal, rhs, columns, values, 4, 0);
            }

            for (int i = 0; i < separations.Count; i++)
            {
                SeparationRequest separation = separations[i];
                columns[0] = 2 * separation.A;
                values[0] = separation.X * OctilinearSeparationWeight;
                columns[1] = (2 * separation.A) + 1;
                values[1] = separation.Y * OctilinearSeparationWeight;
                columns[2] = 2 * separation.B;
                values[2] = -separation.X * OctilinearSeparationWeight;
                columns[3] = (2 * separation.B) + 1;
                values[3] = -separation.Y * OctilinearSeparationWeight;
                AddNormalRow(normal, rhs, columns, values, 4, separation.Wanted * OctilinearSeparationWeight);
            }

            double[] solution = SolveSymmetricPositiveDefinite(normal, rhs);
            Point2[] result = new Point2[count];
            for (int i = 0; i < count; i++)
            {
                double x = solution[2 * i];
                double y = solution[(2 * i) + 1];
                result[i] = IsFiniteValue(x) && IsFiniteValue(y) ? new Point2(x, y) : original[i];
            }

            return result;
        }

        private static void AddNormalRow(
            double[,] normal,
            double[] rhs,
            int[] columns,
            double[] values,
            int used,
            double target)
        {
            for (int a = 0; a < used; a++)
            {
                rhs[columns[a]] += values[a] * target;
                for (int b = 0; b < used; b++)
                {
                    normal[columns[a], columns[b]] += values[a] * values[b];
                }
            }
        }

        // netstandard2.0 has no double.IsFinite.
        private static bool IsFiniteValue(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        /// <summary>
        /// Cholesky solve, in place. The unit-weight "stay put" rows put a 1 on
        /// every diagonal, so the matrix is positive definite by construction; the
        /// ridge is only there so a degenerate network cannot produce a NaN layout.
        /// </summary>
        private static double[] SolveSymmetricPositiveDefinite(double[,] matrix, double[] rhs)
        {
            int size = rhs.Length;
            for (int i = 0; i < size; i++)
            {
                matrix[i, i] += 1e-9;
            }

            for (int i = 0; i < size; i++)
            {
                for (int j = 0; j <= i; j++)
                {
                    double sum = matrix[i, j];
                    for (int k = 0; k < j; k++)
                    {
                        sum -= matrix[i, k] * matrix[j, k];
                    }

                    if (i == j)
                    {
                        matrix[i, j] = sum > 0 ? Math.Sqrt(sum) : 0;
                    }
                    else
                    {
                        matrix[i, j] = matrix[j, j] > 0 ? sum / matrix[j, j] : 0;
                    }
                }
            }

            double[] intermediate = new double[size];
            for (int i = 0; i < size; i++)
            {
                double sum = rhs[i];
                for (int k = 0; k < i; k++)
                {
                    sum -= matrix[i, k] * intermediate[k];
                }

                intermediate[i] = matrix[i, i] > 0 ? sum / matrix[i, i] : 0;
            }

            double[] solution = new double[size];
            for (int i = size - 1; i >= 0; i--)
            {
                double sum = intermediate[i];
                for (int k = i + 1; k < size; k++)
                {
                    sum -= matrix[k, i] * solution[k];
                }

                solution[i] = matrix[i, i] > 0 ? sum / matrix[i, i] : 0;
            }

            return solution;
        }
    }
}
