using System.Text;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    private static void AppendMapIcons(StringBuilder svg, SvgRenderOptions options)
    {
        IReadOnlyDictionary<string, MapIconLayoutOverride>? icons = options.LayoutOverrides?.Icons;
        if (icons is null || icons.Count == 0)
        {
            return;
        }

        svg.AppendLine("""<g id="map-icons" data-map-icons="manual">""");
        foreach ((string id, MapIconLayoutOverride icon) in icons.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!icon.Enabled
                || !double.IsFinite(icon.X)
                || !double.IsFinite(icon.Y))
            {
                continue;
            }

            double size = double.IsFinite(icon.Size) ? Math.Clamp(icon.Size, 14, 96) : 34;
            string type = NormalizeMapIconType(icon.Type);
            string color = NormalizeMapIconColor(icon.Color, type);
            if (type == "information-box")
            {
                AppendMapInformationBox(svg, id, icon, color);
                continue;
            }

            double radius = size * 0.5;
            double glyph = size * 0.31;

            svg.AppendLine($"""<g class="map-icon map-icon-{Escape(type)}" data-map-icon-id="{Escape(id)}" data-map-icon-type="{Escape(type)}" data-icon-x="{Format(icon.X)}" data-icon-y="{Format(icon.Y)}" data-icon-size="{Format(size)}" transform="translate({Format(icon.X)} {Format(icon.Y)})">""");
            svg.AppendLine($"""<title>{Escape(GetMapIconTitle(type, icon.Label))}</title>""");
            if (type is "airport" or "railway")
            {
                svg.AppendLine($"""<rect class="map-icon-background map-icon-transport-badge" data-map-icon-visual="transport-badge" x="{Format(-radius)}" y="{Format(-radius)}" width="{Format(size)}" height="{Format(size)}" rx="{Format(size * 0.14)}" fill="{color}" />""");
            }
            else
            {
                svg.AppendLine($"""<circle class="map-icon-background" cx="0" cy="0" r="{Format(radius)}" fill="#ffffff" stroke="#263238" stroke-width="{Format(Math.Max(1.5, size * 0.065))}" />""");
            }

            AppendMapIconGlyph(svg, type, color, glyph);
            svg.AppendLine($"""<rect class="map-icon-hit-target" x="{Format(-radius)}" y="{Format(-radius)}" width="{Format(size)}" height="{Format(size)}" fill="#ffffff" fill-opacity="0.001" pointer-events="all" />""");

            if (!string.IsNullOrWhiteSpace(icon.Label))
            {
                double labelY = radius + Math.Max(11, size * 0.42);
                double fontSize = Math.Clamp(size * 0.43, 10, 22);
                svg.AppendLine($"""<text class="map-icon-label" x="0" y="{Format(labelY)}" text-anchor="middle" font-family="Segoe UI, Microsoft YaHei, Arial, sans-serif" font-size="{Format(fontSize)}" font-weight="600" fill="#1f2933" paint-order="stroke" stroke="#ffffff" stroke-width="3" stroke-linejoin="round">{Escape(icon.Label)}</text>""");
            }

            svg.AppendLine("</g>");
        }

        svg.AppendLine("</g>");
    }

    private static void AppendMapIconGlyph(StringBuilder svg, string type, string color, double glyph)
    {
        double stroke = Math.Max(1.8, glyph * 0.18);
        string common = $"""fill="none" stroke="{color}" stroke-width="{Format(stroke)}" stroke-linecap="round" stroke-linejoin="round" """;

        switch (type)
        {
            case "airport":
                svg.AppendLine($"""<path class="map-icon-glyph map-icon-glyph-airport" d="M 0 {Format(-glyph)} L {Format(glyph * 0.18)} {Format(-glyph * 0.28)} L {Format(glyph * 0.82)} {Format(glyph * 0.08)} L {Format(glyph * 0.82)} {Format(glyph * 0.3)} L {Format(glyph * 0.18)} {Format(glyph * 0.16)} L {Format(glyph * 0.14)} {Format(glyph * 0.64)} L {Format(glyph * 0.46)} {Format(glyph * 0.83)} L {Format(glyph * 0.46)} {Format(glyph)} L 0 {Format(glyph * 0.88)} L {Format(-glyph * 0.46)} {Format(glyph)} L {Format(-glyph * 0.46)} {Format(glyph * 0.83)} L {Format(-glyph * 0.14)} {Format(glyph * 0.64)} L {Format(-glyph * 0.18)} {Format(glyph * 0.16)} L {Format(-glyph * 0.82)} {Format(glyph * 0.3)} L {Format(-glyph * 0.82)} {Format(glyph * 0.08)} L {Format(-glyph * 0.18)} {Format(-glyph * 0.28)} Z" fill="#ffffff" />""");
                break;
            case "railway":
                double railwayStroke = Math.Max(2.2, glyph * 0.22);
                svg.AppendLine($"""<path class="map-icon-glyph map-icon-glyph-railway map-icon-glyph-railway-arch" data-map-icon-symbol="china-railway-emblem" d="M {Format(-glyph * 0.77)} {Format(glyph * 0.48)} C {Format(-glyph * 0.95)} {Format(glyph * 0.12)}, {Format(-glyph * 0.92)} {Format(-glyph * 0.48)}, {Format(-glyph * 0.55)} {Format(-glyph * 0.77)} C {Format(-glyph * 0.25)} {Format(-glyph)}, {Format(glyph * 0.25)} {Format(-glyph)}, {Format(glyph * 0.55)} {Format(-glyph * 0.77)} C {Format(glyph * 0.92)} {Format(-glyph * 0.48)}, {Format(glyph * 0.95)} {Format(glyph * 0.12)}, {Format(glyph * 0.77)} {Format(glyph * 0.48)}" fill="none" stroke="#ffffff" stroke-width="{Format(railwayStroke)}" stroke-linecap="square" stroke-linejoin="round" />""");
                svg.AppendLine($"""<rect class="map-icon-glyph map-icon-glyph-railway map-icon-glyph-railway-cap" x="{Format(-glyph * 0.22)}" y="{Format(-glyph * 1.08)}" width="{Format(glyph * 0.44)}" height="{Format(glyph * 0.24)}" rx="{Format(glyph * 0.08)}" fill="#ffffff" />""");
                svg.AppendLine($"""<path class="map-icon-glyph map-icon-glyph-railway map-icon-glyph-railway-rail" d="M {Format(-glyph * 0.43)} {Format(-glyph * 0.12)} C {Format(-glyph * 0.43)} {Format(-glyph * 0.34)}, {Format(-glyph * 0.24)} {Format(-glyph * 0.45)}, 0 {Format(-glyph * 0.45)} C {Format(glyph * 0.24)} {Format(-glyph * 0.45)}, {Format(glyph * 0.43)} {Format(-glyph * 0.34)}, {Format(glyph * 0.43)} {Format(-glyph * 0.12)} L {Format(glyph * 0.43)} {Format(glyph * 0.12)} L {Format(glyph * 0.2)} {Format(glyph * 0.24)} L {Format(glyph * 0.2)} {Format(glyph * 0.68)} L {Format(glyph * 0.72)} {Format(glyph * 0.68)} L {Format(glyph * 0.72)} {Format(glyph * 0.94)} L {Format(-glyph * 0.72)} {Format(glyph * 0.94)} L {Format(-glyph * 0.72)} {Format(glyph * 0.68)} L {Format(-glyph * 0.2)} {Format(glyph * 0.68)} L {Format(-glyph * 0.2)} {Format(glyph * 0.24)} L {Format(-glyph * 0.43)} {Format(glyph * 0.12)} Z" fill="#ffffff" />""");
                break;
            case "intercity":
                svg.AppendLine($"""<path class="map-icon-glyph" d="M {Format(-glyph * 0.56)} {Format(-glyph)} L {Format(-glyph * 0.56)} {Format(glyph)} M {Format(glyph * 0.56)} {Format(-glyph)} L {Format(glyph * 0.56)} {Format(glyph)} M {Format(-glyph * 0.9)} {Format(-glyph * 0.68)} L {Format(glyph * 0.9)} {Format(-glyph * 0.68)} M {Format(-glyph * 0.9)} 0 L {Format(glyph * 0.9)} 0 M {Format(-glyph * 0.9)} {Format(glyph * 0.68)} L {Format(glyph * 0.9)} {Format(glyph * 0.68)}" {common}/>""");
                break;
            case "bus":
                svg.AppendLine($"""<rect class="map-icon-glyph" x="{Format(-glyph * 0.82)}" y="{Format(-glyph * 0.75)}" width="{Format(glyph * 1.64)}" height="{Format(glyph * 1.35)}" rx="{Format(glyph * 0.18)}" {common}/>""");
                svg.AppendLine($"""<path class="map-icon-glyph" d="M {Format(-glyph * 0.5)} {Format(-glyph * 0.3)} L {Format(glyph * 0.5)} {Format(-glyph * 0.3)} M {Format(-glyph * 0.5)} {Format(glyph * 0.82)} L {Format(-glyph * 0.5)} {Format(glyph * 0.55)} M {Format(glyph * 0.5)} {Format(glyph * 0.82)} L {Format(glyph * 0.5)} {Format(glyph * 0.55)}" {common}/>""");
                break;
            case "ferry":
                svg.AppendLine($"""<path class="map-icon-glyph" d="M {Format(-glyph)} {Format(glyph * 0.12)} L {Format(-glyph * 0.58)} {Format(glyph * 0.72)} L 0 {Format(glyph)} L {Format(glyph * 0.58)} {Format(glyph * 0.72)} L {Format(glyph)} {Format(glyph * 0.12)} Z M {Format(-glyph * 0.48)} {Format(glyph * 0.05)} L {Format(-glyph * 0.48)} {Format(-glyph * 0.62)} L {Format(glyph * 0.48)} {Format(-glyph * 0.62)} L {Format(glyph * 0.48)} {Format(glyph * 0.05)}" {common}/>""");
                break;
            case "landmark":
                svg.AppendLine($"""<path class="map-icon-glyph" d="M 0 {Format(glyph)} C {Format(-glyph * 0.72)} {Format(glyph * 0.14)}, {Format(-glyph * 0.82)} {Format(-glyph * 0.28)}, {Format(-glyph * 0.82)} {Format(-glyph * 0.5)} A {Format(glyph * 0.82)} {Format(glyph * 0.82)} 0 1 1 {Format(glyph * 0.82)} {Format(-glyph * 0.5)} C {Format(glyph * 0.82)} {Format(-glyph * 0.28)}, {Format(glyph * 0.72)} {Format(glyph * 0.14)}, 0 {Format(glyph)} Z" {common}/>""");
                svg.AppendLine($"""<circle class="map-icon-glyph" cx="0" cy="{Format(-glyph * 0.48)}" r="{Format(glyph * 0.24)}" {common}/>""");
                break;
            default:
                svg.AppendLine($"""<circle class="map-icon-glyph" cx="0" cy="{Format(-glyph * 0.5)}" r="{Format(glyph * 0.12)}" fill="{color}" />""");
                svg.AppendLine($"""<path class="map-icon-glyph" d="M 0 {Format(-glyph * 0.12)} L 0 {Format(glyph * 0.72)}" {common}/>""");
                break;
        }
    }

    private static string NormalizeMapIconType(string? type)
    {
        return type?.Trim().ToLowerInvariant() switch
        {
            "airport" => "airport",
            "railway" or "railway-station" or "train" => "railway",
            "intercity" or "intercity-rail" => "intercity",
            "bus" or "bus-terminal" => "bus",
            "ferry" or "ferry-terminal" => "ferry",
            "landmark" or "poi" => "landmark",
            "information-box" or "info-box" or "notice" => "information-box",
            _ => "information"
        };
    }

    private static string NormalizeMapIconColor(string? color, string type)
    {
        if (!string.IsNullOrWhiteSpace(color)
            && color.Length == 7
            && color[0] == '#'
            && color.Skip(1).All(Uri.IsHexDigit))
        {
            return color.ToUpperInvariant();
        }

        return type switch
        {
            "airport" => "#3D9FD3",
            "railway" => "#D9332C",
            _ => "#0F766E"
        };
    }

    private static string GetMapIconTitle(string type, string? label)
    {
        string typeTitle = type switch
        {
            "airport" => "Airport",
            "railway" => "Railway station",
            "intercity" => "Intercity rail",
            "bus" => "Bus terminal",
            "ferry" => "Ferry terminal",
            "landmark" => "Landmark",
            "information-box" => "Information box",
            _ => "Information"
        };

        return string.IsNullOrWhiteSpace(label) ? typeTitle : $"{typeTitle}: {label}";
    }

    private static void AppendMapInformationBox(
        StringBuilder svg,
        string id,
        MapIconLayoutOverride icon,
        string color)
    {
        double width = icon.Width is double configuredWidth && double.IsFinite(configuredWidth)
            ? Math.Clamp(configuredWidth, 160, 720)
            : 280;
        double height = icon.Height is double configuredHeight && double.IsFinite(configuredHeight)
            ? Math.Clamp(configuredHeight, 90, 420)
            : 132;
        string title = string.IsNullOrWhiteSpace(icon.Title)
            ? (string.IsNullOrWhiteSpace(icon.Label) ? "Information" : icon.Label.Trim())
            : icon.Title.Trim();
        string body = icon.Text?.Trim() ?? string.Empty;
        double left = -width * 0.5;
        double top = -height * 0.5;
        double padding = 18;
        double titleY = top + 31;
        double bodyY = titleY + 26;
        double bodyFontSize = 14;
        int maxCharacters = Math.Max(12, (int)Math.Floor((width - padding * 2) / (bodyFontSize * 0.58)));
        IReadOnlyList<string> lines = WrapMapInformationText(body, maxCharacters, 7);

        svg.AppendLine($"""<g class="map-icon map-information-box" data-map-icon-id="{Escape(id)}" data-map-icon-type="information-box" data-icon-x="{Format(icon.X)}" data-icon-y="{Format(icon.Y)}" data-icon-width="{Format(width)}" data-icon-height="{Format(height)}" transform="translate({Format(icon.X)} {Format(icon.Y)})">""");
        svg.AppendLine($"""<title>{Escape(title)}</title>""");
        svg.AppendLine($"""<rect class="map-information-box-shadow" x="{Format(left + 4)}" y="{Format(top + 5)}" width="{Format(width)}" height="{Format(height)}" rx="8" fill="#17212b" fill-opacity="0.16" />""");
        svg.AppendLine($"""<rect class="map-information-box-surface" x="{Format(left)}" y="{Format(top)}" width="{Format(width)}" height="{Format(height)}" rx="8" fill="#ffffff" stroke="#c8d1da" stroke-width="1.5" />""");
        svg.AppendLine($"""<rect class="map-information-box-accent" x="{Format(left)}" y="{Format(top)}" width="7" height="{Format(height)}" rx="3.5" fill="{color}" />""");
        svg.AppendLine($"""<text class="map-information-box-title" x="{Format(left + padding)}" y="{Format(titleY)}" font-family="Segoe UI, Microsoft YaHei, Arial, sans-serif" font-size="18" font-weight="700" fill="#16202a">{Escape(title)}</text>""");
        if (lines.Count > 0)
        {
            svg.AppendLine($"""<text class="map-information-box-body" x="{Format(left + padding)}" y="{Format(bodyY)}" font-family="Segoe UI, Microsoft YaHei, Arial, sans-serif" font-size="{Format(bodyFontSize)}" fill="#425466">""");
            for (int index = 0; index < lines.Count; index++)
            {
                svg.AppendLine($"""<tspan x="{Format(left + padding)}" dy="{Format(index == 0 ? 0 : bodyFontSize * 1.45)}">{Escape(lines[index])}</tspan>""");
            }

            svg.AppendLine("</text>");
        }

        svg.AppendLine($"""<rect class="map-icon-hit-target" x="{Format(left)}" y="{Format(top)}" width="{Format(width)}" height="{Format(height)}" fill="#ffffff" fill-opacity="0.001" pointer-events="all" />""");
        svg.AppendLine("</g>");
    }

    private static IReadOnlyList<string> WrapMapInformationText(string text, int maxCharacters, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<string> lines = [];
        foreach (string paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string remaining = paragraph.Trim();
            while (remaining.Length > 0 && lines.Count < maxLines)
            {
                int take = Math.Min(maxCharacters, remaining.Length);
                if (take < remaining.Length)
                {
                    int whitespace = remaining.LastIndexOf(' ', take - 1, take);
                    if (whitespace >= Math.Max(1, take / 2))
                    {
                        take = whitespace;
                    }
                }

                lines.Add(remaining[..take].Trim());
                remaining = remaining[take..].TrimStart();
            }

            if (lines.Count >= maxLines)
            {
                break;
            }
        }

        if (lines.Count == maxLines && text.Length > string.Join(" ", lines).Length)
        {
            lines[^1] = lines[^1].TrimEnd('.', '\u2026') + "\u2026";
        }

        return lines;
    }
}
