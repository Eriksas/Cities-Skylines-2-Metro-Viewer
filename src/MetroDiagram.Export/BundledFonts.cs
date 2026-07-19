using System.Reflection;
using Svg.Skia;
using Svg.Skia.TypefaceProviders;

namespace MetroDiagram.Export;

/// <summary>
/// Ships Noto Sans SC (SIL OFL) with the toolchain so sheet typography renders
/// identically on machines without the font installed. Everything here is
/// cosmetic and must degrade silently: any failure falls back to system fonts.
/// </summary>
public static class BundledFonts
{
    public const string FamilyName = "Noto Sans SC";

    private static readonly string[] FontResourceNames =
    [
        "MetroDiagram.Export.Assets.Fonts.NotoSansSC-Regular.otf",
        "MetroDiagram.Export.Assets.Fonts.NotoSansSC-Bold.otf",
    ];

    private const string LicenseResourceName = "MetroDiagram.Export.Assets.Fonts.OFL-LICENSE.txt";

    private static readonly Lazy<IReadOnlyList<ITypefaceProvider>> LazyProviders = new(CreateProviders);
    private static readonly Lazy<string?> LazyExtractedDirectory = new(ExtractToLocalAppData);

    /// <summary>
    /// Directory containing the extracted font files (for consumers that need
    /// file paths, e.g. a WebView2 virtual-host mapping), or null on failure.
    /// </summary>
    public static string? ExtractedFontDirectory => LazyExtractedDirectory.Value;

    /// <summary>Registers the bundled typefaces on an SKSvg instance.</summary>
    public static void Apply(SKSvg svg)
    {
        try
        {
            IList<ITypefaceProvider>? target = svg.Settings?.TypefaceProviders;
            if (target is null)
            {
                return;
            }

            IReadOnlyList<ITypefaceProvider> providers = LazyProviders.Value;
            for (int i = providers.Count - 1; i >= 0; i--)
            {
                target.Insert(0, providers[i]);
            }
        }
        catch
        {
        }
    }

    private static IReadOnlyList<ITypefaceProvider> CreateProviders()
    {
        List<ITypefaceProvider> providers = new();
        try
        {
            Assembly assembly = typeof(BundledFonts).Assembly;
            foreach (string resourceName in FontResourceNames)
            {
                using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    continue;
                }

                // The provider may keep reading lazily, so it gets its own copy.
                MemoryStream copy = new();
                stream.CopyTo(copy);
                copy.Position = 0;
                providers.Add(new CustomTypefaceProvider(copy, 0));
            }
        }
        catch
        {
        }

        return providers;
    }

    private static string? ExtractToLocalAppData()
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CS2MetroDiagram",
                "fonts");
            Directory.CreateDirectory(directory);

            Assembly assembly = typeof(BundledFonts).Assembly;
            foreach (string resourceName in FontResourceNames.Append(LicenseResourceName))
            {
                string fileName = resourceName["MetroDiagram.Export.Assets.Fonts.".Length..];
                string target = Path.Combine(directory, fileName);
                using Stream? stream = assembly.GetManifestResourceStream(resourceName);
                if (stream is null)
                {
                    continue;
                }

                if (File.Exists(target) && new FileInfo(target).Length == stream.Length)
                {
                    continue;
                }

                using FileStream output = File.Create(target);
                stream.CopyTo(output);
            }

            return directory;
        }
        catch
        {
            return null;
        }
    }
}
