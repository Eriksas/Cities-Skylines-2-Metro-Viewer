using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

internal static class DisplayLineFamilyResolver
{
    public static List<DisplayLineFamily> Resolve(
        List<MetroLine> lines,
        Dictionary<string, MetroStation> stationsById,
        bool enableServiceFamilyMerge)
    {
        if (!enableServiceFamilyMerge)
        {
            return lines
                .Select((line, index) => CreateSingleLineFamily(line, index, stationsById))
                .ToList();
        }

        // A numbered family name ("10号线", "Metro Line 3") is itself the service
        // identity, so numbered families merge by name alone even when player
        // colors drift apart (the Zhaoqing shared-corridor case). Number-less
        // duplicate names (auto-named "地铁路线工具" exports) are indistinguishable
        // placeholders, so the color keeps them separate. The family key only
        // gains a color suffix when a name is actually shared by several colors,
        // keeping existing outputs byte-identical.
        List<IGrouping<(string FamilyKey, string ColorKey), LineWithIndex>> groups = lines
            .Select((line, index) => new LineWithIndex(line, index, ExtractFamilyParts(line)))
            .GroupBy(item => (
                item.Parts.FamilyKey,
                ColorKey: HasLineNumber(item.Parts.FamilyKey) ? string.Empty : NormalizeColorKey(item.Line.Color)))
            .ToList();
        Dictionary<string, int> colorsPerName = groups
            .GroupBy(group => group.Key.FamilyKey, StringComparer.Ordinal)
            .ToDictionary(byName => byName.Key, byName => byName.Count(), StringComparer.Ordinal);

        return groups
            .Select(group => CreateFamily(
                group.ToList(),
                stationsById,
                colorsPerName[group.Key.FamilyKey] > 1 ? group.Key.ColorKey : null))
            .ToList();
    }

    private static string NormalizeColorKey(string? color)
    {
        return string.IsNullOrWhiteSpace(color) ? string.Empty : color!.Trim().ToUpperInvariant();
    }

    private static bool HasLineNumber(string name)
    {
        foreach (char character in name)
        {
            if (char.IsDigit(character))
            {
                return true;
            }

            double numericValue = char.GetNumericValue(character);
            if (numericValue >= 0 && numericValue <= 9 && Math.Floor(numericValue) == numericValue)
            {
                return true;
            }
        }

        return false;
    }

    private static DisplayLineFamily CreateSingleLineFamily(MetroLine line, int index, Dictionary<string, MetroStation> stationsById)
    {
        FamilyParts parts = ExtractFamilyParts(line);
        DisplayServiceVariant variant = CreateVariant(line, parts, stationsById);
        string lineName = GetLineName(line);
        string uniqueKey = !string.IsNullOrWhiteSpace(line.Id)
            ? $"line:{line.Id}"
            : $"line-index:{index}";
        return new DisplayLineFamily(
            uniqueKey,
            lineName,
            line.Color,
            line,
            [variant],
            HasColorMismatch([line]));
    }

    private static DisplayLineFamily CreateFamily(
        List<LineWithIndex> lines,
        Dictionary<string, MetroStation> stationsById,
        string? disambiguatingColorKey)
    {
        LineWithIndex primary = lines
            .OrderByDescending(item => item.Line.PathPoints?.Count ?? 0)
            .ThenByDescending(item => item.Line.Stops?.Count ?? 0)
            .ThenBy(item => item.Line.Name ?? item.Line.Id ?? string.Empty, StringComparer.CurrentCulture)
            .ThenBy(item => item.Index)
            .First();

        List<DisplayServiceVariant> variants = lines
            .OrderBy(item => item.Index)
            .Select(item => CreateVariant(item.Line, item.Parts, stationsById))
            .ToList();

        // The family key must stay unique per family: downstream corridor and
        // draw-order logic groups by it. Only suffix the color when several
        // same-name families exist.
        string familyKey = disambiguatingColorKey is null
            ? primary.Parts.FamilyKey
            : $"{primary.Parts.FamilyKey}|{disambiguatingColorKey}";

        return new DisplayLineFamily(
            familyKey,
            primary.Parts.DisplayName,
            primary.Line.Color,
            primary.Line,
            variants,
            HasColorMismatch(lines.Select(item => item.Line)));
    }

