using System.Globalization;
using System.Xml.Linq;
using MetroDiagram.Core.Exporting;
using MetroDiagram.Core.Geometry;
using MetroDiagram.Core.Loading;
using MetroDiagram.Core.Models;
using MetroDiagram.Core.Validation;
using MetroDiagram.Rendering;

SampleExpectation[] samples =
[
    new("sample-metro-small.json", "Example Metroville", ["Central", "North Pier"], 1, false),
    new("sample-metro-interchange.json", "Interchange City", ["Crossroads", "Blue Line", "Green Line"], 2, true),
    new("sample-metro-branch.json", "Branchwater", ["Junction", "Orange Main", "Orange Branch"], 2, true),
    new("sample-metro-loop.json", "Loop City", ["Harbor", "Circle Line", "Convention Spur"], 2, true),
    new("sample-metro-missing-fields.json", "Unnamed City", ["Station 1", "Station 3", "Needs Color", "Line 2"], 2, true),
    new("sample-metro-large-network.json", "Greater Sample City", ["Central", "Airport", "Purple Line"], 5, true),
    new("sample-metro-pathpoints.json", "Path Geometry City", ["West", "Central Bend", "Path Line"], 1, false)
];

List<(string Name, Action Test)> tests =
[
    ("all sample JSON files load and render valid SVG", () => AllSamplesLoadAndRender(samples)),
    ("legend sorts numeric line names naturally", LegendSortsNumericLineNamesNaturally),
    ("renderer builds stable diagram title fallbacks", RendererBuildsStableDiagramTitleFallbacks),
    ("transit-map style adds official map framing", TransitMapStyleAddsOfficialMapFraming),
    ("transit-map style keeps standard style unchanged", TransitMapStyleKeepsStandardStyleUnchanged),
    ("transit-map legend explains express marker", TransitMapLegendExplainsExpressMarker),
    ("transit-map route badges avoid placed labels", TransitMapRouteBadgesAvoidPlacedLabels),
    ("renderer sanitizes XML text values", RendererSanitizesXmlTextValues),
    ("schematic-lite snaps route points to grid and octilinear directions", SchematicLiteSnapsRoutePoints),
    ("schematic-lite offsets two overlapping segments", SchematicLiteOffsetsTwoOverlappingSegments),
    ("schematic-lite trims overlapping segment endpoints", SchematicLiteTrimsOverlappingSegmentEndpoints),
    ("schematic-lite treats reverse segments as one overlap", SchematicLiteTreatsReverseSegmentsAsOneOverlap),
    ("schematic-lite ignores same-family repeated segments", SchematicLiteIgnoresSameFamilyRepeatedSegments),
    ("schematic-lite centers three-family overlap offsets", SchematicLiteCentersThreeFamilyOverlapOffsets),
    ("schematic-lite ignores zero-length segments", SchematicLiteIgnoresZeroLengthSegments),
    ("schematic-lite clamps trim on short overlap segments", SchematicLiteClampsTrimOnShortOverlapSegments),
    ("schematic-lite short overlaps use centered fallback", SchematicLiteShortOverlapsUseCenteredFallback),
    ("schematic-lite preserves reverse short overlap continuity", SchematicLitePreservesReverseShortOverlapContinuity),
    ("schematic-lite long overlaps still use offset mode", SchematicLiteLongOverlapsStillUseOffsetMode),
    ("schematic-lite trim does not affect non-overlap segments", SchematicLiteTrimDoesNotAffectNonOverlapSegments),
    ("schematic-lite falls back conservatively near T junctions", SchematicLiteFallsBackConservativelyNearTJunctions),
    ("schematic-lite overlap resolver does not affect geographic", SchematicLiteOverlapResolverDoesNotAffectGeographic),
    ("schematic-lite station markers stay centered while routes offset", SchematicLiteStationMarkersStayCenteredWhileRoutesOffset),
    ("schematic-lite station marker centers are unchanged by overlap resolver", SchematicLiteStationMarkerCentersUnchangedByOverlapResolver),
    ("schematic-lite station spacing separates close stations", SchematicLiteStationSpacingSeparatesCloseStations),
    ("schematic-lite station spacing moves markers labels and routes together", SchematicLiteStationSpacingMovesMarkersLabelsAndRoutesTogether),
    ("schematic-lite station spacing limits high-degree station movement", SchematicLiteStationSpacingLimitsHighDegreeStationMovement),
    ("schematic-lite overlap resolver keeps loop and branch samples valid", SchematicLiteOverlapResolverKeepsLoopAndBranchSamplesValid),
    ("schematic-v2 preserves stop order and adjacency", SchematicV2PreservesStopOrderAndAdjacency),
    ("schematic-v2 preserves interchange node", SchematicV2PreservesInterchangeNode),
    ("schematic-v2 keeps old schematic-lite available", SchematicV2KeepsOldSchematicLiteAvailable),
    ("schematic-v2 spacing does not reverse close stations", SchematicV2SpacingDoesNotReverseCloseStations),
    ("schematic-v2 relaxes sharp detour station", SchematicV2RelaxesSharpDetourStation),
    ("schematic-v2 uses topology-rich family variant for shared corridors", SchematicV2UsesTopologyRichFamilyVariantForSharedCorridors),
    ("schematic-v2 preserves shared corridor edge", SchematicV2PreservesSharedCorridorEdge),
    ("schematic-v2 shared edge guides do not inject neighboring branch stations", SchematicV2SharedEdgeGuidesDoNotInjectNeighboringBranchStations),
    ("schematic-v2 renders exact shared platform corridors", SchematicV2RendersExactSharedPlatformCorridors),
    ("schematic-v2 renders exact shared edge with express family", SchematicV2RendersExactSharedEdgeWithExpressFamily),
    ("schematic-v2 renders three-family exact shared edge visibly", SchematicV2RendersThreeFamilyExactSharedEdgeVisibly),
    ("schematic-v2 chooses canonical service route by stop count", SchematicV2ChoosesCanonicalServiceRouteByStopCount),
    ("schematic-v2 hides express variant geometry", SchematicV2HidesExpressVariantGeometry),
    ("schematic-v2 marks express service family with center stripe", SchematicV2MarksExpressServiceFamilyWithCenterStripe),
    ("canonical schematic network selects service family route", CanonicalSchematicNetworkSelectsServiceFamilyRoute),
    ("canonical schematic network records exact shared edges", CanonicalSchematicNetworkRecordsExactSharedEdges),
    ("canonical schematic network records geometry corridor hints", CanonicalSchematicNetworkRecordsGeometryCorridorHints),
    ("schematic-v2 detects geometry shared corridor for skip-stop service", SchematicV2DetectsGeometrySharedCorridorForSkipStopService),
    ("schematic-v2 reconstructs follower route chain with pass-through nodes", SchematicV2ReconstructsFollowerRouteChainWithPassThroughNodes),
    ("schematic-v2 materializes route guide as parallel corridor", SchematicV2MaterializesRouteGuideAsParallelCorridor),
    ("schematic-v2 shared corridor is stable across size presets", SchematicV2SharedCorridorIsStableAcrossSizePresets),
    ("schematic-v2 renders express stripe on parallel corridor", SchematicV2RendersExpressStripeOnParallelCorridor),
    ("schematic-v2 normalizes canonical backtracking route chains", SchematicV2NormalizesCanonicalBacktrackingRouteChains),
    ("schematic-v2 straightens zigzag terminal tails", SchematicV2StraightensZigzagTerminalTails),
    ("schematic-v2 does not mistake single crossings for shared corridors", SchematicV2DoesNotMistakeSingleCrossingsForSharedCorridors),
    ("schematic-v2 does not render parallel corridor for single crossing", SchematicV2DoesNotRenderParallelCorridorForSingleCrossing),
    ("schematic-v2 does not merge non-shared lines", SchematicV2DoesNotMergeNonSharedLines),
    ("schematic-v2 leaves geographic unaffected", SchematicV2LeavesGeographicUnaffected),
    ("old JSON without pathPoints still renders", OldJsonWithoutPathPointsStillRenders),
    ("geographic uses pathPoints when enabled", GeographicUsesPathPointsWhenEnabled),
    ("pathPoints de-duplication normalizes consecutive duplicates", PathPointsDeduplicationNormalizesConsecutiveDuplicates),
    ("renderer removes duplicate path points without mutating document", RendererRemovesDuplicatePathPoints),
    ("renderer simplifies nearly-collinear path points", RendererSimplifiesNearlyCollinearPathPoints),
    ("renderer preserves first and last path points", RendererPreservesFirstAndLastPathPoints),
    ("renderer splits suspicious path point jumps", RendererSplitsSuspiciousPathPointJumps),
    ("parallel corridor offset is off by default", ParallelCorridorOffsetIsOffByDefault),
    ("parallel corridor offset separates overlapping lines", ParallelCorridorOffsetSeparatesOverlappingLines),
    ("parallel corridor offset centers three shared services", ParallelCorridorOffsetCentersThreeSharedServices),
    ("parallel corridor offset detects reverse direction sharing", ParallelCorridorOffsetDetectsReverseDirectionSharing),
    ("parallel corridor offset ignores nearby non-overlapping segments", ParallelCorridorOffsetIgnoresNearbyNonOverlappingSegments),
    ("parallel corridor offset tapers at stations", ParallelCorridorOffsetTapersAtStations),
    ("service family merge combines Chinese service variants", ServiceFamilyMergeCombinesChineseServiceVariants),
    ("service family merge keeps different lines separate", ServiceFamilyMergeKeepsDifferentLinesSeparate),
    ("service family merge supports English bracket variants", ServiceFamilyMergeSupportsEnglishBracketVariants),
    ("service family merge chooses densest path as primary", ServiceFamilyMergeChoosesDensestPathAsPrimary),
    ("service family legend shows variant stop patterns", ServiceFamilyLegendShowsVariantStopPatterns),
    ("shared corridor style is off by default", SharedCorridorStyleIsOffByDefault),
    ("shared corridor style builds one continuous run", SharedCorridorStyleBuildsOneContinuousRun),
    ("shared corridor style ignores nearby non-overlapping families", SharedCorridorStyleIgnoresNearbyNonOverlappingFamilies),
    ("shared corridor style detects reverse direction sharing", SharedCorridorStyleDetectsReverseDirectionSharing),
    ("shared corridor style ends when family set changes", SharedCorridorStyleEndsWhenFamilySetChanges),
    ("shared corridor style skips three-family corridors", SharedCorridorStyleSkipsThreeFamilyCorridors),
    ("shared corridor style ignores same-family variants", SharedCorridorStyleIgnoresSameFamilyVariants),
    ("geographic corridor pipeline keeps single family continuous", GeographicCorridorPipelineKeepsSingleFamilyContinuous),
    ("geographic corridor pipeline keeps normal stroke width consistent", GeographicCorridorPipelineKeepsNormalStrokeWidthConsistent),
    ("shared corridor style merges near-touching runs", SharedCorridorStyleMergesNearTouchingRuns),
    ("corridor style widths use normalized tokens", CorridorStyleWidthsUseNormalizedTokens),
    ("station markers use readable white fill", StationMarkersUseReadableWhiteFill),
    ("station route anchoring projects ordinary stations to route segments", StationRouteAnchoringProjectsOrdinaryStationsToRouteSegments),
    ("station route anchoring falls back when route is too far", StationRouteAnchoringFallsBackWhenRouteIsTooFar),
    ("station route anchoring prefers segment projection over path point", StationRouteAnchoringPrefersSegmentProjectionOverPathPoint),
    ("station route anchoring averages close interchange anchors", StationRouteAnchoringAveragesCloseInterchangeAnchors),
    ("station route anchoring rejects spread-out interchange anchors", StationRouteAnchoringRejectsSpreadOutInterchangeAnchors),
    ("station route anchoring shares marker and label anchor metadata", StationRouteAnchoringSharesMarkerAndLabelAnchorMetadata),
    ("station route anchoring leaves schematic-lite raw", StationRouteAnchoringLeavesSchematicLiteRaw),
    ("express stripe does not change base route width", ExpressStripeDoesNotChangeBaseRouteWidth),
    ("shared corridor express conflict writes skip marker", SharedCorridorExpressConflictWritesSkipMarker),
    ("express center stripe marks express family", ExpressCenterStripeMarksExpressFamily),
    ("express center stripe ignores ordinary family", ExpressCenterStripeIgnoresOrdinaryFamily),
    ("size presets map to expected dimensions", SizePresetsMapToExpectedDimensions),
    ("snapshot path builder uses city slug and shared timestamp", SnapshotPathBuilderUsesCitySlugAndSharedTimestamp),
    ("snapshot path builder falls back to unnamed city", SnapshotPathBuilderFallsBackToUnnamedCity),
    ("snapshot path builder sanitizes invalid Windows filename characters", SnapshotPathBuilderSanitizesInvalidWindowsFilenameCharacters),
    ("snapshot path builder keeps latest paths stable and snapshots unique", SnapshotPathBuilderKeepsLatestPathsStableAndSnapshotsUnique),
    ("Bezier sampling helper returns stable curve points", BezierSamplingHelperReturnsStableCurvePoints),
    ("path point source fallback metadata loads", PathPointSourceFallbackMetadataLoads),
    ("generic station name detection covers default names", GenericStationNameDetectionCoversDefaultNames),
    ("crowded label hiding removes low priority labels only", CrowdedLabelHidingRemovesLowPriorityLabels),
    ("missing fields use documented fallbacks", MissingFieldsUseFallbacks),
    ("missing station references report a clear validation issue", MissingStationReferencesReportClearly),
    ("empty networks and empty lines do not crash", EmptyNetworksAndEmptyLinesDoNotCrash)
];

int failed = 0;
foreach ((string name, Action test) in tests)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

return failed == 0 ? 0 : 1;

static void AllSamplesLoadAndRender(IEnumerable<SampleExpectation> samples)
{
    foreach (SampleExpectation sample in samples)
    {
        RenderedSample rendered = LoadAndRenderSample(sample.FileName);
        Assert(rendered.LoadResult.IsValid, $"{sample.FileName}: {string.Join(Environment.NewLine, rendered.LoadResult.Errors)}");
        AssertValidSvg(rendered.Xml, sample.FileName);
        AssertSvgContains(rendered.Svg, $"{sample.CityName} Metro", sample.FileName);

        foreach (string expectedText in sample.ExpectedText)
        {
            AssertSvgContains(rendered.Svg, expectedText, sample.FileName);
        }

        IReadOnlyList<XElement> routes = GetRouteElements(rendered.Xml);
        Assert(routes.Count == sample.ExpectedRouteCount, $"{sample.FileName}: expected {sample.ExpectedRouteCount} route polylines but found {routes.Count}.");
        AssertEveryRenderableLineHasRoute(rendered.Document, routes, sample.FileName);
        AssertLegendDoesNotCoverRoutes(rendered.Xml, routes, sample.FileName);

        bool hasInterchange = rendered.Xml
            .Descendants()
            .Any(element => element.Name.LocalName == "circle"
                && ((string?)element.Attribute("class"))?.Contains("station interchange", StringComparison.Ordinal) == true
                && ReadDouble(element.Attribute("r")) >= 9);
        Assert(hasInterchange == sample.ExpectInterchange, $"{sample.FileName}: interchange marker expectation did not match.");
    }
}

static void MissingFieldsUseFallbacks()
{
    RenderedSample rendered = LoadAndRenderSample("sample-metro-missing-fields.json");

    Assert(rendered.Document.City?.Name == "Unnamed City", "Missing city name did not fall back to 'Unnamed City'.");
    Assert(rendered.Document.Network?.Stations?[0].Name == "Station 1", "First missing station name did not fall back to 'Station 1'.");
    Assert(rendered.Document.Network?.Stations?[2].Name == "Station 3", "Blank station name did not fall back to 'Station 3'.");
    Assert(rendered.Document.Network?.Lines?[0].Color == "#D71920", "Missing line color did not use the first palette color.");
    Assert(rendered.Document.Network?.Lines?[1].Name == "Line 2", "Missing line name did not fall back to 'Line 2'.");
    Assert(rendered.Document.Network?.Lines?[1].Mode == "metro", "Missing line mode did not fall back to 'metro'.");
    Assert(rendered.LoadResult.Warnings.Any(warning => warning.Contains("Missing station reference", StringComparison.Ordinal)), "Missing station reference was not reported.");
}

static void LegendSortsNumericLineNamesNaturally()
{
    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "Legend City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "Station A",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_10", "line_2", "line_airport", "line_1", "line_8"]
                },
                new MetroStation
                {
                    Id = "station_b",
                    Name = "Station B",
                    Position = new MetroPosition { X = 100, Z = 100 },
                    Lines = ["line_10", "line_2", "line_airport", "line_1", "line_8"]
                }
            ],
            Lines =
            [
                new MetroLine { Id = "line_10", Name = "10号线", Color = "#D71920", Stops = ["station_a", "station_b"] },
                new MetroLine { Id = "line_2", Name = "2号线", Color = "#0072BC", Stops = ["station_a", "station_b"] },
                new MetroLine { Id = "line_airport", Name = "Airport Express", Color = "#00A651", Stops = ["station_a", "station_b"] },
                new MetroLine { Id = "line_1", Name = "1号线", Color = "#F7941D", Stops = ["station_a", "station_b"] },
                new MetroLine { Id = "line_8", Name = "Metro Line 8", Color = "#92278F", Stops = ["station_a", "station_b"] }
            ]
        }
    };

    SvgRenderResult renderResult = new MetroSvgRenderer().Render(document);
    XDocument xml = XDocument.Parse(renderResult.Svg);
    XElement legend = xml
        .Descendants()
        .First(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "legend");
    List<string> labels = legend
        .Descendants()
        .Where(element => element.Name.LocalName == "text" && ((string?)element.Attribute("class")) == "legend-label")
        .Select(element => element.Value)
        .ToList();

    Assert(labels.SequenceEqual(["1号线", "2号线", "Metro Line 8", "10号线", "Airport Express"]), $"Legend order was not natural: {string.Join(", ", labels)}.");
}

static void RendererBuildsStableDiagramTitleFallbacks()
{
    MetroExportDocument placeholderDocument = CreateMinimalDocument("CS2 Metro Export");
    XDocument placeholderXml = XDocument.Parse(new MetroSvgRenderer().Render(placeholderDocument).Svg);

    string placeholderTitle = placeholderXml.Root?.Element(placeholderXml.Root.Name.Namespace + "title")?.Value ?? string.Empty;
    Assert(placeholderTitle == "CS2 Metro Diagram", $"Placeholder city title was unexpected: {placeholderTitle}");
    Assert(!placeholderXml.ToString().Contains("CS2 Metro Export Metro", StringComparison.Ordinal), "Placeholder title still repeated the export label.");

    MetroExportDocument unnamedDocument = CreateMinimalDocument(" ");
    XDocument unnamedXml = XDocument.Parse(new MetroSvgRenderer().Render(unnamedDocument).Svg);
    string unnamedTitle = unnamedXml.Root?.Element(unnamedXml.Root.Name.Namespace + "title")?.Value ?? string.Empty;
    Assert(unnamedTitle == "Unnamed City Metro Diagram", $"Unnamed city title was unexpected: {unnamedTitle}");
}

static void TransitMapStyleAddsOfficialMapFraming()
{
    MetroExportDocument document = CreateMinimalDocument("Frame City");
    SvgRenderOptions options = new()
    {
        MapStyle = SvgMapStyle.TransitMap,
        Width = 1400,
        Height = 900,
        LegendWidth = 240
    };

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    Assert((string?)xml.Root?.Attribute("data-map-style") == "transit-map", "Transit-map style did not mark the SVG root.");
    Assert(xml.Descendants().Any(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "transit-map-header"), "Transit-map style did not render the header band.");
    XElement legend = xml.Descendants().First(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "legend");
    Assert((string?)legend.Attribute("data-legend-placement") == "bottom", "Transit-map style did not move the legend to the bottom key panel.");
    Assert(xml.Descendants().Any(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "route-badges"), "Transit-map style did not render route badges.");
    Assert(xml.ToString().Contains("Transport System Map", StringComparison.Ordinal), "Transit-map style did not render the English map subtitle.");
    Assert(xml.ToString().Contains("Key to lines and symbols", StringComparison.Ordinal), "Transit-map style did not render the bottom key caption.");
    AssertValidSvg(xml, "transit-map framed SVG");
}

static void TransitMapStyleKeepsStandardStyleUnchanged()
{
    MetroExportDocument document = CreateMinimalDocument("Standard City");
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document).Svg);

    Assert((string?)xml.Root?.Attribute("data-map-style") == "standard", "Standard style root marker was unexpected.");
    Assert(!xml.Descendants().Any(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "transit-map-header"), "Standard style unexpectedly rendered the transit-map header.");
    XElement legend = xml.Descendants().First(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "legend");
    Assert(legend.Attribute("data-legend-placement") is null, "Standard style unexpectedly moved the legend to the bottom key panel.");
    Assert(!xml.Descendants().Any(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "route-badges"), "Standard style unexpectedly rendered route badges.");
    AssertValidSvg(xml, "standard framed SVG");
}

