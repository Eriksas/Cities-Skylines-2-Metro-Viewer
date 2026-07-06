using System.Globalization;
using System.Text;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    private static List<MetroPathPoint> GetRenderablePathPoints(MetroLine line, SvgRenderOptions options)
    {
        List<MetroPathPoint> original = (line.PathPoints ?? []).ToList();
        if (original.Count < 2)
        {
            return original;
        }

        if (!options.PathPointSimplificationEnabled)
        {
            return original;
        }

        List<MetroPathPoint> cleaned = RemoveConsecutiveDuplicatePathPoints(original, PathPointDuplicateEpsilon);
        cleaned = RemoveShortPathSegments(cleaned, Math.Max(0, options.MinPathSegmentLength));
        cleaned = SimplifyNearlyCollinearPathPoints(cleaned, Math.Max(0, options.PathPointSimplificationTolerance));

        return cleaned.Count >= 2 ? cleaned : original;
    }

    private static PathPointRenderDiagnostics CleanProjectedPathPoints(
        List<SvgPoint> originalPoints,
        List<SvgPoint> stationPoints,
        SvgRenderOptions options)
    {
        if (originalPoints.Count < 2)
        {
            return new PathPointRenderDiagnostics(originalPoints.ToList(), 0, 0, 0, 0, 0);
        }

        List<SvgPoint> cleaned = RemoveConsecutiveDuplicateSvgPoints(originalPoints, 0.001);
        PathPointMetrics metrics = MeasurePathPoints(cleaned, options);
        double minSegmentLength = ResolveMinPathSegmentLength(metrics, options);
        double effectiveTolerance = ResolvePathSimplificationTolerance(metrics, options);

        if (options.PathPointSimplificationEnabled)
        {
            cleaned = RemoveShortSvgSegments(cleaned, minSegmentLength);
            metrics = MeasurePathPoints(cleaned, options);
            effectiveTolerance = ResolvePathSimplificationTolerance(metrics, options);
            cleaned = SimplifyProjectedPathPoints(cleaned, stationPoints, metrics.SuspiciousJumpStartIndices, effectiveTolerance, options);
            metrics = MeasurePathPoints(cleaned, options, detectSparseJumps: metrics.SuspiciousJumpCount > 0);
        }

        double reductionRatio = originalPoints.Count == 0
            ? 0
            : Math.Max(0, (originalPoints.Count - cleaned.Count) / (double)originalPoints.Count);
        return new PathPointRenderDiagnostics(
            cleaned.Count >= 2 ? cleaned : originalPoints.ToList(),
            reductionRatio,
            metrics.MaxSegmentLength,
            metrics.SuspiciousJumpCount,
            metrics.SuspiciousJumpThreshold,
            effectiveTolerance);
    }

    private static List<int> RemoveConsecutiveDuplicateIndices(IReadOnlyList<SvgPoint> points, double epsilon)
    {
        double epsilonSquared = epsilon * epsilon;
        List<int> kept = [0];
        for (int i = 1; i < points.Count - 1; i++)
        {
            if (DistanceSquared(points[i], points[kept[^1]]) > epsilonSquared)
            {
                kept.Add(i);
            }
        }

        int last = points.Count - 1;
        if (DistanceSquared(points[last], points[kept[^1]]) <= epsilonSquared)
        {
            if (kept.Count == 1)
            {
                kept.Add(last);
            }
            else
            {
                kept[^1] = last;
            }
        }
        else
        {
            kept.Add(last);
        }

        return kept;
    }

    private static List<SvgPoint> RemoveConsecutiveDuplicateSvgPoints(List<SvgPoint> points, double epsilon)
    {
        if (points.Count <= 1)
        {
            return points.ToList();
        }

        return RemoveConsecutiveDuplicateIndices(points, epsilon).Select(index => points[index]).ToList();
    }

    private static List<int> RemoveShortSegmentIndices(IReadOnlyList<SvgPoint> points, double minSegmentLength, bool skipNearDuplicateTail)
    {
        List<int> kept = [0];
        for (int i = 1; i < points.Count - 1; i++)
        {
            if (Distance(points[i], points[kept[^1]]) >= minSegmentLength)
            {
                kept.Add(i);
            }
        }

        int last = points.Count - 1;
        if (Distance(points[last], points[kept[^1]]) < minSegmentLength && kept.Count > 1)
        {
            kept[^1] = last;
        }
        else if (!skipNearDuplicateTail || DistanceSquared(points[kept[^1]], points[last]) >= 0.001)
        {
            kept.Add(last);
        }

        return kept;
    }

    private static List<SvgPoint> RemoveShortSvgSegments(List<SvgPoint> points, double minSegmentLength)
    {
        if (points.Count <= 2 || minSegmentLength <= 0)
        {
            return points.ToList();
        }

        return RemoveShortSegmentIndices(points, minSegmentLength, skipNearDuplicateTail: true)
            .Select(index => points[index])
            .ToList();
    }

    private static List<SvgPoint> SimplifyProjectedPathPoints(
        List<SvgPoint> points,
        List<SvgPoint> stationPoints,
        List<int> suspiciousJumpStartIndices,
        double tolerance,
        SvgRenderOptions options)
    {
        if (points.Count <= 2 || tolerance <= 0)
        {
            return points.ToList();
        }

        bool[] keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;

        double stationProtectionRadius = GetStationProtectionRadius(options);
        foreach (int index in GetStationProtectedPathPointIndices(points, stationPoints, stationProtectionRadius))
        {
            if (index > 0 && index < points.Count - 1)
            {
                keep[index] = true;
            }
        }

        foreach (int jumpStartIndex in suspiciousJumpStartIndices)
        {
            if (jumpStartIndex >= 0 && jumpStartIndex < points.Count)
            {
                keep[jumpStartIndex] = true;
            }

            if (jumpStartIndex + 1 >= 0 && jumpStartIndex + 1 < points.Count)
            {
                keep[jumpStartIndex + 1] = true;
            }
        }

        List<int> anchors = [];
        for (int i = 0; i < keep.Length; i++)
        {
            if (keep[i])
            {
                anchors.Add(i);
            }
        }

        for (int i = 1; i < anchors.Count; i++)
        {
            MarkProjectedRamerDouglasPeucker(points, keep, anchors[i - 1], anchors[i], tolerance);
        }

        List<SvgPoint> simplified = [];
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                simplified.Add(points[i]);
            }
        }

        return simplified;
    }

    private static HashSet<int> GetStationProtectedPathPointIndices(
        List<SvgPoint> points,
        List<SvgPoint> stationPoints,
        double radius)
    {
        HashSet<int> protectedIndices = [];
        if (points.Count == 0 || stationPoints.Count == 0)
        {
            return protectedIndices;
        }

        double radiusSquared = radius * radius;
        foreach (SvgPoint stationPoint in stationPoints)
        {
            int bestIndex = -1;
            double bestDistance = double.MaxValue;
            for (int i = 0; i < points.Count; i++)
            {
                double distance = DistanceSquared(points[i], stationPoint);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0 && bestDistance <= radiusSquared)
            {
                protectedIndices.Add(bestIndex);
            }
        }

        return protectedIndices;
    }

    private static void MarkProjectedRamerDouglasPeucker(List<SvgPoint> points, bool[] keep, int start, int end, double tolerance)
    {
        if (end <= start + 1)
        {
            return;
        }

        double maxDistance = -1;
        int maxIndex = -1;
        for (int i = start + 1; i < end; i++)
        {
            double distance = PerpendicularDistance(points[i], points[start], points[end]);
            if (distance > maxDistance)
            {
                maxDistance = distance;
                maxIndex = i;
            }
        }

        if (maxDistance > tolerance && maxIndex > start)
        {
            keep[maxIndex] = true;
            MarkProjectedRamerDouglasPeucker(points, keep, start, maxIndex, tolerance);
            MarkProjectedRamerDouglasPeucker(points, keep, maxIndex, end, tolerance);
        }
    }

    private static PathPointMetrics MeasurePathPoints(List<SvgPoint> points, SvgRenderOptions options, bool detectSparseJumps = false)
    {
        if (points.Count < 2)
        {
            return new PathPointMetrics(0, 0, 0, GetSuspiciousJumpFloor(options), 0, []);
        }

        List<double> lengths = [];
        double totalLength = 0;
        double maxSegmentLength = 0;
        for (int i = 1; i < points.Count; i++)
        {
            double length = Distance(points[i - 1], points[i]);
            lengths.Add(length);
            totalLength += length;
            maxSegmentLength = Math.Max(maxSegmentLength, length);
        }

        double medianSegmentLength = Median(lengths);
        bool canDetectSuspiciousJumps = points.Count >= 8 || detectSparseJumps;
        double suspiciousJumpThreshold = canDetectSuspiciousJumps
            ? ResolveSuspiciousJumpThreshold(medianSegmentLength, options)
            : double.MaxValue;
        List<int> suspiciousJumpStartIndices = [];
        if (canDetectSuspiciousJumps)
        {
            for (int i = 0; i < lengths.Count; i++)
            {
                if (lengths[i] > suspiciousJumpThreshold)
                {
                    suspiciousJumpStartIndices.Add(i);
                }
            }
        }

        return new PathPointMetrics(
            totalLength,
            medianSegmentLength,
            maxSegmentLength,
            suspiciousJumpThreshold,
            suspiciousJumpStartIndices.Count,
            suspiciousJumpStartIndices);
    }

    private static double ResolvePathSimplificationTolerance(PathPointMetrics metrics, SvgRenderOptions options)
    {
        double baseTolerance = Math.Max(0, options.PathPointSimplificationTolerance);
        if (!options.AdaptivePathPointSimplificationEnabled)
        {
            return baseTolerance;
        }

        double diagonal = Math.Sqrt(options.Width * options.Width + options.Height * options.Height);
        double canvasScale = Math.Clamp(Math.Sqrt(options.Width * options.Height) / Math.Sqrt(2200.0 * 1400.0), 0.75, 1.45);
        double longLineFactor = Math.Clamp(metrics.TotalLength / Math.Max(diagonal * 0.75, 1), 0, 4);
        double medianFactor = Math.Clamp(metrics.MedianSegmentLength / 24, 0, 1.5);
        double adaptiveTolerance = (0.8 + longLineFactor * 0.75 + medianFactor * 0.35) * canvasScale;
        return Math.Clamp(Math.Max(baseTolerance, adaptiveTolerance), 0.1, 7);
    }

    private static double ResolveMinPathSegmentLength(PathPointMetrics metrics, SvgRenderOptions options)
    {
        double baseLength = Math.Max(0, options.MinPathSegmentLength);
        if (!options.AdaptivePathPointSimplificationEnabled)
        {
            return baseLength;
        }

        double canvasScale = Math.Clamp(Math.Sqrt(options.Width * options.Height) / Math.Sqrt(2200.0 * 1400.0), 0.75, 1.45);
        double adaptiveLength = (0.7 + Math.Clamp(metrics.MedianSegmentLength / 40, 0, 1.8)) * canvasScale;
        return Math.Clamp(Math.Max(baseLength, adaptiveLength), 0.1, 5);
    }

    private static double ResolveSuspiciousJumpThreshold(double medianSegmentLength, SvgRenderOptions options)
    {
        double floor = GetSuspiciousJumpFloor(options);
        double diagonal = Math.Sqrt(options.Width * options.Width + options.Height * options.Height);
        double networkCap = Math.Max(floor, diagonal * 0.22);
        return Math.Min(networkCap, Math.Max(floor, medianSegmentLength * 14));
    }

    private static double GetSuspiciousJumpFloor(SvgRenderOptions options)
    {
        double diagonal = Math.Sqrt(options.Width * options.Width + options.Height * options.Height);
        return Math.Clamp(diagonal * 0.075, 120, 280);
    }

    private static List<RoutePolyline> SplitAtSuspiciousJumps(List<SvgPoint> points, double suspiciousJumpThreshold)
    {
        if (points.Count < 2)
        {
            return [new RoutePolyline(points.ToList())];
        }

        List<RoutePolyline> polylines = [];
        List<SvgPoint> current = [points[0]];
        for (int i = 1; i < points.Count; i++)
        {
            if (Distance(points[i - 1], points[i]) > suspiciousJumpThreshold)
            {
                if (current.Count >= 2)
                {
                    polylines.Add(new RoutePolyline(current));
                }

                current = [points[i]];
                continue;
            }

            current.Add(points[i]);
        }

        if (current.Count >= 2)
        {
            polylines.Add(new RoutePolyline(current));
        }

        return polylines.Count > 0 ? polylines : [new RoutePolyline(points.ToList())];
    }

    private static bool IsNearStationPoint(SvgPoint point, List<SvgPoint> stationPoints, double radius)
    {
        if (stationPoints.Count == 0)
        {
            return false;
        }

        double radiusSquared = radius * radius;
        return stationPoints.Any(stationPoint => DistanceSquared(point, stationPoint) <= radiusSquared);
    }

    private static double GetStationProtectionRadius(SvgRenderOptions options)
    {
        return Math.Max(18, options.StationRadius * 3 + options.LineWidth);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        List<double> sorted = values.OrderBy(value => value).ToList();
        int middle = sorted.Count / 2;
        if (sorted.Count % 2 == 1)
        {
            return sorted[middle];
        }

        return (sorted[middle - 1] + sorted[middle]) / 2;
    }

    // The MetroPathPoint pipeline reuses the SvgPoint geometry cores through
    // probe points (X -> X, Z -> Y) and index selection, so per-point metadata
    // such as Source/SegmentEntity survives cleaning untouched.
    private static List<SvgPoint> CreatePathPointProbe(List<MetroPathPoint> points)
    {
        return points.Select(point => new SvgPoint(point.X, point.Z)).ToList();
    }

    private static List<MetroPathPoint> RemoveShortPathSegments(List<MetroPathPoint> points, double minSegmentLength)
    {
        if (points.Count <= 2 || minSegmentLength <= 0)
        {
            return points.ToList();
        }

        return RemoveShortSegmentIndices(CreatePathPointProbe(points), minSegmentLength, skipNearDuplicateTail: false)
            .Select(index => points[index])
            .ToList();
    }

    private static List<MetroPathPoint> RemoveConsecutiveDuplicatePathPoints(List<MetroPathPoint> points, double epsilon)
    {
        if (points.Count <= 1)
        {
            return points.ToList();
        }

        return RemoveConsecutiveDuplicateIndices(CreatePathPointProbe(points), epsilon)
            .Select(index => points[index])
            .ToList();
    }

    private static List<MetroPathPoint> SimplifyNearlyCollinearPathPoints(List<MetroPathPoint> points, double tolerance)
    {
        if (points.Count <= 2 || tolerance <= 0)
        {
            return points;
        }

        List<SvgPoint> probe = CreatePathPointProbe(points);
        bool[] keep = new bool[points.Count];
        keep[0] = true;
        keep[^1] = true;
        MarkProjectedRamerDouglasPeucker(probe, keep, 0, points.Count - 1, tolerance);

        List<MetroPathPoint> simplified = [];
        for (int i = 0; i < points.Count; i++)
        {
            if (keep[i])
            {
                simplified.Add(points[i]);
            }
        }

        return simplified;
    }

    private static double PerpendicularDistance(SvgPoint point, SvgPoint start, SvgPoint end)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double denominator = Math.Sqrt(dx * dx + dy * dy);
        if (denominator < 0.000001)
        {
            return Distance(point, start);
        }

        return Math.Abs(dy * point.X - dx * point.Y + end.X * start.Y - end.Y * start.X) / denominator;
    }

    private static double Distance(SvgPoint a, SvgPoint b)
    {
        return Math.Sqrt(DistanceSquared(a, b));
    }

    private static void AddPointIfNotDuplicate(List<SvgPoint> points, SvgPoint point)
    {
        if (points.Count > 0 && DistanceSquared(points[^1], point) < 0.001)
        {
            return;
        }

        points.Add(point);
    }

}