    private static DisplayServiceVariant CreateVariant(
        MetroLine line,
        FamilyParts parts,
        Dictionary<string, MetroStation> stationsById)
    {
        List<string> stops = (line.Stops ?? [])
            .Where(stop => !string.IsNullOrWhiteSpace(stop))
            .ToList();

        string? startName = GetStationName(stops.FirstOrDefault(), stationsById);
        string? endName = GetStationName(stops.LastOrDefault(), stationsById);

        return new DisplayServiceVariant(
            line.Id ?? string.Empty,
            GetLineName(line),
            parts.VariantName,
            stops,
            stops.Count,
            line.PathPoints?.Count ?? 0,
            CalculatePathLength(line.PathPoints),
            startName,
            endName);
    }

    private static double CalculatePathLength(List<MetroPathPoint>? pathPoints)
    {
        if (pathPoints is null || pathPoints.Count < 2)
        {
            return 0;
        }

        double length = 0;
        for (int i = 1; i < pathPoints.Count; i++)
        {
            double dx = pathPoints[i].X - pathPoints[i - 1].X;
            double dz = pathPoints[i].Z - pathPoints[i - 1].Z;
            length += Math.Sqrt((dx * dx) + (dz * dz));
        }

        return length;
    }

    private static string? GetStationName(string? stationId, Dictionary<string, MetroStation> stationsById)
    {
        if (string.IsNullOrWhiteSpace(stationId) || !stationsById.TryGetValue(stationId, out MetroStation? station))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(station.Name) ? stationId : station.Name;
    }

    private static FamilyParts ExtractFamilyParts(MetroLine line)
    {
        string originalName = GetLineName(line);
        if (TryExtractBracketedVariant(originalName, '（', '）', out string? familyKey, out string? variantName)
            || TryExtractBracketedVariant(originalName, '(', ')', out familyKey, out variantName))
        {
            return new FamilyParts(familyKey!, familyKey!, variantName!);
        }

        return new FamilyParts(originalName, originalName, originalName);
    }

    private static bool TryExtractBracketedVariant(
        string name,
        char open,
        char close,
        out string? familyKey,
        out string? variantName)
    {
        familyKey = null;
        variantName = null;

        int openIndex = name.IndexOf(open);
        int closeIndex = name.LastIndexOf(close);
        if (openIndex <= 0 || closeIndex <= openIndex)
        {
            return false;
        }

        string prefix = name[..openIndex].Trim();
        string suffix = name[(openIndex + 1)..closeIndex].Trim();
        if (string.IsNullOrWhiteSpace(prefix) || string.IsNullOrWhiteSpace(suffix))
        {
            return false;
        }

        familyKey = prefix;
        variantName = suffix;
        return true;
    }

    private static string GetLineName(MetroLine line)
    {
        if (!string.IsNullOrWhiteSpace(line.Name))
        {
            return line.Name!;
        }

        if (!string.IsNullOrWhiteSpace(line.Id))
        {
            return line.Id!;
        }

        return "Unnamed Line";
    }

    private static bool HasColorMismatch(IEnumerable<MetroLine> lines)
    {
        return lines
            .Select(line => line.Color)
            .Where(color => !string.IsNullOrWhiteSpace(color))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count() > 1;
    }

    private readonly record struct FamilyParts(string FamilyKey, string DisplayName, string VariantName);

    private readonly record struct LineWithIndex(MetroLine Line, int Index, FamilyParts Parts);
}

internal sealed record DisplayLineFamily(
    string FamilyKey,
    string DisplayName,
    string? Color,
    MetroLine PrimaryLine,
    List<DisplayServiceVariant> Variants,
    bool HasColorMismatch);

internal sealed record DisplayServiceVariant(
    string LineId,
    string OriginalName,
    string VariantName,
    List<string> Stops,
    int StopCount,
    int PathPointCount,
    double PathLength,
    string? StartStationName,
    string? EndStationName);