static void TransitMapLegendExplainsExpressMarker()
{
    MetroExportDocument document = CreateSchematicV2ServiceVariantSimplificationDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(
        SvgLayoutMode.SchematicV2,
        width: 980,
        height: 620,
        legendWidth: 220,
        mapStyle: SvgMapStyle.TransitMap);

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    Assert(xml.Descendants().Any(element => (string?)element.Attribute("data-legend-express-marker") == "white-center-stripe"), "Transit-map legend did not draw the express center stripe sample.");
    Assert(xml.Descendants().Any(element => element.Value.Contains("Express service marker", StringComparison.Ordinal)), "Transit-map legend did not explain the express center stripe.");
    AssertValidSvg(xml, "transit-map express legend SVG");
}

static void TransitMapRouteBadgesAvoidPlacedLabels()
{
    MetroExportDocument document = CreateTransitMapBadgeCollisionDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(
        SvgLayoutMode.SchematicV2,
        width: 980,
        height: 620,
        legendWidth: 220,
        mapStyle: SvgMapStyle.TransitMap);

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    IReadOnlyList<XElement> badges = xml
        .Descendants()
        .Where(element => element.Name.LocalName == "g" && (string?)element.Attribute("class") == "route-badge")
        .ToList();

    Assert(xml.Descendants().Any(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "route-badges"), "Transit-map route badge layer was not rendered.");
    Assert(badges.All(badge => ReadDouble(badge.Attribute("data-route-badge-placement-score")) < 5000), "A route badge was placed despite severe station-label collision.");
    AssertValidSvg(xml, "transit-map route badge collision SVG");
}

static void RendererSanitizesXmlTextValues()
{
    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "XML <City> & Test" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "Bad <Name> & \u0001 \ud800 End",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_1"]
                },
                new MetroStation
                {
                    Id = "station_b",
                    Name = "正常站",
                    Position = new MetroPosition { X = 100, Z = 100 },
                    Lines = ["line_1"]
                }
            ],
            Lines =
            [
                new MetroLine { Id = "line_1", Name = "1号线 <A&B>", Color = "#D71920", Stops = ["station_a", "station_b"] }
            ]
        }
    };

    SvgRenderResult renderResult = new MetroSvgRenderer().Render(document);
    XDocument xml = XDocument.Parse(renderResult.Svg);
    AssertValidSvg(xml, "XML text sanitization");
    Assert(xml.Descendants().Any(element => element.Value.Contains("Bad <Name> &", StringComparison.Ordinal)), "Escaped station text did not round-trip through XML parsing.");
    Assert(xml.Descendants().Any(element => element.Value.Contains("正常站", StringComparison.Ordinal)), "Unicode station text did not round-trip through XML parsing.");
}

static void SchematicLiteSnapsRoutePoints()
{
    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "Layout City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "A",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_test"]
                },
                new MetroStation
                {
                    Id = "station_b",
                    Name = "B",
                    Position = new MetroPosition { X = 84, Z = 29 },
                    Lines = ["line_test"]
                },
                new MetroStation
                {
                    Id = "station_c",
                    Name = "C",
                    Position = new MetroPosition { X = 148, Z = 112 },
                    Lines = ["line_test"]
                },
                new MetroStation
                {
                    Id = "station_d",
                    Name = "D",
                    Position = new MetroPosition { X = 210, Z = 119 },
                    Lines = ["line_test"]
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_test",
                    Name = "Test Line",
                    Color = "#D71920",
                    Stops = ["station_a", "station_b", "station_c", "station_d"]
                }
            ]
        }
    };

    SvgRenderOptions options = new()
    {
        LayoutMode = SvgLayoutMode.SchematicLite,
        Width = 640,
        Height = 480,
        Padding = 64,
        Margin = 64,
        LegendWidth = 160,
        GridSize = 32,
        SchematicMinimumStationSpacing = 1,
        EnableSchematicSegmentOverlapResolver = false
    };

    SvgRenderResult renderResult = new MetroSvgRenderer().Render(document, options);
    XDocument xml = XDocument.Parse(renderResult.Svg);
    AssertValidSvg(xml, "schematic-lite layout");

    XElement routes = xml
        .Descendants()
        .First(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "routes");
    Assert((string?)routes.Attribute("data-layout") == "schematic-lite", "Schematic-lite SVG did not record the layout mode.");

    XElement route = GetRouteElements(xml).Single();
    List<(double X, double Y)> points = SplitPoints((string?)route.Attribute("points")).ToList();
    Assert(points.Count == 4, "Schematic-lite route did not preserve the stop count.");

    foreach ((double x, double y) in points)
    {
        Assert(IsOnGrid(x, options.GridSize) || IsOnGrid(x, options.GridSize / 2), $"Schematic-lite x={x:0.###} was not on the grid or half-grid.");
        Assert(IsOnGrid(y, options.GridSize) || IsOnGrid(y, options.GridSize / 2), $"Schematic-lite y={y:0.###} was not on the grid or half-grid.");
    }

    for (int i = 1; i < points.Count; i++)
    {
        double dx = Math.Abs(points[i].X - points[i - 1].X);
        double dy = Math.Abs(points[i].Y - points[i - 1].Y);
        bool octilinear = dx < 0.001 || dy < 0.001 || Math.Abs(dx - dy) < 0.001;
        Assert(octilinear, $"Segment {i - 1}->{i} was not horizontal, vertical, or 45-degree diagonal.");
    }
}

static void SchematicLiteOffsetsTwoOverlappingSegments()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_a", "station_b"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions()).Svg);
    IReadOnlyList<XElement> overlapRoutes = GetSchematicOverlapRouteElements(xml);

    Assert(overlapRoutes.Count == 2, $"Two overlapping schematic families should render two offset segments, found {overlapRoutes.Count}.");
    Assert(overlapRoutes.All(route => (string?)route.Attribute("data-schematic-overlap-family-count") == "2"), "Two-family schematic overlap did not record family count 2.");
    Assert(overlapRoutes.Select(route => ReadDouble(route.Attribute("data-schematic-overlap-offset"))).Distinct().Count() == 2, "Two-family schematic overlap did not assign distinct offsets.");
    AssertValidSvg(xml, "schematic two-family overlap SVG");
}

static void SchematicLiteTrimsOverlappingSegmentEndpoints()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_a", "station_b"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions()).Svg);
    XElement stationA = GetStationCircle(xml, "station_a");
    XElement stationB = GetStationCircle(xml, "station_b");
    (double X, double Y) centerA = (ReadDouble(stationA.Attribute("cx")), ReadDouble(stationA.Attribute("cy")));
    (double X, double Y) centerB = (ReadDouble(stationB.Attribute("cx")), ReadDouble(stationB.Attribute("cy")));

    foreach (XElement route in GetSchematicOverlapRouteElements(xml))
    {
        List<(double X, double Y)> points = SplitPoints((string?)route.Attribute("points")).ToList();
        Assert((string?)route.Attribute("data-schematic-overlap-trim") == "true", "Overlapping schematic route did not record endpoint trimming.");
        Assert(ReadDouble(route.Attribute("data-schematic-overlap-trim-distance")) > 0, "Schematic overlap trim distance was not recorded.");
        Assert(Distance(points[0], centerA) > 8, "Trimmed overlap segment still begins at the station center.");
        Assert(Distance(points[^1], centerB) > 8, "Trimmed overlap segment still ends at the station center.");
    }
}

static void SchematicLiteTreatsReverseSegmentsAsOneOverlap()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_b", "station_a"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions()).Svg);
    IReadOnlyList<XElement> overlapRoutes = GetSchematicOverlapRouteElements(xml);
    List<string> segmentKeys = overlapRoutes
        .Select(route => (string?)route.Attribute("data-schematic-segment-key") ?? string.Empty)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    Assert(overlapRoutes.Count == 2, $"Reverse overlapping schematic families should render two offset segments, found {overlapRoutes.Count}.");
    Assert(segmentKeys.Count == 1, $"Reverse direction should share one undirected schematic segment key, found {segmentKeys.Count}.");
}

static void SchematicLiteIgnoresSameFamilyRepeatedSegments()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b", "station_a", "station_b"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions()).Svg);

    Assert(GetSchematicOverlapRouteElements(xml).Count == 0, "Same family repeated segment should not be counted as a multi-family schematic overlap.");
    AssertValidSvg(xml, "schematic repeated segment SVG");
}

static void SchematicLiteCentersThreeFamilyOverlapOffsets()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_a", "station_b"]),
        new SchematicLineSpec("line_10", "Line 10", ["station_a", "station_b"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions()).Svg);
    List<double> offsets = GetSchematicOverlapRouteElements(xml)
        .Select(route => ReadDouble(route.Attribute("data-schematic-overlap-offset")))
        .OrderBy(value => value)
        .ToList();

    Assert(offsets.Count == 3, $"Three-family schematic overlap should render three offset segments, found {offsets.Count}.");
    Assert(offsets[0] < 0, "Three-family schematic overlap did not include a negative offset.");
    AssertAlmostEqual(offsets[1], 0, "Three-family schematic overlap did not center one line.");
    Assert(offsets[2] > 0, "Three-family schematic overlap did not include a positive offset.");
    AssertAlmostEqual(Math.Abs(offsets[0]), offsets[2], "Three-family schematic overlap offsets were not centered.");
}

static void SchematicLiteIgnoresZeroLengthSegments()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_a"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions()).Svg);

    Assert(GetRouteElements(xml).Count == 0, "Zero-length schematic segment should not render a route polyline.");
    AssertValidSvg(xml, "schematic zero-length SVG");
}

static void SchematicLiteClampsTrimOnShortOverlapSegments()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_a", "station_b"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions(endpointTrim: 10000)).Svg);
    foreach (XElement route in GetSchematicOverlapRouteElements(xml))
    {
        List<(double X, double Y)> points = SplitPoints((string?)route.Attribute("points")).ToList();
        Assert(points.Count == 2, "Short schematic overlap fallback should still produce a two-point segment.");
        Assert(Distance(points[0], points[1]) > 0.1, "Short schematic overlap trim produced an invalid or reversed zero-length segment.");
        Assert((string?)route.Attribute("data-schematic-overlap-fallback") == "unsafe-short-or-junction", "Unsafe schematic overlap did not use conservative fallback.");
        Assert((string?)route.Attribute("data-schematic-overlap-safe-offset-reason") == "trim-too-short", "Unsafe schematic overlap did not record the trim-too-short safety reason.");
    }

    AssertValidSvg(xml, "schematic short trim fallback SVG");
}

static void SchematicLiteShortOverlapsUseCenteredFallback()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_a", "station_b"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions(shortThreshold: 10000)).Svg);
    foreach (XElement route in GetSchematicOverlapRouteElements(xml))
    {
        Assert((string?)route.Attribute("data-schematic-overlap-fallback") == "unsafe-short-or-junction", "Short schematic overlap did not record conservative fallback.");
        Assert((string?)route.Attribute("data-schematic-overlap-safe-offset") == "false", "Short schematic overlap should mark safe offset as false.");
        Assert((string?)route.Attribute("data-schematic-overlap-safe-offset-reason") == "short-segment", "Short schematic overlap did not record short-segment safety reason.");
        Assert((string?)route.Attribute("data-schematic-overlap-render-mode") == "centered", "Short schematic overlap did not switch to centered render mode.");
        AssertAlmostEqual(ReadDouble(route.Attribute("data-schematic-overlap-offset")), 0, "Short schematic overlap should not apply parallel offset.");
    }

    AssertValidSvg(xml, "schematic short overlap centered fallback SVG");
}

static void SchematicLitePreservesReverseShortOverlapContinuity()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b", "station_a"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_a", "station_b"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions(shortThreshold: 10000)).Svg);
    IReadOnlyList<XElement> overlapRoutes = GetSchematicOverlapRouteElements(xml);
    Assert(overlapRoutes.Count == 3, $"Reverse short overlap should preserve required route continuity segments, found {overlapRoutes.Count}.");
    Assert(overlapRoutes.Any(route => (string?)route.Attribute("data-display-family-key") == "Line 2"
        && (string?)route.Attribute("data-schematic-render-duplicate-count") == "2"
        && (string?)route.Attribute("data-schematic-render-dedupe-skipped") == "continuity-priority"), "Same-family reverse short overlap was not marked as retained for continuity.");
    Assert(overlapRoutes.All(route => (string?)route.Attribute("data-schematic-overlap-render-mode") == "centered"), "Short deduped overlap should remain in centered render mode.");
    AssertValidSvg(xml, "schematic reverse duplicate short overlap SVG");
}

static void SchematicLiteLongOverlapsStillUseOffsetMode()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_a", "station_b"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions(shortThreshold: 1)).Svg);
    IReadOnlyList<XElement> overlapRoutes = GetSchematicOverlapRouteElements(xml);

    Assert(overlapRoutes.Count == 2, $"Long schematic overlap should still render two offset segments, found {overlapRoutes.Count}.");
    Assert(overlapRoutes.All(route => (string?)route.Attribute("data-schematic-overlap-render-mode") == "offset"), "Long schematic overlap should keep offset render mode.");
    Assert(overlapRoutes.Select(route => ReadDouble(route.Attribute("data-schematic-overlap-offset"))).Distinct().Count() == 2, "Long schematic overlap did not preserve distinct offsets.");
}

static void SchematicLiteTrimDoesNotAffectNonOverlapSegments()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_b", "station_c"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions()).Svg);

    Assert(GetRouteElements(xml).All(route => route.Attribute("data-schematic-overlap-trim") is null), "Non-overlap schematic segment unexpectedly received trim metadata.");
    Assert(GetRouteElements(xml).All(route => route.Attribute("data-schematic-overlap") is null), "Non-overlap schematic segment unexpectedly received overlap metadata.");
}

static void SchematicLiteFallsBackConservativelyNearTJunctions()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_a", "station_b"]),
        new SchematicLineSpec("line_10", "Line 10", ["station_b", "station_c"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions()).Svg);
    XElement stationB = GetStationCircle(xml, "station_b");
    (double X, double Y) junctionCenter = (ReadDouble(stationB.Attribute("cx")), ReadDouble(stationB.Attribute("cy")));

    foreach (XElement route in GetSchematicOverlapRouteElements(xml))
    {
        List<(double X, double Y)> points = SplitPoints((string?)route.Attribute("points")).ToList();
        Assert(points.Any(point => Distance(point, junctionCenter) < 0.001), "Conservative T-junction fallback should preserve a centered connection into the station.");
        Assert((string?)route.Attribute("data-schematic-overlap-fallback") == "unsafe-short-or-junction", "T-junction overlap did not use conservative fallback.");
        Assert((string?)route.Attribute("data-schematic-overlap-safe-offset-reason") == "high-degree-junction", "T-junction overlap did not record the high-degree junction reason.");
    }

    Assert(HasStationCircle(xml, "station_b"), "T-junction station marker disappeared.");
}

static void SchematicLiteOverlapResolverDoesNotAffectGeographic()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_a", "station_b"]));

    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.Geographic);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    Assert(GetRouteElements(xml).All(route => route.Attribute("data-schematic-overlap") is null), "Geographic output should not include schematic overlap attributes.");
    Assert(GetRouteElements(xml).Count == 2, "Geographic output should keep normal route-level polylines for this test.");
}

static void SchematicLiteStationMarkersStayCenteredWhileRoutesOffset()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_a", "station_b"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions()).Svg);
    XElement station = GetStationCircle(xml, "station_a");
    List<XElement> overlapRoutes = GetSchematicOverlapRouteElements(xml).ToList();
    List<(double X, double Y)> routeStarts = overlapRoutes
        .Select(route => SplitPoints((string?)route.Attribute("points")).First())
        .ToList();

    double stationX = ReadDouble(station.Attribute("cx"));
    double stationY = ReadDouble(station.Attribute("cy"));
    Assert(routeStarts.All(point => Distance(point, (stationX, stationY)) > 8), "Offset schematic routes should be trimmed away from the station center.");
    AssertAlmostEqual(stationY, routeStarts.Average(point => point.Y), "Station marker Y should stay centered between offset schematic routes.");
}

static void SchematicLiteStationMarkerCentersUnchangedByOverlapResolver()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_a", "station_b"]),
        new SchematicLineSpec("line_10", "Line 10", ["station_b", "station_c"]));

    SvgRenderOptions resolverOff = CreateSchematicOverlapTestOptions(enableResolver: false);
    SvgRenderOptions resolverOn = CreateSchematicOverlapTestOptions();

    XDocument offXml = XDocument.Parse(new MetroSvgRenderer().Render(document, resolverOff).Svg);
    XDocument onXml = XDocument.Parse(new MetroSvgRenderer().Render(document, resolverOn).Svg);

    foreach (string stationId in new[] { "station_a", "station_b", "station_c" })
    {
        XElement offStation = GetStationCircle(offXml, stationId);
        XElement onStation = GetStationCircle(onXml, stationId);
        AssertAlmostEqual(ReadDouble(offStation.Attribute("cx")), ReadDouble(onStation.Attribute("cx")), $"Station {stationId} cx changed after overlap resolver.");
        AssertAlmostEqual(ReadDouble(offStation.Attribute("cy")), ReadDouble(onStation.Attribute("cy")), $"Station {stationId} cy changed after overlap resolver.");
    }
}

static void SchematicLiteStationSpacingSeparatesCloseStations()
{
    MetroExportDocument document = CreateCloseSchematicStationDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(width: 700, height: 460, legendWidth: 160, minStationSpacing: 40);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    XElement stationA = GetStationCircle(xml, "station_a");
    XElement stationB = GetStationCircle(xml, "station_b");
    (double X, double Y) centerA = (ReadDouble(stationA.Attribute("cx")), ReadDouble(stationA.Attribute("cy")));
    (double X, double Y) centerB = (ReadDouble(stationB.Attribute("cx")), ReadDouble(stationB.Attribute("cy")));

    Assert(Distance(centerA, centerB) >= 39.9, "Schematic station spacing did not separate close stations to the requested minimum.");
    Assert((string?)stationA.Attribute("data-schematic-station-adjusted") == "true"
        || (string?)stationB.Attribute("data-schematic-station-adjusted") == "true", "Adjusted station marker did not record spacing metadata.");
    AssertValidSvg(xml, "schematic station spacing SVG");
}

static void SchematicLiteStationSpacingMovesMarkersLabelsAndRoutesTogether()
{
    MetroExportDocument document = CreateCloseSchematicStationDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(width: 700, height: 460, legendWidth: 160, minStationSpacing: 40);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    XElement stationA = GetStationCircle(xml, "station_a");
    XElement stationB = GetStationCircle(xml, "station_b");
    XElement adjustedStation = (string?)stationB.Attribute("data-schematic-station-adjusted") == "true" ? stationB : stationA;
    string adjustedStationId = (string?)adjustedStation.Attribute("data-station-id") ?? "station_a";
    (double X, double Y) adjustedCenter = (ReadDouble(adjustedStation.Attribute("cx")), ReadDouble(adjustedStation.Attribute("cy")));
    XElement adjustedLabel = xml.Descendants()
        .First(element => element.Name.LocalName == "text"
            && (string?)element.Attribute("data-station-id") == adjustedStationId
            && (string?)element.Attribute("class") == "station-label");
    XElement route = GetRouteElements(xml).Single();
    List<(double X, double Y)> routePoints = SplitPoints((string?)route.Attribute("points")).ToList();

    Assert(routePoints.Any(point => Distance(point, adjustedCenter) < 0.001), "Schematic route did not use the adjusted station position.");
    Assert((string?)adjustedLabel.Attribute("data-schematic-station-adjusted") == "true", "Station label did not carry schematic adjustment metadata.");
    Assert(Math.Abs(ReadDouble(adjustedLabel.Attribute("y")) - adjustedCenter.Y) < 28, "Station label was not placed from the adjusted station position.");
}

static void SchematicLiteStationSpacingLimitsHighDegreeStationMovement()
{
    MetroExportDocument document = CreateHighDegreeCloseSchematicStationDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(width: 760, height: 520, legendWidth: 180, minStationSpacing: 100);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    XElement hub = GetStationCircle(xml, "hub");
    double adjustment = ReadDouble(hub.Attribute("data-schematic-station-adjustment-distance"));
    Assert(adjustment <= options.GridSize * 0.75 + 0.001, "High-degree schematic station moved beyond the conservative cap.");
    AssertValidSvg(xml, "schematic high-degree station spacing SVG");
}

