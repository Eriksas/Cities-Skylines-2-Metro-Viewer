namespace MetroDiagram.Rendering;

public enum StationNameKind
{
    Fallback,
    KnownAssetDefault,
    UserNamed
}

public static class StationLabelClassifier
{
    private static readonly string[] GenericStationNames =
    [
        "小型地铁广场",
        "现代地铁站",
        "地下地铁站",
        "地铁站",
        "高架地铁站",
        "Subway Station",
        "Metro Station",
        "Elevated Metro Station"
    ];

    public static StationNameKind Classify(string? name, string? stationId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return StationNameKind.Fallback;
        }

        string trimmed = name.Trim();
        if (!string.IsNullOrWhiteSpace(stationId) && string.Equals(trimmed, stationId.Trim(), StringComparison.Ordinal))
        {
            return StationNameKind.Fallback;
        }

        if (IsStationNumberFallback(trimmed))
        {
            return StationNameKind.Fallback;
        }

        return IsKnownAssetDefaultName(trimmed)
            ? StationNameKind.KnownAssetDefault
            : StationNameKind.UserNamed;
    }

    public static bool IsGenericOrFallbackName(string? name, string? stationId = null)
    {
        StationNameKind kind = Classify(name, stationId);
        return kind is StationNameKind.Fallback or StationNameKind.KnownAssetDefault;
    }

    public static bool IsKnownAssetDefaultName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        string trimmed = name.Trim();
        string baseName = StripParentheticalSuffix(trimmed);
        return GenericStationNames.Any(generic => string.Equals(baseName, generic, StringComparison.OrdinalIgnoreCase));
    }

    // Default CS2 assets ship bracketed variants ("高架地铁站（小型）",
    // "地铁站（高架旁路）"): strip one trailing parenthesized suffix and compare
    // the base name. Player names keep their own base, so a renamed
    // "中央站（东）" is unaffected.
    private static string StripParentheticalSuffix(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        char last = value[^1];
        char open = last switch
        {
            '）' => '（',
            ')' => '(',
            _ => '\0'
        };
        if (open == '\0')
        {
            return value;
        }

        int openIndex = value.LastIndexOf(open);
        return openIndex > 0 ? value[..openIndex].TrimEnd() : value;
    }

    private static bool IsStationNumberFallback(string value)
    {
        const string stationPrefix = "Station ";
        if (!value.StartsWith(stationPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string suffix = value[stationPrefix.Length..];
        return suffix.Length > 0 && suffix.All(char.IsDigit);
    }
}
