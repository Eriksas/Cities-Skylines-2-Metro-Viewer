using System.Globalization;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    // Parallel-corridor rendering for schematic-anneal.
    //
    // When several lines run through the same station-to-station edge they must
    // be drawn side by side, not stacked on top of each other. Because the anneal
    // layout is clean octilinear geometry, each shared edge has one unambiguous
    // perpendicular, so a line only needs a signed offset (its "lane slot") along
    // that perpendicular. Lanes are ordered by a stable global key (line number),
    // which is deterministic and keeps the ordering consistent across the sparse,
    // short shared runs that dominate real networks. Longer trunks that would
    // benefit from crossing-minimising lane ordering are a later refinement.
    private const double ParallelCorridorEndpointRoundingEpsilon = 0.5;

    private readonly record struct ParallelEdgeKey(long X0, long Y0, long X1, long Y1);

    private sealed record ParallelSharedEdge(SvgPoint Normal, Dictionary<string, double> OffsetByFamily);

    private static long QuantizeParallelCoordinate(double value)
    {
        return (long)Math.Round(value / ParallelCorridorEndpointRoundingEpsilon);
    }

    // Canonical undirected key so an out-and-back line that traverses the edge in
    // both directions still maps to a single edge.
    private static ParallelEdgeKey CreateParallelEdgeKey(SvgPoint a, SvgPoint b)
    {
        long ax = QuantizeParallelCoordinate(a.X);
        long ay = QuantizeParallelCoordinate(a.Y);
        long bx = QuantizeParallelCoordinate(b.X);
        long by = QuantizeParallelCoordinate(b.Y);
        bool aFirst = ax < bx || (ax == bx && ay <= by);
        return aFirst
            ? new ParallelEdgeKey(ax, ay, bx, by)
            : new ParallelEdgeKey(bx, by, ax, ay);
    }

    private static double GetParallelCorridorSpacing(SvgRenderOptions options)
    {
        return Math.Max(Math.Max(1, options.LineWidth) * 1.05, Math.Max(8, options.GridSize) * 0.14);
    }

    /// <summary>
    /// Builds the per-edge lane assignment: for every station-to-station edge shared
    /// by two or more display families, orders the families by a stable line key and
    /// assigns each a symmetric signed offset along the edge's canonical normal.
    /// </summary>
    private static Dictionary<ParallelEdgeKey, ParallelSharedEdge> BuildParallelCorridorPlan(
        List<RenderRoute> renderRoutes,
        SvgRenderOptions options)
    {
        // edge -> canonical endpoints + set of families traversing it.
        Dictionary<ParallelEdgeKey, (SvgPoint A, SvgPoint B, Dictionary<string, DisplayLineFamily> Families)> edges = [];
        foreach (RenderRoute route in renderRoutes)
        {
            foreach (RoutePolyline polyline in route.RoutePointSet.Polylines)
            {
                IReadOnlyList<SvgPoint> points = polyline.Points;
                for (int i = 1; i < points.Count; i++)
                {
                    SvgPoint a = points[i - 1];
                    SvgPoint b = points[i];
                    if (Distance(a, b) < 0.001)
                    {
                        continue;
                    }

                    ParallelEdgeKey key = CreateParallelEdgeKey(a, b);
                    if (!edges.TryGetValue(key, out var entry))
                    {
                        bool aFirst = QuantizeParallelCoordinate(a.X) < QuantizeParallelCoordinate(b.X)
                            || (QuantizeParallelCoordinate(a.X) == QuantizeParallelCoordinate(b.X) && QuantizeParallelCoordinate(a.Y) <= QuantizeParallelCoordinate(b.Y));
                        entry = (aFirst ? a : b, aFirst ? b : a, new Dictionary<string, DisplayLineFamily>(StringComparer.Ordinal));
                        edges[key] = entry;
                    }

                    entry.Families[route.Family.FamilyKey] = route.Family;
                }
            }
        }

        double spacing = GetParallelCorridorSpacing(options);
        Dictionary<ParallelEdgeKey, ParallelSharedEdge> plan = [];
        foreach ((ParallelEdgeKey key, var entry) in edges)
        {
            if (entry.Families.Count < 2)
            {
                continue;
            }

            // Visible lane = distinct stroke color. Same-color families (a line and
            // its branch, e.g. 7号线 / 7号线支线) collapse onto one lane so they
            // overlap as a single visible line instead of drawing a doubled stripe.
            // Only genuinely different-colored lines get separate parallel lanes.
            List<IGrouping<string, DisplayLineFamily>> lanes = entry.Families.Values
                .GroupBy(family => string.IsNullOrWhiteSpace(family.Color) ? family.FamilyKey : family.Color, StringComparer.OrdinalIgnoreCase)
                .OrderBy(group => group.Min(family => ExtractLineNumber(family.PrimaryLine.Name) ?? int.MaxValue))
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .ToList();

            if (lanes.Count < 2)
            {
                continue;
            }

            SvgPoint normal = GetSegmentNormal(entry.A, entry.B);
            Dictionary<string, double> offsets = new(StringComparer.Ordinal);
            double center = (lanes.Count - 1) / 2.0;
            for (int i = 0; i < lanes.Count; i++)
            {
                double laneOffset = (i - center) * spacing;
                foreach (DisplayLineFamily family in lanes[i])
                {
                    offsets[family.FamilyKey] = laneOffset;
                }
            }

            plan[key] = new ParallelSharedEdge(normal, offsets);
        }

        return plan;
    }

    /// <summary>
    /// Offsets one route's polyline for parallel rendering. Each vertex is displaced
    /// by the average of its incident SHARED edges' offset vectors, so shared stations
    /// sit on the parallel bundle while the ramp onto the bundle falls on the adjacent
    /// non-shared segment. Returns the input unchanged when the route touches no shared
    /// edge.
    /// </summary>
    private static List<SvgPoint> OffsetRouteForParallelCorridor(
        IReadOnlyList<SvgPoint> points,
        string familyKey,
        Dictionary<ParallelEdgeKey, ParallelSharedEdge> plan)
    {
        if (points.Count < 2 || plan.Count == 0)
        {
            return points.ToList();
        }

        // Per-edge offset vector for this family (zero vector when not shared).
        SvgPoint[] edgeVectors = new SvgPoint[points.Count - 1];
        bool[] edgeShared = new bool[points.Count - 1];
        bool touchesShared = false;
        for (int i = 1; i < points.Count; i++)
        {
            ParallelEdgeKey key = CreateParallelEdgeKey(points[i - 1], points[i]);
            if (plan.TryGetValue(key, out ParallelSharedEdge? shared) && shared.OffsetByFamily.TryGetValue(familyKey, out double offset))
            {
                edgeVectors[i - 1] = new SvgPoint(shared.Normal.X * offset, shared.Normal.Y * offset);
                edgeShared[i - 1] = true;
                touchesShared = true;
            }
        }

        if (!touchesShared)
        {
            return points.ToList();
        }

        List<SvgPoint> result = new(points.Count);
        for (int i = 0; i < points.Count; i++)
        {
            double sumX = 0;
            double sumY = 0;
            int count = 0;
            if (i - 1 >= 0 && edgeShared[i - 1])
            {
                sumX += edgeVectors[i - 1].X;
                sumY += edgeVectors[i - 1].Y;
                count++;
            }

            if (i < edgeVectors.Length && edgeShared[i])
            {
                sumX += edgeVectors[i].X;
                sumY += edgeVectors[i].Y;
                count++;
            }

            if (count == 0)
            {
                result.Add(points[i]);
            }
            else
            {
                result.Add(new SvgPoint(points[i].X + sumX / count, points[i].Y + sumY / count));
            }
        }

        return result;
    }
}