static void SchematicLiteOverlapResolverKeepsLoopAndBranchSamplesValid()
{
    foreach (string sample in new[] { "sample-metro-loop.json", "sample-metro-branch.json" })
    {
        string samplePath = Path.Combine(FindRepositoryRoot(), "samples", sample);
        MetroLoadResult loadResult = MetroJsonLoader.LoadFromFile(samplePath);
        Assert(loadResult.Document is not null, $"{sample}: sample did not load.");
        SvgRenderOptions options = CreateSchematicOverlapTestOptions(width: 1200, height: 800, legendWidth: 280);
        XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(loadResult.Document!, options).Svg);
        AssertValidSvg(xml, $"{sample} schematic overlap resolver");
    }
}

static void SchematicV2PreservesStopOrderAndAdjacency()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b", "station_c"]));

    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, minStationSpacing: 64);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    XElement route = GetRouteElements(xml).Single();
    List<(double X, double Y)> routePoints = SplitPoints((string?)route.Attribute("points")).ToList();

    Assert((string?)xml.Descendants().First(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "routes").Attribute("data-layout") == "schematic-v2", "Schematic-v2 did not record its layout mode.");
    Assert(routePoints.Count == 3, "Schematic-v2 route did not preserve stop count/order.");
    Assert(Distance(routePoints[0], GetStationCenter(xml, "station_a")) < 0.001, "Schematic-v2 route first point is not station A.");
    Assert(Distance(routePoints[1], GetStationCenter(xml, "station_b")) < 0.001, "Schematic-v2 route second point is not station B.");
    Assert(Distance(routePoints[2], GetStationCenter(xml, "station_c")) < 0.001, "Schematic-v2 route third point is not station C.");
    AssertValidSvg(xml, "schematic-v2 stop order SVG");
}

static void SchematicV2PreservesInterchangeNode()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]),
        new SchematicLineSpec("line_8", "Line 8", ["station_c", "station_b"]));

    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, minStationSpacing: 64);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    (double X, double Y) interchange = GetStationCenter(xml, "station_b");

    foreach (XElement route in GetRouteElements(xml))
    {
        Assert(SplitPoints((string?)route.Attribute("points")).Any(point => Distance(point, interchange) < 0.001), "Schematic-v2 route did not use the shared interchange node.");
    }
}

static void SchematicV2KeepsOldSchematicLiteAvailable()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicLite)).Svg);
    XElement routes = xml.Descendants().First(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "routes");
    Assert((string?)routes.Attribute("data-layout") == "schematic-lite", "Old schematic-lite layout mode is no longer available.");
}

static void SchematicV2SpacingDoesNotReverseCloseStations()
{
    MetroExportDocument document = CreateCloseSchematicStationDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 700, height: 460, legendWidth: 160, minStationSpacing: 40);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    XElement route = GetRouteElements(xml).Single();
    List<(double X, double Y)> routePoints = SplitPoints((string?)route.Attribute("points")).ToList();

    Assert(Distance(routePoints[0], GetStationCenter(xml, "station_a")) < 0.001, "Schematic-v2 close station order was reversed at the first stop.");
    Assert(Distance(routePoints[1], GetStationCenter(xml, "station_b")) < 0.001, "Schematic-v2 close station order was reversed at the second stop.");
}

static void SchematicV2RelaxesSharpDetourStation()
{
    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "Sharp Detour City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation { Id = "station_a", Name = "A", Position = new MetroPosition { X = 0, Z = 0 }, Lines = ["line_3"] },
                new MetroStation { Id = "station_b", Name = "B", Position = new MetroPosition { X = 120, Z = -110 }, Lines = ["line_3"] },
                new MetroStation { Id = "station_c", Name = "C", Position = new MetroPosition { X = -320, Z = -30 }, Lines = ["line_3"] },
                new MetroStation { Id = "station_d", Name = "D", Position = new MetroPosition { X = -520, Z = 120 }, Lines = ["line_3"] }
            ],
            Lines =
            [
                new MetroLine { Id = "line_3", Name = "Line 3", Color = "#D71920", Stops = ["station_a", "station_b", "station_c", "station_d"] }
            ]
        }
    };

    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 760, height: 520, legendWidth: 180);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    XElement route = GetRouteElements(xml).Single();
    List<(double X, double Y)> routePoints = SplitPoints((string?)route.Attribute("points")).ToList();

    Assert(routePoints.Count == 4, "Sharp detour route did not preserve the stop count.");
    double detourRatio = (Distance(routePoints[0], routePoints[1]) + Distance(routePoints[1], routePoints[2])) / Math.Max(1, Distance(routePoints[0], routePoints[2]));
    double turnAngle = CalculateTurnAngleDegrees(routePoints[0], routePoints[1], routePoints[2]);
    Assert(detourRatio < 1.55 || turnAngle >= 58, $"Schematic-v2 kept a sharp detour: ratio={detourRatio:0.###}, angle={turnAngle:0.###}.");
    AssertValidSvg(xml, "schematic-v2 sharp detour relaxation SVG");
}

static void SchematicV2UsesTopologyRichFamilyVariantForSharedCorridors()
{
    MetroExportDocument document = CreateSchematicV2SharedCorridorDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 900, height: 560, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    XElement line10 = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "10号线");
    List<(double X, double Y)> points = SplitPoints((string?)line10.Attribute("points")).ToList();

    Assert((string?)line10.Attribute("data-line-id") == "line_10_local", "Schematic-v2 did not render the topology-rich 10号线 service variant.");
    Assert(points.Any(point => Distance(point, GetStationCenter(xml, "station_shared_start")) < 0.001), "Schematic-v2 10号线 route skipped the shared corridor start station.");
    Assert(points.Any(point => Distance(point, GetStationCenter(xml, "station_shared_end")) < 0.001), "Schematic-v2 10号线 route skipped the shared corridor end station.");
}

static void SchematicV2PreservesSharedCorridorEdge()
{
    MetroExportDocument document = CreateSchematicV2SharedCorridorDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 900, height: 560, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    (double X, double Y) sharedStart = GetStationCenter(xml, "station_shared_start");
    (double X, double Y) sharedEnd = GetStationCenter(xml, "station_shared_end");

    XElement line2 = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "2号线");
    XElement line10 = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "10号线");

    Assert(ContainsAdjacentSegment(SplitPoints((string?)line2.Attribute("points")).ToList(), sharedStart, sharedEnd), "2号线 did not preserve the shared corridor adjacency.");
    Assert(ContainsAdjacentSegment(SplitPoints((string?)line10.Attribute("points")).ToList(), sharedStart, sharedEnd), "10号线 did not preserve the shared corridor adjacency.");
    AssertValidSvg(xml, "schematic-v2 shared corridor SVG");
}

static void SchematicV2SharedEdgeGuidesDoNotInjectNeighboringBranchStations()
{
    MetroExportDocument document = CreateSchematicV2BranchingSharedEdgeDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    XElement line3 = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "Line 3");
    List<(double X, double Y)> line3Points = SplitPoints((string?)line3.Attribute("points")).ToList();
    (double X, double Y) sharedStart = GetStationCenter(xml, "station_shared_start");
    (double X, double Y) sharedEnd = GetStationCenter(xml, "station_shared_end");
    (double X, double Y) line4Before = GetStationCenter(xml, "station_line4_before");
    (double X, double Y) line4After = GetStationCenter(xml, "station_line4_after");

    Assert(ContainsAdjacentSegment(line3Points, sharedStart, sharedEnd), "Line 3 did not preserve the actual shared edge.");
    Assert(!line3Points.Any(point => Distance(point, line4Before) < 0.001), "Line 3 route guide incorrectly injected Line 4's preceding branch station.");
    Assert(!line3Points.Any(point => Distance(point, line4After) < 0.001), "Line 3 route guide incorrectly injected Line 4's following branch station.");
    IReadOnlyList<XElement> singleEdgeOverlays = GetSchematicV2ParallelCorridorElements(xml)
        .Where(element => (string?)element.Attribute("data-schematic-v2-parallel-corridor-source") == "exact-shared-platform")
        .ToList();
    Assert(singleEdgeOverlays.Count == 2, $"A single shared edge should render two exact shared platform overlays, found {singleEdgeOverlays.Count}.");
    Assert(singleEdgeOverlays.All(element => int.TryParse((string?)element.Attribute("data-schematic-v2-shared-corridor-point-count"), out int count) && count == 2), "Single-edge exact shared platform overlays should report two corridor points.");
    AssertValidSvg(xml, "schematic-v2 shared edge branch guard SVG");
}

static void SchematicV2RendersExactSharedPlatformCorridors()
{
    MetroExportDocument document = CreateSchematicV2ExactSharedPlatformDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    IReadOnlyList<XElement> platformCorridors = GetSchematicV2ParallelCorridorElements(xml)
        .Where(element => (string?)element.Attribute("data-schematic-v2-parallel-corridor-source") == "exact-shared-platform")
        .ToList();

    Assert(platformCorridors.Count == 2, $"Expected two exact shared platform overlays, but found {platformCorridors.Count}.");
    Assert(platformCorridors.Select(element => (string?)element.Attribute("data-display-family-key")).OrderBy(value => value).SequenceEqual(["Line 3", "Line 4"]), "Exact shared platform overlays were not rendered for both families.");
    Assert(platformCorridors.All(element => (string?)element.Attribute("data-schematic-v2-parallel-platform") == "true"), "Exact shared platform overlays did not carry the platform debug attribute.");
    Assert(platformCorridors.All(element => (string?)element.Attribute("data-schematic-v2-canonical-corridor") == "true"), "Exact shared platform overlays should be marked as canonical corridor output.");
    Assert(platformCorridors.All(element => int.TryParse((string?)element.Attribute("data-schematic-v2-shared-corridor-point-count"), out int count) && count == 3), "Exact shared platform corridor should cover the full three-station shared chain.");
    Assert(!platformCorridors.Any(element => (string?)element.Attribute("data-schematic-v2-route-guide-materialized") == "true"), "Exact shared platform rendering should not claim route-guide materialization.");
    AssertValidSvg(xml, "schematic-v2 exact shared platform SVG");
}

static void SchematicV2RendersExactSharedEdgeWithExpressFamily()
{
    MetroExportDocument document = CreateSchematicV2ExpressExactSharedEdgeDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    IReadOnlyList<XElement> platformCorridors = GetSchematicV2ParallelCorridorElements(xml)
        .Where(element => (string?)element.Attribute("data-schematic-v2-parallel-corridor-source") == "exact-shared-platform")
        .ToList();

    Assert(platformCorridors.Count == 2, $"Expected two exact shared-edge overlays with an express family, but found {platformCorridors.Count}.");
    List<string?> overlayFamilies = platformCorridors
        .Select(element => (string?)element.Attribute("data-display-family-key"))
        .OrderBy(value => value)
        .ToList();
    Assert(overlayFamilies.ToHashSet(StringComparer.Ordinal).SetEquals(["Line 7", "Line 10"]), $"Exact shared-edge overlays did not include the express and ordinary families: {string.Join(", ", overlayFamilies)}.");
    Assert(platformCorridors.All(element => int.TryParse((string?)element.Attribute("data-schematic-v2-shared-corridor-point-count"), out int count) && count == 2), "Single exact shared-edge overlays should report two corridor points.");

    IReadOnlyList<XElement> expressOverlays = GetExpressMarkerElements(xml)
        .Where(element => (string?)element.Attribute("class") == "express-decoration schematic-v2-parallel-corridor-express"
            && (string?)element.Attribute("data-display-family-key") == "Line 10")
        .ToList();
    Assert(expressOverlays.Count == 1, "Express family exact shared edge should keep its white center stripe on the overlay.");
    AssertValidSvg(xml, "schematic-v2 exact shared edge with express family SVG");
}

static void SchematicV2RendersThreeFamilyExactSharedEdgeVisibly()
{
    MetroExportDocument document = CreateSchematicV2ThreeFamilyExactSharedEdgeDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    IReadOnlyList<XElement> platformCorridors = GetSchematicV2ParallelCorridorElements(xml)
        .Where(element => (string?)element.Attribute("data-schematic-v2-parallel-corridor-source") == "exact-shared-platform")
        .ToList();

    Assert(platformCorridors.Count == 3, $"Expected three exact shared-edge overlays, but found {platformCorridors.Count}.");
    Assert(platformCorridors.All(element => (string?)element.Attribute("data-schematic-v2-shared-corridor-family-count") == "3"), "Three-family exact shared edge did not record the family count.");
    List<double> offsets = platformCorridors
        .Select(element => ReadDouble(element.Attribute("data-schematic-v2-parallel-offset")))
        .OrderBy(value => value)
        .ToList();
    Assert(offsets[0] < 0 && Math.Abs(offsets[1]) < 0.001 && offsets[2] > 0, $"Three-family exact shared edge offsets were not centered: {string.Join(", ", offsets)}.");
    AssertValidSvg(xml, "schematic-v2 three-family exact shared edge SVG");
}

static void SchematicV2DetectsGeometrySharedCorridorForSkipStopService()
{
    MetroExportDocument document = CreateSchematicV2GeometrySharedCorridorDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    XElement local = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "Line 2");
    XElement express = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "Line 10");
    (double X, double Y) corridorMid = GetStationCenter(xml, "station_corridor_mid");
    (double X, double Y) corridorMid2 = GetStationCenter(xml, "station_corridor_mid_2");
    (double X, double Y) corridorEnd = GetStationCenter(xml, "station_corridor_end");

    List<(double X, double Y)> localPoints = SplitPoints((string?)local.Attribute("points")).ToList();
    List<(double X, double Y)> expressPoints = SplitPoints((string?)express.Attribute("points")).ToList();

    Assert(localPoints.Any(point => Distance(point, corridorMid) < 0.001), "Local corridor route did not include the corridor mid station.");
    Assert(expressPoints.Any(point => Distance(point, corridorMid) < 0.001), "Skip-stop express route did not reuse the shared corridor mid station.");
    Assert(expressPoints.Any(point => Distance(point, corridorMid2) < 0.001), "Skip-stop express route did not reuse the second shared corridor mid station.");
    Assert(ContainsAdjacentSegment(expressPoints, corridorMid, corridorMid2) && ContainsAdjacentSegment(expressPoints, corridorMid2, corridorEnd), "Skip-stop express route did not stay on the shared corridor before diverging.");
}

static void SchematicV2ChoosesCanonicalServiceRouteByStopCount()
{
    MetroExportDocument document = CreateSchematicV2ServiceVariantSimplificationDocument();
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(
        document,
        CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220)).Svg);

    XElement route = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "Line 10");
    List<(double X, double Y)> routePoints = SplitPoints((string?)route.Attribute("points")).ToList();

    Assert((string?)route.Attribute("data-line-id") == "line_10_local", "Schematic-v2 did not choose the all-stop/local service as the canonical route.");
    Assert(routePoints.Count == 5, "Canonical schematic-v2 route should use the full all-stop service stop count.");
    Assert(routePoints.Any(point => Distance(point, GetStationCenter(xml, "station_b")) < 0.001), "Canonical route skipped station B.");
    Assert(routePoints.Any(point => Distance(point, GetStationCenter(xml, "station_c")) < 0.001), "Canonical route skipped station C.");
    Assert((string?)route.Attribute("data-schematic-v2-canonical-route") == "line_10_local", "Canonical route debug attribute was missing.");
    AssertValidSvg(xml, "schematic-v2 canonical service route SVG");
}

static void SchematicV2HidesExpressVariantGeometry()
{
    MetroExportDocument document = CreateSchematicV2ServiceVariantSimplificationDocument();
    MetroLine expressSource = document.Network!.Lines!.Single(line => line.Id == "line_10_express");
    Assert(expressSource.Stops!.SequenceEqual(["station_a", "station_d", "station_f"]), "Fixture express raw stops were unexpectedly changed.");

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(
        document,
        CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220)).Svg);
    IReadOnlyList<XElement> familyRoutes = GetRouteElements(xml)
        .Where(route => (string?)route.Attribute("data-display-family-key") == "Line 10")
        .ToList();

    Assert(familyRoutes.Count == 1, $"Schematic-v2 should render one canonical Line 10 route, but found {familyRoutes.Count}.");
    Assert(familyRoutes.All(route => (string?)route.Attribute("data-line-id") != "line_10_express"), "Express variant leaked as independent schematic-v2 route geometry.");
    Assert(((string?)familyRoutes.Single().Attribute("data-schematic-v2-hidden-service-variants"))?.Contains("Line 10 (Express)", StringComparison.Ordinal) == true, "Hidden express variant was not recorded in SVG diagnostics.");
}

static void SchematicV2MarksExpressServiceFamilyWithCenterStripe()
{
    MetroExportDocument document = CreateSchematicV2ServiceVariantSimplificationDocument();
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(
        document,
        CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220)).Svg);
    IReadOnlyList<XElement> stripes = GetExpressMarkerElements(xml)
        .Where(element => (string?)element.Attribute("data-display-family-key") == "Line 10")
        .ToList();

    Assert(stripes.Count == 1, $"Schematic-v2 should render one center stripe for the express service family, but found {stripes.Count}.");
    Assert((string?)stripes.Single().Attribute("data-schematic-v2-express-marker") == "white-center-stripe", "Schematic-v2 express marker debug attribute was missing.");
    Assert(GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "Line 10").Attribute("data-schematic-v2-express-service-family") is not null, "Base route did not record express service family metadata.");
    AssertSvgContains(xml.ToString(), ".express-decoration { fill: none;", "Schematic-v2 express marker style");
}

static void CanonicalSchematicNetworkSelectsServiceFamilyRoute()
{
    MetroExportDocument document = CreateSchematicV2ServiceVariantSimplificationDocument();

    CanonicalSchematicNetwork network = CanonicalSchematicNetworkBuilder.Build(document);
    CanonicalServiceFamily family = network.Families.Single(family => family.FamilyKey == "Line 10");

    Assert(family.CanonicalLineId == "line_10_local", "Canonical network should choose the all-stop Line 10 local variant.");
    Assert(family.CanonicalStops.SequenceEqual(["station_a", "station_b", "station_c", "station_d", "station_e"]), "Canonical stops should preserve the all-stop service order.");
    Assert(family.HasExpressService, "Canonical network should record that the family has an express service variant.");
    Assert(family.Variants.Single(variant => variant.LineId == "line_10_express").IsExpressService, "Express variant metadata was not detected.");
    Assert(family.Variants.Single(variant => variant.LineId == "line_10_local").IsCanonical, "Local variant should be marked canonical.");
    Assert(network.Stations["station_a"].FamilyKeys.SequenceEqual(["Line 10"]), "Station family membership should collapse variants into one service family.");
}

static void CanonicalSchematicNetworkRecordsExactSharedEdges()
{
    MetroExportDocument document = CreateSchematicV2ExactSharedPlatformDocument();

    CanonicalSchematicNetwork network = CanonicalSchematicNetworkBuilder.Build(document);
    CanonicalCorridorHint sharedEdge = network.CorridorHints.Single(hint =>
        hint.Source == "exact-shared-adjacent-stop-edge"
        && hint.StationIds.SequenceEqual(["station_shared_a", "station_shared_b"]));

    Assert(sharedEdge.FamilyKeys.Count == 2, "Exact shared edge should include both display families.");
    Assert(sharedEdge.Confidence == 1, "Exact shared edge should have full confidence.");
    Assert(network.AdjacencyEdges.Count(edge => edge.EdgeKey == "station_shared_a|station_shared_b") == 2, "Adjacency should preserve one edge per involved family.");
    Assert(network.InterchangeGroups.Any(group => group.StationId == "station_shared_a"), "Shared station should be represented as an interchange/corridor group node.");
}

static void CanonicalSchematicNetworkRecordsGeometryCorridorHints()
{
    MetroExportDocument document = CreateSchematicV2GeometrySharedCorridorDocument();

    CanonicalSchematicNetwork network = CanonicalSchematicNetworkBuilder.Build(document);
    CanonicalCorridorHint geometryHint = network.CorridorHints.Single(hint =>
        hint.Source == "geometry-pathPoints-corridor"
        && hint.FamilyKeys.Contains("Line 2")
        && hint.FamilyKeys.Contains("Line 10"));

    Assert(geometryHint.ApproximateSharedLength >= 300, "Geometry corridor should record a substantial shared pathPoints length.");
    Assert(geometryHint.AverageDistance <= 1, "Synthetic shared pathPoints corridor should have near-zero average distance.");
    Assert(geometryHint.Confidence > 0.35, "Geometry corridor confidence should be strong enough for downstream schematic constraints.");
}

