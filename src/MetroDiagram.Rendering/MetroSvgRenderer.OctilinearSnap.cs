using System;
using System.Collections.Generic;
using System.Linq;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    // Pull the annealed layout onto the eight compass headings, exactly.
    //
    // The annealer already optimises for octilinearity, but as one term among
    // several, so it lands near the grid rather than on it: measured over 22 real
    // networks, only 65% of drawn segments were within a degree of a 45-degree
    // multiple. A reader does not see "65% aligned", they see a set of arbitrary
    // angles, and that is what makes our output look plotted where a real metro
    // map looks drawn. It is also not something corner rounding can fix --
    // rounding smooths the joint, not the direction.
    //
    // Once an edge has been assigned a heading, "this edge lies exactly on that
    // heading" is a *linear* condition on the two station positions: the
    // component of (pj - pi) along the heading's normal is zero. So the whole map
    // becomes
    //
    //     minimise    sum |p - p0|^2            stations move as little as possible
    //     subject to  (pj - pi) . n_e = 0       every assigned edge is exact
    //
    // which is convex, has a single global optimum, and is automatically
    // consistent for a station shared by several lines. Solved as weighted least
    // squares -- the constraints are penalties with a large weight rather than
    // hard equalities, which keeps the system positive definite and degrades
    // gracefully when a station's runs disagree.
    //
    // Headings come from *runs*, not from single edges. Doglegging each edge onto
    // the grid separately does not remove its angular error, it folds the error
    // into a corner: a 200px edge 5 degrees off horizontal gains a bulge of
    // 200*sin(5) = 17px, wider than the stroke. Snapping a whole run removes the
    // error instead, and what is left between runs is a real direction change.
    //
    // Corpus result: 65.0% -> 99.9% of segments exactly on a heading, lines drawn
    // over each other 2.91% -> 0.03% of stroke length, crowded stations
    // 12.6% -> 8.9%, edge-length spread unchanged.

    /// <summary>Direction agreement, in degrees, for two edges to count as one run.</summary>
    private const double OctilinearRunToleranceDegrees = 22.0;

    /// <summary>
    /// Weight on the heading conditions relative to the unit weight on "stay put".
    ///
    /// This ratio is the whole design, not a tuning knob to shrug at. The heading
    /// rows are *relative* ((pj - pi).n = 0), so sliding an entire run sideways
    /// satisfies them exactly and costs only the anchor rows. Given a large enough
    /// ratio, the cheapest way to answer a separation request is therefore to
    /// translate a whole run rather than to bend one edge off its heading. Too
    /// small and it does the opposite: at a ratio of 40 the separation pass broke
    /// octilinearity on 肇庆 (100% -> 84.9%) and let overlaps back in. Crowding
    /// improved monotonically with the ratio -- 奥斯特-0728 went 20.8% -> 13.2%
    /// -> 7.5% at 40, 800 and 12000.
    /// </summary>
    private const double OctilinearConstraintWeight = 12_000;

    private const double OctilinearSeparationWeight = 40;

    /// <summary>Minimum station spacing to restore, as a share of the median edge.</summary>
    private const double OctilinearSeparationFraction = 0.45;

    private const int OctilinearSeparationRounds = 6;

    private const double OctilinearEpsilon = 1e-9;

    private static readonly (double X, double Y)[] OctilinearAxes =
    [
        (1, 0),
        (Math.Sqrt(0.5), Math.Sqrt(0.5)),
        (0, 1),
        (-Math.Sqrt(0.5), Math.Sqrt(0.5)),
    ];

    /// <summary>
    /// Returns the layout with every run placed exactly on a compass heading, or
    /// the input unchanged when the pass is disabled or has nothing to work with.
    /// </summary>
    private static Dictionary<string, SvgPoint> ApplyOctilinearSnap(
        Dictionary<string, SvgPoint> points,
        List<DisplayLineFamily> families,
        SvgRenderOptions options,
        List<string> warnings)
    {
        if (!options.EnableOctilinearSnap || points.Count < 2)
        {
            return points;
        }

        SchematicLayoutTopology topology = BuildSchematicLayoutTopology(families, points);
        if (topology.Routes.Length == 0)
        {
            return points;
        }

        int count = topology.StationIds.Length;
        SvgPoint[] original = new SvgPoint[count];
        for (int i = 0; i < count; i++)
        {
            original[i] = points[topology.StationIds[i]];
        }

        List<(int A, int B, int Axis)> constraints = BuildRunConstraints(topology, original);
        if (constraints.Count == 0)
        {
            return points;
        }

        SvgPoint[] solved = SolveOctilinear(original, constraints, []);

        double minimumSpacing = ResolveOctilinearSpacing(topology, original);
        int separationRounds = 0;
        for (int round = 0; round < OctilinearSeparationRounds && minimumSpacing > 0; round++)
        {
            List<(int A, int B, double X, double Y, double Wanted)> separations =
                FindCrowdedPairs(solved, minimumSpacing);
            if (separations.Count == 0)
            {
                break;
            }

            separationRounds = round + 1;
            solved = SolveOctilinear(original, constraints, separations);
        }

        Dictionary<string, SvgPoint> result = new(points.Count, StringComparer.Ordinal);
        foreach ((string id, SvgPoint point) in points)
        {
            result[id] = point;
        }

        double moved = 0;
        double worst = 0;
        for (int i = 0; i < count; i++)
        {
            double shift = Distance(original[i], solved[i]);
            moved += shift;
            worst = Math.Max(worst, shift);
            result[topology.StationIds[i]] = solved[i];
        }

        warnings.Add(
            $"Octilinear snap: {constraints.Count} of {topology.Edges.Length} edges pinned to a heading; "
            + $"stations moved {Format(moved / count)} mean, {Format(worst)} worst; "
            + $"separation rounds {separationRounds}.");
        return result;
    }

    /// <summary>
    /// One heading per edge, taken from the run it belongs to. Consecutive edges
    /// stay in the same run while they agree with its heading to within
    /// <see cref="OctilinearRunToleranceDegrees"/>; the first edge that does not
    /// starts a new run, and that junction is a direction change the map keeps.
    /// </summary>
    private static List<(int A, int B, int Axis)> BuildRunConstraints(
        SchematicLayoutTopology topology,
        SvgPoint[] positions)
    {
        Dictionary<(int A, int B), int> assigned = [];
        foreach (int[] route in topology.Routes)
        {
            int current = -1;
            for (int i = 1; i < route.Length; i++)
            {
                int a = route[i - 1];
                int b = route[i];
                double dx = positions[b].X - positions[a].X;
                double dy = positions[b].Y - positions[a].Y;
                if ((dx * dx) + (dy * dy) < OctilinearEpsilon)
                {
                    continue;
                }

                double degrees = ((Math.Atan2(dy, dx) * 180 / Math.PI) % 180 + 180) % 180;
                if (current < 0 || AxisErrorDegrees(degrees, current) > OctilinearRunToleranceDegrees)
                {
                    current = NearestAxis(degrees);
                }

                (int A, int B) key = a < b ? (a, b) : (b, a);
                // First run to claim an edge wins, so a shared trunk keeps one
                // heading rather than flipping with whichever line is read last.
                assigned.TryAdd(key, current);
            }
        }

        return assigned
            .OrderBy(entry => entry.Key.A)
            .ThenBy(entry => entry.Key.B)
            .Select(entry => (entry.Key.A, entry.Key.B, entry.Value))
            .ToList();
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
        for (int axis = 0; axis < OctilinearAxes.Length; axis++)
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

    private static double ResolveOctilinearSpacing(SchematicLayoutTopology topology, SvgPoint[] positions)
    {
        List<double> lengths = topology.Edges
            .Select(edge => Distance(positions[edge.A], positions[edge.B]))
            .Where(length => length > OctilinearEpsilon)
            .OrderBy(length => length)
            .ToList();
        return lengths.Count == 0 ? 0 : lengths[lengths.Count / 2] * OctilinearSeparationFraction;
    }

    private static List<(int A, int B, double X, double Y, double Wanted)> FindCrowdedPairs(
        SvgPoint[] positions,
        double minimumSpacing)
    {
        List<(int A, int B, double X, double Y, double Wanted)> pairs = [];
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

                pairs.Add((i, j, dx / gap, dy / gap, minimumSpacing));
            }
        }

        return pairs;
    }

    /// <summary>
    /// Weighted least squares over the station coordinates, solved through the
    /// normal equations. Every row is sparse (two or four entries), so the normal
    /// matrix is accumulated directly rather than forming the design matrix.
    /// </summary>
    private static SvgPoint[] SolveOctilinear(
        SvgPoint[] original,
        List<(int A, int B, int Axis)> constraints,
        List<(int A, int B, double X, double Y, double Wanted)> separations)
    {
        int count = original.Length;
        int size = count * 2;
        double[,] normal = new double[size, size];
        double[] rhs = new double[size];

        int[] columns = new int[4];
        double[] values = new double[4];

        void AddRow(int used, double target)
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

        // stay put
        for (int i = 0; i < count; i++)
        {
            columns[0] = 2 * i;
            values[0] = 1;
            AddRow(1, original[i].X);
            columns[0] = (2 * i) + 1;
            values[0] = 1;
            AddRow(1, original[i].Y);
        }

        // exactly on the assigned heading
        foreach ((int a, int b, int axis) in constraints)
        {
            (double hx, double hy) = OctilinearAxes[axis];
            double nx = -hy;
            double ny = hx;
            columns[0] = 2 * b;
            values[0] = nx * OctilinearConstraintWeight;
            columns[1] = (2 * b) + 1;
            values[1] = ny * OctilinearConstraintWeight;
            columns[2] = 2 * a;
            values[2] = -nx * OctilinearConstraintWeight;
            columns[3] = (2 * a) + 1;
            values[3] = -ny * OctilinearConstraintWeight;
            AddRow(4, 0);
        }

        foreach ((int a, int b, double ex, double ey, double wanted) in separations)
        {
            columns[0] = 2 * a;
            values[0] = ex * OctilinearSeparationWeight;
            columns[1] = (2 * a) + 1;
            values[1] = ey * OctilinearSeparationWeight;
            columns[2] = 2 * b;
            values[2] = -ex * OctilinearSeparationWeight;
            columns[3] = (2 * b) + 1;
            values[3] = -ey * OctilinearSeparationWeight;
            AddRow(4, wanted * OctilinearSeparationWeight);
        }

        double[] solution = SolveSymmetricPositiveDefinite(normal, rhs);
        SvgPoint[] result = new SvgPoint[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = double.IsFinite(solution[2 * i]) && double.IsFinite(solution[(2 * i) + 1])
                ? new SvgPoint(solution[2 * i], solution[(2 * i) + 1])
                : original[i];
        }

        return result;
    }

    /// <summary>
    /// Cholesky solve, in place. The unit-weight "stay put" rows put a 1 on every
    /// diagonal, so the matrix is positive definite by construction; the ridge is
    /// only there so a degenerate network cannot produce a NaN layout.
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
