namespace MetroDiagram.Rendering;

public sealed class SvgRenderResult
{
    public SvgRenderResult(string svg, IReadOnlyList<string> warnings, SchematicLayoutScore? layoutScore = null)
    {
        Svg = svg;
        Warnings = warnings;
        LayoutScore = layoutScore;
    }

    public string Svg { get; }

    public IReadOnlyList<string> Warnings { get; }

    /// <summary>Layout-quality metrics for the rendered station layout; null when the network has no usable edges.</summary>
    public SchematicLayoutScore? LayoutScore { get; }
}