static void SchematicV2ReconstructsFollowerRouteChainWithPassThroughNodes()
{
    MetroExportDocument document = CreateSchematicV2GeometrySharedCorridorDocument();
    MetroLine expressSource = document.Network!.Lines!.Single(line => line.Id == "line_10_express");
    Assert(expressSource.Stops!.SequenceEqual(["station_local_start", "station_corridor_mid", "station_corridor_end", "station_line10_terminal"]), "Fixture express raw stops were unexpectedly changed.");

    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    XElement express = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "Line 10");
    List<(double X, double Y)> points = SplitPoints((string?)express.Attribute("points")).ToList();

    (double X, double Y) start = GetStationCenter(xml, "station_local_start");
    (double X, double Y) midA = GetStationCenter(xml, "station_corridor_mid");
    (double X, double Y) midB = GetStationCenter(xml, "station_corridor_mid_2");
    (double X, double Y) end = GetStationCenter(xml, "station_corridor_end");
    int startIndex = FindPointIndex(points, start);
    int midAIndex = FindPointIndex(points, midA);
    int midBIndex = FindPointIndex(points, midB);
    int endIndex = FindPointIndex(points, end);

    Assert(points.Count > expressSource.Stops!.Count, "Follower render route chain did not gain pass-through guide nodes.");
    Assert(startIndex >= 0 && midAIndex >= 0 && midBIndex >= 0 && endIndex >= 0, "Follower render route chain does not include the full host corridor interval.");
    Assert(startIndex < midAIndex && midAIndex < midBIndex && midBIndex < endIndex, "Follower render route chain does not preserve host corridor order.");
    Assert(((string?)express.Attribute("data-schematic-v2-route-guide-stations"))?.Contains("station_corridor_mid_2", StringComparison.Ordinal) == true, "Follower route guide debug attributes do not include the second pass-through node.");
    Assert(!expressSource.Stops.Contains("station_corridor_mid_2"), "Pass-through nodes leaked into raw express stops.");
    AssertValidSvg(xml, "schematic-v2 route chain reconstruction SVG");
}

static void SchematicV2MaterializesRouteGuideAsParallelCorridor()
{
    MetroExportDocument document = CreateSchematicV2GeometrySharedCorridorDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    IReadOnlyList<XElement> parallelCorridors = GetSchematicV2ParallelCorridorElements(xml);

    Assert(parallelCorridors.Count == 2, $"Expected two parallel corridor overlays for the materialized shared run, but found {parallelCorridors.Count}.");
    Assert(parallelCorridors.All(element => (string?)element.Attribute("data-schematic-v2-route-guide-materialized") == "true"), "Parallel corridor did not mark route guide materialization.");
    Assert(parallelCorridors.All(element => (string?)element.Attribute("data-schematic-v2-canonical-corridor") == "true"), "Materialized route-guide overlays should be marked as canonical corridor output.");
    Assert(parallelCorridors.All(element => ((string?)element.Attribute("data-schematic-v2-pass-through-stations"))?.Contains("station_corridor_mid", StringComparison.Ordinal) == true), "Parallel corridor did not record the pass-through corridor station.");
    Assert(parallelCorridors.All(element => ((string?)element.Attribute("data-schematic-v2-pass-through-stations"))?.Contains("station_corridor_mid_2", StringComparison.Ordinal) == true), "Parallel corridor did not record the second pass-through corridor station.");
    Assert(parallelCorridors.All(element => int.TryParse((string?)element.Attribute("data-schematic-v2-shared-corridor-point-count"), out int count) && count > 2), "Parallel corridor still looks like a short two-point overlay.");
    Assert(parallelCorridors.Select(element => (string?)element.Attribute("data-display-family-key")).OrderBy(value => value).SequenceEqual(["Line 10", "Line 2"]), "Parallel corridor was not rendered for both display families.");
    AssertValidSvg(xml, "schematic-v2 parallel corridor SVG");
}

static void SchematicV2SharedCorridorIsStableAcrossSizePresets()
{
    MetroExportDocument document = CreateSchematicV2GeometrySharedCorridorDocument();
    XDocument standard = XDocument.Parse(new MetroSvgRenderer().Render(
        document,
        CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 2200, height: 1400, legendWidth: 240)).Svg);
    XDocument poster = XDocument.Parse(new MetroSvgRenderer().Render(
        document,
        CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 3200, height: 2000, legendWidth: 240)).Svg);

    IReadOnlyList<XElement> standardCorridors = GetSchematicV2ParallelCorridorElements(standard)
        .Where(element => (string?)element.Attribute("data-schematic-v2-parallel-corridor-source") == "geometry-route-guide")
        .ToList();
    IReadOnlyList<XElement> posterCorridors = GetSchematicV2ParallelCorridorElements(poster)
        .Where(element => (string?)element.Attribute("data-schematic-v2-parallel-corridor-source") == "geometry-route-guide")
        .ToList();

    Assert(standardCorridors.Count == 2, $"Standard schematic-v2 should preserve the materialized geometry corridor, but found {standardCorridors.Count} overlays.");
    Assert(posterCorridors.Count == 2, $"Poster schematic-v2 should preserve the materialized geometry corridor, but found {posterCorridors.Count} overlays.");
    Assert(
        standardCorridors.Select(element => (string?)element.Attribute("data-display-family-key")).OrderBy(value => value).SequenceEqual(
            posterCorridors.Select(element => (string?)element.Attribute("data-display-family-key")).OrderBy(value => value)),
        "Schematic-v2 materialized different corridor families at different output sizes.");
    Assert(standardCorridors.All(element => (string?)element.Attribute("data-schematic-v2-route-guide-materialized") == "true"), "Standard schematic-v2 did not materialize the route guide.");
    Assert(posterCorridors.All(element => (string?)element.Attribute("data-schematic-v2-route-guide-materialized") == "true"), "Poster schematic-v2 did not materialize the route guide.");
    Assert(standardCorridors.All(element => (string?)element.Attribute("data-schematic-v2-pass-through-stations") == (string?)posterCorridors.First().Attribute("data-schematic-v2-pass-through-stations")), "Standard schematic-v2 did not keep the same pass-through corridor station chain.");
    Assert(standardCorridors.All(element => int.TryParse((string?)element.Attribute("data-schematic-v2-shared-corridor-point-count"), out int count) && count > 2), "Standard schematic-v2 collapsed the corridor into a short overlay.");
    AssertValidSvg(standard, "standard schematic-v2 size-stable corridor SVG");
    AssertValidSvg(poster, "poster schematic-v2 size-stable corridor SVG");
}

static void SchematicV2RendersExpressStripeOnParallelCorridor()
{
    MetroExportDocument document = CreateSchematicV2GeometrySharedCorridorWithExpressFamilyDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    IReadOnlyList<XElement> corridorStripes = GetExpressMarkerElements(xml)
        .Where(element => (string?)element.Attribute("data-schematic-v2-parallel-corridor-express-marker") == "true")
        .ToList();

    Assert(corridorStripes.Count == 1, $"Expected one express stripe on the materialized Line 10 shared corridor, but found {corridorStripes.Count}.");
    XElement stripe = corridorStripes.Single();
    Assert((string?)stripe.Attribute("data-display-family-key") == "Line 10", "Parallel corridor express stripe used the wrong family.");
    Assert((string?)stripe.Attribute("data-schematic-v2-shared-corridor-family-b") == "Line 2", "Parallel corridor express stripe did not keep shared corridor metadata.");
    Assert(int.TryParse((string?)stripe.Attribute("data-schematic-v2-shared-corridor-point-count"), out int count) && count > 2, "Parallel corridor express stripe was not drawn across the materialized corridor run.");
    Assert(ReadStrokeWidth(stripe) < 8.68, "Parallel corridor express stripe should remain an internal white marker.");
    AssertValidSvg(xml, "schematic-v2 parallel corridor express stripe SVG");
}

static void SchematicV2NormalizesCanonicalBacktrackingRouteChains()
{
    MetroExportDocument document = CreateSchematicV2BacktrackingCanonicalRouteDocument();
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(
        document,
        CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220)).Svg);

    XElement route = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "Line 10");
    List<(double X, double Y)> points = SplitPoints((string?)route.Attribute("points")).ToList();

    Assert((string?)route.Attribute("data-line-id") == "line_10_local", "Backtracking fixture did not select the canonical local route.");
    Assert(points.Count == 4, $"Backtracking canonical route should be normalized to one visible direction, but rendered {points.Count} points.");
    Assert(points.Any(point => Distance(point, GetStationCenter(xml, "station_d")) < 0.001), "Normalized route lost the far terminal.");
    Assert(FindPointIndex(points, GetStationCenter(xml, "station_d")) == points.Count - 1, "Normalized route still contains points after the far terminal.");
    AssertValidSvg(xml, "schematic-v2 normalized backtracking route SVG");
}

static void SchematicV2StraightensZigzagTerminalTails()
{
    MetroExportDocument document = CreateSchematicV2ZigzagTerminalTailDocument();
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(
        document,
        CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 1600, height: 1000, legendWidth: 220)).Svg);

    XElement route = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "Tail Line");
    List<(double X, double Y)> points = SplitPoints((string?)route.Attribute("points")).ToList();

    (double X, double Y) anchor = GetStationCenter(xml, "station_anchor");
    (double X, double Y) terminal = GetStationCenter(xml, "station_terminal");
    (double X, double Y) bendA = GetStationCenter(xml, "station_bend_a");
    (double X, double Y) bendB = GetStationCenter(xml, "station_bend_b");

    Assert(points.Count == 4, $"Terminal tail fixture should render four route points, but rendered {points.Count}.");
    Assert(DistancePointToLine(bendA, anchor, terminal) < 0.001, "First terminal-tail bend was not straightened onto the anchor-terminal line.");
    Assert(DistancePointToLine(bendB, anchor, terminal) < 0.001, "Second terminal-tail bend was not straightened onto the anchor-terminal line.");
    Assert(FindPointIndex(points, anchor) == 0, "Terminal tail straightening moved the anchor out of route order.");
    Assert(FindPointIndex(points, terminal) == points.Count - 1, "Terminal tail straightening moved the terminal out of route order.");
    AssertValidSvg(xml, "schematic-v2 straightened terminal tail SVG");
}

static void SchematicV2DoesNotMistakeSingleCrossingsForSharedCorridors()
{
    MetroExportDocument document = CreateSchematicV2CrossingOnlyDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    XElement vertical = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-display-family-key") == "Line 10");
    List<(double X, double Y)> points = SplitPoints((string?)vertical.Attribute("points")).ToList();

    Assert(points.Count == 2, "Single-point crossing was incorrectly expanded into a shared corridor guide.");
}

static void SchematicV2DoesNotRenderParallelCorridorForSingleCrossing()
{
    MetroExportDocument document = CreateSchematicV2CrossingOnlyDocument();
    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, width: 980, height: 620, legendWidth: 220);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);

    Assert(GetSchematicV2ParallelCorridorElements(xml).Count == 0, "Single-point crossing incorrectly rendered a schematic-v2 parallel corridor.");
}

static void SchematicV2DoesNotMergeNonSharedLines()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "2号线", ["station_a", "station_b"]),
        new SchematicLineSpec("line_10", "10号线", ["station_b", "station_c"]));

    SvgRenderOptions options = CreateSchematicOverlapTestOptions(SvgLayoutMode.SchematicV2, minStationSpacing: 64);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    IReadOnlyList<XElement> routes = GetRouteElements(xml);

    Assert(routes.Count == 2, "Non-shared schematic-v2 lines should remain separate display routes.");
    Assert(!ContainsAdjacentSegment(
        SplitPoints((string?)routes.Single(route => (string?)route.Attribute("data-display-family-key") == "2号线").Attribute("points")).ToList(),
        GetStationCenter(xml, "station_b"),
        GetStationCenter(xml, "station_c")),
        "2号线 was incorrectly merged onto the 10号线-only edge.");
}

static void SchematicV2LeavesGeographicUnaffected()
{
    MetroExportDocument document = CreateSchematicOverlapDocument(
        new SchematicLineSpec("line_2", "Line 2", ["station_a", "station_b"]));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateSchematicOverlapTestOptions(SvgLayoutMode.Geographic)).Svg);
    XElement routes = xml.Descendants().First(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "routes");
    Assert((string?)routes.Attribute("data-layout") == "geographic", "Geographic layout was affected by schematic-v2.");
    Assert(GetRouteElements(xml).All(route => route.Attribute("data-schematic-station-adjusted") is null), "Geographic routes should not include schematic-v2 station adjustment attributes.");
}

static void OldJsonWithoutPathPointsStillRenders()
{
    RenderedSample rendered = LoadAndRenderSample("sample-metro-small.json");
    MetroLine line = rendered.Document.Network!.Lines!.Single();
    Assert(line.PathPoints is not null, "Missing pathPoints did not normalize to an empty list.");
    Assert(line.PathPoints!.Count == 0, "Old sample unexpectedly loaded pathPoints.");
    XElement route = GetRouteElements(rendered.Xml).Single();
    Assert((string?)route.Attribute("data-route-source") == "stops", "Old sample route did not render from stops.");
}

static void GeographicUsesPathPointsWhenEnabled()
{
    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "Path Test City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "A",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_path"]
                },
                new MetroStation
                {
                    Id = "station_b",
                    Name = "B",
                    Position = new MetroPosition { X = 100, Z = 0 },
                    Lines = ["line_path"]
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_path",
                    Name = "Path Line",
                    Color = "#D71920",
                    Stops = ["station_a", "station_b"],
                    PathPoints =
                    [
                        new MetroPathPoint { X = 0, Z = 0 },
                        new MetroPathPoint { X = 50, Z = 100 },
                        new MetroPathPoint { X = 100, Z = 0 }
                    ]
                }
            ]
        }
    };

    XDocument stopsXml = XDocument.Parse(new MetroSvgRenderer().Render(document).Svg);
    XElement stopsRoute = GetRouteElements(stopsXml).Single();
    Assert((string?)stopsRoute.Attribute("data-route-source") == "stops", "Path points were used even though UsePathPoints was not enabled.");
    Assert(SplitPoints((string?)stopsRoute.Attribute("points")).Count() == 2, "Stops route did not contain two station points.");

    SvgRenderOptions pathOptions = new() { UsePathPoints = true };
    XDocument pathXml = XDocument.Parse(new MetroSvgRenderer().Render(document, pathOptions).Svg);
    XElement pathRoute = GetRouteElements(pathXml).Single();
    Assert((string?)pathRoute.Attribute("data-route-source") == "pathPoints", "Geographic route did not use pathPoints when enabled.");
    Assert(SplitPoints((string?)pathRoute.Attribute("points")).Count() == 3, "Path route did not preserve the three path points.");

    SvgRenderOptions schematicOptions = new() { LayoutMode = SvgLayoutMode.SchematicLite, UsePathPoints = true };
    XDocument schematicXml = XDocument.Parse(new MetroSvgRenderer().Render(document, schematicOptions).Svg);
    XElement schematicRoute = GetRouteElements(schematicXml).Single();
    Assert((string?)schematicRoute.Attribute("data-route-source") == "stops", "Schematic-lite should continue to render from stops by default.");
}

static void PathPointsDeduplicationNormalizesConsecutiveDuplicates()
{
    string samplePath = Path.Combine(FindRepositoryRoot(), "samples", "sample-metro-pathpoints.json");
    MetroLoadResult loadResult = MetroJsonLoader.LoadFromFile(samplePath);
    Assert(loadResult.IsValid, string.Join(Environment.NewLine, loadResult.Errors));
    MetroLine line = loadResult.Document!.Network!.Lines!.Single();
    Assert(line.PathPoints is not null, "Path point sample did not load pathPoints.");
    Assert(line.PathPoints!.Count == 4, $"Expected duplicate path point to be removed, but found {line.PathPoints.Count} pathPoints.");

    SvgRenderOptions options = new() { UsePathPoints = true };
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(loadResult.Document, options).Svg);
    XElement route = GetRouteElements(xml).Single();
    Assert((string?)route.Attribute("data-route-source") == "pathPoints", "Path point sample did not render from pathPoints when enabled.");
    Assert(SplitPoints((string?)route.Attribute("points")).Count() == 4, "Rendered path point route did not use the normalized path point count.");
}

static void RendererRemovesDuplicatePathPoints()
{
    MetroExportDocument document = CreatePathPointCleanupDocument(
    [
        new MetroPathPoint { X = 0, Z = 0 },
        new MetroPathPoint { X = 10, Z = 0 },
        new MetroPathPoint { X = 10.0001, Z = 0.0001 },
        new MetroPathPoint { X = 20, Z = 10 },
        new MetroPathPoint { X = 30, Z = 0 }
    ]);

    MetroLine line = document.Network!.Lines!.Single();
    int originalCount = line.PathPoints!.Count;
    SvgRenderOptions options = new()
    {
        UsePathPoints = true,
        MinPathSegmentLength = 0.01,
        PathPointSimplificationTolerance = 0.01
    };

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    XElement route = GetRouteElements(xml).Single();

    Assert((string?)route.Attribute("data-route-source") == "pathPoints", "Duplicate cleanup route did not use pathPoints.");
    Assert(ReadInt(route.Attribute("data-path-point-count")) == originalCount, "Original path point count was not recorded.");
    Assert(ReadInt(route.Attribute("data-cleaned-path-point-count")) == 4, "Consecutive near-duplicate path point was not removed.");
    Assert(line.PathPoints.Count == originalCount, "Renderer mutated the original pathPoints list.");
}

static void RendererSimplifiesNearlyCollinearPathPoints()
{
    MetroExportDocument document = CreatePathPointCleanupDocument(
    [
        new MetroPathPoint { X = 0, Z = 0 },
        new MetroPathPoint { X = 25, Z = 0.1 },
        new MetroPathPoint { X = 50, Z = -0.1 },
        new MetroPathPoint { X = 75, Z = 0.1 },
        new MetroPathPoint { X = 100, Z = 0 }
    ]);

    SvgRenderOptions options = new()
    {
        UsePathPoints = true,
        MinPathSegmentLength = 0.01,
        PathPointSimplificationTolerance = 1
    };

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    XElement route = GetRouteElements(xml).Single();

    Assert(ReadInt(route.Attribute("data-path-point-count")) == 5, "Original nearly-collinear path point count was not recorded.");
    Assert(ReadInt(route.Attribute("data-cleaned-path-point-count")) == 2, "Nearly-collinear path points were not simplified.");
    Assert(SplitPoints((string?)route.Attribute("points")).Count() == 2, "Simplified path did not render with two points.");
}

static void RendererPreservesFirstAndLastPathPoints()
{
    MetroExportDocument document = CreatePathPointCleanupDocument(
    [
        new MetroPathPoint { X = 0, Z = 0 },
        new MetroPathPoint { X = 0.1, Z = 0.1 },
        new MetroPathPoint { X = 0.2, Z = 0.2 },
        new MetroPathPoint { X = 100, Z = 200 }
    ]);

    SvgRenderOptions options = new()
    {
        UsePathPoints = true,
        MinPathSegmentLength = 10,
        PathPointSimplificationTolerance = 1,
        Width = 640,
        Height = 480,
        Padding = 64,
        Margin = 64,
        LegendWidth = 120
    };

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    XElement route = GetRouteElements(xml).Single();
    List<(double X, double Y)> points = SplitPoints((string?)route.Attribute("points")).ToList();

    Assert(points.Count == 2, "First/last preservation test expected the cleaned path to contain two rendered points.");
    Assert(points[0].X < points[^1].X, "Cleaned path did not keep the original first point before the last point.");
    Assert(points[0].Y > points[^1].Y, "Cleaned path did not preserve the original high-Z last point.");
}

static void RendererSplitsSuspiciousPathPointJumps()
{
    MetroExportDocument document = CreatePathPointCleanupDocument(
    [
        new MetroPathPoint { X = 0, Z = 0 },
        new MetroPathPoint { X = 10, Z = 0.1 },
        new MetroPathPoint { X = 20, Z = -0.1 },
        new MetroPathPoint { X = 30, Z = 0.1 },
        new MetroPathPoint { X = 40, Z = 0 },
        new MetroPathPoint { X = 1000, Z = 1000 },
        new MetroPathPoint { X = 1010, Z = 1000.1 },
        new MetroPathPoint { X = 1020, Z = 999.9 },
        new MetroPathPoint { X = 1030, Z = 1000.1 },
        new MetroPathPoint { X = 1040, Z = 1000 }
    ]);

    int originalCount = document.Network!.Lines!.Single().PathPoints!.Count;
    SvgRenderOptions options = new()
    {
        UsePathPoints = true,
        Width = 1200,
        Height = 800,
        Padding = 80,
        Margin = 80,
        LegendWidth = 240
    };

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    IReadOnlyList<XElement> routes = GetRouteElements(xml);

    Assert(routes.Count == 2, $"Suspicious jump should split the route into two polylines, but found {routes.Count}.");
    foreach (XElement route in routes)
    {
        Assert((string?)route.Attribute("data-route-source") == "pathPoints", "Split route did not keep pathPoints as the source.");
        Assert(ReadInt(route.Attribute("data-path-point-count")) == originalCount, "Original path point count diagnostic was not preserved on split routes.");
        Assert(ReadInt(route.Attribute("data-cleaned-path-point-count")) < originalCount, "Suspicious jump test expected path simplification to reduce the point count.");
        Assert(ReadInt(route.Attribute("data-suspicious-jump-count")) == 1, "Suspicious jump diagnostic count was not emitted.");
        Assert(ReadDouble(route.Attribute("data-path-reduction-ratio")) > 0, "Reduction ratio diagnostic was not emitted.");
        Assert(ReadDouble(route.Attribute("data-max-path-segment-length")) > 0, "Max segment length diagnostic was not emitted.");
        Assert((string?)route.Attribute("data-route-part-count") == "2", "Split route did not record route part count.");
        Assert(MaxSegmentLength(SplitPoints((string?)route.Attribute("points")).ToList()) < 300, "A split route still contains the suspicious long jump.");
    }

    AssertValidSvg(xml, "suspicious path point jump SVG");
}

