using System;
using System.Collections.Generic;
using System.Text;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    // Geometric corner rounding for route strokes.
    //
    // Until now a route was a <polyline> and its only softening came from
    // `stroke-linejoin: round`, whose effective radius is half the stroke width
    // (7 on our 14px routes). Real metro maps round corners far more: rmp uses a
    // roundCornerFactor of 10 on a 5px line, i.e. 2x the stroke width, and 18.33
    // (3.7x) for its perpendicular paths. That is the difference between "the
    // joint is not a spike" and "the line sweeps through the turn".
    //
    // The stroke is therefore emitted as a <path> whose corners are bridged by
    // quadratic Beziers. The polyline point list survives as `data-points` so the
    // Viewer's bend/segment editing and the tests keep reading the geometry they
    // always read -- display geometry and edit geometry are separate concerns,
    // which is also how rmp splits path from node model.

    // Distance retreated from a corner along each of its two legs before the
    // curve starts, as a multiple of the base route stroke width.
    private const double CornerRoundingWidthFactor = 2.0;

    // A corner may never eat more than this fraction of a leg, so two adjacent
    // corners on a short segment can never overrun each other.
    private const double CornerRoundingLegFraction = 0.45;

    // How far the curve is allowed to fall away from the original vertex, as a
    // multiple of the stroke width.
    //
    // This is the constraint that makes rounding safe for us specifically. rmp
    // bends mostly at Virtual nodes -- invisible points that carry no marker --
    // so it can cut a corner as deeply as it likes. Almost every corner we draw
    // sits *on a station*, and a cut corner leaves the station marker hanging off
    // the side of the line. Bounding the deviation instead of the radius keeps
    // the marker on the stroke at every corner.
    //
    // It also produces the behaviour we actually want: for a quadratic bridge the
    // deviation is retreat * cos(interior/2) / 2, so solving for the retreat gives
    // the most rounding at shallow turns -- the 135-degree corners -- and pulls it
    // back automatically as a turn sharpens.
    private const double CornerRoundingMaxDeviationFactor = 0.22;

    // Below this turn angle the vertex is treated as straight and passed through
    // as a plain lineto -- curving a 2-degree kink only adds noise.
    private const double CornerRoundingMinTurnDegrees = 3.0;

    private const double CornerRoundingEpsilon = 1e-6;

    /// <summary>
    /// The two bounds on how far a corner may be cut: how far back the curve may
    /// start along each leg, and how far it may fall away from the vertex itself.
    /// </summary>
    private readonly record struct RouteCornerRounding(double MaxRetreat, double MaxDeviation)
    {
        public bool IsEnabled => MaxRetreat > 0 && MaxDeviation > 0;
    }

    private static RouteCornerRounding GetRouteCornerRounding(SvgRenderOptions options)
    {
        if (!options.EnableRouteCornerRounding)
        {
            return default;
        }

        double routeWidth = SvgVisualStyle.From(options).BaseRouteWidth;
        return new RouteCornerRounding(
            routeWidth * CornerRoundingWidthFactor,
            routeWidth * CornerRoundingMaxDeviationFactor);
    }

    /// <summary>
    /// Builds the geometry attributes shared by every layer drawn along one route:
    /// <c>d</c> for the visible rounded stroke and <c>data-points</c> for the
    /// underlying polyline. All layers of a route must reuse the same string, or
    /// the base stroke and its overlays will diverge at the corners.
    /// </summary>
    /// <param name="displayPoints">
    /// What gets drawn. May carry bends the layout never placed -- see
    /// <see cref="BuildStationStraightRuns"/>.
    /// </param>
    /// <param name="editPoints">
    /// The route as the layout produced it. This is what the Viewer's handles and
    /// the tests read, so derived display geometry must never leak into it.
    /// </param>
    private static string BuildRouteGeometryAttributes(
        IReadOnlyList<SvgPoint> displayPoints,
        IReadOnlyList<SvgPoint> editPoints,
        RouteCornerRounding cornerRounding)
    {
        return $"d=\"{BuildRoundedPathData(displayPoints, cornerRounding)}\" data-points=\"{BuildPointList(editPoints)}\"";
    }

    private static string BuildPointList(IReadOnlyList<SvgPoint> points)
    {
        StringBuilder builder = new();
        for (int i = 0; i < points.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(Format(points[i].X)).Append(',').Append(Format(points[i].Y));
        }

        return builder.ToString();
    }

    /// <summary>
    /// Renders a point list as path data. With rounding disabled this is exactly
    /// the polyline it replaces (M/L only).
    /// </summary>
    private static string BuildRoundedPathData(IReadOnlyList<SvgPoint> points, RouteCornerRounding cornerRounding)
    {
        List<SvgPoint> cleaned = RemoveRepeatedPoints(points);
        if (cleaned.Count == 0)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        builder.Append("M ").Append(Format(cleaned[0].X)).Append(',').Append(Format(cleaned[0].Y));

        if (cleaned.Count == 1)
        {
            return builder.ToString();
        }

        for (int i = 1; i < cleaned.Count - 1; i++)
        {
            SvgPoint previous = cleaned[i - 1];
            SvgPoint vertex = cleaned[i];
            SvgPoint next = cleaned[i + 1];

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

            SvgPoint entry = new(
                vertex.X + (toPreviousX / previousLength * retreat),
                vertex.Y + (toPreviousY / previousLength * retreat));
            SvgPoint exit = new(
                vertex.X + (toNextX / nextLength * retreat),
                vertex.Y + (toNextY / nextLength * retreat));

            AppendLineTo(builder, entry);
            builder
                .Append(" Q ").Append(Format(vertex.X)).Append(',').Append(Format(vertex.Y))
                .Append(' ').Append(Format(exit.X)).Append(',').Append(Format(exit.Y));
        }

        AppendLineTo(builder, cleaned[^1]);
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

        double dot = Math.Clamp((previousUnitX * nextUnitX) + (previousUnitY * nextUnitY), -1, 1);

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

    private static void AppendLineTo(StringBuilder builder, SvgPoint point)
    {
        builder.Append(" L ").Append(Format(point.X)).Append(',').Append(Format(point.Y));
    }

    private static List<SvgPoint> RemoveRepeatedPoints(IReadOnlyList<SvgPoint> points)
    {
        List<SvgPoint> cleaned = new(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            if (cleaned.Count > 0)
            {
                SvgPoint last = cleaned[^1];
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
