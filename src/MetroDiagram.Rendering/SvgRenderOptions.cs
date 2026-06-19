namespace MetroDiagram.Rendering;

public enum SvgLayoutMode
{
    Geographic,
    SchematicLite,
    SchematicV2,
    SchematicMap
}

public enum SvgRenderSizePreset
{
    Custom,
    Compact,
    Standard,
    Poster,
    Ultra
}

public enum SvgMapStyle
{
    Standard,
    TransitMap
}

public readonly record struct SvgRenderSize(int Width, int Height);

public static class SvgRenderSizePresets
{
    public static SvgRenderSize Get(SvgRenderSizePreset preset)
    {
        return preset switch
        {
            SvgRenderSizePreset.Compact => new SvgRenderSize(1600, 1000),
            SvgRenderSizePreset.Standard => new SvgRenderSize(2200, 1400),
            SvgRenderSizePreset.Poster => new SvgRenderSize(3200, 2000),
            SvgRenderSizePreset.Ultra => new SvgRenderSize(4200, 2600),
            _ => throw new ArgumentException("Custom size preset does not have fixed dimensions.", nameof(preset))
        };
    }
}

public sealed class SvgRenderOptions
{
    public SvgLayoutMode LayoutMode { get; init; } = SvgLayoutMode.Geographic;

    public SvgMapStyle MapStyle { get; init; } = SvgMapStyle.Standard;

    public int Width { get; init; } = 1200;

    public int Height { get; init; } = 800;

    public int Padding { get; init; } = 72;

    public int Margin { get; init; } = 72;

    public int LegendWidth { get; init; } = 380;

    public int LegendGap { get; init; } = 48;

    public double LineWidth { get; init; } = 14;

    public double StationRadius { get; init; } = 6.2;

    public double InterchangeStationRadius { get; init; } = 9.8;

    public double LabelFontSize { get; init; } = 14;

    public double LegendLabelFontSize { get; init; } = 17;

    public double LabelGap { get; init; } = 12;

    public bool EnableCenterExpansion { get; init; }

    public double CenterExpansionStrength { get; init; } = 0.18;

    public double GridSize { get; init; } = 32;

    public bool HideGenericStationLabels { get; init; }

    public bool EnableVirtualTransferHints { get; init; }

    public bool HideCrowdedLabels { get; init; }

    public bool AlwaysShowInterchanges { get; init; } = true;

    public bool AlwaysShowTerminals { get; init; } = true;

    public bool UsePathPoints { get; init; }

    public bool PathPointSimplificationEnabled { get; init; } = true;

    public double PathPointSimplificationTolerance { get; init; } = 1.25;

    public double MinPathSegmentLength { get; init; } = 1.25;

    public bool AdaptivePathPointSimplificationEnabled { get; init; } = true;

    public bool EnableParallelCorridorOffset { get; init; }

    public bool EnableServiceFamilyMerge { get; init; } = true;

    public bool EnableSharedCorridorCompositeStroke { get; init; }

    public bool EnableExpressCenterStripe { get; init; }

    public bool EnableStationRouteAnchoring { get; init; } = true;

    public double StationRouteAnchorMaxDistance { get; init; } = 36;

    public double StationRouteAnchorMultiFamilyMaxSpread { get; init; } = 40;

    public bool EnableSchematicSegmentOverlapResolver { get; init; } = true;

    public double SchematicSegmentOverlapOffsetDistance { get; init; }

    public double SchematicOverlapEndpointTrim { get; init; }

    public double SchematicShortOverlapSegmentThreshold { get; init; }

    public double SchematicMinimumStationSpacing { get; init; }

    internal bool CompactTransitMapFrame { get; set; }

    internal bool EnableSchematicMapOctilinearNormalization { get; set; }

    internal double SchematicMapOctilinearSnapAngleDegrees { get; set; } = 22.5;

    internal bool EnableSchematicMapSimpleRunLinearization { get; set; }

    internal double SchematicMapPreferredStationSpacing { get; set; }

    internal bool EnableSchematicMapLocalClearance { get; set; }

    internal double SchematicMapLocalClearanceDistance { get; set; }

    internal bool EnableSchematicMapSyntheticBends { get; set; }

    internal double SchematicMapSyntheticBendMinimumLength { get; set; } = 220;

    internal int EffectivePadding => Padding != 72 ? Padding : Margin;
}