static void ParallelCorridorOffsetIsOffByDefault()
{
    MetroExportDocument document = CreateParallelCorridorDocument(
        [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }],
        [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }]);
    SvgRenderOptions options = CreateCorridorTestOptions(enableOffset: false);

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    IReadOnlyList<XElement> routes = GetRouteElements(xml);

    Assert(routes.Count == 2, $"Default corridor rendering should keep two route polylines, but found {routes.Count}.");
    Assert(routes.All(route => route.Attribute("data-parallel-corridor-offset") is null), "Parallel corridor debug attributes appeared while the option was disabled.");
    Assert((string?)routes[0].Attribute("points") == (string?)routes[1].Attribute("points"), "Overlapping routes changed while parallel corridor offset was disabled.");
}

static void ParallelCorridorOffsetSeparatesOverlappingLines()
{
    MetroExportDocument document = CreateParallelCorridorDocument(
        [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }],
        [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }]);
    SvgRenderOptions options = CreateCorridorTestOptions(enableOffset: true);

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    IReadOnlyList<XElement> routes = GetRouteElements(xml);
    IReadOnlyList<XElement> offsetRoutes = routes
        .Where(route => (string?)route.Attribute("data-parallel-corridor-offset") == "true")
        .ToList();

    Assert(offsetRoutes.Count == 4, $"Expected four offset segment fragments for two overlapping two-segment routes, but found {offsetRoutes.Count}.");
    Assert(offsetRoutes.All(route => !string.IsNullOrWhiteSpace((string?)route.Attribute("data-corridor-id"))), "Offset routes did not include corridor ids.");
    Assert(offsetRoutes.All(route => ReadInt(route.Attribute("data-corridor-member-count")) == 2), "Two-line corridor did not record member count 2.");
    Assert(offsetRoutes.Select(route => (string?)route.Attribute("points")).Distinct(StringComparer.Ordinal).Count() > 2, "Offset routes did not separate overlapping geometry.");
    AssertValidSvg(xml, "parallel two-line corridor SVG");
}

static void ParallelCorridorOffsetCentersThreeSharedServices()
{
    MetroExportDocument document = CreateParallelCorridorDocument(
        [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }],
        [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }],
        [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }]);
    SvgRenderOptions options = CreateCorridorTestOptions(enableOffset: true);

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    List<double> offsetIndexes = GetRouteElements(xml)
        .Where(route => (string?)route.Attribute("data-parallel-corridor-offset") == "true")
        .Select(route => ReadDouble(route.Attribute("data-corridor-offset-index")))
        .Distinct()
        .OrderBy(value => value)
        .ToList();
    List<double> offsetPixels = GetRouteElements(xml)
        .Where(route => (string?)route.Attribute("data-parallel-corridor-offset") == "true")
        .Select(route => ReadDouble(route.Attribute("data-corridor-offset-px")))
        .Distinct()
        .OrderBy(value => value)
        .ToList();

    Assert(offsetIndexes.SequenceEqual([-1, 0, 1]), $"Three-line corridor offsets were not centered: {string.Join(", ", offsetIndexes)}.");
    Assert(offsetPixels.Count == 3 && offsetPixels[0] < 0 && Math.Abs(offsetPixels[1]) < 0.001 && offsetPixels[2] > 0, "Three-line corridor pixel offsets were not centered around zero.");
}

static void ParallelCorridorOffsetDetectsReverseDirectionSharing()
{
    MetroExportDocument document = CreateParallelCorridorDocument(
        [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }],
        [new MetroPathPoint { X = 100, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 0, Z = 0 }]);
    SvgRenderOptions options = CreateCorridorTestOptions(enableOffset: true);

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    int offsetRouteCount = GetRouteElements(xml)
        .Count(route => (string?)route.Attribute("data-parallel-corridor-offset") == "true");

    Assert(offsetRouteCount == 4, $"Reverse-direction shared corridor expected four offset fragments, but found {offsetRouteCount}.");
}

static void ParallelCorridorOffsetIgnoresNearbyNonOverlappingSegments()
{
    MetroExportDocument document = CreateParallelCorridorDocument(
        [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }],
        [new MetroPathPoint { X = 145, Z = 0 }, new MetroPathPoint { X = 195, Z = 0 }, new MetroPathPoint { X = 245, Z = 0 }]);
    SvgRenderOptions options = CreateCorridorTestOptions(enableOffset: true);

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    Assert(GetRouteElements(xml).All(route => route.Attribute("data-corridor-id") is null), "Non-overlapping nearby segments were incorrectly grouped into a shared corridor.");
}

static void ParallelCorridorOffsetTapersAtStations()
{
    MetroExportDocument document = CreateParallelCorridorDocument(
        [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }],
        [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 50, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }]);

    SvgRenderOptions noOffsetOptions = CreateCorridorTestOptions(enableOffset: false);
    XDocument noOffsetXml = XDocument.Parse(new MetroSvgRenderer().Render(document, noOffsetOptions).Svg);
    List<(double X, double Y)> originalLinePoints = SplitPoints((string?)GetRouteElements(noOffsetXml)
        .First(route => (string?)route.Attribute("data-line-id") == "line_1")
        .Attribute("points")).ToList();

    SvgRenderOptions offsetOptions = CreateCorridorTestOptions(enableOffset: true);
    XDocument offsetXml = XDocument.Parse(new MetroSvgRenderer().Render(document, offsetOptions).Svg);
    XElement firstOffsetFragment = GetRouteElements(offsetXml)
        .First(route => (string?)route.Attribute("data-line-id") == "line_1"
            && (string?)route.Attribute("data-parallel-corridor-offset") == "true");
    List<(double X, double Y)> offsetFragmentPoints = SplitPoints((string?)firstOffsetFragment.Attribute("points")).ToList();

    AssertAlmostEqual(offsetFragmentPoints[0].X, originalLinePoints[0].X, "Corridor offset did not taper to the station X.");
    AssertAlmostEqual(offsetFragmentPoints[0].Y, originalLinePoints[0].Y, "Corridor offset did not taper to the station Y.");
    Assert(Distance(offsetFragmentPoints[1], originalLinePoints[1]) > 1, "Corridor offset did not separate away from the station.");
}

static void ServiceFamilyMergeCombinesChineseServiceVariants()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10_express", "10号线（特快）", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_10_rapid", "10号线（大站快车）", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(12, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateFamilyTestOptions()).Svg);
    IReadOnlyList<XElement> routes = GetRouteElements(xml);

    Assert(routes.Count == 1, $"Merged Chinese service family should render one route, but found {routes.Count}.");
    XElement route = routes.Single();
    Assert((string?)route.Attribute("data-display-family-key") == "10号线", "Chinese family key was not normalized to 10号线.");
    Assert(ReadInt(route.Attribute("data-display-family-member-count")) == 2, "Chinese service family did not record two members.");
    Assert((string?)route.Attribute("data-display-family-merged") == "true", "Chinese service family did not mark merged route.");
    AssertSvgContains(xml.ToString(), "特快", "Chinese family legend");
    AssertSvgContains(xml.ToString(), "大站快车", "Chinese family legend");
    AssertValidSvg(xml, "Chinese service family SVG");
}

static void ServiceFamilyMergeKeepsDifferentLinesSeparate()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_11", "11号线", ["station_airport", "station_sports"], GeneratePathPoints(8, 12)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateFamilyTestOptions()).Svg);
    IReadOnlyList<XElement> routes = GetRouteElements(xml);

    Assert(routes.Count == 2, $"Different line families should render two routes, but found {routes.Count}.");
    Assert(routes.All(route => ReadInt(route.Attribute("data-display-family-member-count")) == 1), "Different line families were unexpectedly merged.");
    Assert(routes.Select(route => (string?)route.Attribute("data-display-family-key")).OrderBy(value => value).SequenceEqual(["10号线", "11号线"]), "Different line family keys were unexpected.");
}

static void ServiceFamilyMergeSupportsEnglishBracketVariants()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10_express", "Line 10 (Express)", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_10_local", "Line 10 (Local)", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(10, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateFamilyTestOptions()).Svg);
    XElement route = GetRouteElements(xml).Single();

    Assert((string?)route.Attribute("data-display-family-key") == "Line 10", "English family key was not normalized to Line 10.");
    AssertSvgContains(xml.ToString(), "Express", "English family legend");
    AssertSvgContains(xml.ToString(), "Local", "English family legend");
}

static void ServiceFamilyMergeChoosesDensestPathAsPrimary()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_a", "10号线（短线）", ["station_airport", "station_sports"], GeneratePathPoints(10, 0)),
        new ServiceLineSpec("line_b", "10号线（长线）", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(100, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateFamilyTestOptions()).Svg);
    XElement route = GetRouteElements(xml).Single();

    Assert((string?)route.Attribute("data-display-family-primary-line-id") == "line_b", "Service family did not choose the pathPoints-densest line as primary.");
    Assert((string?)route.Attribute("data-line-id") == "line_b", "Rendered route did not use the primary line id.");
}

static void ServiceFamilyLegendShowsVariantStopPatterns()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10_express", "10号线（机场快线-特快）", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_10_rapid", "10号线（机场快线-大站快车）", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(12, 0)));

    string svg = new MetroSvgRenderer().Render(document, CreateFamilyTestOptions()).Svg;
    XDocument xml = XDocument.Parse(svg);

    AssertSvgContains(svg, "机场快线-特快: 2 stops, 机场北 -&gt; 体育西路", "Service family variant legend");
    AssertSvgContains(svg, "机场快线-大站快车: 3 stops, 机场北 -&gt; 体育西路", "Service family variant legend");
    AssertValidSvg(xml, "Service family legend SVG");
}

static void SharedCorridorStyleIsOffByDefault()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_18", "18号线", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateFamilyTestOptions()).Svg);

    Assert(!HasSharedCorridorStyle(xml), "Shared corridor style appeared while the option was disabled.");
}

static void SharedCorridorStyleBuildsOneContinuousRun()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_18", "18号线", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(8, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions()).Svg);
    IReadOnlyList<XElement> sharedRoutes = GetSharedCorridorRouteElements(xml);
    List<string> runIds = sharedRoutes
        .Select(route => (string?)route.Attribute("data-shared-corridor-run-id") ?? string.Empty)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    Assert(runIds.Count == 1, $"Long shared corridor should render as one continuous run, but found {runIds.Count} runs.");
    Assert(sharedRoutes.Count == 2, $"Shanghai-like shared corridor should render two continuous layers, but found {sharedRoutes.Count} route elements.");
    Assert(sharedRoutes.All(route => (string?)route.Attribute("data-shared-corridor-style") == "shanghai-like-continuous"), "Shared corridor did not use the continuous Shanghai-like style marker.");
    Assert(sharedRoutes.Any(route => (string?)route.Attribute("data-shared-corridor-layer") == "corridor-base"), "Shared corridor did not include a corridor base layer.");
    Assert(sharedRoutes.Any(route => (string?)route.Attribute("data-shared-corridor-layer") == "inner-band"), "Shared corridor did not include an inner band layer.");
    Assert(sharedRoutes.All(route => ReadInt(route.Attribute("data-shared-corridor-point-count")) >= 8), "Shared corridor run did not preserve the continuous path points.");
    Assert(sharedRoutes.All(route => (string?)route.Attribute("data-shared-corridor-family-a") == "10号线"), "Shared corridor family A was not stable by display name.");
    Assert(sharedRoutes.All(route => (string?)route.Attribute("data-shared-corridor-family-b") == "18号线"), "Shared corridor family B was not stable by display name.");
    AssertValidSvg(xml, "continuous shared corridor SVG");
}

static void SharedCorridorStyleIgnoresNearbyNonOverlappingFamilies()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_18", "18号线", ["station_airport", "station_sports"], GeneratePathPoints(8, 0).Select(point => new MetroPathPoint { X = point.X + 145, Z = point.Z }).ToList()));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions()).Svg);

    Assert(!HasSharedCorridorStyle(xml), "Nearby non-overlapping families incorrectly produced shared corridor style.");
}

static void SharedCorridorStyleDetectsReverseDirectionSharing()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_18", "18号线", ["station_sports", "station_airport"], GeneratePathPoints(8, 0).AsEnumerable().Reverse().ToList()));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions()).Svg);

    Assert(HasSharedCorridorStyle(xml), "Reverse-direction shared corridor did not produce shared corridor style.");
}

static void SharedCorridorStyleEndsWhenFamilySetChanges()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_18", "18号线", ["station_airport", "station_mid"], GeneratePathPoints(4, 0).Select(point => new MetroPathPoint { X = point.X * 0.5, Z = point.Z }).ToList()),
        new ServiceLineSpec("line_22", "22号线", ["station_mid", "station_sports"], GeneratePathPoints(5, 0).Select(point => new MetroPathPoint { X = 50 + point.X * 0.5, Z = point.Z }).ToList()));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions()).Svg);
    List<string> runIds = GetSharedCorridorRouteElements(xml)
        .Select(route => (string?)route.Attribute("data-shared-corridor-run-id") ?? string.Empty)
        .Distinct(StringComparer.Ordinal)
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToList();

    Assert(runIds.Count == 2, $"Family-set changes should split the shared corridor into two runs, but found {runIds.Count}.");
}

static void SharedCorridorStyleSkipsThreeFamilyCorridors()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_18", "18号线", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_22", "22号线", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions()).Svg);

    Assert(!HasSharedCorridorStyle(xml), "Three-family corridor should not produce v1 shared corridor style.");
    Assert(xml.Descendants().Any(element => (string?)element.Attribute("data-shared-corridor-skipped") == "too-many-families"), "Three-family corridor did not emit the too-many-families debug skip attribute.");
}

static void SharedCorridorStyleIgnoresSameFamilyVariants()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10_express", "10号线（特快）", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_10_local", "10号线（大站快车）", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(8, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions()).Svg);
    IReadOnlyList<XElement> routes = GetRouteElements(xml);

    Assert(!HasSharedCorridorStyle(xml), "Same-family variants should be merged before shared corridor detection.");
    Assert(routes.Select(route => (string?)route.Attribute("data-display-family-key")).Distinct(StringComparer.Ordinal).SequenceEqual(["10号线"]), "Same-family variants were not reduced to one display family.");
}

static void GeographicCorridorPipelineKeepsSingleFamilyContinuous()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(24, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions()).Svg);
    IReadOnlyList<XElement> normalRoutes = GetRouteElements(xml)
        .Where(route => (string?)route.Attribute("data-corridor-run-layer") == "normal-base")
        .ToList();

    Assert(normalRoutes.Count == 1, $"Single-family continuous path should render as one normal run, but found {normalRoutes.Count}.");
    Assert(SplitPoints((string?)normalRoutes.Single().Attribute("points")).Count() == 24, "Single-family continuous run did not preserve the path points.");
}

static void GeographicCorridorPipelineKeepsNormalStrokeWidthConsistent()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(24, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions()).Svg);
    List<double> normalWidths = GetRouteElements(xml)
        .Where(route => (string?)route.Attribute("data-corridor-run-layer") == "normal-base")
        .Select(ReadStrokeWidth)
        .Distinct()
        .ToList();

    Assert(normalWidths.SequenceEqual([14]), $"Normal route stroke widths were not consistent: {string.Join(", ", normalWidths)}.");
}

static void SharedCorridorStyleMergesNearTouchingRuns()
{
    List<MetroPathPoint> pathPoints =
    [
        new() { X = 0, Z = 0 },
        new() { X = 50, Z = 0 },
        new() { X = 55, Z = 0 },
        new() { X = 105, Z = 0 }
    ];
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_mid", "station_sports"], pathPoints),
        new ServiceLineSpec("line_18", "18号线", ["station_airport", "station_mid", "station_sports"], pathPoints));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions()).Svg);
    List<string> runIds = GetSharedCorridorRouteElements(xml)
        .Select(route => (string?)route.Attribute("data-shared-corridor-run-id") ?? string.Empty)
        .Distinct(StringComparer.Ordinal)
        .ToList();

    Assert(runIds.Count == 1, $"Near-touching shared corridor fragments should merge into one visual run, but found {runIds.Count}.");
}

static void CorridorStyleWidthsUseNormalizedTokens()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(12, 0)),
        new ServiceLineSpec("line_18", "18号线", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(12, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions()).Svg);
    IReadOnlyList<XElement> sharedRoutes = GetSharedCorridorRouteElements(xml);
    double sharedOuterWidth = sharedRoutes
        .Where(route => (string?)route.Attribute("data-shared-corridor-layer") == "corridor-base")
        .Select(ReadStrokeWidth)
        .Single();
    double sharedInnerWidth = sharedRoutes
        .Where(route => (string?)route.Attribute("data-shared-corridor-layer") == "inner-band")
        .Select(ReadStrokeWidth)
        .Single();

    AssertAlmostEqual(sharedOuterWidth, 14, "Shared corridor total width should match the normal route width token.");
    Assert(sharedInnerWidth > 3 && sharedInnerWidth < sharedOuterWidth, "Shared corridor inner band should be narrower than the base corridor.");
}

static void StationMarkersUseReadableWhiteFill()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(12, 0)));

    string svg = new MetroSvgRenderer().Render(document, CreateCompositeTestOptions()).Svg;
    XDocument xml = XDocument.Parse(svg);

    Assert(svg.Contains(".station { fill: #ffffff;", StringComparison.Ordinal), "Station markers should use a white fill for metro-map readability.");
    Assert(xml.Descendants().Any(element => element.Name.LocalName == "circle" && element.Attribute("data-marker-stroke-width") is not null), "Station marker stroke width was not exposed for visual diagnostics.");
}

static void StationRouteAnchoringProjectsOrdinaryStationsToRouteSegments()
{
    MetroExportDocument document = CreateStationAnchorDocument(
        new StationAnchorSpec("station_a", 50, 8, ["line_10"]),
        [new ServiceLineSpec("line_10", "10号线", ["station_a"], [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }])]);

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateAnchorTestOptions()).Svg);
    XElement station = GetStationCircle(xml, "station_a");

    Assert((string?)station.Attribute("data-station-anchor") == "route-projection", "Ordinary station was not anchored to the route projection.");
    Assert((string?)station.Attribute("data-station-anchor-applied") == "true", "Ordinary station anchor was not applied.");
    Assert(ReadDouble(station.Attribute("data-station-anchor-distance")) > 0, "Station anchor distance should show the raw-to-route correction.");
}

static void StationRouteAnchoringFallsBackWhenRouteIsTooFar()
{
    MetroExportDocument document = CreateStationAnchorDocument(
        new StationAnchorSpec("station_a", 50, 80, ["line_10"]),
        [new ServiceLineSpec("line_10", "10号线", ["station_a"], [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }])]);

    SvgRenderOptions options = CreateAnchorTestOptions(stationRouteAnchorMaxDistance: 10);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    XElement station = GetStationCircle(xml, "station_a");

    Assert((string?)station.Attribute("data-station-anchor") == "raw", "Too-far station should keep its raw render position.");
    Assert((string?)station.Attribute("data-station-anchor-fallback") == "too-far", "Too-far station did not record a clear fallback reason.");
}

static void StationRouteAnchoringPrefersSegmentProjectionOverPathPoint()
{
    MetroExportDocument document = CreateStationAnchorDocument(
        new StationAnchorSpec("station_a", 50, 6, ["line_10"]),
        [new ServiceLineSpec("line_10", "10号线", ["station_a"], [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }])]);

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateAnchorTestOptions()).Svg);
    XElement station = GetStationCircle(xml, "station_a");
    XElement route = GetRouteElements(xml).Single();
    List<(double X, double Y)> routePoints = SplitPoints((string?)route.Attribute("points")).ToList();
    double stationX = ReadDouble(station.Attribute("cx"));

    Assert(Math.Abs(stationX - routePoints[0].X) > 20, "Station anchor incorrectly snapped to the first path point.");
    Assert(Math.Abs(stationX - routePoints[^1].X) > 20, "Station anchor incorrectly snapped to the last path point.");
}

