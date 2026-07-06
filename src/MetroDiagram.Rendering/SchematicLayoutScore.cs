namespace MetroDiagram.Rendering;

/// <summary>
/// Objective layout-quality metrics computed from final station positions and
/// per-family stop sequences. The same numbers drive the schematic-anneal cost
/// function and the corpus regression comparison, so a change is only "better"
/// when it improves these metrics across the whole sample corpus, not on one map.
/// </summary>
public sealed record SchematicLayoutScore(
    int StationCount,
    int EdgeCount,
    double OctilinearEdgeRatio,
    double MeanOctilinearDeviationDegrees,
    double EdgeLengthCoefficientOfVariation,
    int BendCount,
    double MeanBendAngleDegrees,
    int RouteCrossingCount,
    int MinimumSpacingViolationCount,
    int StationClearanceViolationCount,
    double WeightedCost);
