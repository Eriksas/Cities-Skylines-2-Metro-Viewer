using System.Globalization;
using System.Text;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    private readonly record struct GeometryCacheKey(
        SvgLayoutMode LayoutMode,
        SvgMapStyle MapStyle,
        int Width,
        int Height,
        int Padding,
        int Margin,
        int LegendWidth,
        int LegendGap,
        double LineWidth,
        double StationRadius,
        double InterchangeStationRadius,
        double LabelFontSize,
        double LegendLabelFontSize,
        double LabelGap,
        bool EnableCenterExpansion,
        double CenterExpansionStrength,
        double GridSize,
        bool UsePathPoints,
        bool PathPointSimplificationEnabled,
        double PathPointSimplificationTolerance,
        double MinPathSegmentLength,
        bool AdaptivePathPointSimplificationEnabled,
        bool EnableServiceFamilyMerge,
        double SchematicMinimumStationSpacing,
        bool CompactTransitMapFrame,
        bool EnableSchematicMapOctilinearNormalization,
        double SchematicMapOctilinearSnapAngleDegrees,
        bool EnableSchematicMapSimpleRunLinearization,
        double SchematicMapPreferredStationSpacing,
        bool EnableSchematicMapLocalClearance,
        double SchematicMapLocalClearanceDistance,
        bool EnableSchematicMapSyntheticBends,
        double SchematicMapSyntheticBendMinimumLength,
        bool HasLegend);

    private sealed class CoordinateProjector
    {
        private readonly double _minX;
        private readonly double _maxZ;
        private readonly double _scale;
        private readonly double _originX;
        private readonly double _originY;
        private readonly double _offsetX;
        private readonly double _offsetY;
        private readonly CenterExpansionTransform? _centerExpansion;

        private CoordinateProjector(
            double minX,
            double maxZ,
            double scale,
            double originX,
            double originY,
            double offsetX,
            double offsetY,
            CenterExpansionTransform? centerExpansion)
        {
            _minX = minX;
            _maxZ = maxZ;
            _scale = scale;
            _originX = originX;
            _originY = originY;
            _offsetX = offsetX;
            _offsetY = offsetY;
            _centerExpansion = centerExpansion;
        }

        public static CoordinateProjector? Create(List<SourceCoordinate> sourceCoordinates, SvgRenderOptions options, bool reserveLegendSpace)
        {
            if (sourceCoordinates.Count == 0)
            {
                return null;
            }

            CenterExpansionTransform? centerExpansion = options.EnableCenterExpansion
                ? CenterExpansionTransform.Create(sourceCoordinates, options.CenterExpansionStrength)
                : null;
            List<SourceCoordinate> projectedCoordinates = centerExpansion is null
                ? sourceCoordinates
                : sourceCoordinates.Select(point => centerExpansion.Value.Apply(point.X, point.Z)).ToList();

            double minX = projectedCoordinates.Min(point => point.X);
            double maxX = projectedCoordinates.Max(point => point.X);
            double minZ = projectedCoordinates.Min(point => point.Z);
            double maxZ = projectedCoordinates.Max(point => point.Z);
            double sourceWidth = Math.Max(maxX - minX, 1);
            double sourceHeight = Math.Max(maxZ - minZ, 1);
            SvgRect bounds = CreateGeometryBounds(options, reserveLegendSpace);
            double innerWidth = Math.Max(bounds.Right - bounds.Left, 1);
            double innerHeight = Math.Max(bounds.Bottom - bounds.Top, 1);
            double scale = Math.Min(innerWidth / sourceWidth, innerHeight / sourceHeight);
            double scaledWidth = sourceWidth * scale;
            double scaledHeight = sourceHeight * scale;
            double offsetX = (innerWidth - scaledWidth) / 2;
            double offsetY = (innerHeight - scaledHeight) / 2;

            return new CoordinateProjector(minX, maxZ, scale, bounds.Left, bounds.Top, offsetX, offsetY, centerExpansion);
        }

        public SvgPoint Project(double sourceX, double sourceZ)
        {
            SourceCoordinate coordinate = _centerExpansion is null
                ? new SourceCoordinate(sourceX, sourceZ)
                : _centerExpansion.Value.Apply(sourceX, sourceZ);
            double x = _originX + _offsetX + (coordinate.X - _minX) * _scale;
            double y = _originY + _offsetY + (_maxZ - coordinate.Z) * _scale;
            return new SvgPoint(x, y);
        }
    }

    private readonly record struct CenterExpansionTransform(double CenterX, double CenterZ, double HalfWidth, double HalfHeight, double Strength)
    {
        public static CenterExpansionTransform? Create(List<SourceCoordinate> sourceCoordinates, double strength)
        {
            strength = Math.Clamp(strength, 0, 0.45);
            if (strength <= 0 || sourceCoordinates.Count < 3)
            {
                return null;
            }

            double minX = sourceCoordinates.Min(point => point.X);
            double maxX = sourceCoordinates.Max(point => point.X);
            double minZ = sourceCoordinates.Min(point => point.Z);
            double maxZ = sourceCoordinates.Max(point => point.Z);
            return new CenterExpansionTransform(
                (minX + maxX) / 2,
                (minZ + maxZ) / 2,
                Math.Max((maxX - minX) / 2, 1),
                Math.Max((maxZ - minZ) / 2, 1),
                strength);
        }

        public SourceCoordinate Apply(double x, double z)
        {
            double normalizedX = (x - CenterX) / HalfWidth;
            double normalizedZ = (z - CenterZ) / HalfHeight;
            double normalizedDistance = Math.Sqrt(normalizedX * normalizedX + normalizedZ * normalizedZ);
            double centerWeight = Math.Max(0, 1 - Math.Min(1, normalizedDistance));
            double factor = 1 + Strength * centerWeight;
            return new SourceCoordinate(
                CenterX + (x - CenterX) * factor,
                CenterZ + (z - CenterZ) * factor);
        }
    }

    private readonly record struct SvgVisualStyle(
        double BaseRouteWidth,
        double SharedCorridorOuterWidth,
        double SharedCorridorInnerWidth,
        double ExpressStripeWidth,
        double StationMarkerOuterRadius,
        double StationMarkerStrokeWidth,
        double InterchangeMarkerRadius,
        double InterchangeMarkerStrokeWidth,
        double LabelHaloWidth)
    {
        public static SvgVisualStyle From(SvgRenderOptions options)
        {
            bool transitMapStyle = options.MapStyle == SvgMapStyle.TransitMap;
            double baseRouteWidth = Math.Max(1, options.LineWidth);
            double sharedOuterWidth = baseRouteWidth;
            double sharedInnerWidth = Math.Clamp(baseRouteWidth * 0.48, 3, baseRouteWidth * 0.62);
            double expressStripeWidth = Math.Clamp(baseRouteWidth * 0.24, 2.4, Math.Max(2.4, baseRouteWidth * 0.34));
            double stationRadius = Math.Max(transitMapStyle ? 4.8 : 3.5, options.StationRadius + (transitMapStyle ? 0.8 : 0));
            double interchangeRadius = Math.Max(options.InterchangeStationRadius + (transitMapStyle ? 1.1 : 0), stationRadius + (transitMapStyle ? 4.2 : 3.5));
            double stationStrokeWidth = transitMapStyle
                ? Math.Clamp(baseRouteWidth * 0.15, 1.8, 2.4)
                : Math.Clamp(baseRouteWidth * 0.13, 1.6, 2.2);
            double interchangeStrokeWidth = transitMapStyle
                ? Math.Clamp(baseRouteWidth * 0.18, 2.2, 3.0)
                : Math.Clamp(baseRouteWidth * 0.16, 2, 2.8);
            double labelHaloWidth = Math.Max(transitMapStyle ? 6 : 5, options.LabelFontSize * (transitMapStyle ? 0.44 : 0.38));

            return new SvgVisualStyle(
                baseRouteWidth,
                sharedOuterWidth,
                sharedInnerWidth,
                expressStripeWidth,
                stationRadius,
                stationStrokeWidth,
                interchangeRadius,
                interchangeStrokeWidth,
                labelHaloWidth);
        }
    }

    private readonly record struct RenderGeometry(
        Dictionary<string, SvgPoint> StationPoints,
        CoordinateProjector? Projector,
        Dictionary<string, SchematicStationAdjustment> SchematicStationAdjustments,
        List<SchematicV2DenseStationPair> SchematicV2DenseStationPairs,
        Dictionary<string, List<string>>? SchematicV2RouteGuideByFamily,
        Dictionary<string, SchematicV2RouteGuideMetadata>? SchematicV2RouteGuideMetadataByFamily);

    private readonly record struct SchematicLayoutResult(
        Dictionary<string, SvgPoint> Points,
        Dictionary<string, SchematicStationAdjustment> Adjustments,
        List<SchematicV2DenseStationPair> DenseStationPairs,
        Dictionary<string, List<string>> RouteGuideByFamily,
        Dictionary<string, SchematicV2RouteGuideMetadata> RouteGuideMetadataByFamily);

    private readonly record struct SchematicStationAdjustment(
        string StationId,
        SvgPoint OriginalPoint,
        SvgPoint AdjustedPoint,
        double Distance,
        string Reason);

    private readonly record struct SchematicV2DenseStationPair(
        string FirstStationId,
        string SecondStationId,
        double Distance,
        double MinimumSpacing,
        bool Adjacent,
        bool SameNameCluster,
        bool SameNameAssetDefaultCluster,
        bool SameNameLikelyUserCluster,
        bool FirstInterchange,
        bool SecondInterchange);

    private readonly record struct VirtualTransferHint(
        string StationName,
        string FirstStationId,
        string SecondStationId,
        SvgPoint FirstPoint,
        SvgPoint SecondPoint,
        double Distance);

    private readonly record struct SchematicV2FamilyPath(
        string FamilyKey,
        List<string> Stops,
        SvgPoint Direction);

    private sealed record SchematicV2RouteGuideResult(
        Dictionary<string, List<string>> RouteGuideByFamily,
        Dictionary<string, SchematicV2RouteGuideMetadata> MetadataByFamily);

    private sealed record SchematicV2RouteGuideMetadata(
        string CorridorId,
        string FamilyAKey,
        string FamilyBKey,
        double Confidence,
        double SharedLength,
        double AverageDistance,
        double MaxDistance,
        List<string> GuideStationIds);

    private sealed record SchematicV2GeometryCorridorConstraint(
        string FamilyAKey,
        string FamilyBKey,
        string GuideFamilyKey,
        string FamilyAStartStationId,
        string FamilyAEndStationId,
        string FamilyBStartStationId,
        string FamilyBEndStationId,
        List<string> GuideStationIds,
        double SharedLength,
        double AverageDistance,
        double MaxDistance,
        bool StopSequenceMatched,
        bool UseFullGuideInterval);

    private sealed class SchematicV2SharedSegmentBuilder(SvgPoint start, SvgPoint end)
    {
        public SvgPoint Start { get; } = start;

        public SvgPoint End { get; } = end;

        public HashSet<string> FamilyKeys { get; } = new(StringComparer.Ordinal);
    }

    private readonly record struct SchematicV2SharedSegment(
        string Key,
        SvgPoint Start,
        SvgPoint End,
        List<string> FamilyKeys,
        string Source);

    private sealed record SchematicV2SharedCorridorRun(
        string RunId,
        List<string> FamilyKeys,
        string Source,
        List<SvgPoint> Points,
        List<string> StationIds)
    {
        public string FamilyAKey => FamilyKeys.Count > 0 ? FamilyKeys[0] : string.Empty;

        public string FamilyBKey => FamilyKeys.Count > 1 ? FamilyKeys[1] : string.Empty;
    }

    private sealed record SchematicV2ParallelCorridorLane(
        VisibleLaneKey LaneKey,
        List<RenderRoute> Routes,
        RenderRoute PrimaryRoute);

    private readonly record struct SchematicV2ProjectedLine(
        DisplayLineFamily Family,
        MetroLine Line,
        List<string> TopologyStops,
        List<SvgPoint> PathPoints,
        List<SvgPoint> StopPoints);

    private readonly record struct GeometrySharedCorridorRun(
        int PathIndexAStart,
        int PathIndexAEnd,
        int PathIndexBStart,
        int PathIndexBEnd,
        int DirectionSign,
        double SharedLength,
        double AverageDistance,
        double MaxDistance);

    private readonly record struct PolylineStationProjection(
        string StationId,
        int SegmentIndex,
        double Distance,
        double PolylineProgress);

    private readonly record struct PolylinePointProjection(
        int SegmentIndex,
        double Distance,
        double PolylineProgress);

    private readonly record struct GeometryPointProjectionMatch(
        int PathIndexA,
        int SegmentIndexB,
        double ProgressA,
        double ProgressB,
        double Distance);

    private readonly record struct SharedAdjacentEdge(
        string FamilyAStartStationId,
        string FamilyAEndStationId,
        string FamilyBStartStationId,
        string FamilyBEndStationId);

    private readonly record struct StationRouteAnchorMap(
        Dictionary<string, SvgPoint> Points,
        Dictionary<string, StationRenderAnchor> Anchors);

    private readonly record struct StationRenderAnchor(
        string StationId,
        SvgPoint RawPoint,
        SvgPoint Point,
        bool Applied,
        string Source,
        double Distance,
        List<string> FamilyKeys,
        string? FallbackReason);

    private readonly record struct StationRouteAnchorCandidate(string FamilyKey, SvgPoint Point, double Distance);

    private readonly record struct RenderRoute(DisplayLineFamily Family, MetroLine Line, RoutePointSet RoutePointSet);

    private readonly record struct SchematicMapRouteSegment(
        int RouteIndex,
        int PolylineIndex,
        int SegmentIndex,
        string FamilyKey,
        string DisplayName,
        string Color,
        SvgPoint Start,
        SvgPoint End,
        double Length,
        SvgPoint Direction);

    private readonly record struct SchematicMapRouteCrossing(
        int Index,
        SvgPoint Point,
        SchematicMapRouteSegment TopSegment,
        SchematicMapRouteSegment BottomSegment,
        double AngleDegrees,
        string FamilyPairKey);

    private sealed record CorridorRenderPlan(
        List<CorridorDrawCommand> NormalBase,
        List<CorridorDrawCommand> SharedBase,
        List<CorridorDrawCommand> SharedInner,
        List<CorridorDrawCommand> ExpressDecorations);

    private sealed record CorridorDrawCommand(
        RenderRoute RenderRoute,
        List<SvgPoint> Points,
        CorridorDrawLayer Layer,
        string? Stroke,
        double StrokeWidth,
        string ExtraAttributes,
        int RoutePartIndex = -1,
        int RoutePartCount = 0);

    private enum CorridorDrawLayer
    {
        NormalBase,
        SharedBase,
        SharedInner,
        ExpressDecoration
    }

    private readonly record struct RoutePointSet(
        List<RoutePolyline> Polylines,
        string Source,
        int OriginalPathPointCount,
        int CleanedPathPointCount,
        double ReductionRatio,
        double MaxPathSegmentLength,
        int SuspiciousJumpCount,
        double EffectiveSimplificationTolerance);

    private readonly record struct RoutePolyline(
        List<SvgPoint> Points,
        string? CorridorId = null,
        int CorridorMemberCount = 0,
        double CorridorOffsetIndex = 0,
        double CorridorOffsetPx = 0,
        SharedCorridorStroke? SharedCorridorStroke = null,
        string? SharedCorridorSkipped = null,
        int SyntheticBendCount = 0);

    private readonly record struct SchematicSegmentKey(string Key, SvgPoint Start, SvgPoint End);

    private sealed record SharedCorridorStroke(
        string RunId,
        string FamilyAKey,
        string FamilyBKey,
        string? OuterColor,
        string? InnerColor,
        int PointCount);

    private sealed record SharedCorridorRun(
        string RunId,
        int RouteIndex,
        DisplayLineFamily FamilyA,
        DisplayLineFamily FamilyB,
        string? OuterColor,
        string? InnerColor,
        List<SvgPoint> Points,
        int FragmentCount,
        HashSet<int> FragmentIndices);

    private readonly record struct SharedCorridorPairKey(string OuterFamilyKey, string InnerFamilyKey);

    private readonly record struct CorridorMatch(int LeftFragmentIndex, int RightFragmentIndex);

    private readonly record struct GeometrySharedSegmentMatch(
        int SegmentIndexA,
        int SegmentIndexB,
        int DirectionSign,
        double OverlapLength,
        double AverageDistance,
        double MaxDistance);

    private readonly record struct CorridorFamilyInfo(
        string FamilyKey,
        DisplayLineFamily Family,
        string DisplayName,
        string? Color,
        int RouteIndex);

    private readonly record struct CorridorSegmentFragment(
        int Index,
        int RouteIndex,
        int PolylineIndex,
        int SegmentIndex,
        string LineId,
        DisplayLineFamily Family,
        string FamilyKey,
        string FamilyDisplayName,
        string? FamilyColor,
        SvgPoint Start,
        SvgPoint End,
        SvgPoint Direction,
        SvgPoint Normal,
        double Length,
        bool IsEligible,
        List<SvgPoint> StationPoints);

    private readonly record struct CorridorAssignment(
        string CorridorId,
        int MemberCount,
        double OffsetIndex,
        double OffsetPx,
        SvgPoint BaseDirection);

    private readonly record struct PathPointRenderDiagnostics(
        List<SvgPoint> Points,
        double ReductionRatio,
        double MaxSegmentLength,
        int SuspiciousJumpCount,
        double SuspiciousJumpThreshold,
        double EffectiveSimplificationTolerance);

    private readonly record struct PathPointMetrics(
        double TotalLength,
        double MedianSegmentLength,
        double MaxSegmentLength,
        double SuspiciousJumpThreshold,
        int SuspiciousJumpCount,
        List<int> SuspiciousJumpStartIndices);

    private readonly record struct SourceStationPoint(string Id, double X, double Z);

    private readonly record struct SourceCoordinate(double X, double Z);

    private readonly record struct SvgPoint(double X, double Y);

    private readonly record struct LegendLine(DisplayLineFamily Family, int Index, int? LineNumber);

    private readonly record struct RouteBadgePlacement(
        double X,
        double Y,
        double Width,
        double Height,
        string Text,
        SvgRect Box,
        double Score);

    private readonly record struct LabelRequest(
        MetroStation Station,
        string Text,
        SvgPoint Point,
        double StationRadius,
        int Priority,
        int Index,
        bool IsProtected,
        bool CanHideWhenCrowded,
        bool IsInterchange,
        bool IsTerminal,
        bool IsGenericName);

    private readonly record struct LabelCandidate(
        string PositionName,
        double X,
        double Y,
        string Anchor,
        SvgRect Box);

    private readonly record struct PlacedLabel(
        string StationId,
        string Text,
        double X,
        double Y,
        string Anchor,
        string PositionName,
        SvgRect Box,
        int Priority,
        int Index,
        double Score,
        double LabelOverlapArea,
        bool IsInterchange,
        bool IsTerminal,
        bool IsGenericName,
        bool OverrideApplied = false);

    private readonly record struct SvgRect(double Left, double Top, double Right, double Bottom)
    {
        public static SvgRect FromCenter(double x, double y, double width, double height)
        {
            return new SvgRect(x - width / 2, y - height / 2, x + width / 2, y + height / 2);
        }

        public double OverlapArea(SvgRect other)
        {
            double width = Math.Max(0, Math.Min(Right, other.Right) - Math.Max(Left, other.Left));
            double height = Math.Max(0, Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top));
            return width * height;
        }

        public double OutsideArea(SvgRect bounds)
        {
            double width = Math.Max(0, Right - Left);
            double height = Math.Max(0, Bottom - Top);
            double insideWidth = Math.Max(0, Math.Min(Right, bounds.Right) - Math.Max(Left, bounds.Left));
            double insideHeight = Math.Max(0, Math.Min(Bottom, bounds.Bottom) - Math.Max(Top, bounds.Top));
            return width * height - insideWidth * insideHeight;
        }

        public SvgRect Inflate(double horizontal, double vertical)
        {
            return new SvgRect(Left - horizontal, Top - vertical, Right + horizontal, Bottom + vertical);
        }
    }

}
