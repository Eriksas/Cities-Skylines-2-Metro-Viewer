using System.Globalization;
using System.Text;
using System.Text.Json;
using MetroDiagram.Core.Loading;
using MetroDiagram.Export;
using MetroDiagram.Rendering;

if (args.Length < 2 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: MetroDiagram.Cli <input.json> <output.svg|.png|.pdf> [--layout geographic|schematic-v2|schematic-map|schematic-anneal] [--emit-layout-score path.csv] [--style standard|transit-map] [--size compact|standard|poster|ultra] [--grid-size N] [--schematic-min-station-spacing N] [--width N] [--height N] [--legend-width N] [--padding N] [--line-width N] [--station-radius N] [--label-font-size N] [--center-expansion] [--hide-generic-labels] [--enable-virtual-transfer-hints] [--hide-crowded-labels] [--always-show-interchanges] [--always-show-terminals] [--use-path-points] [--simplify-path-points] [--no-simplify-path-points] [--path-simplification-tolerance N] [--min-path-segment-length N] [--enable-parallel-corridor-offset] [--disable-service-family-merge] [--enable-shared-corridor-composite-stroke] [--enable-express-center-stripe] [--overrides path]");
    return args.Length < 2 ? 2 : 0;
}

string inputPath = Path.GetFullPath(args[0]);
string outputPath = Path.GetFullPath(args[1]);
SvgRenderOptions renderOptions;
string? layoutScorePath = null;

try
{
    string[] optionArgs = args.Skip(2).ToArray();
    List<string> renderOptionArgs = [];
    for (int i = 0; i < optionArgs.Length; i++)
    {
        if (string.Equals(optionArgs[i], "--emit-layout-score", StringComparison.Ordinal))
        {
            if (i + 1 >= optionArgs.Length)
            {
                throw new ArgumentException("--emit-layout-score expects a CSV file path.");
            }

            layoutScorePath = Path.GetFullPath(optionArgs[++i]);
            continue;
        }

        renderOptionArgs.Add(optionArgs[i]);
    }

    renderOptions = ParseRenderOptions(renderOptionArgs.ToArray());
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 2;
}

MetroLoadResult loadResult = MetroJsonLoader.LoadFromFile(inputPath);
foreach (string warning in loadResult.Warnings)
{
    Console.Error.WriteLine($"Warning: {warning}");
}

if (!loadResult.IsValid || loadResult.Document is null)
{
    foreach (string error in loadResult.Errors)
    {
        Console.Error.WriteLine($"Error: {error}");
    }

    return 1;
}

MetroSvgRenderer renderer = new();
SvgRenderResult renderResult = renderer.Render(loadResult.Document, renderOptions);
foreach (string warning in renderResult.Warnings)
{
    Console.Error.WriteLine($"Warning: {warning}");
}

string? outputDirectory = Path.GetDirectoryName(outputPath);
if (!string.IsNullOrWhiteSpace(outputDirectory))
{
    Directory.CreateDirectory(outputDirectory);
}

string outputExtension = Path.GetExtension(outputPath).ToLowerInvariant();
switch (outputExtension)
{
    case ".png":
        SvgDocumentExporter.ExportPng(renderResult.Svg, outputPath);
        Console.WriteLine($"PNG written to {outputPath}");
        break;
    case ".pdf":
        SvgDocumentExporter.ExportPdf(renderResult.Svg, outputPath);
        Console.WriteLine($"PDF written to {outputPath}");
        break;
    default:
        File.WriteAllText(outputPath, renderResult.Svg, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"SVG written to {outputPath}");
        break;
}

