using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetroDiagram.Viewer;

public sealed class ViewerSettings
{
    [JsonPropertyName("lastJsonPath")]
    public string? LastJsonPath { get; set; }

    [JsonPropertyName("language")]
    public string Language { get; set; } = "en";

    [JsonPropertyName("layoutMode")]
    public string LayoutMode { get; set; } = "schematic-anneal";

    [JsonPropertyName("sizePreset")]
    public string SizePreset { get; set; } = "custom";

    [JsonPropertyName("previewZoom")]
    public string PreviewZoom { get; set; } = "fit-page";

    [JsonPropertyName("saveFormat")]
    public string SaveFormat { get; set; } = "svg";

    [JsonPropertyName("width")]
    public int Width { get; set; } = 1200;

    [JsonPropertyName("height")]
    public int Height { get; set; } = 800;

    [JsonPropertyName("legendWidth")]
    public int LegendWidth { get; set; } = 240;

    [JsonPropertyName("padding")]
    public int Padding { get; set; } = 80;

    [JsonPropertyName("lineWidth")]
    public double LineWidth { get; set; } = 14;

    [JsonPropertyName("stationRadius")]
    public double StationRadius { get; set; } = 5.5;

    [JsonPropertyName("labelFontSize")]
    public double LabelFontSize { get; set; } = 12;

    [JsonPropertyName("gridSize")]
    public double GridSize { get; set; } = 32;

    [JsonPropertyName("hideGenericStationLabels")]
    public bool HideGenericStationLabels { get; set; } = true;

    [JsonPropertyName("enableVirtualTransferHints")]
    public bool EnableVirtualTransferHints { get; set; }

    [JsonPropertyName("hideCrowdedLabels")]
    public bool HideCrowdedLabels { get; set; } = true;

    [JsonPropertyName("alwaysShowInterchanges")]
    public bool AlwaysShowInterchanges { get; set; } = true;

    [JsonPropertyName("alwaysShowTerminals")]
    public bool AlwaysShowTerminals { get; set; } = true;

    [JsonPropertyName("usePathPoints")]
    public bool UsePathPoints { get; set; } = true;

    [JsonPropertyName("pathPointSimplificationEnabled")]
    public bool PathPointSimplificationEnabled { get; set; } = true;

    [JsonPropertyName("pathPointSimplificationTolerance")]
    public double PathPointSimplificationTolerance { get; set; } = 0.75;

    public void NormalizeRenderSettings()
    {
        Width = Math.Clamp(Width, 600, 8000);
        Height = Math.Clamp(Height, 400, 6000);
        LegendWidth = ViewerRenderSettingSanitizer.NormalizeLegendWidth(LegendWidth, Width);
        Padding = ViewerRenderSettingSanitizer.NormalizePadding(Padding, Width, Height);
    }
}

public static class ViewerRenderSettingSanitizer
{
    public static int NormalizeLegendWidth(int legendWidth, int canvasWidth)
    {
        if (legendWidth < 120)
        {
            return 240;
        }

        int maxLegendWidth = Math.Max(240, canvasWidth / 3);
        return Math.Min(legendWidth, maxLegendWidth);
    }

    public static int NormalizePadding(int padding, int canvasWidth, int canvasHeight)
    {
        if (padding < 20)
        {
            return 80;
        }

        int shortSide = Math.Min(canvasWidth, canvasHeight);
        int maxPadding = Math.Min(360, Math.Max(80, shortSide / 5));
        return padding > maxPadding ? 80 : padding;
    }
}

public static class ViewerSettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static string SettingsPath
    {
        get
        {
            string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (string.IsNullOrWhiteSpace(documents))
            {
                documents = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            }

            return Path.Combine(documents, "CS2MetroDiagram", "viewer-settings.json");
        }
    }

    public static ViewerSettings Load()
    {
        try
        {
            string path = SettingsPath;
            if (!File.Exists(path))
            {
                return new ViewerSettings();
            }

            string json = File.ReadAllText(path);
            ViewerSettings settings = JsonSerializer.Deserialize<ViewerSettings>(json, Options) ?? new ViewerSettings();
            settings.NormalizeRenderSettings();
            return settings;
        }
        catch
        {
            return new ViewerSettings();
        }
    }

    public static void Save(ViewerSettings settings)
    {
        settings.NormalizeRenderSettings();
        string path = SettingsPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
    }
}
