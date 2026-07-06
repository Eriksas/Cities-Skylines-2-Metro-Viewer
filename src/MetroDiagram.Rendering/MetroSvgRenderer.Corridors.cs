using System.Globalization;
using System.Text;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    private static CorridorRenderPlan BuildGeographicCorridorRenderPlan(
        List<RenderRoute> renderRoutes,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry,
        SvgRenderOptions options)
    {
        List<CorridorSegmentFragment> fragments = CreateCorridorSegmentFragments(renderRoutes, stationsById, geometry, options);
        Dictionary<int, HashSet<string>> sharedFamiliesByFragment = [];
        List<SharedCorridorRun> sharedRuns = [];
        HashSet<int> hiddenSharedFragments = [];
        HashSet<int> tooManyFamilyFragments = [];

        if (options.EnableSharedCorridorCompositeStroke && fragments.Count > 1)
        {
            List<CorridorMatch> matches = FindSharedCorridorMatches(fragments, options);
            sharedFamiliesByFragment = GetSharedFamiliesByFragment(fragments, matches);
            hiddenSharedFragments = sharedFamiliesByFragment
                .Where(pair => pair.Value.Count == 2)
                .Select(pair => pair.Key)
                .ToHashSet();
            tooManyFamilyFragments = sharedFamiliesByFragment
                .Where(pair => pair.Value.Count > 2)
                .Select(pair => pair.Key)
                .ToHashSet();
            sharedRuns = BuildSharedCorridorRuns(fragments, sharedFamiliesByFragment, options);
        }

        Dictionary<int, List<CorridorSegmentFragment>> fragmentsByRoute = fragments
            .GroupBy(fragment => fragment.RouteIndex)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(fragment => fragment.PolylineIndex)
                .ThenBy(fragment => fragment.SegmentIndex)
                .ToList());

        List<CorridorDrawCommand> normal = [];
        List<CorridorDrawCommand> sharedBase = [];
        List<CorridorDrawCommand> sharedInner = [];
        List<CorridorDrawCommand> expressDecorations = [];

        foreach (SharedCorridorRun run in sharedRuns)
        {
            RenderRoute renderRoute = renderRoutes[run.RouteIndex];
            bool expressConflict = options.EnableExpressCenterStripe
                && (HasExpressServiceVariant(run.FamilyA) || HasExpressServiceVariant(run.FamilyB));
            string sharedAttributes = $" data-shared-corridor=\"true\" data-shared-corridor-run-id=\"{Escape(run.RunId)}\" data-shared-corridor-family-a=\"{Escape(run.FamilyA.FamilyKey)}\" data-shared-corridor-family-b=\"{Escape(run.FamilyB.FamilyKey)}\" data-shared-corridor-style=\"shanghai-like-continuous\" data-shared-corridor-point-count=\"{run.Points.Count}\" data-shared-corridor-fragment-count=\"{run.FragmentCount}\"";
            if (expressConflict)
            {
                sharedAttributes += " data-express-marker-skipped=\"shared-corridor-style-conflict\"";
            }

            sharedBase.Add(new CorridorDrawCommand(
                renderRoute,
                run.Points,
                CorridorDrawLayer.SharedBase,
                run.FamilyA.Color,
                GetSharedCorridorStrokeWidth(options),
                sharedAttributes + " data-shared-corridor-layer=\"corridor-base\""));
            sharedInner.Add(new CorridorDrawCommand(
                renderRoute,
                run.Points,
                CorridorDrawLayer.SharedInner,
                run.FamilyB.Color,
                GetSharedCorridorInnerStrokeWidth(options),
                sharedAttributes + " data-shared-corridor-layer=\"inner-band\""));
        }

        for (int routeIndex = 0; routeIndex < renderRoutes.Count; routeIndex++)
        {
            RenderRoute renderRoute = renderRoutes[routeIndex];
            if (renderRoute.RoutePointSet.Source != "pathPoints"
                || !fragmentsByRoute.TryGetValue(routeIndex, out List<CorridorSegmentFragment>? routeFragments))
            {
                AddWholeRoutePolylines(renderRoute, normal, expressDecorations, options);
                continue;
            }

            List<SvgPoint> normalPoints = [];
            int normalPartIndex = -1;
            foreach (CorridorSegmentFragment fragment in routeFragments)
            {
                if (hiddenSharedFragments.Contains(fragment.Index))
                {
                    FlushNormalRun();
                    continue;
                }

                if (tooManyFamilyFragments.Contains(fragment.Index))
                {
                    FlushNormalRun();
                    List<SvgPoint> fallbackPoints = [fragment.Start, fragment.End];
                    CorridorDrawCommand fallback = new(
                        renderRoute,
                        fallbackPoints,
                        CorridorDrawLayer.NormalBase,
                        renderRoute.Family.Color,
                        GetNormalRouteStrokeWidth(options),
                        HasExpressServiceVariant(renderRoute.Family)
                            ? " data-shared-corridor-skipped=\"too-many-families\" data-express-marker-skipped=\"shared-corridor-style-conflict\""
                            : " data-shared-corridor-skipped=\"too-many-families\"",
                        fragment.PolylineIndex,
                        renderRoute.RoutePointSet.Polylines.Count);
                    normal.Add(fallback);
                    AddExpressDecorationIfNeeded(fallback, expressDecorations, options);
                    continue;
                }

                AddNormalSegment(fragment);
            }

            FlushNormalRun();

            void AddNormalSegment(CorridorSegmentFragment fragment)
            {
                if (normalPoints.Count == 0)
                {
                    normalPartIndex = fragment.PolylineIndex;
                    normalPoints.Add(fragment.Start);
                    normalPoints.Add(fragment.End);
                    return;
                }

                if (normalPartIndex == fragment.PolylineIndex && Distance(normalPoints[^1], fragment.Start) <= 0.001)
                {
                    AddPointIfNotDuplicate(normalPoints, fragment.End);
                    return;
                }

                FlushNormalRun();
                normalPoints.Add(fragment.Start);
                normalPoints.Add(fragment.End);
            }

            void FlushNormalRun()
            {
                if (normalPoints.Count >= 2)
                {
                    CorridorDrawCommand command = new(
                        renderRoute,
                        normalPoints.ToList(),
                        CorridorDrawLayer.NormalBase,
                        renderRoute.Family.Color,
                        GetNormalRouteStrokeWidth(options),
                        string.Empty,
                        normalPartIndex,
                        renderRoute.RoutePointSet.Polylines.Count);
                    normal.Add(command);
                    AddExpressDecorationIfNeeded(command, expressDecorations, options);
                }

                normalPoints.Clear();
                normalPartIndex = -1;
            }
        }

        return new CorridorRenderPlan(normal, sharedBase, sharedInner, expressDecorations);
    }

    private static void AddWholeRoutePolylines(
        RenderRoute renderRoute,
        List<CorridorDrawCommand> normal,
        List<CorridorDrawCommand> expressDecorations,
        SvgRenderOptions options)
    {
        for (int index = 0; index < renderRoute.RoutePointSet.Polylines.Count; index++)
        {
            RoutePolyline polyline = renderRoute.RoutePointSet.Polylines[index];
            if (polyline.Points.Count < 2)
            {
                continue;
            }

            CorridorDrawCommand command = new(
                renderRoute,
                polyline.Points,
                CorridorDrawLayer.NormalBase,
                renderRoute.Family.Color,
                GetNormalRouteStrokeWidth(options),
                string.Empty,
                index,
                renderRoute.RoutePointSet.Polylines.Count);
            normal.Add(command);
            AddExpressDecorationIfNeeded(command, expressDecorations, options);
        }
    }

    private static void AddExpressDecorationIfNeeded(
        CorridorDrawCommand baseCommand,
        List<CorridorDrawCommand> expressDecorations,
        SvgRenderOptions options)
    {
        if (!ShouldRenderExpressCenterStripe(options, baseCommand.RenderRoute.Family))
        {
            return;
        }

        if (baseCommand.ExtraAttributes.Contains("data-shared-corridor-skipped=", StringComparison.Ordinal)
            || baseCommand.ExtraAttributes.Contains("data-shared-corridor=\"true\"", StringComparison.Ordinal))
        {
            return;
        }

        expressDecorations.Add(baseCommand with
        {
            Layer = CorridorDrawLayer.ExpressDecoration,
            Stroke = "#ffffff",
            StrokeWidth = GetExpressCenterStripeWidth(options),
            ExtraAttributes = baseCommand.ExtraAttributes
                + $" data-express-marker=\"white-center-stripe\" data-express-family=\"{Escape(baseCommand.RenderRoute.Family.FamilyKey)}\""
                + (IsSchematicV2FamilyLayout(options.LayoutMode) ? " data-schematic-v2-express-marker=\"white-center-stripe\"" : string.Empty)
        });
    }

    private static void AppendCorridorRenderPlan(StringBuilder svg, CorridorRenderPlan plan, SvgRenderOptions options)
    {
        foreach (CorridorDrawCommand command in plan.NormalBase)
        {
            AppendCorridorDrawCommand(svg, command, options);
        }

        foreach (CorridorDrawCommand command in plan.SharedBase)
        {
            AppendCorridorDrawCommand(svg, command, options);
        }

        foreach (CorridorDrawCommand command in plan.SharedInner)
        {
            AppendCorridorDrawCommand(svg, command, options);
        }

        foreach (CorridorDrawCommand command in plan.ExpressDecorations)
        {
            AppendCorridorDrawCommand(svg, command, options);
        }
    }

    private static void AppendCorridorDrawCommand(
        StringBuilder svg,
        CorridorDrawCommand command,
        SvgRenderOptions options)
    {
        RenderRoute renderRoute = command.RenderRoute;
        DisplayLineFamily family = renderRoute.Family;
        MetroLine line = renderRoute.Line;
        RoutePointSet routePointSet = renderRoute.RoutePointSet;
        string pointList = string.Join(" ", command.Points.Select(point => $"{Format(point.X)},{Format(point.Y)}"));
        string pathPointAttributes = routePointSet.Source == "pathPoints"
            ? $" data-path-point-count=\"{routePointSet.OriginalPathPointCount}\" data-cleaned-path-point-count=\"{routePointSet.CleanedPathPointCount}\" data-path-reduction-ratio=\"{Format(routePointSet.ReductionRatio)}\" data-max-path-segment-length=\"{Format(routePointSet.MaxPathSegmentLength)}\" data-suspicious-jump-count=\"{routePointSet.SuspiciousJumpCount}\" data-path-simplification-tolerance=\"{Format(routePointSet.EffectiveSimplificationTolerance)}\""
            : string.Empty;
        string routePartAttributes = command.RoutePartCount > 1
            ? $" data-route-part-index=\"{command.RoutePartIndex}\" data-route-part-count=\"{command.RoutePartCount}\""
            : string.Empty;
        string familyAttributes = $" data-display-family-key=\"{Escape(family.FamilyKey)}\" data-display-family-member-count=\"{family.Variants.Count}\" data-display-family-primary-line-id=\"{Escape(family.PrimaryLine.Id)}\" data-display-family-merged=\"{(family.Variants.Count > 1 ? "true" : "false")}\"";
        string colorMismatchAttribute = family.HasColorMismatch
            ? " data-display-family-color-mismatch=\"true\""
            : string.Empty;
        string layerAttributes = $" data-corridor-run-layer=\"{GetCorridorDrawLayerName(command.Layer)}\"";
        svg.AppendLine($"""<polyline class="route" data-line-id="{Escape(line.Id)}" data-route-source="{routePointSet.Source}"{familyAttributes}{colorMismatchAttribute}{pathPointAttributes}{routePartAttributes}{layerAttributes}{command.ExtraAttributes} points="{pointList}" stroke="{Escape(command.Stroke)}" style="stroke-width: {Format(command.StrokeWidth)};" />""");
    }

    private static double GetNormalRouteStrokeWidth(SvgRenderOptions options)
    {
        return SvgVisualStyle.From(options).BaseRouteWidth;
    }

    private static double GetSharedCorridorStrokeWidth(SvgRenderOptions options)
    {
        return SvgVisualStyle.From(options).SharedCorridorOuterWidth;
    }

    private static double GetSharedCorridorInnerStrokeWidth(SvgRenderOptions options)
    {
        return SvgVisualStyle.From(options).SharedCorridorInnerWidth;
    }

    private static double GetExpressCenterStripeWidth(SvgRenderOptions options)
    {
        return SvgVisualStyle.From(options).ExpressStripeWidth;
    }

    private static bool ShouldRenderExpressCenterStripe(SvgRenderOptions options, DisplayLineFamily family)
    {
        return HasExpressServiceVariant(family)
            && (options.EnableExpressCenterStripe || IsSchematicV2FamilyLayout(options.LayoutMode));
    }

    private static string GetCorridorDrawLayerName(CorridorDrawLayer layer)
    {
        return layer switch
        {
            CorridorDrawLayer.NormalBase => "normal-base",
            CorridorDrawLayer.SharedBase => "shared-corridor-base",
            CorridorDrawLayer.SharedInner => "shared-corridor-inner",
            CorridorDrawLayer.ExpressDecoration => "express-decoration",
            _ => "unknown"
        };
    }

    private static void AppendStandardRoutePolyline(
        StringBuilder svg,
        string commonAttributes,
        DisplayLineFamily family,
        SvgRenderOptions options,
        string routeStyle = "")
    {
        string styleAttribute = string.IsNullOrWhiteSpace(routeStyle)
            ? string.Empty
            : $" style=\"{Escape(routeStyle)}\"";
        svg.AppendLine($"""<polyline {commonAttributes} stroke="{Escape(family.Color)}"{styleAttribute} />""");
        if (!ShouldRenderExpressCenterStripe(options, family))
        {
            return;
        }

        double stripeWidth = GetExpressCenterStripeWidth(options);
        string stripeStyle = $"stroke-width: {Format(stripeWidth)};";
        if (!string.IsNullOrWhiteSpace(routeStyle))
        {
            stripeStyle += $" {routeStyle}";
        }

        string schematicV2MarkerAttribute = IsSchematicV2FamilyLayout(options.LayoutMode)
            ? " data-schematic-v2-express-marker=\"white-center-stripe\""
            : string.Empty;
        string stripeAttributes = commonAttributes.Replace("class=\"route\"", "class=\"express-decoration\"", StringComparison.Ordinal);
        svg.AppendLine($"""<polyline {stripeAttributes} data-express-marker="white-center-stripe" data-express-family="{Escape(family.FamilyKey)}"{schematicV2MarkerAttribute} stroke="#ffffff" style="{Escape(stripeStyle)}" />""");
    }

    private static void AppendSharedCorridorRoutePolylines(
        StringBuilder svg,
        string commonAttributes,
        SharedCorridorStroke sharedStroke,
        SvgRenderOptions options)
    {
        string sharedAttributes = $" data-shared-corridor=\"true\" data-shared-corridor-run-id=\"{Escape(sharedStroke.RunId)}\" data-shared-corridor-family-a=\"{Escape(sharedStroke.FamilyAKey)}\" data-shared-corridor-family-b=\"{Escape(sharedStroke.FamilyBKey)}\" data-shared-corridor-style=\"shanghai-like\" data-shared-corridor-point-count=\"{sharedStroke.PointCount}\"";
        double corridorWidth = GetSharedCorridorStrokeWidth(options);
        double bandWidth = GetSharedCorridorInnerStrokeWidth(options);

        svg.AppendLine($"""<polyline {commonAttributes}{sharedAttributes} data-shared-corridor-layer="corridor-base" stroke="{Escape(sharedStroke.OuterColor)}" style="stroke-width: {Format(corridorWidth)};" />""");
        svg.AppendLine($"""<polyline {commonAttributes}{sharedAttributes} data-shared-corridor-layer="inner-band" stroke="{Escape(sharedStroke.InnerColor)}" style="stroke-width: {Format(bandWidth)};" />""");
    }

    private static List<RenderRoute> ApplyParallelCorridorOffset(
        List<RenderRoute> renderRoutes,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry,
        SvgRenderOptions options)
    {
        List<CorridorSegmentFragment> fragments = CreateCorridorSegmentFragments(renderRoutes, stationsById, geometry, options);

        if (fragments.Count < 2)
        {
            return renderRoutes;
        }

        Dictionary<int, CorridorAssignment> assignments = DetectCorridors(fragments, options);
        if (assignments.Count == 0)
        {
            return SegmentizePathPointRoutes(renderRoutes, fragments, []);
        }

        return SegmentizePathPointRoutes(renderRoutes, fragments, assignments);
    }

    private static List<RenderRoute> ApplySharedCorridorCompositeStroke(
        List<RenderRoute> renderRoutes,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry,
        SvgRenderOptions options)
    {
        List<CorridorSegmentFragment> fragments = CreateCorridorSegmentFragments(renderRoutes, stationsById, geometry, options);
        if (fragments.Count < 2)
        {
            return renderRoutes;
        }

        List<CorridorMatch> matches = FindSharedCorridorMatches(fragments, options);
        if (matches.Count == 0)
        {
            return renderRoutes;
        }

        Dictionary<int, HashSet<string>> sharedFamiliesByFragment = GetSharedFamiliesByFragment(fragments, matches);
        HashSet<int> tooManyFamilyFragments = sharedFamiliesByFragment
            .Where(pair => pair.Value.Count > 2)
            .Select(pair => pair.Key)
            .ToHashSet();
        HashSet<int> twoFamilySharedFragments = sharedFamiliesByFragment
            .Where(pair => pair.Value.Count == 2)
            .Select(pair => pair.Key)
            .ToHashSet();
        List<SharedCorridorRun> runs = BuildSharedCorridorRuns(fragments, sharedFamiliesByFragment, options);
        if (runs.Count == 0 && tooManyFamilyFragments.Count == 0)
        {
            return renderRoutes;
        }

        return BuildSharedCorridorRenderRoutes(renderRoutes, fragments, runs, twoFamilySharedFragments, tooManyFamilyFragments);
    }

    private static List<CorridorSegmentFragment> CreateCorridorSegmentFragments(
        List<RenderRoute> renderRoutes,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry,
        SvgRenderOptions options)
    {
        List<CorridorSegmentFragment> fragments = [];
        double minEligibleLength = Math.Max(20, options.LineWidth * 1.5);
        for (int routeIndex = 0; routeIndex < renderRoutes.Count; routeIndex++)
        {
            RenderRoute renderRoute = renderRoutes[routeIndex];
            if (renderRoute.RoutePointSet.Source != "pathPoints")
            {
                continue;
            }

            string lineId = renderRoute.Line.Id ?? $"line_{routeIndex + 1}";
            List<SvgPoint> stationPoints = GetLineStationPoints(renderRoute.Line, stationsById, geometry);
            for (int polylineIndex = 0; polylineIndex < renderRoute.RoutePointSet.Polylines.Count; polylineIndex++)
            {
                List<SvgPoint> points = renderRoute.RoutePointSet.Polylines[polylineIndex].Points;
                for (int segmentIndex = 1; segmentIndex < points.Count; segmentIndex++)
                {
                    SvgPoint start = points[segmentIndex - 1];
                    SvgPoint end = points[segmentIndex];
                    double length = Distance(start, end);
                    if (length <= 0.001)
                    {
                        continue;
                    }

                    SvgPoint direction = new((end.X - start.X) / length, (end.Y - start.Y) / length);
                    SvgPoint normal = new(-direction.Y, direction.X);
                    fragments.Add(new CorridorSegmentFragment(
                        fragments.Count,
                        routeIndex,
                        polylineIndex,
                        segmentIndex - 1,
                        lineId,
                        renderRoute.Family,
                        renderRoute.Family.FamilyKey,
                        renderRoute.Family.DisplayName,
                        renderRoute.Family.Color,
                        start,
                        end,
                        direction,
                        normal,
                        length,
                        length >= minEligibleLength,
                        stationPoints));
                }
            }
        }

        return fragments;
    }

    private static Dictionary<int, CorridorAssignment> DetectCorridors(
        List<CorridorSegmentFragment> fragments,
        SvgRenderOptions options)
    {
        Dictionary<int, CorridorAssignment> assignments = [];
        int corridorNumber = 1;
        foreach (List<CorridorSegmentFragment> group in FindSharedCorridorGroups(fragments, options)
            .Where(group => group.Select(fragment => fragment.LineId).Distinct(StringComparer.Ordinal).Count() > 1)
            .OrderBy(group => group.Min(fragment => fragment.RouteIndex))
            .ThenBy(group => group.Min(fragment => fragment.PolylineIndex))
            .ThenBy(group => group.Min(fragment => fragment.SegmentIndex)))
        {
            List<string> lineOrder = group
                .Select(fragment => new { fragment.LineId, fragment.RouteIndex })
                .GroupBy(item => item.LineId, StringComparer.Ordinal)
                .Select(grouping => new { LineId = grouping.Key, RouteIndex = grouping.Min(item => item.RouteIndex) })
                .OrderBy(item => item.RouteIndex)
                .ThenBy(item => item.LineId, StringComparer.Ordinal)
                .Select(item => item.LineId)
                .ToList();

            if (lineOrder.Count < 2)
            {
                continue;
            }

            string corridorId = $"corridor-{corridorNumber++}";
            double spacing = options.LineWidth + 4;
            SvgPoint baseDirection = group
                .OrderBy(fragment => fragment.RouteIndex)
                .ThenBy(fragment => fragment.PolylineIndex)
                .ThenBy(fragment => fragment.SegmentIndex)
                .First()
                .Direction;

            foreach (CorridorSegmentFragment fragment in group)
            {
                int lineIndex = lineOrder.IndexOf(fragment.LineId);
                if (lineIndex < 0)
                {
                    continue;
                }

                double offsetIndex = lineIndex - (lineOrder.Count - 1) / 2.0;
                assignments[fragment.Index] = new CorridorAssignment(
                    corridorId,
                    lineOrder.Count,
                    offsetIndex,
                    offsetIndex * spacing,
                    baseDirection);
            }
        }

        return assignments;
    }

    private static List<List<CorridorSegmentFragment>> FindSharedCorridorGroups(
        List<CorridorSegmentFragment> fragments,
        SvgRenderOptions options)
    {
        double distanceThreshold = options.LineWidth * 1.5 + 6;
        double cellSize = Math.Max(48, distanceThreshold * 3);
        int[] parent = Enumerable.Range(0, fragments.Count).ToArray();

        int Find(int value)
        {
            while (parent[value] != value)
            {
                parent[value] = parent[parent[value]];
                value = parent[value];
            }

            return value;
        }

        void Union(int left, int right)
        {
            int leftRoot = Find(left);
            int rightRoot = Find(right);
            if (leftRoot != rightRoot)
            {
                parent[rightRoot] = leftRoot;
            }
        }

        Dictionary<(int X, int Y), List<int>> grid = [];
        HashSet<(int A, int B)> checkedPairs = [];
        for (int index = 0; index < fragments.Count; index++)
        {
            CorridorSegmentFragment fragment = fragments[index];
            if (!fragment.IsEligible)
            {
                continue;
            }

            (int minX, int maxX, int minY, int maxY) = GetGridBounds(fragment, cellSize, distanceThreshold);
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    (int X, int Y) cell = (x, y);
                    if (!grid.TryGetValue(cell, out List<int>? candidates))
                    {
                        candidates = [];
                        grid[cell] = candidates;
                    }

                    foreach (int candidateIndex in candidates)
                    {
                        int a = Math.Min(candidateIndex, index);
                        int b = Math.Max(candidateIndex, index);
                        if (!checkedPairs.Add((a, b)))
                        {
                            continue;
                        }

                        if (AreSharedCorridorCandidates(fragments[a], fragments[b], distanceThreshold))
                        {
                            Union(a, b);
                        }
                    }

                    candidates.Add(index);
                }
            }
        }

        Dictionary<int, List<CorridorSegmentFragment>> groups = [];
        for (int index = 0; index < fragments.Count; index++)
        {
            if (!fragments[index].IsEligible)
            {
                continue;
            }

            int root = Find(index);
            if (!groups.TryGetValue(root, out List<CorridorSegmentFragment>? group))
            {
                group = [];
                groups[root] = group;
            }

            group.Add(fragments[index]);
        }

        return groups.Values
            .Where(group => group.Select(fragment => fragment.FamilyKey).Distinct(StringComparer.Ordinal).Count() > 1)
            .ToList();
    }

    private static List<CorridorMatch> FindSharedCorridorMatches(
        List<CorridorSegmentFragment> fragments,
        SvgRenderOptions options)
    {
        List<CorridorMatch> matches = [];
        double distanceThreshold = options.LineWidth * 1.5 + 6;
        double cellSize = Math.Max(48, distanceThreshold * 3);
        Dictionary<(int X, int Y), List<int>> grid = [];
        HashSet<(int A, int B)> checkedPairs = [];

        for (int index = 0; index < fragments.Count; index++)
        {
            CorridorSegmentFragment fragment = fragments[index];
            if (!fragment.IsEligible)
            {
                continue;
            }

            (int minX, int maxX, int minY, int maxY) = GetGridBounds(fragment, cellSize, distanceThreshold);
            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    (int X, int Y) cell = (x, y);
                    if (!grid.TryGetValue(cell, out List<int>? candidates))
                    {
                        candidates = [];
                        grid[cell] = candidates;
                    }

                    foreach (int candidateIndex in candidates)
                    {
                        int a = Math.Min(candidateIndex, index);
                        int b = Math.Max(candidateIndex, index);
                        if (!checkedPairs.Add((a, b)))
                        {
                            continue;
                        }

                        if (string.Equals(fragments[a].FamilyKey, fragments[b].FamilyKey, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (AreSharedCorridorCandidates(fragments[a], fragments[b], distanceThreshold))
                        {
                            matches.Add(new CorridorMatch(a, b));
                        }
                    }

                    candidates.Add(index);
                }
            }
        }

        return matches;
    }

    private static Dictionary<int, HashSet<string>> GetSharedFamiliesByFragment(
        List<CorridorSegmentFragment> fragments,
        List<CorridorMatch> matches)
    {
        Dictionary<int, HashSet<string>> familiesByFragment = [];
        foreach (CorridorMatch match in matches)
        {
            CorridorSegmentFragment left = fragments[match.LeftFragmentIndex];
            CorridorSegmentFragment right = fragments[match.RightFragmentIndex];
            AddSharedFamily(left.Index, left.FamilyKey);
            AddSharedFamily(left.Index, right.FamilyKey);
            AddSharedFamily(right.Index, right.FamilyKey);
            AddSharedFamily(right.Index, left.FamilyKey);
        }

        return familiesByFragment;

        void AddSharedFamily(int fragmentIndex, string familyKey)
        {
            if (!familiesByFragment.TryGetValue(fragmentIndex, out HashSet<string>? families))
            {
                families = new HashSet<string>(StringComparer.Ordinal);
                familiesByFragment[fragmentIndex] = families;
            }

            families.Add(familyKey);
        }
    }

    private static List<SharedCorridorRun> BuildSharedCorridorRuns(
        List<CorridorSegmentFragment> fragments,
        Dictionary<int, HashSet<string>> sharedFamiliesByFragment,
        SvgRenderOptions options)
    {
        Dictionary<string, CorridorFamilyInfo> familyInfoByKey = fragments
            .GroupBy(fragment => fragment.FamilyKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => new CorridorFamilyInfo(
                    group.Key,
                    group.First().Family,
                    group.First().FamilyDisplayName,
                    group.First().FamilyColor,
                    group.Min(fragment => fragment.RouteIndex)),
                StringComparer.Ordinal);

        Dictionary<SharedCorridorPairKey, List<CorridorSegmentFragment>> outerFragmentsByPair = [];
        foreach ((int fragmentIndex, HashSet<string> familySet) in sharedFamiliesByFragment)
        {
            if (familySet.Count != 2)
            {
                continue;
            }

            List<CorridorFamilyInfo> orderedFamilies = familySet
                .Where(familyInfoByKey.ContainsKey)
                .Select(familyKey => familyInfoByKey[familyKey])
                .OrderBy(info => info.DisplayName, StringComparer.CurrentCulture)
                .ThenBy(info => info.RouteIndex)
                .ThenBy(info => info.FamilyKey, StringComparer.Ordinal)
                .ToList();

            if (orderedFamilies.Count != 2)
            {
                continue;
            }

            CorridorFamilyInfo outer = orderedFamilies[0];
            CorridorFamilyInfo inner = orderedFamilies[1];
            CorridorSegmentFragment fragment = fragments[fragmentIndex];
            if (!string.Equals(fragment.FamilyKey, outer.FamilyKey, StringComparison.Ordinal))
            {
                continue;
            }

            SharedCorridorPairKey pairKey = new(outer.FamilyKey, inner.FamilyKey);
            if (!outerFragmentsByPair.TryGetValue(pairKey, out List<CorridorSegmentFragment>? pairFragments))
            {
                pairFragments = [];
                outerFragmentsByPair[pairKey] = pairFragments;
            }

            pairFragments.Add(fragment);
        }

        List<SharedCorridorRun> runs = [];
        int runNumber = 1;
        foreach ((SharedCorridorPairKey pairKey, List<CorridorSegmentFragment> pairFragments) in outerFragmentsByPair
            .OrderBy(pair => familyInfoByKey[pair.Key.OuterFamilyKey].RouteIndex)
            .ThenBy(pair => familyInfoByKey[pair.Key.InnerFamilyKey].RouteIndex))
        {
            CorridorFamilyInfo outer = familyInfoByKey[pairKey.OuterFamilyKey];
            CorridorFamilyInfo inner = familyInfoByKey[pairKey.InnerFamilyKey];
            foreach (List<CorridorSegmentFragment> runFragments in SplitContiguousSharedRuns(pairFragments))
            {
                List<SvgPoint> points = BuildRunPoints(runFragments);
                if (points.Count < 2)
                {
                    continue;
                }

                runs.Add(new SharedCorridorRun(
                    $"shared-corridor-run-{runNumber++}",
                    runFragments.Min(fragment => fragment.RouteIndex),
                    outer.Family,
                    inner.Family,
                    outer.Color,
                    inner.Color,
                    points,
                    runFragments.Count,
                    runFragments.Select(fragment => fragment.Index).ToHashSet()));
            }
        }

        return MergeAdjacentSharedCorridorRuns(runs, options);
    }

    private static List<SharedCorridorRun> MergeAdjacentSharedCorridorRuns(List<SharedCorridorRun> runs, SvgRenderOptions options)
    {
        if (runs.Count <= 1)
        {
            return runs;
        }

        double endpointTolerance = Math.Max(18, GetNormalRouteStrokeWidth(options) * 2.6);
        List<SharedCorridorRun> mergedRuns = [];
        foreach (IGrouping<(string FamilyA, string FamilyB), SharedCorridorRun> group in runs
            .GroupBy(run => (run.FamilyA.FamilyKey, run.FamilyB.FamilyKey))
            .OrderBy(group => group.Min(run => run.FragmentIndices.DefaultIfEmpty(0).Min())))
        {
            SharedCorridorRun? current = null;
            foreach (SharedCorridorRun run in group
                .OrderBy(run => run.FragmentIndices.DefaultIfEmpty(0).Min()))
            {
                if (current is null)
                {
                    current = run;
                    continue;
                }

                if (TryMergeSharedCorridorRuns(current, run, endpointTolerance, out SharedCorridorRun merged))
                {
                    current = merged;
                }
                else
                {
                    mergedRuns.Add(current);
                    current = run;
                }
            }

            if (current is not null)
            {
                mergedRuns.Add(current);
            }
        }

        return mergedRuns
            .OrderBy(run => run.RouteIndex)
            .ThenBy(run => run.FragmentIndices.DefaultIfEmpty(0).Min())
            .ToList();
    }

    private static bool TryMergeSharedCorridorRuns(
        SharedCorridorRun current,
        SharedCorridorRun next,
        double endpointTolerance,
        out SharedCorridorRun merged)
    {
        merged = current;
        if (current.Points.Count < 2 || next.Points.Count < 2)
        {
            return false;
        }

        List<(double Distance, int Mode)> candidates =
        [
            (Distance(current.Points[^1], next.Points[0]), 0),
            (Distance(current.Points[^1], next.Points[^1]), 1),
            (Distance(current.Points[0], next.Points[^1]), 2),
            (Distance(current.Points[0], next.Points[0]), 3)
        ];
        (double distance, int mode) = candidates.OrderBy(candidate => candidate.Distance).First();
        if (distance > endpointTolerance)
        {
            return false;
        }

        merged = MergeSharedCorridorRun(current, next, mode);
        return true;
    }

    private static SharedCorridorRun MergeSharedCorridorRun(SharedCorridorRun current, SharedCorridorRun next, int mode)
    {
        List<SvgPoint> points = mode switch
        {
            1 => AppendPoints(current.Points, next.Points.AsEnumerable().Reverse()),
            2 => AppendPoints(next.Points, current.Points),
            3 => AppendPoints(next.Points.AsEnumerable().Reverse(), current.Points),
            _ => AppendPoints(current.Points, next.Points)
        };

        HashSet<int> fragmentIndices = current.FragmentIndices.ToHashSet();
        foreach (int fragmentIndex in next.FragmentIndices)
        {
            fragmentIndices.Add(fragmentIndex);
        }

        return current with
        {
            Points = points,
            FragmentCount = current.FragmentCount + next.FragmentCount,
            FragmentIndices = fragmentIndices
        };

        static List<SvgPoint> AppendPoints(IEnumerable<SvgPoint> first, IEnumerable<SvgPoint> second)
        {
            List<SvgPoint> points = [];
            foreach (SvgPoint point in first)
            {
                AddPointIfNotDuplicate(points, point);
            }

            foreach (SvgPoint point in second)
            {
                AddPointIfNotDuplicate(points, point);
            }

            return points;
        }
    }

    private static IEnumerable<List<CorridorSegmentFragment>> SplitContiguousSharedRuns(List<CorridorSegmentFragment> fragments)
    {
        foreach (IGrouping<(int RouteIndex, int PolylineIndex), CorridorSegmentFragment> group in fragments
            .GroupBy(fragment => (fragment.RouteIndex, fragment.PolylineIndex))
            .OrderBy(group => group.Key.RouteIndex)
            .ThenBy(group => group.Key.PolylineIndex))
        {
            List<CorridorSegmentFragment> ordered = group
                .OrderBy(fragment => fragment.SegmentIndex)
                .ToList();
            List<CorridorSegmentFragment> current = [];
            foreach (CorridorSegmentFragment fragment in ordered)
            {
                if (current.Count == 0)
                {
                    current.Add(fragment);
                    continue;
                }

                CorridorSegmentFragment previous = current[^1];
                bool contiguous = fragment.SegmentIndex == previous.SegmentIndex + 1
                    && Distance(previous.End, fragment.Start) <= 0.001;
                if (!contiguous)
                {
                    yield return current;
                    current = [];
                }

                current.Add(fragment);
            }

            if (current.Count > 0)
            {
                yield return current;
            }
        }
    }

    private static List<SvgPoint> BuildRunPoints(List<CorridorSegmentFragment> runFragments)
    {
        List<SvgPoint> points = [];
        foreach (CorridorSegmentFragment fragment in runFragments
            .OrderBy(fragment => fragment.RouteIndex)
            .ThenBy(fragment => fragment.PolylineIndex)
            .ThenBy(fragment => fragment.SegmentIndex))
        {
            if (points.Count == 0)
            {
                points.Add(fragment.Start);
            }

            AddPointIfNotDuplicate(points, fragment.End);
        }

        return points;
    }

    private static List<RenderRoute> SegmentizePathPointRoutes(
        List<RenderRoute> renderRoutes,
        List<CorridorSegmentFragment> fragments,
        Dictionary<int, CorridorAssignment> assignments)
    {
        Dictionary<int, List<CorridorSegmentFragment>> fragmentsByRoute = fragments
            .GroupBy(fragment => fragment.RouteIndex)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(fragment => fragment.PolylineIndex)
                .ThenBy(fragment => fragment.SegmentIndex)
                .ToList());

        List<RenderRoute> adjustedRoutes = [];
        for (int routeIndex = 0; routeIndex < renderRoutes.Count; routeIndex++)
        {
            RenderRoute renderRoute = renderRoutes[routeIndex];
            if (renderRoute.RoutePointSet.Source != "pathPoints" || !fragmentsByRoute.TryGetValue(routeIndex, out List<CorridorSegmentFragment>? routeFragments))
            {
                adjustedRoutes.Add(renderRoute);
                continue;
            }

            List<RoutePolyline> polylines = [];
            foreach (CorridorSegmentFragment fragment in routeFragments)
            {
                if (assignments.TryGetValue(fragment.Index, out CorridorAssignment assignment))
                {
                    polylines.Add(CreateOffsetCorridorPolyline(fragment, assignment));
                }
                else
                {
                    polylines.Add(new RoutePolyline([fragment.Start, fragment.End]));
                }
            }

            adjustedRoutes.Add(renderRoute with
            {
                RoutePointSet = renderRoute.RoutePointSet with { Polylines = polylines }
            });
        }

        return adjustedRoutes;
    }

    private static List<RenderRoute> BuildSharedCorridorRenderRoutes(
        List<RenderRoute> renderRoutes,
        List<CorridorSegmentFragment> fragments,
        List<SharedCorridorRun> runs,
        HashSet<int> twoFamilySharedFragments,
        HashSet<int> tooManyFamilyFragments)
    {
        Dictionary<int, List<CorridorSegmentFragment>> fragmentsByRoute = fragments
            .GroupBy(fragment => fragment.RouteIndex)
            .ToDictionary(group => group.Key, group => group
                .OrderBy(fragment => fragment.PolylineIndex)
                .ThenBy(fragment => fragment.SegmentIndex)
                .ToList());
        Dictionary<int, SharedCorridorRun> runByStartFragment = runs
            .Where(run => run.FragmentIndices.Count > 0)
            .ToDictionary(run => run.FragmentIndices.Min(), run => run);
        HashSet<int> hiddenSharedFragmentIndices = twoFamilySharedFragments;

        List<RenderRoute> adjustedRoutes = [];
        for (int routeIndex = 0; routeIndex < renderRoutes.Count; routeIndex++)
        {
            RenderRoute renderRoute = renderRoutes[routeIndex];
            if (renderRoute.RoutePointSet.Source != "pathPoints" || !fragmentsByRoute.TryGetValue(routeIndex, out List<CorridorSegmentFragment>? routeFragments))
            {
                adjustedRoutes.Add(renderRoute);
                continue;
            }

            List<RoutePolyline> polylines = [];
            List<SvgPoint> normalPoints = [];
            foreach (CorridorSegmentFragment fragment in routeFragments)
            {
                if (runByStartFragment.TryGetValue(fragment.Index, out SharedCorridorRun? run))
                {
                    FlushNormalPolyline();
                    polylines.Add(new RoutePolyline(
                        run.Points,
                        SharedCorridorStroke: new SharedCorridorStroke(
                            run.RunId,
                            run.FamilyA.FamilyKey,
                            run.FamilyB.FamilyKey,
                            run.OuterColor,
                            run.InnerColor,
                            run.Points.Count)));
                }
                else if (hiddenSharedFragmentIndices.Contains(fragment.Index))
                {
                    FlushNormalPolyline();
                }
                else if (tooManyFamilyFragments.Contains(fragment.Index))
                {
                    FlushNormalPolyline();
                    polylines.Add(new RoutePolyline([fragment.Start, fragment.End], SharedCorridorSkipped: "too-many-families"));
                }
                else
                {
                    AddNormalSegment(fragment);
                }
            }

            FlushNormalPolyline();
            adjustedRoutes.Add(renderRoute with
            {
                RoutePointSet = renderRoute.RoutePointSet with { Polylines = polylines }
            });

            void AddNormalSegment(CorridorSegmentFragment fragment)
            {
                if (normalPoints.Count == 0)
                {
                    normalPoints.Add(fragment.Start);
                    normalPoints.Add(fragment.End);
                    return;
                }

                if (Distance(normalPoints[^1], fragment.Start) <= 0.001)
                {
                    AddPointIfNotDuplicate(normalPoints, fragment.End);
                    return;
                }

                FlushNormalPolyline();
                normalPoints.Add(fragment.Start);
                normalPoints.Add(fragment.End);
            }

            void FlushNormalPolyline()
            {
                if (normalPoints.Count >= 2)
                {
                    polylines.Add(new RoutePolyline(normalPoints.ToList()));
                }

                normalPoints.Clear();
            }
        }

        return adjustedRoutes;
    }

    private static RoutePolyline CreateOffsetCorridorPolyline(CorridorSegmentFragment fragment, CorridorAssignment assignment)
    {
        SvgPoint normal = fragment.Normal;
        if (Dot(fragment.Direction, assignment.BaseDirection) < 0)
        {
            normal = new SvgPoint(-normal.X, -normal.Y);
        }

        double startWeight = GetCorridorOffsetWeight(fragment.Start, fragment.StationPoints);
        double endWeight = GetCorridorOffsetWeight(fragment.End, fragment.StationPoints);
        SvgPoint offsetStart = OffsetPoint(fragment.Start, normal, assignment.OffsetPx * startWeight);
        SvgPoint offsetEnd = OffsetPoint(fragment.End, normal, assignment.OffsetPx * endWeight);

        if (!IsFinite(offsetStart) || !IsFinite(offsetEnd) || Distance(offsetStart, offsetEnd) <= 0.001)
        {
            return new RoutePolyline([fragment.Start, fragment.End]);
        }

        return new RoutePolyline(
            [offsetStart, offsetEnd],
            assignment.CorridorId,
            assignment.MemberCount,
            assignment.OffsetIndex,
            assignment.OffsetPx);
    }

    private static bool AreSharedCorridorCandidates(
        CorridorSegmentFragment left,
        CorridorSegmentFragment right,
        double distanceThreshold)
    {
        if (!left.IsEligible || !right.IsEligible || string.Equals(left.LineId, right.LineId, StringComparison.Ordinal))
        {
            return false;
        }

        double directionSimilarity = Math.Abs(Dot(left.Direction, right.Direction));
        if (directionSimilarity < Math.Cos(15 * Math.PI / 180))
        {
            return false;
        }

        double distance = SegmentDistance(left.Start, left.End, right.Start, right.End);
        if (distance > distanceThreshold)
        {
            return false;
        }

        double overlapLength = ProjectedOverlapLength(left, right);
        double overlapRatio = overlapLength / Math.Max(1, Math.Min(left.Length, right.Length));
        return overlapRatio > 0.35 || overlapLength > 40;
    }

    private static (int MinX, int MaxX, int MinY, int MaxY) GetGridBounds(
        CorridorSegmentFragment fragment,
        double cellSize,
        double expansion)
    {
        double minX = Math.Min(fragment.Start.X, fragment.End.X) - expansion;
        double maxX = Math.Max(fragment.Start.X, fragment.End.X) + expansion;
        double minY = Math.Min(fragment.Start.Y, fragment.End.Y) - expansion;
        double maxY = Math.Max(fragment.Start.Y, fragment.End.Y) + expansion;
        return (
            (int)Math.Floor(minX / cellSize),
            (int)Math.Floor(maxX / cellSize),
            (int)Math.Floor(minY / cellSize),
            (int)Math.Floor(maxY / cellSize));
    }

    private static double ProjectedOverlapLength(CorridorSegmentFragment left, CorridorSegmentFragment right)
    {
        double leftStart = Dot(left.Start, left.Direction);
        double leftEnd = Dot(left.End, left.Direction);
        double rightStart = Dot(right.Start, left.Direction);
        double rightEnd = Dot(right.End, left.Direction);
        double minLeft = Math.Min(leftStart, leftEnd);
        double maxLeft = Math.Max(leftStart, leftEnd);
        double minRight = Math.Min(rightStart, rightEnd);
        double maxRight = Math.Max(rightStart, rightEnd);
        return Math.Max(0, Math.Min(maxLeft, maxRight) - Math.Max(minLeft, minRight));
    }

    private static double SegmentDistance(SvgPoint aStart, SvgPoint aEnd, SvgPoint bStart, SvgPoint bEnd)
    {
        return Math.Min(
            Math.Min(PointToSegmentDistance(aStart, bStart, bEnd), PointToSegmentDistance(aEnd, bStart, bEnd)),
            Math.Min(PointToSegmentDistance(bStart, aStart, aEnd), PointToSegmentDistance(bEnd, aStart, aEnd)));
    }

    private static double PointToSegmentDistance(SvgPoint point, SvgPoint segmentStart, SvgPoint segmentEnd)
    {
        double dx = segmentEnd.X - segmentStart.X;
        double dy = segmentEnd.Y - segmentStart.Y;
        double lengthSquared = dx * dx + dy * dy;
        if (lengthSquared <= 0.000001)
        {
            return Distance(point, segmentStart);
        }

        double t = ((point.X - segmentStart.X) * dx + (point.Y - segmentStart.Y) * dy) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        SvgPoint projected = new(segmentStart.X + t * dx, segmentStart.Y + t * dy);
        return Distance(point, projected);
    }

    private static double GetCorridorOffsetWeight(SvgPoint point, List<SvgPoint> stationPoints)
    {
        if (stationPoints.Count == 0)
        {
            return 1;
        }

        double distance = stationPoints.Min(stationPoint => Distance(point, stationPoint));
        const double taperRadius = 44;
        if (distance >= taperRadius)
        {
            return 1;
        }

        double t = Math.Clamp(distance / taperRadius, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static SvgPoint OffsetPoint(SvgPoint point, SvgPoint normal, double offset)
    {
        return new SvgPoint(point.X + normal.X * offset, point.Y + normal.Y * offset);
    }

    private static SvgPoint Normalize(SvgPoint point)
    {
        double length = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
        if (length <= 0.000001)
        {
            return new SvgPoint(0, 0);
        }

        return new SvgPoint(point.X / length, point.Y / length);
    }

    private static double Dot(SvgPoint a, SvgPoint b)
    {
        return a.X * b.X + a.Y * b.Y;
    }

    private static SvgPoint Midpoint(SvgPoint a, SvgPoint b)
    {
        return new SvgPoint((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
    }

    private static double DistancePointToSegment(SvgPoint point, SvgPoint start, SvgPoint end)
    {
        double t = Math.Clamp(ProjectFraction(point, start, end), 0, 1);
        SvgPoint projected = new(start.X + (end.X - start.X) * t, start.Y + (end.Y - start.Y) * t);
        return Distance(point, projected);
    }

    private static bool IsFinite(SvgPoint point)
    {
        return double.IsFinite(point.X) && double.IsFinite(point.Y);
    }

    private static RoutePointSet CreateRoutePoints(
        DisplayLineFamily family,
        MetroLine line,
        Dictionary<string, MetroStation> stationsById,
        RenderGeometry geometry,
        SvgRenderOptions options,
        List<string> warnings)
    {
        if (options.LayoutMode == SvgLayoutMode.Geographic
            && options.UsePathPoints
            && geometry.Projector is not null
            && (line.PathPoints?.Count ?? 0) >= 2)
        {
            List<MetroPathPoint> renderablePathPoints = (line.PathPoints ?? []).ToList();
            List<SvgPoint> projectedPathPoints = [];
            foreach (MetroPathPoint pathPoint in renderablePathPoints)
            {
                AddPointIfNotDuplicate(projectedPathPoints, geometry.Projector.Project(pathPoint.X, pathPoint.Z));
            }

            if (projectedPathPoints.Count >= 2)
            {
                List<SvgPoint> stationPoints = GetLineStationPoints(line, stationsById, geometry);
                PathPointRenderDiagnostics diagnostics = CleanProjectedPathPoints(projectedPathPoints, stationPoints, options);
                if (diagnostics.Points.Count >= 2)
                {
                    return new RoutePointSet(
                        SplitAtSuspiciousJumps(diagnostics.Points, diagnostics.SuspiciousJumpThreshold),
                        "pathPoints",
                        line.PathPoints!.Count,
                        diagnostics.Points.Count,
                        diagnostics.ReductionRatio,
                        diagnostics.MaxSegmentLength,
                        diagnostics.SuspiciousJumpCount,
                        diagnostics.EffectiveSimplificationTolerance);
                }

                if (projectedPathPoints.Count >= 2)
                {
                    PathPointMetrics fallbackMetrics = MeasurePathPoints(projectedPathPoints, options);
                    return new RoutePointSet(
                        SplitAtSuspiciousJumps(projectedPathPoints, fallbackMetrics.SuspiciousJumpThreshold),
                        "pathPoints",
                        line.PathPoints!.Count,
                        projectedPathPoints.Count,
                        0,
                        fallbackMetrics.MaxSegmentLength,
                        fallbackMetrics.SuspiciousJumpCount,
                        0);
                }
            }

            warnings.Add($"Line '{line.Id}' had pathPoints, but fewer than two distinct projected path points were usable.");
        }

        List<string> routeStopIds = (line.Stops ?? []).ToList();
        if (IsSchematicV2FamilyLayout(options.LayoutMode)
            && geometry.SchematicV2RouteGuideByFamily is not null
            && geometry.SchematicV2RouteGuideByFamily.TryGetValue(family.FamilyKey, out List<string>? routeGuide)
            && routeGuide.Count >= 2)
        {
            routeStopIds = routeGuide.ToList();
        }

        if (options.LayoutMode == SvgLayoutMode.SchematicMap)
        {
            routeStopIds = NormalizeSchematicMapRenderRouteChain(routeStopIds);
        }

        List<SvgPoint> stopPoints = [];
        foreach (string stopId in routeStopIds)
        {
            if (stationsById.ContainsKey(stopId) && geometry.StationPoints.TryGetValue(stopId, out SvgPoint point))
            {
                stopPoints.Add(point);
            }
        }

        return new RoutePointSet(
            [CreateSchematicMapRoutePolyline(stopPoints, options)],
            "stops",
            line.PathPoints?.Count ?? 0,
            0,
            0,
            0,
            0,
            0);
    }

}