static void StationRouteAnchoringAveragesCloseInterchangeAnchors()
{
    MetroExportDocument document = CreateStationAnchorDocument(
        new StationAnchorSpec("station_a", 50, 50, ["line_10", "line_18"], IsInterchange: true),
        [
            new ServiceLineSpec("line_10", "10号线", ["station_a"], [new MetroPathPoint { X = 0, Z = 48 }, new MetroPathPoint { X = 100, Z = 48 }]),
            new ServiceLineSpec("line_18", "18号线", ["station_a"], [new MetroPathPoint { X = 52, Z = 0 }, new MetroPathPoint { X = 52, Z = 100 }])
        ]);

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateAnchorTestOptions()).Svg);
    XElement station = GetStationCircle(xml, "station_a");

    Assert((string?)station.Attribute("data-station-anchor") == "multi-family-route-projection", "Close interchange anchors should use a multi-family route projection.");
    Assert(((string?)station.Attribute("data-station-anchor-family"))?.Contains("10号线", StringComparison.Ordinal) == true, "Interchange anchor did not record the first family.");
    Assert(((string?)station.Attribute("data-station-anchor-family"))?.Contains("18号线", StringComparison.Ordinal) == true, "Interchange anchor did not record the second family.");
}

static void StationRouteAnchoringRejectsSpreadOutInterchangeAnchors()
{
    MetroExportDocument document = CreateStationAnchorDocument(
        new StationAnchorSpec("station_a", 50, 50, ["line_10", "line_18"], IsInterchange: true),
        [
            new ServiceLineSpec("line_10", "10号线", ["station_a"], [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }]),
            new ServiceLineSpec("line_18", "18号线", ["station_a"], [new MetroPathPoint { X = 0, Z = 100 }, new MetroPathPoint { X = 100, Z = 100 }])
        ]);

    SvgRenderOptions options = CreateAnchorTestOptions(stationRouteAnchorMaxDistance: 500, stationRouteAnchorMultiFamilyMaxSpread: 20);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    XElement station = GetStationCircle(xml, "station_a");

    Assert((string?)station.Attribute("data-station-anchor") == "raw", "Spread-out interchange anchors should keep the raw render position.");
    Assert((string?)station.Attribute("data-station-anchor-fallback") == "multi-family-anchor-spread-too-large", "Spread-out interchange did not record the expected fallback reason.");
}

static void StationRouteAnchoringSharesMarkerAndLabelAnchorMetadata()
{
    MetroExportDocument document = CreateStationAnchorDocument(
        new StationAnchorSpec("station_a", 50, 8, ["line_10"]),
        [new ServiceLineSpec("line_10", "10号线", ["station_a"], [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }])]);

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateAnchorTestOptions()).Svg);
    XElement station = GetStationCircle(xml, "station_a");
    XElement label = xml
        .Descendants()
        .First(element => element.Name.LocalName == "text"
            && (string?)element.Attribute("data-station-id") == "station_a"
            && ((string?)element.Attribute("class")) == "station-label");

    Assert((string?)label.Attribute("data-station-anchor") == (string?)station.Attribute("data-station-anchor"), "Label did not use the same anchor source metadata as the marker.");
    Assert((string?)label.Attribute("data-station-anchor-distance") == (string?)station.Attribute("data-station-anchor-distance"), "Label did not use the same anchor distance metadata as the marker.");
}

static void StationRouteAnchoringLeavesSchematicLiteRaw()
{
    MetroExportDocument document = CreateStationAnchorDocument(
        new StationAnchorSpec("station_a", 50, 8, ["line_10"]),
        [new ServiceLineSpec("line_10", "10号线", ["station_a"], [new MetroPathPoint { X = 0, Z = 0 }, new MetroPathPoint { X = 100, Z = 0 }])]);

    SvgRenderOptions options = CreateAnchorTestOptions(layoutMode: SvgLayoutMode.SchematicLite);
    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    XElement station = GetStationCircle(xml, "station_a");

    Assert((string?)station.Attribute("data-station-anchor") == "raw", "Schematic-lite station anchoring should keep the existing raw/schematic station point.");
    Assert((string?)station.Attribute("data-station-anchor-fallback") == "schematic-lite", "Schematic-lite station anchoring did not record the expected fallback reason.");
}

static void ExpressStripeDoesNotChangeBaseRouteWidth()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10_express", "10号线（机场快线-特快）", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_10_local", "10号线（Local）", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(8, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions(enableExpressCenterStripe: true)).Svg);
    XElement baseRoute = GetRouteElements(xml).Single(route => (string?)route.Attribute("data-corridor-run-layer") == "normal-base");
    XElement stripe = GetExpressMarkerElements(xml).Single();

    AssertAlmostEqual(ReadStrokeWidth(baseRoute), 14, "Express marker changed the normal base route width.");
    Assert(ReadStrokeWidth(stripe) < ReadStrokeWidth(baseRoute), "Express marker should be an internal stripe, not a wider route.");
}

static void SharedCorridorExpressConflictWritesSkipMarker()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10_express", "10号线（机场快线-特快）", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(12, 0)),
        new ServiceLineSpec("line_18", "18号线", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(12, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions(enableExpressCenterStripe: true)).Svg);

    Assert(HasSharedCorridorStyle(xml), "Shared express conflict setup did not produce a shared corridor.");
    Assert(GetExpressMarkerElements(xml).Count == 0, "Express stripe should be skipped on fully shared corridor style conflicts.");
    Assert(xml.Descendants().Any(element => (string?)element.Attribute("data-express-marker-skipped") == "shared-corridor-style-conflict"), "Shared corridor express conflict did not emit a skip marker.");
}

static void ExpressCenterStripeMarksExpressFamily()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10_express", "10号线（机场快线-特快）", ["station_airport", "station_sports"], GeneratePathPoints(8, 0)),
        new ServiceLineSpec("line_10_local", "10号线（Local）", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(8, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions(enableExpressCenterStripe: true)).Svg);
    IReadOnlyList<XElement> stripes = GetExpressMarkerElements(xml);

    Assert(stripes.Count == 1, $"Expected one express center stripe for one merged family, but found {stripes.Count}.");
    Assert((string?)stripes.Single().Attribute("data-express-family") == "10号线", "Express marker did not record the display family key.");
    Assert(HasStationCircle(xml, "station_airport"), "Express center stripe should not remove station circles.");
    AssertValidSvg(xml, "express center stripe SVG");
}

static void ExpressCenterStripeIgnoresOrdinaryFamily()
{
    MetroExportDocument document = CreateServiceFamilyDocument(
        new ServiceLineSpec("line_10", "10号线", ["station_airport", "station_mid", "station_sports"], GeneratePathPoints(8, 0)));

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, CreateCompositeTestOptions(enableExpressCenterStripe: true)).Svg);

    Assert(GetExpressMarkerElements(xml).Count == 0, "Ordinary line family incorrectly received an express center stripe.");
}

static void SizePresetsMapToExpectedDimensions()
{
    Assert(SvgRenderSizePresets.Get(SvgRenderSizePreset.Compact) == new SvgRenderSize(1600, 1000), "Compact preset size changed.");
    Assert(SvgRenderSizePresets.Get(SvgRenderSizePreset.Standard) == new SvgRenderSize(2200, 1400), "Standard preset size changed.");
    Assert(SvgRenderSizePresets.Get(SvgRenderSizePreset.Poster) == new SvgRenderSize(3200, 2000), "Poster preset size changed.");
    Assert(SvgRenderSizePresets.Get(SvgRenderSizePreset.Ultra) == new SvgRenderSize(4200, 2600), "Ultra preset size changed.");
}

static void SnapshotPathBuilderUsesCitySlugAndSharedTimestamp()
{
    DateTime timestamp = new(2026, 5, 29, 23, 15, 30, DateTimeKind.Local);
    ExportSnapshotPaths paths = ExportSnapshotPathBuilder.Build(
        @"D:\CS2MetroDiagram",
        "metro-export.json",
        "metro-export-diagnostics.txt",
        "metro-export",
        "metro-export-diagnostics",
        "My City",
        timestamp);

    Assert(paths.CitySlug == "My-City", $"Unexpected city slug: {paths.CitySlug}");
    Assert(paths.TimestampToken == "20260529-231530", $"Unexpected timestamp token: {paths.TimestampToken}");
    Assert(paths.SnapshotJsonPath.EndsWith(@"exports\metro-export-My-City-20260529-231530.json", StringComparison.Ordinal), "Snapshot JSON file name was incorrect.");
    Assert(paths.SnapshotDiagnosticsPath.EndsWith(@"exports\metro-export-diagnostics-My-City-20260529-231530.txt", StringComparison.Ordinal), "Snapshot diagnostics file name was incorrect.");
}

static void SnapshotPathBuilderFallsBackToUnnamedCity()
{
    ExportSnapshotPaths paths = ExportSnapshotPathBuilder.Build(
        @"D:\CS2MetroDiagram",
        "metro-export.json",
        "metro-export-diagnostics.txt",
        "metro-export",
        "metro-export-diagnostics",
        "   ",
        new DateTime(2026, 5, 29, 23, 20, 10, DateTimeKind.Local));

    Assert(paths.CitySlug == ExportSnapshotPathBuilder.FallbackCitySlug, "Blank city name should fall back to UnnamedCity.");
    Assert(paths.SnapshotJsonPath.Contains(@"exports\metro-export-UnnamedCity-", StringComparison.Ordinal), "Fallback snapshot JSON path did not use UnnamedCity.");
}

static void SnapshotPathBuilderSanitizesInvalidWindowsFilenameCharacters()
{
    string slug = ExportSnapshotPathBuilder.SanitizeCitySlug("  Guang/zhou: Metro* Hub?  ");
    Assert(slug == "Guang-zhou-Metro-Hub", $"City slug was not sanitized as expected: {slug}");
}

static void SnapshotPathBuilderKeepsLatestPathsStableAndSnapshotsUnique()
{
    ExportSnapshotPaths first = ExportSnapshotPathBuilder.Build(
        @"D:\CS2MetroDiagram",
        "metro-export.json",
        "metro-export-diagnostics.txt",
        "metro-export",
        "metro-export-diagnostics",
        "Alpha City",
        new DateTime(2026, 5, 29, 23, 15, 30, DateTimeKind.Local));

    ExportSnapshotPaths second = ExportSnapshotPathBuilder.Build(
        @"D:\CS2MetroDiagram",
        "metro-export.json",
        "metro-export-diagnostics.txt",
        "metro-export",
        "metro-export-diagnostics",
        "Alpha City",
        new DateTime(2026, 5, 29, 23, 20, 10, DateTimeKind.Local));

    Assert(first.LatestJsonPath == @"D:\CS2MetroDiagram\metro-export.json", "Latest JSON path changed unexpectedly.");
    Assert(first.LatestDiagnosticsPath == @"D:\CS2MetroDiagram\metro-export-diagnostics.txt", "Latest diagnostics path changed unexpectedly.");
    Assert(first.SnapshotDirectory == @"D:\CS2MetroDiagram\exports", "Snapshot directory should be under exports.");
    Assert(first.SnapshotJsonPath != second.SnapshotJsonPath, "Snapshots from different timestamps should not overwrite each other.");
    Assert(first.SnapshotDiagnosticsPath != second.SnapshotDiagnosticsPath, "Diagnostics snapshots from different timestamps should not overwrite each other.");
}

static void BezierSamplingHelperReturnsStableCurvePoints()
{
    IReadOnlyList<PathGeometryPoint> points = PathGeometrySampler.SampleCubicBezier(
        new PathGeometryPoint(0, 0),
        new PathGeometryPoint(0, 10),
        new PathGeometryPoint(10, 10),
        new PathGeometryPoint(10, 0),
        intervals: 4);

    Assert(points.Count == 5, "Bezier sampler did not return t=0, 0.25, 0.5, 0.75, 1 points.");
    AssertAlmostEqual(points[0].X, 0, "Bezier start X changed.");
    AssertAlmostEqual(points[0].Z, 0, "Bezier start Z changed.");
    AssertAlmostEqual(points[2].X, 5, "Bezier midpoint X was unexpected.");
    AssertAlmostEqual(points[2].Z, 7.5, "Bezier midpoint Z was unexpected.");
    AssertAlmostEqual(points[^1].X, 10, "Bezier end X changed.");
    AssertAlmostEqual(points[^1].Z, 0, "Bezier end Z changed.");
}

static void PathPointSourceFallbackMetadataLoads()
{
    MetroExportDocument document = CreatePathPointCleanupDocument(
    [
        new MetroPathPoint { X = 0, Z = 0, Source = "RouteSegment.PathTargets", SegmentEntity = "1:1" },
        new MetroPathPoint { X = 50, Z = 25, Source = "RouteSegment.PathElement", SegmentEntity = "2:1" },
        new MetroPathPoint { X = 100, Z = 0, Source = "RouteSegment.CurveElement", SegmentEntity = "3:1" }
    ]);

    MetroLoadResult result = MetroNetworkValidator.ValidateAndNormalize(document);
    MetroLine line = result.Document!.Network!.Lines!.Single();

    Assert(line.PathPoints!.Select(point => point.Source).SequenceEqual(["RouteSegment.PathTargets", "RouteSegment.PathElement", "RouteSegment.CurveElement"]), "Path point source metadata was not preserved.");
    Assert(line.PathPoints!.Select(point => point.SegmentEntity).SequenceEqual(["1:1", "2:1", "3:1"]), "Path point segment entity metadata was not preserved.");

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(result.Document, new SvgRenderOptions { UsePathPoints = true }).Svg);
    XElement route = GetRouteElements(xml).Single();
    Assert((string?)route.Attribute("data-route-source") == "pathPoints", "Fallback source metadata sample did not render from pathPoints.");
}

static void GenericStationNameDetectionCoversDefaultNames()
{
    string[] genericNames =
    [
        "小型地铁广场",
        "现代地铁站",
        "地下地铁站",
        "地铁站",
        "Subway Station",
        "Metro Station",
        "Station 1",
        "Station 25"
    ];

    foreach (string name in genericNames)
    {
        Assert(StationLabelClassifier.IsGenericOrFallbackName(name), $"Expected '{name}' to be detected as generic/default.");
    }

    Assert(StationLabelClassifier.IsGenericOrFallbackName("station_a", "station_a"), "Station id fallback was not detected.");
    Assert(!StationLabelClassifier.IsGenericOrFallbackName("Central Park"), "User station name was incorrectly treated as generic.");
    Assert(!StationLabelClassifier.IsGenericOrFallbackName("城东站"), "Named Chinese station was incorrectly treated as generic.");
}

static void CrowdedLabelHidingRemovesLowPriorityLabels()
{
    List<MetroStation> stations =
    [
        new MetroStation
        {
            Id = "station_terminal",
            Name = "Central Terminal",
            Position = new MetroPosition { X = 0, Z = 0 },
            Lines = ["line_test"]
        }
    ];
    List<string> stops = ["station_terminal"];
    for (int i = 1; i <= 12; i++)
    {
        string stationId = $"station_crowded_{i}";
        stations.Add(new MetroStation
        {
            Id = stationId,
            Name = i % 2 == 0 ? $"Local Stop {i}" : "现代地铁站",
            Position = new MetroPosition { X = 1 + i * 0.02, Z = 1 + i * 0.02 },
            Lines = ["line_test"]
        });
        stops.Add(stationId);
    }

    stations.Add(new MetroStation
    {
        Id = "station_end",
        Name = "Airport",
        Position = new MetroPosition { X = 200, Z = 160 },
        Lines = ["line_test"]
    });
    stops.Add("station_end");

    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "Crowded City" },
        Network = new MetroNetwork
        {
            Stations = stations,
            Lines =
            [
                new MetroLine
                {
                    Id = "line_test",
                    Name = "Test Line",
                    Color = "#D71920",
                    Stops = stops
                }
            ]
        }
    };

    SvgRenderOptions options = new()
    {
        Width = 500,
        Height = 360,
        Padding = 80,
        Margin = 80,
        LegendWidth = 140,
        LabelFontSize = 18,
        HideCrowdedLabels = true,
        AlwaysShowTerminals = true
    };

    XDocument xml = XDocument.Parse(new MetroSvgRenderer().Render(document, options).Svg);
    int visibleLabels = CountVisibleStationLabels(xml);
    Assert(visibleLabels < stations.Count, $"Crowded label hiding did not reduce labels. Visible={visibleLabels}, stations={stations.Count}.");
    Assert(HasVisibleLabel(xml, "station_terminal"), "High priority terminal label was hidden.");
    Assert(stations.All(station => HasStationCircle(xml, station.Id!)), "One or more station circles were hidden with labels.");

    SvgRenderOptions hideGenericOptions = new()
    {
        HideGenericStationLabels = true,
        AlwaysShowTerminals = true
    };
    XDocument hiddenGenericXml = XDocument.Parse(new MetroSvgRenderer().Render(document, hideGenericOptions).Svg);
    Assert(!HasVisibleLabel(hiddenGenericXml, "station_crowded_1"), "Generic intermediate label was not hidden by HideGenericStationLabels.");
    Assert(HasVisibleLabel(hiddenGenericXml, "station_terminal"), "Terminal label was hidden by HideGenericStationLabels.");
}

static void MissingStationReferencesReportClearly()
{
    MetroExportDocument document = new()
    {
        City = new CityInfo { Name = "Warning City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "Only Station",
                    Position = new MetroPosition { X = 0, Z = 0 }
                },
                new MetroStation
                {
                    Id = "station_b",
                    Name = "Other Station",
                    Position = new MetroPosition { X = 100, Z = 100 }
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_a",
                    Name = "Broken Line",
                    Color = "#0072BC",
                    Stops = ["station_a", "station_missing", "station_b"]
                }
            ]
        }
    };

    MetroLoadResult result = MetroNetworkValidator.ValidateAndNormalize(document);
    string message = result.Warnings.FirstOrDefault(warning => warning.Contains("station_missing", StringComparison.Ordinal)) ?? string.Empty;
    Assert(message.StartsWith("Missing station reference:", StringComparison.Ordinal), "Missing station reference message was not clear.");

    SvgRenderResult renderResult = new MetroSvgRenderer().Render(result.Document!);
    AssertValidSvg(XDocument.Parse(renderResult.Svg), "missing station reference document");
    Assert(renderResult.Svg.Contains("Broken Line", StringComparison.Ordinal), "Renderer did not preserve the line in the legend.");
}

static void EmptyNetworksAndEmptyLinesDoNotCrash()
{
    MetroLoadResult emptyNetwork = MetroNetworkValidator.ValidateAndNormalize(new MetroExportDocument
    {
        City = new CityInfo { Name = "Empty City" },
        Network = new MetroNetwork
        {
            Stations = [],
            Lines = []
        }
    });

    SvgRenderResult emptySvg = new MetroSvgRenderer().Render(emptyNetwork.Document!);
    AssertValidSvg(XDocument.Parse(emptySvg.Svg), "empty network");
    Assert(emptySvg.Svg.Contains("No metro stations or lines", StringComparison.Ordinal), "Empty network did not render an empty notice.");

    MetroLoadResult emptyLine = MetroNetworkValidator.ValidateAndNormalize(new MetroExportDocument
    {
        City = new CityInfo { Name = "Empty Line City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "Station A",
                    Position = new MetroPosition { X = 0, Z = 0 }
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_empty",
                    Name = "Empty Line",
                    Color = "#D71920",
                    Stops = []
                }
            ]
        }
    });

    Assert(emptyLine.Warnings.Any(warning => warning.Contains("fewer than two valid stops", StringComparison.Ordinal)), "Empty line did not produce a warning.");
    SvgRenderResult emptyLineSvg = new MetroSvgRenderer().Render(emptyLine.Document!);
    AssertValidSvg(XDocument.Parse(emptyLineSvg.Svg), "empty line document");
}

static MetroExportDocument CreatePathPointCleanupDocument(List<MetroPathPoint> pathPoints)
{
    MetroPathPoint first = pathPoints[0];
    MetroPathPoint last = pathPoints[^1];
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Path Cleanup City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_start",
                    Name = "Start",
                    Position = new MetroPosition { X = first.X, Z = first.Z },
                    Lines = ["line_path"]
                },
                new MetroStation
                {
                    Id = "station_end",
                    Name = "End",
                    Position = new MetroPosition { X = last.X, Z = last.Z },
                    Lines = ["line_path"]
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_path",
                    Name = "Path Cleanup Line",
                    Color = "#D71920",
                    Stops = ["station_start", "station_end"],
                    PathPoints = pathPoints
                }
            ]
        }
    };
}

static MetroExportDocument CreateMinimalDocument(string? cityName)
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = cityName },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "Station A",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_1"]
                },
                new MetroStation
                {
                    Id = "station_b",
                    Name = "Station B",
                    Position = new MetroPosition { X = 100, Z = 0 },
                    Lines = ["line_1"]
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_1",
                    Name = "Line 1",
                    Color = "#D71920",
                    Stops = ["station_a", "station_b"]
                }
            ]
        }
    };
}

