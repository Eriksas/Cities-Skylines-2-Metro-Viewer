using SkiaSharp;
using Svg.Skia;

namespace MetroDiagram.Export;

/// <summary>
/// Converts a rendered SVG document into raster (PNG) or print (PDF) form via
/// Svg.Skia. Text stays real text: PNG rasterizes with system font fallback
/// (CJK included), PDF embeds subsetted fonts and keeps vector geometry.
/// </summary>
public static class SvgDocumentExporter
{
    public static void ExportPng(string svgText, string outputPath, double scale = 1.0)
    {
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "Scale must be positive.");
        }

        using SKSvg svg = LoadSvg(svgText, out SKPicture picture);
        int width = Math.Max(1, (int)Math.Ceiling(picture.CullRect.Width * scale));
        int height = Math.Max(1, (int)Math.Ceiling(picture.CullRect.Height * scale));

        using SKSurface surface = SKSurface.Create(new SKImageInfo(width, height))
            ?? throw new InvalidOperationException($"Could not allocate a {width}x{height} surface for PNG export.");
        SKCanvas canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.Scale((float)scale);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using SKImage image = surface.Snapshot();
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new InvalidOperationException("PNG encoding failed.");
        using FileStream stream = File.Create(outputPath);
        data.SaveTo(stream);
    }

    public static void ExportPdf(string svgText, string outputPath)
    {
        using SKSvg svg = LoadSvg(svgText, out SKPicture picture);
        using FileStream stream = File.Create(outputPath);
        using SKDocument document = SKDocument.CreatePdf(stream)
            ?? throw new InvalidOperationException("Could not create the PDF document.");
        using (SKCanvas canvas = document.BeginPage(picture.CullRect.Width, picture.CullRect.Height))
        {
            canvas.Clear(SKColors.White);
            canvas.DrawPicture(picture);
        }

        document.EndPage();
        document.Close();
    }

    private static SKSvg LoadSvg(string svgText, out SKPicture picture)
    {
        // Bundled typefaces are cosmetic: any failure falls back to system fonts.
        SKSvg svg = new();
        BundledFonts.Apply(svg);
        try
        {
            picture = svg.FromSvg(svgText)
                ?? throw new InvalidOperationException("The SVG document could not be parsed for export.");
            return svg;
        }
        catch
        {
            svg.Dispose();
            throw;
        }
    }
}
