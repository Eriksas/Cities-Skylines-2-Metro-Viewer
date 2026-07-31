using System;
using System.Collections.Generic;
using System.Linq;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    // Give each line its own lane where several share a corridor.
    //
    // The existing parallel-corridor pass keys on an *edge* -- the same pair of
    // stations -- so it only separates lines that run station-for-station
    // together. After the octilinear snap the common case is different: two lines
    // land on the same grid line and overlap for a long stretch while calling at
    // different stops. Measured on 肇庆, that left 26% of stroke length drawn on
    // top of another line, against 0% before the snap.
    //
    // Snapping is also what makes the fix exact. "These two segments share a
    // corridor" stops being a tolerance judgement and becomes a test on the
    // heading and the intercept, so lanes can be assigned by construction rather
    // than guessed. The mechanism is rmp's: an offset per edge end, applied along
    // that end's own normal, which is what lets a line ramp in and out of a
    // corridor (offsetFrom/offsetTo in its path generators). A lane change on a
    // straight run is drawn as a 45-degree jog, so the ramp stays on the grid too.

    /// <summary>Lane pitch, as a multiple of the base route width.</summary>
    ///
    /// <remarks>
    /// Bounded above by the interchange marker: a station served by every line in
    /// the corridor keeps one centred symbol, and that symbol has to reach both
    /// outermost lanes or it stops reading as an interchange. With a marker radius
    /// of 9.8 on a 14-wide stroke, 1.35 is the widest pitch that still touches.
    /// Going wider needs the elongated interchange symbol Shanghai uses in rmp.
    /// </remarks>
    private const double CorridorLaneSpacingFactor = 1.35;

    private const double CorridorLaneEpsilon = 1e-6;

    /// <summary>Two headings genuinely differ by at least sin(45); below that it is float dust.</summary>
    private const double CorridorLaneParallelDot = 0.2;

    private sealed record CorridorSegment(int Route, int Polyline, int Edge, int Axis, double Intercept, double From, double To, bool Flipped);

    /// <summary>
    /// Offsets, keyed by route/polyline/edge. Absent means the edge is drawn where
    /// the layout put it.
    /// </summary>
    private static Dictionary<(int Route, int Polyline, int Edge), double> BuildCorridorLanePlan(
        List<RenderRoute> renderRoutes,
        SvgRenderOptions options)
    {
        double spacing = SvgVisualStyle.From(options).BaseRouteWidth * CorridorLaneSpacingFactor;
        double stroke = SvgVisualStyle.From(options).BaseRouteWidth;

        List<CorridorSegment> segments = [];
        for (int r = 0; r < renderRoutes.Count; r++)
        {
            List<RoutePolyline> polylines = renderRoutes[r].RoutePointSet.Polylines;
            for (int p = 0; p < polylines.Count; p++)
            {
                IReadOnlyList<SvgPoint> points = polylines[p].Points;
                for (int e = 1; e < points.Count; e++)
                {
                    CorridorSegment? segment = DescribeCorridorSegment(r, p, e - 1, points[e - 1], points[e]);
                    if (segment is not null)
                    {
                        segments.Add(segment);
                    }
                }
            }
        }

        Dictionary<(int, int, int), double> plan = [];
        foreach (IGrouping<int, CorridorSegment> byAxis in segments.GroupBy(segment => segment.Axis))
        {
            // Cluster on intercept. One lane pitch, not one stroke width:
            // clustering is transitive, so a gap this size chains A-B-C into a
            // single corridor and gives all three distinct lanes at once. Narrower
            // and they separate pairwise, after which laning A away from B pushes
            // A onto C.
            double mergeGap = Math.Max(spacing, stroke);
            List<List<CorridorSegment>> corridors = [];
            List<CorridorSegment> current = [];
            double previous = double.NaN;
            foreach (CorridorSegment segment in byAxis.OrderBy(s => s.Intercept))
            {
                if (current.Count > 0 && segment.Intercept - previous > mergeGap)
                {
                    corridors.Add(current);
                    current = [];
                }

                current.Add(segment);
                previous = segment.Intercept;
            }

            if (current.Count > 0)
            {
                corridors.Add(current);
            }

            foreach (List<CorridorSegment> corridor in corridors)
            {
                AssignCorridorLanes(corridor, renderRoutes, spacing, plan);
            }
        }

        return plan;
    }

    private static CorridorSegment? DescribeCorridorSegment(int route, int polyline, int edge, SvgPoint a, SvgPoint b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < CorridorLaneEpsilon)
        {
            return null;
        }

        // Quantise to the nearest of the four axes. Keying on the raw direction
        // drops an edge a fraction of a degree off horizontal into a bucket of its
        // own, where it never gets a lane and keeps drawing over its neighbour.
        double degrees = ((Math.Atan2(dy, dx) * 180 / Math.PI) % 180 + 180) % 180;
        int axis = NearestAxis(degrees);
        (double hx, double hy) = OctilinearAxes[axis];
        bool flipped = ((dx / length * hx) + (dy / length * hy)) < 0;
        double nx = -hy;
        double ny = hx;
        double from = (a.X * hx) + (a.Y * hy);
        double to = (b.X * hx) + (b.Y * hy);
        return new CorridorSegment(
            route,
            polyline,
            edge,
            axis,
            (a.X * nx) + (a.Y * ny),
            Math.Min(from, to),
            Math.Max(from, to),
            flipped);
    }

    private static void AssignCorridorLanes(
        List<CorridorSegment> corridor,
        List<RenderRoute> renderRoutes,
        double spacing,
        Dictionary<(int, int, int), double> plan)
    {
        Dictionary<int, (double From, double To)> spans = [];
        Dictionary<int, List<double>> intercepts = [];
        foreach (CorridorSegment segment in corridor)
        {
            (double from, double to) = spans.TryGetValue(segment.Route, out var span)
                ? span
                : (segment.From, segment.To);
            spans[segment.Route] = (Math.Min(from, segment.From), Math.Max(to, segment.To));
            intercepts.TryAdd(segment.Route, []);
            intercepts[segment.Route].Add(segment.Intercept);
        }

        HashSet<int> overlapping = [];
        foreach (int a in spans.Keys)
        {
            foreach (int b in spans.Keys)
            {
                if (a != b && Math.Min(spans[a].To, spans[b].To) - Math.Max(spans[a].From, spans[b].From) > 1.0)
                {
                    overlapping.Add(a);
                    break;
                }
            }
        }

        if (overlapping.Count < 2)
        {
            return;
        }

        // A line and its own branch share a colour and share track; drawing them
        // as two lanes shows one service as two. They ride the same lane.
        Dictionary<string, List<int>> byColour = [];
        foreach (int route in overlapping.OrderBy(index => index))
        {
            string colour = renderRoutes[route].Family.Color ?? string.Empty;
            byColour.TryAdd(colour, []);
            byColour[colour].Add(route);
        }

        if (byColour.Count < 2)
        {
            return;
        }

        // Lanes follow the order the lines already sit in across the corridor.
        // Ordering by anything else lets two lines swap sides, which moves them
        // towards each other instead of apart.
        List<string> order = byColour.Keys
            .OrderBy(colour => byColour[colour].Average(route => intercepts[route].Average()))
            .ToList();
        double centre = (order.Count - 1) / 2.0;

        foreach (CorridorSegment segment in corridor)
        {
            if (!overlapping.Contains(segment.Route))
            {
                continue;
            }

            string colour = renderRoutes[segment.Route].Family.Color ?? string.Empty;
            double lane = (order.IndexOf(colour) - centre) * spacing;
            // The lane is chosen in the corridor's canonical direction but applied
            // along each edge's own normal, so a line running the corridor the
            // other way needs the sign flipped or the two land on the same side.
            plan[(segment.Route, segment.Polyline, segment.Edge)] = segment.Flipped ? -lane : lane;
        }
    }

    /// <summary>
    /// Rebuilds a polyline with each edge displaced along its own normal. Where
    /// two consecutive edges share a heading but sit in different lanes, the
    /// transition is a 45-degree jog rather than an impossible intersection.
    /// </summary>
    private static List<SvgPoint> OffsetRouteForCorridorLane(
        IReadOnlyList<SvgPoint> points,
        int route,
        int polyline,
        Dictionary<(int Route, int Polyline, int Edge), double> plan)
    {
        if (points.Count < 2)
        {
            return points.ToList();
        }

        double[] offsets = new double[points.Count - 1];
        bool any = false;
        for (int e = 0; e < offsets.Length; e++)
        {
            offsets[e] = plan.GetValueOrDefault((route, polyline, e));
            any |= Math.Abs(offsets[e]) > CorridorLaneEpsilon;
        }

        if (!any)
        {
            return points.ToList();
        }

        (double X, double Y) Normal(int edge)
        {
            double dx = points[edge + 1].X - points[edge].X;
            double dy = points[edge + 1].Y - points[edge].Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            return length < CorridorLaneEpsilon ? (0, 0) : (-dy / length, dx / length);
        }

        List<SvgPoint> result = [];
        (double X, double Y) first = Normal(0);
        result.Add(new SvgPoint(points[0].X + (first.X * offsets[0]), points[0].Y + (first.Y * offsets[0])));

        for (int e = 1; e < points.Count - 1; e++)
        {
            (double X, double Y) incoming = Normal(e - 1);
            (double X, double Y) outgoing = Normal(e);
            double before = offsets[e - 1];
            double after = offsets[e];
            SvgPoint vertex = points[e];
            double determinant = (incoming.X * outgoing.Y) - (incoming.Y * outgoing.X);

            if (Math.Abs(determinant) < CorridorLaneParallelDot)
            {
                if (Math.Abs(before - after) < CorridorLaneEpsilon)
                {
                    result.Add(new SvgPoint(vertex.X + (incoming.X * before), vertex.Y + (incoming.Y * before)));
                    continue;
                }

                double dx = points[e + 1].X - vertex.X;
                double dy = points[e + 1].Y - vertex.Y;
                double length = Math.Sqrt((dx * dx) + (dy * dy));
                double jog = Math.Abs(after - before);
                if (length > CorridorLaneEpsilon)
                {
                    result.Add(new SvgPoint(
                        vertex.X + (incoming.X * before) - (dx / length * jog),
                        vertex.Y + (incoming.Y * before) - (dy / length * jog)));
                }

                result.Add(new SvgPoint(vertex.X + (outgoing.X * after), vertex.Y + (outgoing.Y * after)));
                continue;
            }

            double c1 = (incoming.X * (vertex.X + (incoming.X * before))) + (incoming.Y * (vertex.Y + (incoming.Y * before)));
            double c2 = (outgoing.X * (vertex.X + (outgoing.X * after))) + (outgoing.Y * (vertex.Y + (outgoing.Y * after)));
            result.Add(new SvgPoint(
                ((c1 * outgoing.Y) - (c2 * incoming.Y)) / determinant,
                ((c2 * incoming.X) - (c1 * outgoing.X)) / determinant));
        }

        (double X, double Y) last = Normal(points.Count - 2);
        SvgPoint end = points[^1];
        result.Add(new SvgPoint(end.X + (last.X * offsets[^1]), end.Y + (last.Y * offsets[^1])));
        return result;
    }
}