static MetroExportDocument CreateParallelCorridorDocument(params List<MetroPathPoint>[] linePathPoints)
{
    MetroNetwork network = new()
    {
        Stations = [],
        Lines = []
    };

    string[] colors = ["#D71920", "#005EB8", "#00A859", "#F58220", "#7B2CBF"];
    for (int i = 0; i < linePathPoints.Length; i++)
    {
        List<MetroPathPoint> pathPoints = linePathPoints[i];
        MetroPathPoint first = pathPoints[0];
        MetroPathPoint last = pathPoints[^1];
        string lineId = $"line_{i + 1}";
        string startId = $"{lineId}_start";
        string endId = $"{lineId}_end";
        network.Stations.Add(new MetroStation
        {
            Id = startId,
            Name = $"Start {i + 1}",
            Position = new MetroPosition { X = first.X, Z = first.Z },
            Lines = [lineId]
        });
        network.Stations.Add(new MetroStation
        {
            Id = endId,
            Name = $"End {i + 1}",
            Position = new MetroPosition { X = last.X, Z = last.Z },
            Lines = [lineId]
        });
        network.Lines.Add(new MetroLine
        {
            Id = lineId,
            Name = $"Line {i + 1}",
            Color = colors[i % colors.Length],
            Mode = "metro",
            Stops = [startId, endId],
            PathPoints = pathPoints
        });
    }

    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Parallel Corridor City" },
        Network = network
    };
}

static MetroExportDocument CreateSchematicOverlapDocument(params SchematicLineSpec[] lineSpecs)
{
    string[] allLineIds = lineSpecs.Select(spec => spec.Id).ToArray();
    MetroNetwork network = new()
    {
        Stations =
        [
            new MetroStation
            {
                Id = "station_a",
                Name = "Station A",
                Position = new MetroPosition { X = 0, Z = 0 },
                Lines = allLineIds.ToList()
            },
            new MetroStation
            {
                Id = "station_b",
                Name = "Station B",
                Position = new MetroPosition { X = 100, Z = 0 },
                Lines = allLineIds.ToList()
            },
            new MetroStation
            {
                Id = "station_c",
                Name = "Station C",
                Position = new MetroPosition { X = 100, Z = 100 },
                Lines = allLineIds.ToList()
            }
        ],
        Lines = []
    };

    string[] colors = ["#005EB8", "#00A859", "#D71920", "#F58220"];
    for (int i = 0; i < lineSpecs.Length; i++)
    {
        SchematicLineSpec spec = lineSpecs[i];
        network.Lines.Add(new MetroLine
        {
            Id = spec.Id,
            Name = spec.Name,
            Color = colors[i % colors.Length],
            Mode = "metro",
            Stops = spec.Stops.ToList()
        });
    }

    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Schematic Overlap City" },
        Network = network
    };
}

static MetroExportDocument CreateCloseSchematicStationDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Close Schematic City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_a",
                    Name = "A",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_close"]
                },
                new MetroStation
                {
                    Id = "station_b",
                    Name = "B",
                    Position = new MetroPosition { X = 28, Z = 0 },
                    Lines = ["line_close"]
                },
                new MetroStation
                {
                    Id = "station_c",
                    Name = "C",
                    Position = new MetroPosition { X = 1000, Z = 0 },
                    Lines = []
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_close",
                    Name = "Line Close",
                    Color = "#005EB8",
                    Mode = "metro",
                    Stops = ["station_a", "station_b"]
                }
            ]
        }
    };
}

static MetroExportDocument CreateSchematicV2BranchingSharedEdgeDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Branching Shared Edge City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_line3_before",
                    Name = "Line 3 Before",
                    Position = new MetroPosition { X = -200, Z = -80 },
                    Lines = ["line_3"]
                },
                new MetroStation
                {
                    Id = "station_line4_before",
                    Name = "Line 4 Before",
                    Position = new MetroPosition { X = -200, Z = 80 },
                    Lines = ["line_4"]
                },
                new MetroStation
                {
                    Id = "station_shared_start",
                    Name = "Shared Start",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_3", "line_4"]
                },
                new MetroStation
                {
                    Id = "station_shared_end",
                    Name = "Shared End",
                    Position = new MetroPosition { X = 120, Z = 0 },
                    Lines = ["line_3", "line_4"]
                },
                new MetroStation
                {
                    Id = "station_line3_after",
                    Name = "Line 3 After",
                    Position = new MetroPosition { X = 260, Z = -80 },
                    Lines = ["line_3"]
                },
                new MetroStation
                {
                    Id = "station_line4_after",
                    Name = "Line 4 After",
                    Position = new MetroPosition { X = 260, Z = 80 },
                    Lines = ["line_4"]
                },
                new MetroStation
                {
                    Id = "station_line4_terminal",
                    Name = "Line 4 Terminal",
                    Position = new MetroPosition { X = 420, Z = 120 },
                    Lines = ["line_4"]
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_3",
                    Name = "Line 3",
                    Color = "#ED2A2A",
                    Mode = "metro",
                    Stops = ["station_line3_before", "station_shared_start", "station_shared_end", "station_line3_after"]
                },
                new MetroLine
                {
                    Id = "line_4",
                    Name = "Line 4",
                    Color = "#DE2FFF",
                    Mode = "metro",
                    Stops = ["station_line4_before", "station_shared_start", "station_shared_end", "station_line4_after", "station_line4_terminal"]
                }
            ]
        }
    };
}

static MetroExportDocument CreateSchematicV2ExactSharedPlatformDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Exact Shared Platform City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation { Id = "station_3_west", Name = "Line 3 West", Position = new MetroPosition { X = -120, Z = 20 }, Lines = ["line_3"] },
                new MetroStation { Id = "station_4_west", Name = "Line 4 West", Position = new MetroPosition { X = -120, Z = -20 }, Lines = ["line_4"] },
                new MetroStation { Id = "station_shared_a", Name = "Shared A", Position = new MetroPosition { X = 0, Z = 0 }, Lines = ["line_3", "line_4"] },
                new MetroStation { Id = "station_shared_b", Name = "Shared B", Position = new MetroPosition { X = 100, Z = 0 }, Lines = ["line_3", "line_4"] },
                new MetroStation { Id = "station_shared_c", Name = "Shared C", Position = new MetroPosition { X = 200, Z = 0 }, Lines = ["line_3", "line_4"] },
                new MetroStation { Id = "station_3_east", Name = "Line 3 East", Position = new MetroPosition { X = 320, Z = 30 }, Lines = ["line_3"] },
                new MetroStation { Id = "station_4_east", Name = "Line 4 East", Position = new MetroPosition { X = 320, Z = -30 }, Lines = ["line_4"] }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_3",
                    Name = "Line 3",
                    Color = "#D71920",
                    Mode = "metro",
                    Stops = ["station_3_west", "station_shared_a", "station_shared_b", "station_shared_c", "station_3_east"]
                },
                new MetroLine
                {
                    Id = "line_4",
                    Name = "Line 4",
                    Color = "#92278F",
                    Mode = "metro",
                    Stops = ["station_4_west", "station_shared_a", "station_shared_b", "station_shared_c", "station_4_east"]
                }
            ]
        }
    };
}

static MetroExportDocument CreateSchematicV2ExpressExactSharedEdgeDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Express Exact Shared Edge City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation { Id = "station_a", Name = "A", Position = new MetroPosition { X = 0, Z = 0 }, Lines = ["line_7", "line_10_local", "line_10_express"] },
                new MetroStation { Id = "station_b", Name = "B", Position = new MetroPosition { X = 100, Z = 0 }, Lines = ["line_7", "line_10_local", "line_10_express"] },
                new MetroStation { Id = "station_7_west", Name = "7 West", Position = new MetroPosition { X = -120, Z = -60 }, Lines = ["line_7"] },
                new MetroStation { Id = "station_10_east", Name = "10 East", Position = new MetroPosition { X = 220, Z = 60 }, Lines = ["line_10_local", "line_10_express"] }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_7",
                    Name = "Line 7",
                    Color = "#00FFFB",
                    Mode = "metro",
                    Stops = ["station_7_west", "station_a", "station_b"]
                },
                new MetroLine
                {
                    Id = "line_10_local",
                    Name = "Line 10 (Local)",
                    Color = "#0FBA7C",
                    Mode = "metro",
                    Stops = ["station_a", "station_b", "station_10_east"]
                },
                new MetroLine
                {
                    Id = "line_10_express",
                    Name = "Line 10 (Express)",
                    Color = "#0FBA7C",
                    Mode = "metro",
                    Stops = ["station_a", "station_b"]
                }
            ]
        }
    };
}

static MetroExportDocument CreateSchematicV2ThreeFamilyExactSharedEdgeDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Three Family Exact Shared Edge City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation { Id = "station_a", Name = "A", Position = new MetroPosition { X = 0, Z = 0 }, Lines = ["line_1", "line_2", "line_7"] },
                new MetroStation { Id = "station_b", Name = "B", Position = new MetroPosition { X = 100, Z = 0 }, Lines = ["line_1", "line_2", "line_7"] },
                new MetroStation { Id = "station_1_west", Name = "1 West", Position = new MetroPosition { X = -120, Z = -80 }, Lines = ["line_1"] },
                new MetroStation { Id = "station_2_west", Name = "2 West", Position = new MetroPosition { X = -120, Z = 0 }, Lines = ["line_2"] },
                new MetroStation { Id = "station_7_west", Name = "7 West", Position = new MetroPosition { X = -120, Z = 80 }, Lines = ["line_7"] }
            ],
            Lines =
            [
                new MetroLine { Id = "line_1", Name = "Line 1", Color = "#2AED33", Mode = "metro", Stops = ["station_1_west", "station_a", "station_b"] },
                new MetroLine { Id = "line_2", Name = "Line 2", Color = "#342AED", Mode = "metro", Stops = ["station_2_west", "station_a", "station_b"] },
                new MetroLine { Id = "line_7", Name = "Line 7", Color = "#00FFFB", Mode = "metro", Stops = ["station_7_west", "station_a", "station_b"] }
            ]
        }
    };
}

static MetroExportDocument CreateSchematicV2SharedCorridorDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Schematic Shared Corridor City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_airport",
                    Name = "Airport",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_10_express", "line_10_local"]
                },
                new MetroStation
                {
                    Id = "station_city",
                    Name = "City",
                    Position = new MetroPosition { X = 0, Z = 120 },
                    Lines = ["line_2", "line_10_express"]
                },
                new MetroStation
                {
                    Id = "station_shared_start",
                    Name = "Shared Start",
                    IsInterchange = true,
                    Position = new MetroPosition { X = 120, Z = 60 },
                    Lines = ["line_2", "line_10_local"]
                },
                new MetroStation
                {
                    Id = "station_shared_end",
                    Name = "Shared End",
                    IsInterchange = true,
                    Position = new MetroPosition { X = 220, Z = 60 },
                    Lines = ["line_2", "line_10_express", "line_10_local"]
                },
                new MetroStation
                {
                    Id = "station_terminal_2",
                    Name = "Line 2 Terminal",
                    Position = new MetroPosition { X = 320, Z = 120 },
                    Lines = ["line_2"]
                },
                new MetroStation
                {
                    Id = "station_terminal_10",
                    Name = "Line 10 Terminal",
                    Position = new MetroPosition { X = 320, Z = 0 },
                    Lines = ["line_10_express", "line_10_local"]
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_10_express",
                    Name = "10号线（特快）",
                    Color = "#00843D",
                    Mode = "metro",
                    Stops = ["station_airport", "station_shared_end", "station_terminal_10"],
                    PathPoints = GeneratePathPoints(24, 0)
                },
                new MetroLine
                {
                    Id = "line_10_local",
                    Name = "10号线（站站停）",
                    Color = "#00843D",
                    Mode = "metro",
                    Stops = ["station_airport", "station_shared_start", "station_shared_end", "station_terminal_10"],
                    PathPoints = GeneratePathPoints(4, 0)
                },
                new MetroLine
                {
                    Id = "line_2",
                    Name = "2号线",
                    Color = "#005EB8",
                    Mode = "metro",
                    Stops = ["station_city", "station_shared_start", "station_shared_end", "station_terminal_2"]
                }
            ]
        }
    };
}

static MetroExportDocument CreateSchematicV2GeometrySharedCorridorDocument()
{
    List<MetroPathPoint> sharedCorridor =
    [
        new() { X = 0, Z = 0 },
        new() { X = 80, Z = 0 },
        new() { X = 160, Z = 0 },
        new() { X = 240, Z = 0 },
        new() { X = 320, Z = 0 }
    ];

    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Geometry Shared Corridor City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_local_start",
                    Name = "Local Start",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_2"]
                },
                new MetroStation
                {
                    Id = "station_corridor_mid",
                    Name = "Corridor Mid",
                    Position = new MetroPosition { X = 160, Z = 0 },
                    Lines = ["line_2"]
                },
                new MetroStation
                {
                    Id = "station_corridor_mid_2",
                    Name = "Corridor Mid 2",
                    Position = new MetroPosition { X = 240, Z = 0 },
                    Lines = ["line_2"]
                },
                new MetroStation
                {
                    Id = "station_corridor_end",
                    Name = "Corridor End",
                    Position = new MetroPosition { X = 320, Z = 0 },
                    Lines = ["line_2", "line_10_express"]
                },
                new MetroStation
                {
                    Id = "station_line2_terminal",
                    Name = "Line 2 Terminal",
                    Position = new MetroPosition { X = 420, Z = 0 },
                    Lines = ["line_2"]
                },
                new MetroStation
                {
                    Id = "station_line10_terminal",
                    Name = "Line 10 Terminal",
                    Position = new MetroPosition { X = 420, Z = -90 },
                    Lines = ["line_10_express"]
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_2",
                    Name = "Line 2",
                    Color = "#005EB8",
                    Mode = "metro",
                    Stops = ["station_local_start", "station_corridor_mid", "station_corridor_mid_2", "station_corridor_end", "station_line2_terminal"],
                    PathPoints = sharedCorridor.Concat([new MetroPathPoint { X = 420, Z = 0 }]).ToList()
                },
                new MetroLine
                {
                    Id = "line_10_express",
                    Name = "Line 10 (Express)",
                    Color = "#00843D",
                    Mode = "metro",
                    Stops = ["station_local_start", "station_corridor_mid", "station_corridor_end", "station_line10_terminal"],
                    PathPoints =
                    [
                        ..sharedCorridor,
                        new MetroPathPoint { X = 360, Z = -30 },
                        new MetroPathPoint { X = 400, Z = -60 },
                        new MetroPathPoint { X = 420, Z = -90 }
                    ]
                }
            ]
        }
    };
}

static MetroExportDocument CreateSchematicV2GeometrySharedCorridorWithExpressFamilyDocument()
{
    MetroExportDocument document = CreateSchematicV2GeometrySharedCorridorDocument();
    MetroNetwork network = document.Network!;
    network.Lines ??= [];
    MetroLine expressLine = network.Lines!.Single(line => line.Id == "line_10_express");
    expressLine.Id = "line_10_local";
    expressLine.Name = "Line 10 (Local)";

    foreach (MetroStation station in network.Stations!)
    {
        if (station.Lines is null)
        {
            continue;
        }

        for (int i = 0; i < station.Lines.Count; i++)
        {
            if (station.Lines[i] == "line_10_express")
            {
                station.Lines[i] = "line_10_local";
            }
        }
    }

    network.Lines.Add(new MetroLine
    {
        Id = "line_10_express",
        Name = "Line 10 (Express)",
        Color = "#00843D",
        Mode = "metro",
        Stops = ["station_local_start", "station_line10_terminal"],
        PathPoints = expressLine.PathPoints!.ToList()
    });

    foreach (MetroStation station in network.Stations)
    {
        if (station.Id is "station_local_start" or "station_line10_terminal")
        {
            station.Lines ??= [];
            if (!station.Lines.Contains("line_10_express", StringComparer.Ordinal))
            {
                station.Lines.Add("line_10_express");
            }
        }
    }

    return document;
}

static MetroExportDocument CreateSchematicV2ServiceVariantSimplificationDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Service Variant City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation { Id = "station_a", Name = "A", Position = new MetroPosition { X = 0, Z = 0 }, Lines = ["line_10_local", "line_10_express"] },
                new MetroStation { Id = "station_b", Name = "B", Position = new MetroPosition { X = 100, Z = 0 }, Lines = ["line_10_local"] },
                new MetroStation { Id = "station_c", Name = "C", Position = new MetroPosition { X = 200, Z = 0 }, Lines = ["line_10_local"] },
                new MetroStation { Id = "station_d", Name = "D", Position = new MetroPosition { X = 300, Z = 0 }, Lines = ["line_10_local", "line_10_express"] },
                new MetroStation { Id = "station_e", Name = "E", Position = new MetroPosition { X = 400, Z = 0 }, Lines = ["line_10_local"] },
                new MetroStation { Id = "station_f", Name = "F", Position = new MetroPosition { X = 400, Z = -80 }, Lines = ["line_10_express"] }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_10_express",
                    Name = "Line 10 (Express)",
                    Color = "#00843D",
                    Mode = "metro",
                    Stops = ["station_a", "station_d", "station_f"],
                    PathPoints = GeneratePathPoints(20, 0)
                },
                new MetroLine
                {
                    Id = "line_10_local",
                    Name = "Line 10 (Local)",
                    Color = "#00843D",
                    Mode = "metro",
                    Stops = ["station_a", "station_b", "station_c", "station_d", "station_e"],
                    PathPoints = GeneratePathPoints(5, 0)
                }
            ]
        }
    };
}

static MetroExportDocument CreateTransitMapBadgeCollisionDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Badge Collision City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_west",
                    Name = "Very Long Western Terminal Station",
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_3", "line_4"]
                },
                new MetroStation
                {
                    Id = "station_mid",
                    Name = "Central Long Label Station",
                    Position = new MetroPosition { X = 100, Z = 0 },
                    Lines = ["line_3", "line_4"]
                },
                new MetroStation
                {
                    Id = "station_east",
                    Name = "Eastern Terminal",
                    Position = new MetroPosition { X = 220, Z = 0 },
                    Lines = ["line_3", "line_4"]
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_3",
                    Name = "3号线",
                    Color = "#D71920",
                    Mode = "metro",
                    Stops = ["station_west", "station_mid", "station_east"]
                },
                new MetroLine
                {
                    Id = "line_4",
                    Name = "4号线",
                    Color = "#92278F",
                    Mode = "metro",
                    Stops = ["station_west", "station_mid", "station_east"]
                }
            ]
        }
    };
}

static MetroExportDocument CreateSchematicV2BacktrackingCanonicalRouteDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Backtracking Canonical Route City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation { Id = "station_a", Name = "A", Position = new MetroPosition { X = 0, Z = 0 }, Lines = ["line_10_local", "line_10_express"] },
                new MetroStation { Id = "station_b", Name = "B", Position = new MetroPosition { X = 100, Z = 0 }, Lines = ["line_10_local"] },
                new MetroStation { Id = "station_c", Name = "C", Position = new MetroPosition { X = 200, Z = 0 }, Lines = ["line_10_local"] },
                new MetroStation { Id = "station_d", Name = "D", Position = new MetroPosition { X = 300, Z = 0 }, Lines = ["line_10_local", "line_10_express"] }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_10_local",
                    Name = "Line 10 (Local)",
                    Color = "#00843D",
                    Mode = "metro",
                    Stops = ["station_a", "station_b", "station_c", "station_d", "station_c", "station_b"],
                    PathPoints = GeneratePathPoints(12, 0)
                },
                new MetroLine
                {
                    Id = "line_10_express",
                    Name = "Line 10 (Express)",
                    Color = "#00843D",
                    Mode = "metro",
                    Stops = ["station_a", "station_d"],
                    PathPoints = GeneratePathPoints(4, 0)
                }
            ]
        }
    };
}

static MetroExportDocument CreateSchematicV2ZigzagTerminalTailDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Zigzag Terminal Tail City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation { Id = "station_anchor", Name = "Anchor", Position = new MetroPosition { X = 0, Z = 0 }, Lines = ["line_tail", "line_stub_east", "line_stub_west"], IsInterchange = true },
                new MetroStation { Id = "station_bend_a", Name = "Bend A", Position = new MetroPosition { X = 160, Z = -240 }, Lines = ["line_tail"] },
                new MetroStation { Id = "station_bend_b", Name = "Bend B", Position = new MetroPosition { X = -110, Z = -500 }, Lines = ["line_tail"] },
                new MetroStation { Id = "station_terminal", Name = "Terminal", Position = new MetroPosition { X = -20, Z = -760 }, Lines = ["line_tail"] },
                new MetroStation { Id = "station_east", Name = "East", Position = new MetroPosition { X = 420, Z = 0 }, Lines = ["line_stub_east"] },
                new MetroStation { Id = "station_west", Name = "West", Position = new MetroPosition { X = -420, Z = 0 }, Lines = ["line_stub_west"] }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_tail",
                    Name = "Tail Line",
                    Color = "#DE5D00",
                    Mode = "metro",
                    Stops = ["station_anchor", "station_bend_a", "station_bend_b", "station_terminal"]
                },
                new MetroLine
                {
                    Id = "line_stub_east",
                    Name = "East Stub",
                    Color = "#2AED33",
                    Mode = "metro",
                    Stops = ["station_anchor", "station_east"]
                },
                new MetroLine
                {
                    Id = "line_stub_west",
                    Name = "West Stub",
                    Color = "#342AED",
                    Mode = "metro",
                    Stops = ["station_anchor", "station_west"]
                }
            ]
        }
    };
}

