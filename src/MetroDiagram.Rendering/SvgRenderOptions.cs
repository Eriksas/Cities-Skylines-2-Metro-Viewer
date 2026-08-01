namespace MetroDiagram.Rendering;

public enum SvgLayoutMode
{
    Geographic,
    SchematicV2,
    SchematicMap,
    SchematicAnneal,

    /// <summary>
    /// The annealed layout plus the passes that pull it onto the eight compass
    /// headings, relieve its dense core, lane its shared corridors and carry
    /// plain stops out of bends.
    ///
    /// Kept as its own mode rather than switched on inside
    /// <see cref="SchematicAnneal"/> so that mode keeps producing exactly what it
    /// produced before: these passes move station positions, and anyone with a
    /// saved layout or a printed map should opt into that rather than find it.
    /// </summary>
    SchematicBeta
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
    Auto,
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

    public SvgMapStyle MapStyle { get; init; } = SvgMapStyle.Auto;

    public int Width { get; set; } = 1200;

    public int Height { get; set; } = 800;

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

    /// <summary>
    /// Draws route corners as geometric arcs instead of relying on
    /// <c>stroke-linejoin: round</c>, whose radius is only half the stroke width.
    /// </summary>
    public bool EnableRouteCornerRounding { get; init; } = true;

    /// <summary>
    /// Pulls each annealed run exactly onto one of the eight compass headings.
    /// Runs after the layout and before the canvas fit, so the fit sees the final
    /// bounds.
    /// </summary>
    public bool EnableOctilinearSnap { get; init; } = true;

    /// <summary>
    /// Expands the dense core of the network at the sparse rim's expense, the way
    /// a real transit map draws its city centre at a larger effective scale.
    /// Runs before the octilinear snap, which puts the headings back.
    /// </summary>
    public bool EnableDensityRelief { get; init; } = true;

    /// <summary>
    /// Carries the line straight past a plain stop that would otherwise sit in a
    /// bend, turning just after it instead. Interchanges keep their corners.
    /// Display geometry only -- <c>data-points</c> is unaffected.
    /// </summary>
    public bool EnableStationStraightRuns { get; init; } = true;

    /// <summary>Radial exponent; 1 leaves the layout alone, lower expands the core more.</summary>
    public double DensityReliefExponent { get; init; } = DensityReliefDefaultExponent;

    public const double DensityReliefDefaultExponent = 0.8;

    /// <summary>
    /// Development-only geographic passenger-rail overlay. It is intentionally
    /// not wired to the CLI or Viewer until real CS2 validation is complete.
    /// </summary>
    public bool EnableIntercityRail { get; init; }

    public LayoutOverrideDocument? LayoutOverrides { get; init; }

    public bool EnableStationRouteAnchoring { get; init; } = true;

    public double StationRouteAnchorMaxDistance { get; init; } = 36;

    public double StationRouteAnchorMultiFamilyMaxSpread { get; init; } = 40;

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
