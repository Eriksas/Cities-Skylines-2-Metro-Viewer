using System;
using System.Collections.Generic;
using System.Text;

namespace MetroDiagram.Engine
{
    // Geometric corner rounding for in-game route strokes.
    //
    // Port of MetroSvgRenderer.RoundedCorners.cs. Same constants, same
    // derivation, same emitted commands -- the two renderers have to agree on
    // what a corner looks like or the in-game preview stops predicting the
    // desktop output.
    //
    // Two deliberate differences from the desktop version:
    //   * no data-points attribute. That exists so the Viewer's bend handles and
    //     the tests can read the layout polyline behind the drawn curve. Nothing
    //     in-game edits geometry, and the SVG is inlined into CS2's browser, so
    //     the second copy of every coordinate would be paid for and never read.
    //   * netstandard2.0: no Math.Clamp, no index-from-end, no record structs.
    public sealed partial class PortableMetroSvgRenderer
    {
        // Distance retreated from a corner along each of its two legs before the
        // curve starts, as a multiple of the base route stroke width.
        private const double CornerRoundingWidthFactor = 2.0;

        // A corner may never eat more than this fraction of a leg, so two
        // adjacent corners on a short segment can never overrun each other.
        private const double CornerRoundingLegFraction = 0.45;

        // How far the curve may fall away from the original vertex, as a
        // multiple of the stroke width. This is the bound that keeps rounding
        // safe for us: almost every corner we draw sits on a station, and a cut
        // corner would leave that station's marker hanging off the side of the
        // line. Bounding deviation rather than radius also gives the most
        // rounding on shallow turns and pulls back as a turn sharpens.
        private const double CornerRoundingMaxDeviationFactor = 0.22;

        // Below this turn angle the vertex is passed through as a plain lineto.
        private const double CornerRoundingMinTurnDegrees = 3.0;

        private const double CornerRoundingEpsilon = 1e-6;

        /// <summary>
        /// The two bounds on how far a corner may be cut: how far back the curve
        /// may start along each leg, and how far it may fall from the vertex.
        /// </summary>
        private struct RouteCornerRounding
        {
            public RouteCornerRounding(double maxRetreat, double maxDeviation)
            {
                MaxRetreat = maxRetreat;
                MaxDeviation = maxDeviation;
            }

            public double MaxRetreat { get; }

            public double MaxDeviation { get; }

            public bool IsEnabled { get { return MaxRetreat > 0 && MaxDeviation > 0; } }
        }

        private static RouteCornerRounding GetRouteCornerRounding(PortableRenderOptions options)
        {
            if (!options.RoundRouteCorners)
            {
                return default(RouteCornerRounding);
            }

            return new RouteCornerRounding(
                options.RouteWidth * CornerRoundingWidthFactor,
                options.RouteWidth * CornerRoundingMaxDeviationFactor);
        }

        /// <summary>
        /// Renders a point list as path data. With rounding disabled this draws
        /// exactly the polyline it replaces (M/L only).
        /// </summary>
        private static string BuildRoundedPathData(IReadOnlyList<Point2> points, RouteCornerRounding cornerRounding)
        {
            List<Point2> cleaned = RemoveRepeatedPoints(points);
            if (cleaned.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("M ").Append(F(cleaned[0].X)).Append(',').Append(F(cleaned[0].Y));

            if (cleaned.Count == 1)
            {
                return builder.ToString();
            }

            for (int i = 1; i < cleaned.Count - 1; i++)
            {
                Point2 previous = cleaned[i - 1];
                Point2 vertex = cleaned[i];
                Point2 next = cleaned[i + 1];

                double toPreviousX = previous.X - vertex.X;
                double toPreviousY = previous.Y - vertex.Y;
                double toNextX = next.X - vertex.X;
                double toNextY = next.Y - vertex.Y;

                double previousLength = Math.Sqrt((toPreviousX * toPreviousX) + (toPreviousY * toPreviousY));
                double nextLength = Math.Sqrt((toNextX * toNextX) + (toNextY * toNextY));
                if (previousLength < CornerRoundingEpsilon || nextLength < CornerRoundingEpsilon)
                {
                    continue;
                }

                double retreat = ResolveCornerRetreat(
                    cornerRounding,
                    toPreviousX / previousLength,
                    toPreviousY / previousLength,
                    toNextX / nextLength,
                    toNextY / nextLength,
                    previousLength,
                    nextLength);

                if (retreat <= CornerRoundingEpsilon)
                {
                    AppendLineTo(builder, vertex);
                    continue;
                }

                Point2 entry = new Point2(
                    vertex.X + (toPreviousX / previousLength * retreat),
                    vertex.Y + (toPreviousY / previousLength * retreat));
                Point2 exit = new Point2(
                    vertex.X + (toNextX / nextLength * retreat),
                    vertex.Y + (toNextY / nextLength * retreat));

                AppendLineTo(builder, entry);
                builder
                    .Append(" Q ").Append(F(vertex.X)).Append(',').Append(F(vertex.Y))
                    .Append(' ').Append(F(exit.X)).Append(',').Append(F(exit.Y));
            }

            AppendLineTo(builder, cleaned[cleaned.Count - 1]);
            return builder.ToString();
        }

        private static double ResolveCornerRetreat(
            RouteCornerRounding cornerRounding,
            double previousUnitX,
            double previousUnitY,
            double nextUnitX,
            double nextUnitY,
            double previousLength,
            double nextLength)
        {
            if (!cornerRounding.IsEnabled)
            {
                return 0;
            }

            double dot = Clamp((previousUnitX * nextUnitX) + (previousUnitY * nextUnitY), -1, 1);

            // Interior angle at the vertex. 180 degrees means straight through;
            // the turn the reader perceives is 180 minus this.
            double interiorDegrees = Math.Acos(dot) * 180 / Math.PI;
            double turnDegrees = 180 - interiorDegrees;
            if (turnDegrees < CornerRoundingMinTurnDegrees)
            {
                return 0;
            }

            double retreat = cornerRounding.MaxRetreat;

            // cos(interior / 2), via the half-angle identity, so no second trig call.
            double halfInteriorCosine = Math.Sqrt((1 + dot) / 2);
            if (halfInteriorCosine > CornerRoundingEpsilon)
            {
                retreat = Math.Min(retreat, cornerRounding.MaxDeviation * 2 / halfInteriorCosine);
            }

            return Math.Min(retreat, Math.Min(previousLength, nextLength) * CornerRoundingLegFraction);
        }

        private static void AppendLineTo(StringBuilder builder, Point2 point)
        {
            builder.Append(" L ").Append(F(point.X)).Append(',').Append(F(point.Y));
        }

        private static List<Point2> RemoveRepeatedPoints(IReadOnlyList<Point2> points)
        {
            List<Point2> cleaned = new List<Point2>(points.Count);
            for (int i = 0; i < points.Count; i++)
            {
                if (cleaned.Count > 0)
                {
                    Point2 last = cleaned[cleaned.Count - 1];
                    if (Math.Abs(last.X - points[i].X) < CornerRoundingEpsilon
                        && Math.Abs(last.Y - points[i].Y) < CornerRoundingEpsilon)
                    {
                        continue;
                    }
                }

                cleaned.Add(points[i]);
            }

            return cleaned;
        }
    }
}