static MetroExportDocument CreateSchematicV2CrossingOnlyDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Crossing Only City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "station_west",
                    Name = "West",
                    Position = new MetroPosition { X = -120, Z = 0 },
                    Lines = ["line_2"]
                },
                new MetroStation
                {
                    Id = "station_east",
                    Name = "East",
                    Position = new MetroPosition { X = 120, Z = 0 },
                    Lines = ["line_2"]
                },
                new MetroStation
                {
                    Id = "station_north",
                    Name = "North",
                    Position = new MetroPosition { X = 0, Z = 120 },
                    Lines = ["line_10"]
                },
                new MetroStation
                {
                    Id = "station_south",
                    Name = "South",
                    Position = new MetroPosition { X = 0, Z = -120 },
                    Lines = ["line_10"]
                }
            ],
            Lines =
            [
                new MetroLine
                {
                    Id = "line_2",
                    Name = "Line 2",
                    Color = "#005EB8",
                    Mode = "metro",
                    Stops = ["station_west", "station_east"],
                    PathPoints =
                    [
                        new MetroPathPoint { X = -120, Z = 0 },
                        new MetroPathPoint { X = 120, Z = 0 }
                    ]
                },
                new MetroLine
                {
                    Id = "line_10",
                    Name = "Line 10",
                    Color = "#00843D",
                    Mode = "metro",
                    Stops = ["station_north", "station_south"],
                    PathPoints =
                    [
                        new MetroPathPoint { X = 0, Z = 120 },
                        new MetroPathPoint { X = 0, Z = -120 }
                    ]
                }
            ]
        }
    };
}

static MetroExportDocument CreateHighDegreeCloseSchematicStationDocument()
{
    return new MetroExportDocument
    {
        City = new CityInfo { Name = "High Degree Schematic City" },
        Network = new MetroNetwork
        {
            Stations =
            [
                new MetroStation
                {
                    Id = "hub",
                    Name = "Hub",
                    IsInterchange = true,
                    Position = new MetroPosition { X = 0, Z = 0 },
                    Lines = ["line_a", "line_b", "line_c"]
                },
                new MetroStation
                {
                    Id = "near",
                    Name = "Near",
                    Position = new MetroPosition { X = 24, Z = 0 },
                    Lines = ["line_a"]
                },
                new MetroStation
                {
                    Id = "north",
                    Name = "North",
                    Position = new MetroPosition { X = 0, Z = 100 },
                    Lines = ["line_b"]
                },
                new MetroStation
                {
                    Id = "south",
                    Name = "South",
                    Position = new MetroPosition { X = 0, Z = -100 },
                    Lines = ["line_c"]
                }
            ],
            Lines =
            [
                new MetroLine { Id = "line_a", Name = "Line A", Color = "#005EB8", Mode = "metro", Stops = ["hub", "near"] },
                new MetroLine { Id = "line_b", Name = "Line B", Color = "#00A859", Mode = "metro", Stops = ["hub", "north"] },
                new MetroLine { Id = "line_c", Name = "Line C", Color = "#D71920", Mode = "metro", Stops = ["hub", "south"] }
            ]
        }
    };
}

static MetroExportDocument CreateStationAnchorDocument(StationAnchorSpec station, ServiceLineSpec[] lineSpecs)
{
    MetroNetwork network = new()
    {
        Stations =
        [
            new MetroStation
            {
                Id = station.Id,
                Name = "Anchor Station",
                Position = new MetroPosition { X = station.X, Z = station.Z },
                Lines = station.Lines.ToList(),
                IsInterchange = station.IsInterchange
            }
        ],
        Lines = []
    };

    string[] colors = ["#00843D", "#005EB8", "#D71920", "#F58220"];
    for (int i = 0; i < lineSpecs.Length; i++)
    {
        ServiceLineSpec spec = lineSpecs[i];
        network.Lines.Add(new MetroLine
        {
            Id = spec.Id,
            Name = spec.Name,
            Color = colors[i % colors.Length],
            Mode = "metro",
            Stops = spec.Stops.ToList(),
            PathPoints = spec.PathPoints
        });
    }

    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Station Anchor City" },
        Network = network
    };
}

static MetroExportDocument CreateServiceFamilyDocument(params ServiceLineSpec[] lineSpecs)
{
    List<string> lineIds = lineSpecs.Select(spec => spec.Id).ToList();
    MetroNetwork network = new()
    {
        Stations =
        [
            new MetroStation
            {
                Id = "station_airport",
                Name = "机场北",
                Position = new MetroPosition { X = 0, Z = 0 },
                Lines = lineIds
            },
            new MetroStation
            {
                Id = "station_mid",
                Name = "中途站",
                Position = new MetroPosition { X = 50, Z = 0 },
                Lines = lineIds
            },
            new MetroStation
            {
                Id = "station_sports",
                Name = "体育西路",
                Position = new MetroPosition { X = 100, Z = 0 },
                Lines = lineIds
            }
        ],
        Lines = []
    };

    string[] colors = ["#00843D", "#00843D", "#005EB8", "#D71920"];
    for (int i = 0; i < lineSpecs.Length; i++)
    {
        ServiceLineSpec spec = lineSpecs[i];
        network.Lines.Add(new MetroLine
        {
            Id = spec.Id,
            Name = spec.Name,
            Color = colors[i % colors.Length],
            Mode = "metro",
            Stops = spec.Stops.ToList(),
            PathPoints = spec.PathPoints
        });
    }

    return new MetroExportDocument
    {
        City = new CityInfo { Name = "Service Family City" },
        Network = network
    };
}

static List<MetroPathPoint> GeneratePathPoints(int count, double zOffset)
{
    List<MetroPathPoint> points = [];
    for (int i = 0; i < count; i++)
    {
        double t = count <= 1 ? 0 : i / (double)(count - 1);
        points.Add(new MetroPathPoint { X = t * 100, Z = zOffset + Math.Sin(t * Math.PI) * 4 });
    }

    return points;
}

static SvgRenderOptions CreateCorridorTestOptions(bool enableOffset)
{
    return new SvgRenderOptions
    {
        UsePathPoints = true,
        EnableParallelCorridorOffset = enableOffset,
        PathPointSimplificationEnabled = false,
        Width = 900,
        Height = 520,
        Padding = 80,
        Margin = 80,
        LegendWidth = 160,
        LineWidth = 14
    };
}

static SvgRenderOptions CreateFamilyTestOptions()
{
    return new SvgRenderOptions
    {
        UsePathPoints = true,
        PathPointSimplificationEnabled = false,
        EnableServiceFamilyMerge = true,
        Width = 900,
        Height = 520,
        Padding = 80,
        Margin = 80,
        LegendWidth = 260,
        LineWidth = 14
    };
}

static SvgRenderOptions CreateAnchorTestOptions(
    SvgLayoutMode layoutMode = SvgLayoutMode.Geographic,
    double stationRouteAnchorMaxDistance = 120,
    double stationRouteAnchorMultiFamilyMaxSpread = 120)
{
    return new SvgRenderOptions
    {
        LayoutMode = layoutMode,
        UsePathPoints = true,
        PathPointSimplificationEnabled = false,
        EnableServiceFamilyMerge = true,
        EnableStationRouteAnchoring = true,
        StationRouteAnchorMaxDistance = stationRouteAnchorMaxDistance,
        StationRouteAnchorMultiFamilyMaxSpread = stationRouteAnchorMultiFamilyMaxSpread,
        Width = 800,
        Height = 500,
        Padding = 70,
        Margin = 70,
        LegendWidth = 160,
        LineWidth = 14
    };
}

static SvgRenderOptions CreateCompositeTestOptions(bool enableExpressCenterStripe = false)
{
    return new SvgRenderOptions
    {
        UsePathPoints = true,
        PathPointSimplificationEnabled = false,
        EnableServiceFamilyMerge = true,
        EnableSharedCorridorCompositeStroke = true,
        EnableExpressCenterStripe = enableExpressCenterStripe,
        Width = 900,
        Height = 520,
        Padding = 80,
        Margin = 80,
        LegendWidth = 260,
        LineWidth = 14
    };
}

static SvgRenderOptions CreateSchematicOverlapTestOptions(
    SvgLayoutMode layoutMode = SvgLayoutMode.SchematicLite,
    int width = 900,
    int height = 520,
    int legendWidth = 220,
    double endpointTrim = 0,
    double shortThreshold = 0,
    bool enableResolver = true,
    double minStationSpacing = 0,
    SvgMapStyle mapStyle = SvgMapStyle.Standard)
{
    return new SvgRenderOptions
    {
        LayoutMode = layoutMode,
        MapStyle = mapStyle,
        UsePathPoints = false,
        EnableServiceFamilyMerge = true,
        EnableSchematicSegmentOverlapResolver = enableResolver,
        SchematicSegmentOverlapOffsetDistance = 12,
        Width = width,
        Height = height,
        Padding = 80,
        Margin = 80,
        LegendWidth = legendWidth,
        GridSize = 40,
        LineWidth = 14,
        SchematicOverlapEndpointTrim = endpointTrim,
        SchematicShortOverlapSegmentThreshold = shortThreshold,
        SchematicMinimumStationSpacing = minStationSpacing
    };
}

static RenderedSample LoadAndRenderSample(string fileName)
{
    string samplePath = Path.Combine(FindRepositoryRoot(), "samples", fileName);
    MetroLoadResult loadResult = MetroJsonLoader.LoadFromFile(samplePath);
    Assert(loadResult.Document is not null, $"{fileName}: sample did not load a document.");

    MetroExportDocument document = loadResult.Document!;
    SvgRenderResult renderResult = new MetroSvgRenderer().Render(document);
    XDocument xml = XDocument.Parse(renderResult.Svg);
    return new RenderedSample(document, loadResult, renderResult.Svg, xml);
}

static void AssertEveryRenderableLineHasRoute(MetroExportDocument document, IReadOnlyList<XElement> routes, string context)
{
    HashSet<string> routeLineIds = routes
        .Select(route => (string?)route.Attribute("data-line-id"))
        .Where(id => !string.IsNullOrWhiteSpace(id))
        .Select(id => id!)
        .ToHashSet(StringComparer.Ordinal);

    Dictionary<string, MetroStation> stationsById = (document.Network?.Stations ?? [])
        .Where(station => !string.IsNullOrWhiteSpace(station.Id) && station.Position is not null)
        .GroupBy(station => station.Id!, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

    foreach (MetroLine line in document.Network?.Lines ?? [])
    {
        int validPositionedStops = (line.Stops ?? []).Count(stopId => stationsById.ContainsKey(stopId));
        if (validPositionedStops >= 2)
        {
            Assert(routeLineIds.Contains(line.Id!), $"{context}: renderable line '{line.Id}' did not produce a route polyline.");
        }
    }
}

static void AssertLegendDoesNotCoverRoutes(XDocument xml, IReadOnlyList<XElement> routes, string context)
{
    XElement? legend = xml
        .Descendants()
        .FirstOrDefault(element => element.Name.LocalName == "g" && (string?)element.Attribute("id") == "legend");

    if (legend is null || routes.Count == 0)
    {
        return;
    }

    XElement? legendTitle = legend
        .Descendants()
        .FirstOrDefault(element => element.Name.LocalName == "text" && element.Value == "Legend");
    double legendX = ReadDouble(legendTitle?.Attribute("x"));
    double maxRouteX = routes
        .SelectMany(route => SplitPoints((string?)route.Attribute("points")))
        .Select(point => point.X)
        .DefaultIfEmpty(0)
        .Max();

    Assert(maxRouteX <= legendX - 20, $"{context}: route geometry reached x={maxRouteX:0.###}, too close to legend at x={legendX:0.###}.");
}

static IReadOnlyList<XElement> GetRouteElements(XDocument xml)
{
    return xml
        .Descendants()
        .Where(element => element.Name.LocalName == "polyline" && (string?)element.Attribute("class") == "route")
        .ToList();
}

static IReadOnlyList<XElement> GetSchematicV2ParallelCorridorElements(XDocument xml)
{
    return xml
        .Descendants()
        .Where(element => element.Name.LocalName == "polyline" && (string?)element.Attribute("class") == "schematic-v2-parallel-corridor")
        .ToList();
}

static XElement GetStationCircle(XDocument xml, string stationId)
{
    return xml
        .Descendants()
        .First(element => element.Name.LocalName == "circle"
            && (string?)element.Attribute("data-station-id") == stationId);
}

static (double X, double Y) GetStationCenter(XDocument xml, string stationId)
{
    XElement station = GetStationCircle(xml, stationId);
    return (ReadDouble(station.Attribute("cx")), ReadDouble(station.Attribute("cy")));
}

static IReadOnlyList<XElement> GetSharedCorridorRouteElements(XDocument xml)
{
    return xml
        .Descendants()
        .Where(element => element.Name.LocalName == "polyline"
            && (string?)element.Attribute("data-shared-corridor") == "true")
        .ToList();
}

static IReadOnlyList<XElement> GetSchematicOverlapRouteElements(XDocument xml)
{
    return xml
        .Descendants()
        .Where(element => element.Name.LocalName == "polyline"
            && (string?)element.Attribute("data-schematic-overlap") == "true")
        .ToList();
}

static bool HasSharedCorridorStyle(XDocument xml)
{
    return GetSharedCorridorRouteElements(xml).Count > 0;
}

static IReadOnlyList<XElement> GetExpressMarkerElements(XDocument xml)
{
    return xml
        .Descendants()
        .Where(element => element.Name.LocalName == "polyline"
            && (string?)element.Attribute("data-express-marker") == "white-center-stripe")
        .ToList();
}

static bool HasVisibleLabel(XDocument xml, string stationId)
{
    return xml
        .Descendants()
        .Any(element => element.Name.LocalName == "text"
            && ((string?)element.Attribute("class")) == "station-label"
            && (string?)element.Attribute("data-station-id") == stationId);
}

static bool HasStationCircle(XDocument xml, string stationId)
{
    return xml
        .Descendants()
        .Any(element => element.Name.LocalName == "circle"
            && (string?)element.Attribute("data-station-id") == stationId);
}

static int CountVisibleStationLabels(XDocument xml)
{
    return xml
        .Descendants()
        .Count(element => element.Name.LocalName == "text"
            && ((string?)element.Attribute("class")) == "station-label"
            && !string.IsNullOrWhiteSpace((string?)element.Attribute("data-station-id")));
}

static IEnumerable<(double X, double Y)> SplitPoints(string? points)
{
    foreach (string pair in (points ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
    {
        string[] parts = pair.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            yield return (double.Parse(parts[0], CultureInfo.InvariantCulture), double.Parse(parts[1], CultureInfo.InvariantCulture));
        }
    }
}

static bool ContainsAdjacentSegment(
    IReadOnlyList<(double X, double Y)> points,
    (double X, double Y) a,
    (double X, double Y) b)
{
    for (int i = 1; i < points.Count; i++)
    {
        bool forward = Distance(points[i - 1], a) < 0.001 && Distance(points[i], b) < 0.001;
        bool reverse = Distance(points[i - 1], b) < 0.001 && Distance(points[i], a) < 0.001;
        if (forward || reverse)
        {
            return true;
        }
    }

    return false;
}

static int FindPointIndex(IReadOnlyList<(double X, double Y)> points, (double X, double Y) target)
{
    for (int i = 0; i < points.Count; i++)
    {
        if (Distance(points[i], target) < 0.001)
        {
            return i;
        }
    }

    return -1;
}

static void AssertValidSvg(XDocument xml, string context)
{
    XElement root = xml.Root ?? throw new InvalidOperationException($"{context}: SVG XML had no root element.");
    Assert(root.Name.LocalName == "svg", $"{context}: XML root was not <svg>.");
    Assert(root.Name.NamespaceName == "http://www.w3.org/2000/svg", $"{context}: SVG namespace was missing.");
    Assert(root.Descendants().Any(element => element.Name.LocalName == "title"), $"{context}: SVG did not contain a <title> element.");
}

static void AssertSvgContains(string svg, string expected, string context)
{
    Assert(svg.Contains(expected, StringComparison.Ordinal), $"{context}: SVG did not contain '{expected}'.");
}

static string FindRepositoryRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        if (Directory.Exists(Path.Combine(directory.FullName, "samples")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new InvalidOperationException("Could not locate repository root containing the samples folder.");
}

static double ReadDouble(XAttribute? attribute)
{
    return double.Parse(attribute?.Value ?? "0", CultureInfo.InvariantCulture);
}

static double ReadStrokeWidth(XElement route)
{
    string? style = (string?)route.Attribute("style");
    Assert(!string.IsNullOrWhiteSpace(style), "Expected route to include a stroke-width style.");
    const string marker = "stroke-width:";
    int markerIndex = style!.IndexOf(marker, StringComparison.Ordinal);
    Assert(markerIndex >= 0, "Expected route style to include stroke-width.");
    int valueStart = markerIndex + marker.Length;
    int valueEnd = style.IndexOf(';', valueStart);
    string value = valueEnd >= 0
        ? style[valueStart..valueEnd]
        : style[valueStart..];
    return double.Parse(value.Trim(), CultureInfo.InvariantCulture);
}

static double MaxSegmentLength(IReadOnlyList<(double X, double Y)> points)
{
    double maxLength = 0;
    for (int i = 1; i < points.Count; i++)
    {
        double dx = points[i].X - points[i - 1].X;
        double dy = points[i].Y - points[i - 1].Y;
        maxLength = Math.Max(maxLength, Math.Sqrt(dx * dx + dy * dy));
    }

    return maxLength;
}

static double Distance((double X, double Y) a, (double X, double Y) b)
{
    double dx = a.X - b.X;
    double dy = a.Y - b.Y;
    return Math.Sqrt(dx * dx + dy * dy);
}

static double DistancePointToLine((double X, double Y) point, (double X, double Y) start, (double X, double Y) end)
{
    double dx = end.X - start.X;
    double dy = end.Y - start.Y;
    double denominator = Math.Sqrt(dx * dx + dy * dy);
    if (denominator <= 0.001)
    {
        return Distance(point, start);
    }

    return Math.Abs(((end.X - start.X) * (start.Y - point.Y)) - ((start.X - point.X) * (end.Y - start.Y))) / denominator;
}

static double CalculateTurnAngleDegrees((double X, double Y) previous, (double X, double Y) current, (double X, double Y) next)
{
    double ax = previous.X - current.X;
    double ay = previous.Y - current.Y;
    double bx = next.X - current.X;
    double by = next.Y - current.Y;
    double aLength = Math.Sqrt(ax * ax + ay * ay);
    double bLength = Math.Sqrt(bx * bx + by * by);
    if (aLength <= 0.001 || bLength <= 0.001)
    {
        return 180;
    }

    double cosine = (ax * bx + ay * by) / (aLength * bLength);
    cosine = Math.Clamp(cosine, -1, 1);
    return Math.Acos(cosine) * 180 / Math.PI;
}

static void AssertAlmostEqual(double actual, double expected, string message)
{
    if (Math.Abs(actual - expected) > 0.0001)
    {
        throw new InvalidOperationException($"{message} Expected {expected:0.####}, got {actual:0.####}.");
    }
}

static int ReadInt(XAttribute? attribute)
{
    return int.Parse(attribute?.Value ?? "0", CultureInfo.InvariantCulture);
}

static bool IsOnGrid(double value, double gridSize)
{
    return Math.Abs(value / gridSize - Math.Round(value / gridSize)) < 0.001;
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed record SampleExpectation(
    string FileName,
    string CityName,
    IReadOnlyList<string> ExpectedText,
    int ExpectedRouteCount,
    bool ExpectInterchange);

internal sealed record ServiceLineSpec(
    string Id,
    string Name,
    IReadOnlyList<string> Stops,
    List<MetroPathPoint> PathPoints);

internal sealed record SchematicLineSpec(
    string Id,
    string Name,
    IReadOnlyList<string> Stops);

internal sealed record StationAnchorSpec(
    string Id,
    double X,
    double Z,
    IReadOnlyList<string> Lines,
    bool IsInterchange = false);

internal sealed record RenderedSample(
    MetroExportDocument Document,
    MetroLoadResult LoadResult,
    string Svg,
    XDocument Xml);
