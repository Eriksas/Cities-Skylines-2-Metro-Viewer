using System.Text.Json;
using System.Text.Json.Serialization;

namespace MetroDiagram.Rendering;

public sealed class LayoutOverrideDocument
{
    public int Version { get; set; } = 1;

    public Dictionary<string, StationLayoutOverride> Stations { get; set; } = new(StringComparer.Ordinal);

    public Dictionary<string, LabelLayoutOverride> Labels { get; set; } = new(StringComparer.Ordinal);

    [JsonIgnore]
    public bool IsEmpty => Stations.Count == 0 && Labels.Count == 0;
}

public sealed class StationLayoutOverride
{
    public double? X { get; set; }

    public double? Y { get; set; }

    public double? Dx { get; set; }

    public double? Dy { get; set; }

    public bool Enabled { get; set; } = true;

    public string? Note { get; set; }
}

public sealed class LabelLayoutOverride
{
    public double? X { get; set; }

    public double? Y { get; set; }

    public double? Dx { get; set; }

    public double? Dy { get; set; }

    public bool? Hidden { get; set; }

    public string? Position { get; set; }

    public string? Note { get; set; }
}

public static class LayoutOverrideLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static LayoutOverrideDocument LoadFromFile(string path)
    {
        string json = File.ReadAllText(path);
        LayoutOverrideDocument document = JsonSerializer.Deserialize<LayoutOverrideDocument>(json, Options)
            ?? new LayoutOverrideDocument();
        document.Stations ??= new Dictionary<string, StationLayoutOverride>(StringComparer.Ordinal);
        document.Labels ??= new Dictionary<string, LabelLayoutOverride>(StringComparer.Ordinal);
        return document;
    }

    public static void SaveToFile(string path, LayoutOverrideDocument document)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(document, Options);
        File.WriteAllText(path, json);
    }

    public static string GetDefaultSidecarPath(string jsonPath)
    {
        string directory = Path.GetDirectoryName(jsonPath) ?? string.Empty;
        string fileName = Path.GetFileNameWithoutExtension(jsonPath);
        return Path.Combine(directory, $"{fileName}.layout-overrides.json");
    }

    public static bool TryLoadDefaultSidecar(string jsonPath, out LayoutOverrideDocument? document, out string? path, out string? error)
    {
        path = GetDefaultSidecarPath(jsonPath);
        document = null;
        error = null;

        if (!File.Exists(path))
        {
            path = null;
            return false;
        }

        try
        {
            document = LoadFromFile(path);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = ex.Message;
            return false;
        }
    }
}