if (layoutScorePath is not null)
{
    if (renderResult.LayoutScore is SchematicLayoutScore score)
    {
        string? scoreDirectory = Path.GetDirectoryName(layoutScorePath);
        if (!string.IsNullOrWhiteSpace(scoreDirectory))
        {
            Directory.CreateDirectory(scoreDirectory);
        }

        File.WriteAllText(layoutScorePath, BuildLayoutScoreCsv(renderOptions, score), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Console.WriteLine($"Layout score written to {layoutScorePath}");
    }
    else
    {
        Console.Error.WriteLine("Warning: layout score was requested, but the network had no usable edges to score.");
    }
}

return 0;

static string BuildLayoutScoreCsv(SvgRenderOptions options, SchematicLayoutScore score)
{
    StringBuilder csv = new();
    csv.AppendLine("layout,stations,edges,octilinearEdgeRatio,meanOctilinearDeviationDeg,edgeLengthCv,bendCount,meanBendAngleDeg,crossings,minSpacingViolations,clearanceViolations,weightedCost");
    csv.AppendLine(string.Join(",",
        LayoutModeToText(options.LayoutMode),
        score.StationCount.ToString(CultureInfo.InvariantCulture),
        score.EdgeCount.ToString(CultureInfo.InvariantCulture),
        score.OctilinearEdgeRatio.ToString("0.####", CultureInfo.InvariantCulture),
        score.MeanOctilinearDeviationDegrees.ToString("0.###", CultureInfo.InvariantCulture),
        score.EdgeLengthCoefficientOfVariation.ToString("0.####", CultureInfo.InvariantCulture),
        score.BendCount.ToString(CultureInfo.InvariantCulture),
        score.MeanBendAngleDegrees.ToString("0.###", CultureInfo.InvariantCulture),
        score.RouteCrossingCount.ToString(CultureInfo.InvariantCulture),
        score.MinimumSpacingViolationCount.ToString(CultureInfo.InvariantCulture),
        score.StationClearanceViolationCount.ToString(CultureInfo.InvariantCulture),
        score.WeightedCost.ToString("0.###", CultureInfo.InvariantCulture)));
    return csv.ToString();
}

static string LayoutModeToText(SvgLayoutMode layoutMode)
{
    return layoutMode switch
    {
        SvgLayoutMode.SchematicV2 => "schematic-v2",
        SvgLayoutMode.SchematicMap => "schematic-map",
        SvgLayoutMode.SchematicAnneal => "schematic-anneal",
        _ => "geographic"
    };
}

static SvgRenderOptions ParseRenderOptions(string[] optionArgs)
{
    int? width = null;
    int? height = null;
    int? legendWidth = null;
    int? padding = null;
    double? lineWidth = null;
    double? stationRadius = null;
    double? labelFontSize = null;
    SvgLayoutMode? layoutMode = null;
    SvgMapStyle? mapStyle = null;
    double? gridSize = null;
    double? schematicMinimumStationSpacing = null;
    SvgRenderSizePreset? sizePreset = null;
    double? pathSimplificationTolerance = null;
    double? minPathSegmentLength = null;
    bool centerExpansion = false;
    bool hideGenericLabels = false;
    bool enableVirtualTransferHints = false;
    bool hideCrowdedLabels = false;
    bool alwaysShowInterchanges = false;
    bool alwaysShowTerminals = false;
    bool usePathPoints = false;
    bool? simplifyPathPoints = null;
    bool enableParallelCorridorOffset = false;
    bool enableServiceFamilyMerge = true;
    bool enableSharedCorridorCompositeStroke = false;
    bool enableExpressCenterStripe = false;
    string? overridePath = null;

    for (int i = 0; i < optionArgs.Length; i++)
    {
        string option = optionArgs[i];
        switch (option)
        {
            case "--layout":
                layoutMode = ReadLayoutMode(optionArgs, ref i, option);
                break;
            case "--style":
                mapStyle = ReadMapStyle(optionArgs, ref i, option);
                break;
            case "--grid-size":
                gridSize = ReadDouble(optionArgs, ref i, option);
                break;
            case "--schematic-min-station-spacing":
                schematicMinimumStationSpacing = ReadDouble(optionArgs, ref i, option);
                break;
            case "--size":
                sizePreset = ReadSizePreset(optionArgs, ref i, option);
                break;
            case "--width":
                width = ReadInt(optionArgs, ref i, option);
                break;
            case "--height":
                height = ReadInt(optionArgs, ref i, option);
                break;
            case "--legend-width":
                legendWidth = ReadInt(optionArgs, ref i, option);
                break;
            case "--padding":
                padding = ReadInt(optionArgs, ref i, option);
                break;
            case "--line-width":
                lineWidth = ReadDouble(optionArgs, ref i, option);
                break;
            case "--station-radius":
                stationRadius = ReadDouble(optionArgs, ref i, option);
                break;
            case "--label-font-size":
                labelFontSize = ReadDouble(optionArgs, ref i, option);
                break;
            case "--center-expansion":
                centerExpansion = true;
                break;
            case "--hide-generic-labels":
                hideGenericLabels = true;
                break;
            case "--enable-virtual-transfer-hints":
                enableVirtualTransferHints = true;
                break;
            case "--hide-crowded-labels":
                hideCrowdedLabels = true;
                break;
            case "--always-show-interchanges":
                alwaysShowInterchanges = true;
                break;
            case "--always-show-terminals":
                alwaysShowTerminals = true;
                break;
            case "--use-path-points":
                usePathPoints = true;
                break;
            case "--simplify-path-points":
                simplifyPathPoints = true;
                break;
            case "--no-simplify-path-points":
                simplifyPathPoints = false;
                break;
            case "--path-simplification-tolerance":
                pathSimplificationTolerance = ReadDouble(optionArgs, ref i, option);
                break;
            case "--min-path-segment-length":
                minPathSegmentLength = ReadDouble(optionArgs, ref i, option);
                break;
            case "--enable-parallel-corridor-offset":
                enableParallelCorridorOffset = true;
                break;
            case "--disable-service-family-merge":
                enableServiceFamilyMerge = false;
                break;
            case "--enable-shared-corridor-composite-stroke":
                enableSharedCorridorCompositeStroke = true;
                break;
            case "--enable-express-center-stripe":
                enableExpressCenterStripe = true;
                break;
            case "--overrides":
                overridePath = ReadValue(optionArgs, ref i, option);
                break;
            default:
                throw new ArgumentException($"Unknown option '{option}'.");
        }
    }

    LayoutOverrideDocument? layoutOverrides = null;
    if (!string.IsNullOrWhiteSpace(overridePath))
    {
        try
        {
            layoutOverrides = LayoutOverrideLoader.LoadFromFile(Path.GetFullPath(overridePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ArgumentException($"Could not load layout overrides '{overridePath}': {ex.Message}");
        }
    }

    SvgRenderOptions defaults = new();
    int resolvedWidth = defaults.Width;
    int resolvedHeight = defaults.Height;
    if (sizePreset.HasValue)
    {
        SvgRenderSize size = SvgRenderSizePresets.Get(sizePreset.Value);
        resolvedWidth = size.Width;
        resolvedHeight = size.Height;
    }

    resolvedWidth = width ?? resolvedWidth;
    resolvedHeight = height ?? resolvedHeight;

    return new SvgRenderOptions
    {
        LayoutMode = layoutMode ?? defaults.LayoutMode,
        MapStyle = mapStyle ?? defaults.MapStyle,
        Width = resolvedWidth,
        Height = resolvedHeight,
        Padding = padding ?? defaults.Padding,
        Margin = padding ?? defaults.Margin,
        LegendWidth = legendWidth ?? defaults.LegendWidth,
        LegendGap = defaults.LegendGap,
        LineWidth = lineWidth ?? defaults.LineWidth,
        StationRadius = stationRadius ?? defaults.StationRadius,
        InterchangeStationRadius = stationRadius.HasValue ? Math.Max(stationRadius.Value + 3.5, stationRadius.Value * 1.45) : defaults.InterchangeStationRadius,
        LabelFontSize = labelFontSize ?? defaults.LabelFontSize,
        LegendLabelFontSize = defaults.LegendLabelFontSize,
        LabelGap = defaults.LabelGap,
        EnableCenterExpansion = centerExpansion,
        CenterExpansionStrength = defaults.CenterExpansionStrength,
        GridSize = gridSize ?? defaults.GridSize,
        HideGenericStationLabels = hideGenericLabels,
        EnableVirtualTransferHints = enableVirtualTransferHints,
        HideCrowdedLabels = hideCrowdedLabels,
        AlwaysShowInterchanges = alwaysShowInterchanges || defaults.AlwaysShowInterchanges,
        AlwaysShowTerminals = alwaysShowTerminals || defaults.AlwaysShowTerminals,
        UsePathPoints = usePathPoints,
        PathPointSimplificationEnabled = simplifyPathPoints ?? defaults.PathPointSimplificationEnabled,
        PathPointSimplificationTolerance = pathSimplificationTolerance ?? defaults.PathPointSimplificationTolerance,
        MinPathSegmentLength = minPathSegmentLength ?? defaults.MinPathSegmentLength,
        AdaptivePathPointSimplificationEnabled = defaults.AdaptivePathPointSimplificationEnabled,
        EnableParallelCorridorOffset = enableParallelCorridorOffset,
        EnableServiceFamilyMerge = enableServiceFamilyMerge,
        EnableSharedCorridorCompositeStroke = enableSharedCorridorCompositeStroke,
        EnableExpressCenterStripe = enableExpressCenterStripe,
        LayoutOverrides = layoutOverrides,
        SchematicMinimumStationSpacing = schematicMinimumStationSpacing ?? defaults.SchematicMinimumStationSpacing
    };
}

static SvgLayoutMode ReadLayoutMode(string[] args, ref int index, string option)
{
    string value = ReadValue(args, ref index, option);
    return value switch
    {
        "geographic" => SvgLayoutMode.Geographic,
        "schematic-v2" => SvgLayoutMode.SchematicV2,
        "schematic-map" => SvgLayoutMode.SchematicMap,
        "schematic-anneal" => SvgLayoutMode.SchematicAnneal,
        _ => throw new ArgumentException($"{option} expects 'geographic', 'schematic-v2', 'schematic-map', or 'schematic-anneal'.")
    };
}

static SvgMapStyle ReadMapStyle(string[] args, ref int index, string option)
{
    string value = ReadValue(args, ref index, option);
    return value switch
    {
        "auto" => SvgMapStyle.Auto,
        "standard" => SvgMapStyle.Standard,
        "transit-map" => SvgMapStyle.TransitMap,
        _ => throw new ArgumentException($"{option} expects 'auto', 'standard', or 'transit-map'.")
    };
}

static SvgRenderSizePreset ReadSizePreset(string[] args, ref int index, string option)
{
    string value = ReadValue(args, ref index, option);
    return value switch
    {
        "compact" => SvgRenderSizePreset.Compact,
        "standard" => SvgRenderSizePreset.Standard,
        "poster" => SvgRenderSizePreset.Poster,
        "ultra" => SvgRenderSizePreset.Ultra,
        _ => throw new ArgumentException($"{option} expects 'compact', 'standard', 'poster', or 'ultra'.")
    };
}

static int ReadInt(string[] args, ref int index, string option)
{
    string value = ReadValue(args, ref index, option);
    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) || parsed <= 0)
    {
        throw new ArgumentException($"{option} expects a positive integer.");
    }

    return parsed;
}

static double ReadDouble(string[] args, ref int index, string option)
{
    string value = ReadValue(args, ref index, option);
    if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || parsed <= 0)
    {
        throw new ArgumentException($"{option} expects a positive number.");
    }

    return parsed;
}

static string ReadValue(string[] args, ref int index, string option)
{
    if (index + 1 >= args.Length)
    {
        throw new ArgumentException($"{option} expects a value.");
    }

    index++;
    return args[index];
}
