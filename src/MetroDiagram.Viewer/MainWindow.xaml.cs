using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MetroDiagram.Core;
using MetroDiagram.Core.Loading;
using MetroDiagram.Core.Models;
using MetroDiagram.Rendering;
using Microsoft.Win32;

namespace MetroDiagram.Viewer;

public partial class MainWindow : Window
{
    private readonly MetroSvgRenderer _renderer = new();
    private MetroExportDocument? _document;
    private string? _jsonPath;
    private string? _currentSvg;
    private string? _defaultExportPath;
    private string? _diagnosticsPath;
    private string? _previewHtmlPath;
    private ViewerSettings _settings = new();
    private string _language = "en";
    private bool _uiReady;
    private bool _suppressUiEvents;
    private IReadOnlyList<string> _loadWarnings = [];
    private IReadOnlyList<string> _renderWarnings = [];

    public MainWindow()
    {
        InitializeComponent();

        _settings = ViewerSettingsStore.Load();
        _language = NormalizeLanguage(_settings.Language);
        ApplySettingsToUi(_settings);
        ApplyLanguage();
        RefreshDefaultExportState(showStatus: false);
        UpdateSummary(null, null);
        UpdateInspector(null, null, [], []);
        ClearPreview(T("InitialPreview"));
        SetStatus(_defaultExportPath is null ? T("Ready") : string.Format(CultureInfo.CurrentCulture, T("DefaultFound"), _defaultExportPath));

        _uiReady = true;
    }

    private void OpenJson_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Filter = "Metro JSON (*.json)|*.json|All files (*.*)|*.*",
            Title = T("OpenMetroJsonTitle")
        };

        string? initialDirectory = GetInitialOpenDirectory();
        if (!string.IsNullOrWhiteSpace(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        LoadDocument(dialog.FileName);
    }

    private void OpenDefaultExport_Click(object sender, RoutedEventArgs e)
    {
        RefreshDefaultExportState(showStatus: false);
        if (string.IsNullOrWhiteSpace(_defaultExportPath))
        {
            SetStatus(T("DefaultMissing"));
            return;
        }

        LoadDocument(_defaultExportPath);
    }

    private void OpenExportFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = GetPreferredExportFolder(createIfMissing: true);
            Process.Start(new ProcessStartInfo("explorer.exe", folder)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("OpenFolderFailed"), ex.Message));
        }
    }

    private void OpenDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_diagnosticsPath) || !File.Exists(_diagnosticsPath))
        {
            SetStatus(T("DiagnosticsMissing"));
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(_diagnosticsPath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("OpenDiagnosticsFailed"), ex.Message));
        }
    }

    private void ResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        ViewerSettings defaults = new()
        {
            LastJsonPath = _jsonPath ?? _settings.LastJsonPath,
            Language = _language
        };

        _settings = defaults;
        ApplySettingsToUi(defaults);
        ClearError();
        SetStatus(T("DefaultsReset"));
        TrySaveCurrentSettings(showError: true);

        if (_document is not null)
        {
            RenderPreview();
        }
    }

    private void RefreshPreview_Click(object sender, RoutedEventArgs e)
    {
        RenderPreview();
    }

    private void SaveSvg_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentSvg))
        {
            SetError(T("NoSvgToSave"));
            return;
        }

        SaveFileDialog dialog = new()
        {
            Filter = "SVG files (*.svg)|*.svg|All files (*.*)|*.*",
            Title = T("SaveSvgTitle"),
            FileName = BuildDefaultSvgFileName()
        };

        string folder = GetPreferredExportFolder(createIfMissing: false);
        if (Directory.Exists(folder))
        {
            dialog.InitialDirectory = folder;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, _currentSvg, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            SetStatus(string.Format(CultureInfo.CurrentCulture, T("SvgSaved"), dialog.FileName));
            ClearError();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("SaveFailed"), ex.Message));
        }
    }

    private void LayoutComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        if (_document is not null)
        {
            RenderPreview();
            return;
        }

        TrySaveCurrentSettings(showError: false);
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        _language = ReadSelectedLanguage();
        ApplyLanguage();
        if (_document is null)
        {
            ClearPreview(T("InitialPreview"));
        }

        TrySaveCurrentSettings(showError: false);
        RefreshDefaultExportState(showStatus: true);
    }

    private void SizePresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        ApplySelectedSizePresetToUi();
        if (_document is not null)
        {
            RenderPreview();
            return;
        }

        TrySaveCurrentSettings(showError: false);
    }

    private void PreviewZoomComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentSvg))
        {
            WritePreviewHtml(_currentSvg);
        }
        else if (_document is null)
        {
            ClearPreview(T("InitialPreview"));
        }

        TrySaveCurrentSettings(showError: false);
    }

    private void SizeTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        _suppressUiEvents = true;
        try
        {
            SelectComboBoxItem(SizePresetComboBox, "custom");
        }
        finally
        {
            _suppressUiEvents = false;
        }

        TrySaveCurrentSettings(showError: false);
    }

    private void RenderOptionChanged(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        if (_document is not null)
        {
            RenderPreview();
            return;
        }

        TrySaveCurrentSettings(showError: false);
    }

    private void LoadDocument(string path)
    {
        try
        {
            MetroLoadResult loadResult = MetroJsonLoader.LoadFromFile(path);
            if (!loadResult.IsValid || loadResult.Document is null)
            {
                _document = null;
                _jsonPath = null;
                _currentSvg = null;
                SaveButton.IsEnabled = false;
                ClearPreview(T("JsonCouldNotLoad"));
                UpdateSummary(null, null);
                UpdateInspector(null, null, [], []);
                SetError(string.Format(CultureInfo.CurrentCulture, T("InvalidJson"), string.Join(Environment.NewLine, loadResult.Errors)));
                SetStatus(T("JsonLoadFailed"));
                return;
            }

            _document = loadResult.Document;
            _jsonPath = path;
            _loadWarnings = loadResult.Warnings;
            _renderWarnings = [];
            FileTextBlock.Text = path;
            UpdateSummary(_document, path);
            UpdateInspector(_document, path, _loadWarnings, _renderWarnings);
            ClearError();
            SetStatus(loadResult.Warnings.Count == 0
                ? T("JsonLoaded")
                : string.Format(CultureInfo.CurrentCulture, T("JsonLoadedWarnings"), string.Join(" | ", loadResult.Warnings)));
            TrySaveCurrentSettings(showError: false);
            RenderPreview();
        }
        catch (Exception ex)
        {
            _document = null;
            _jsonPath = null;
            _currentSvg = null;
            _loadWarnings = [ex.Message];
            _renderWarnings = [];
            SaveButton.IsEnabled = false;
            UpdateSummary(null, null);
            UpdateInspector(null, null, _loadWarnings, _renderWarnings);
            SetError(string.Format(CultureInfo.CurrentCulture, T("InvalidJson"), ex.Message));
            SetStatus(T("JsonLoadFailed"));
        }
    }

    private void RenderPreview()
    {
        if (_document is null)
        {
            SetError(T("RenderFirst"));
            return;
        }

        try
        {
            SvgRenderOptions options = ReadRenderOptions();
            SvgRenderResult renderResult = _renderer.Render(_document, options);
            _currentSvg = renderResult.Svg;
            _renderWarnings = renderResult.Warnings;
            SaveButton.IsEnabled = true;
            WritePreviewHtml(renderResult.Svg);
            MainContentTabControl.SelectedItem = MapPreviewTabItem;
            UpdateInspector(_document, _jsonPath, _loadWarnings, _renderWarnings);
            ClearError();
            SetStatus(renderResult.Warnings.Count == 0
                ? string.Format(CultureInfo.CurrentCulture, T("Rendered"), GetLayoutModeText(options.LayoutMode))
                : string.Format(CultureInfo.CurrentCulture, T("RenderedWarnings"), string.Join(" | ", renderResult.Warnings)));
            TrySaveCurrentSettings(showError: false);
        }
        catch (Exception ex)
        {
            _renderWarnings = [ex.Message];
            UpdateInspector(_document, _jsonPath, _loadWarnings, _renderWarnings);
            SetError(string.Format(CultureInfo.CurrentCulture, T("RenderFailed"), ex.Message));
            SetStatus(T("RenderFailed").Replace("{0}", ex.Message, StringComparison.Ordinal));
        }
    }

    private SvgRenderOptions ReadRenderOptions()
    {
        int width = ReadPositiveInt(WidthTextBox, T("Width"));
        int height = ReadPositiveInt(HeightTextBox, T("Height"));
        int legendWidth = ReadPositiveInt(LegendWidthTextBox, T("Legend"));
        int padding = ReadPositiveInt(PaddingTextBox, T("Padding"));
        NormalizeRenderLayoutInputs(width, height, ref legendWidth, ref padding);
        double lineWidth = ReadPositiveDouble(LineWidthTextBox, T("Line"));
        double stationRadius = ReadPositiveDouble(StationRadiusTextBox, T("Station"));
        double labelFontSize = ReadPositiveDouble(LabelFontSizeTextBox, T("Label"));
        double gridSize = ReadPositiveDouble(GridSizeTextBox, T("Grid"));
        double pathSimplificationTolerance = ReadPositiveDouble(PathSimplificationToleranceTextBox, T("PathTolerance"));
        SvgLayoutMode layoutMode = ReadSelectedLayoutMode();

        return new SvgRenderOptions
        {
            LayoutMode = layoutMode,
            Width = width,
            Height = height,
            Padding = padding,
            Margin = padding,
            LegendWidth = legendWidth,
            LineWidth = lineWidth,
            StationRadius = stationRadius,
            InterchangeStationRadius = Math.Max(stationRadius + 3.5, stationRadius * 1.45),
            LabelFontSize = labelFontSize,
            GridSize = gridSize,
            HideGenericStationLabels = ShowNonImportantStationLabelsCheckBox.IsChecked != true,
            EnableVirtualTransferHints = VirtualTransferHintsCheckBox.IsChecked == true,
            HideCrowdedLabels = HideCrowdedCheckBox.IsChecked == true,
            AlwaysShowInterchanges = AlwaysInterchangesCheckBox.IsChecked == true,
            AlwaysShowTerminals = AlwaysTerminalsCheckBox.IsChecked == true,
            UsePathPoints = UsePathPointsCheckBox.IsChecked == true,
            PathPointSimplificationEnabled = SimplifyPathPointsCheckBox.IsChecked == true,
            PathPointSimplificationTolerance = pathSimplificationTolerance
        };
    }

    private SvgLayoutMode ReadSelectedLayoutMode()
    {
        string? tag = (LayoutComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return tag switch
        {
            "schematic-map" => SvgLayoutMode.SchematicMap,
            "schematic-v2" => SvgLayoutMode.SchematicV2,
            _ => SvgLayoutMode.Geographic
        };
    }

    private string ReadSelectedLanguage()
    {
        string? tag = (LanguageComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return NormalizeLanguage(tag);
    }

    private string ReadSelectedSizePreset()
    {
        string? tag = (SizePresetComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return NormalizeSizePreset(tag);
    }

    private string ReadSelectedPreviewZoom()
    {
        string? tag = (PreviewZoomComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return NormalizePreviewZoom(tag);
    }

    private int ReadPositiveInt(TextBox textBox, string label)
    {
        if (!int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, T("PositiveInteger"), label));
        }

        return value;
    }

    private double ReadPositiveDouble(TextBox textBox, string label)
    {
        if (!double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || value <= 0)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, T("PositiveNumber"), label));
        }

        return value;
    }

    private void WritePreviewHtml(string svg)
    {
        string previewPath = EnsurePreviewHtmlPath();
        File.WriteAllText(previewPath, BuildPreviewHtml(svg, ReadSelectedPreviewZoom()), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        PreviewBrowser.Navigate(new Uri(previewPath));
    }

    private void ClearPreview(string message)
    {
        string escapedMessage = System.Security.SecurityElement.Escape(message) ?? string.Empty;
        string svg = $"""
            <svg xmlns="http://www.w3.org/2000/svg" width="960" height="540" viewBox="0 0 960 540">
              <rect width="960" height="540" fill="#ffffff" />
              <text x="48" y="72" font-family="Arial, sans-serif" font-size="20" font-weight="600" fill="#52616f">{escapedMessage}</text>
            </svg>
            """;
        WritePreviewHtml(svg);
    }

    private readonly record struct SvgPixelSize(double Width, double Height);

    private static string BuildPreviewHtml(string svg, string previewZoom)
    {
        SvgPixelSize svgSize = ReadSvgPixelSize(svg);
        bool fitWidth = string.Equals(previewZoom, "fit-width", StringComparison.Ordinal);
        int zoomPercent = ParsePreviewZoomPercent(previewZoom);
        double displayedWidth = fitWidth ? svgSize.Width : svgSize.Width * zoomPercent / 100;
        double displayedHeight = fitWidth ? svgSize.Height : svgSize.Height * zoomPercent / 100;
        string widthText = displayedWidth.ToString("0.###", CultureInfo.InvariantCulture);
        string heightText = displayedHeight.ToString("0.###", CultureInfo.InvariantCulture);
        string svgWidthText = svgSize.Width.ToString("0.###", CultureInfo.InvariantCulture);
        string svgHeightText = svgSize.Height.ToString("0.###", CultureInfo.InvariantCulture);
        string svgCss = fitWidth
            ? "svg { display: block; width: 100%; max-width: 100%; height: auto; margin: 0 auto; box-shadow: 0 1px 4px rgba(16, 24, 40, 0.18); background: white; }"
            : string.Create(CultureInfo.InvariantCulture, $"svg {{ display: block; width: {widthText}px; height: {heightText}px; max-width: none; margin: 0; box-shadow: 0 1px 4px rgba(16, 24, 40, 0.18); background: white; }}");
        string previewScript = BuildPreviewFocusScript(fitWidth, zoomPercent, svgWidthText, svgHeightText);

        return string.Join(Environment.NewLine,
        [
            "<!doctype html>",
            "<html>",
            "<head>",
            "  <meta charset=\"utf-8\">",
            "  <meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\">",
            "  <style>",
            "    html, body { margin: 0; min-height: 100%; background: #f4f6f8; }",
            "    body { padding: 16px; box-sizing: border-box; overflow: auto; }",
            "    .preview-frame { min-width: 100%; overflow: visible; }",
            $"    {svgCss}",
            "  </style>",
            previewScript,
            "</head>",
            "<body>",
            "<div id=\"preview-frame\" class=\"preview-frame\">",
            svg,
            "</div>",
            "</body>",
            "</html>"
        ]);
    }

    private static string BuildPreviewFocusScript(bool fitWidth, int zoomPercent, string svgWidthText, string svgHeightText)
    {
        string fitWidthText = fitWidth ? "true" : "false";
        string zoomScaleText = (zoomPercent / 100.0).ToString("0.###", CultureInfo.InvariantCulture);

        return string.Join(Environment.NewLine,
        [
            "  <script>",
            "    (function () {",
            $"      var fitWidth = {fitWidthText};",
            $"      var zoomScale = {zoomScaleText};",
            $"      var fallbackSvgWidth = {svgWidthText};",
            $"      var fallbackSvgHeight = {svgHeightText};",
            "      var focusBounds = null;",
            "      function addBox(box, state) {",
            "        if (!box || box.width <= 0 || box.height <= 0) { return; }",
            "        if (!state.hasBox) {",
            "          state.left = box.x;",
            "          state.top = box.y;",
            "          state.right = box.x + box.width;",
            "          state.bottom = box.y + box.height;",
            "          state.hasBox = true;",
            "          return;",
            "        }",
            "        state.left = Math.min(state.left, box.x);",
            "        state.top = Math.min(state.top, box.y);",
            "        state.right = Math.max(state.right, box.x + box.width);",
            "        state.bottom = Math.max(state.bottom, box.y + box.height);",
            "      }",
            "      function readContentBounds(svg) {",
            "        var ids = ['routes', 'route-badges', 'stations', 'labels', 'virtual-transfer-hints'];",
            "        var state = { hasBox: false, left: 0, top: 0, right: 0, bottom: 0 };",
            "        for (var i = 0; i < ids.length; i++) {",
            "          var element = document.getElementById(ids[i]);",
            "          if (!element || !element.getBBox) { continue; }",
            "          try { addBox(element.getBBox(), state); } catch (e) { }",
            "        }",
            "        var legend = document.getElementById('legend');",
            "        if (legend && legend.getBBox && legend.getAttribute('data-legend-placement') !== 'bottom') {",
            "          try { addBox(legend.getBBox(), state); } catch (e) { }",
            "        }",
            "        if (!state.hasBox) { return null; }",
            "        var width = Math.max(1, state.right - state.left);",
            "        var height = Math.max(1, state.bottom - state.top);",
            "        var padding = Math.max(72, Math.min(220, Math.max(width, height) * 0.06));",
            "        var viewLeft = Math.max(0, state.left - padding);",
            "        var viewTop = Math.max(0, state.top - padding);",
            "        var viewRight = Math.min(Math.max(1, fallbackSvgWidth), state.right + padding);",
            "        var viewBottom = Math.min(Math.max(1, fallbackSvgHeight), state.bottom + padding);",
            "        return { left: viewLeft, top: viewTop, width: Math.max(1, viewRight - viewLeft), height: Math.max(1, viewBottom - viewTop) };",
            "      }",
            "      function applyPreviewFocus() {",
            "        var svg = document.getElementsByTagName('svg')[0];",
            "        var frame = document.getElementById('preview-frame');",
            "        if (!svg || !frame) { return; }",
            "        if (!focusBounds) { focusBounds = readContentBounds(svg); }",
            "        var width = fallbackSvgWidth;",
            "        var height = fallbackSvgHeight;",
            "        if (focusBounds) {",
            "          svg.setAttribute('viewBox', focusBounds.left + ' ' + focusBounds.top + ' ' + focusBounds.width + ' ' + focusBounds.height);",
            "          svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');",
            "          svg.setAttribute('data-viewer-preview-focus', 'content-bounds');",
            "          width = focusBounds.width;",
            "          height = focusBounds.height;",
            "        }",
            "        if (fitWidth) {",
            "          var availableWidth = Math.max(320, frame.clientWidth - 32);",
            "          var scale = availableWidth / Math.max(1, width);",
            "          svg.style.width = availableWidth + 'px';",
            "          svg.style.height = Math.max(1, height * scale) + 'px';",
            "        } else {",
            "          svg.style.width = Math.max(1, width * zoomScale) + 'px';",
            "          svg.style.height = Math.max(1, height * zoomScale) + 'px';",
            "        }",
            "        window.scrollTo(0, 0);",
            "        window.setTimeout(function () { window.scrollTo(0, 0); }, 0);",
            "      }",
            "      if (window.attachEvent) {",
            "        window.attachEvent('onload', applyPreviewFocus);",
            "        window.attachEvent('onresize', applyPreviewFocus);",
            "      } else {",
            "        window.addEventListener('load', applyPreviewFocus, false);",
            "        window.addEventListener('resize', applyPreviewFocus, false);",
            "      }",
            "    }());",
            "  </script>"
        ]);
    }

    private static SvgPixelSize ReadSvgPixelSize(string svg)
    {
        Match svgTagMatch = Regex.Match(svg, "<svg\\b[^>]*>", RegexOptions.IgnoreCase);
        if (!svgTagMatch.Success)
        {
            return new SvgPixelSize(1200, 800);
        }

        string svgTag = svgTagMatch.Value;
        double? width = ReadSvgLengthAttribute(svgTag, "width");
        double? height = ReadSvgLengthAttribute(svgTag, "height");

        if ((width is null || height is null) && TryReadViewBoxSize(svgTag, out SvgPixelSize viewBoxSize))
        {
            width ??= viewBoxSize.Width;
            height ??= viewBoxSize.Height;
        }

        return new SvgPixelSize(
            Math.Max(1, width ?? 1200),
            Math.Max(1, height ?? 800));
    }

    private static double? ReadSvgLengthAttribute(string svgTag, string attributeName)
    {
        Match match = Regex.Match(
            svgTag,
            $@"\b{Regex.Escape(attributeName)}\s*=\s*[""'](?<value>[-+]?\d+(?:\.\d+)?)(?:px)?[""']",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return null;
        }

        return double.TryParse(match.Groups["value"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
    }

    private static bool TryReadViewBoxSize(string svgTag, out SvgPixelSize size)
    {
        Match match = Regex.Match(
            svgTag,
            @"\bviewBox\s*=\s*[""'](?<value>[^""']+)[""']",
            RegexOptions.IgnoreCase);
        if (match.Success)
        {
            string[] parts = match.Groups["value"].Value
                .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 4
                && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double width)
                && double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double height))
            {
                size = new SvgPixelSize(width, height);
                return true;
            }
        }

        size = default;
        return false;
    }

    private static int ParsePreviewZoomPercent(string previewZoom)
    {
        return int.TryParse(previewZoom, NumberStyles.Integer, CultureInfo.InvariantCulture, out int zoomPercent) && zoomPercent > 0
            ? zoomPercent
            : 100;
    }

    private string EnsurePreviewHtmlPath()
    {
        if (!string.IsNullOrWhiteSpace(_previewHtmlPath))
        {
            return _previewHtmlPath;
        }

        string folder = Path.Combine(Path.GetTempPath(), "CS2MetroDiagramViewer");
        Directory.CreateDirectory(folder);
        _previewHtmlPath = Path.Combine(folder, $"preview-{Guid.NewGuid():N}.html");
        return _previewHtmlPath;
    }

    private void UpdateSummary(MetroExportDocument? document, string? path)
    {
        if (document is null)
        {
            FileTextBlock.Text = T("NoJsonLoaded");
            SummaryTextBlock.Text = T("SummaryEmpty");
            return;
        }

        string city = string.IsNullOrWhiteSpace(document.City?.Name) ? T("UnnamedCity") : document.City!.Name!;
        int lineCount = document.Network?.Lines?.Count ?? 0;
        int stationCount = document.Network?.Stations?.Count ?? 0;
        FileTextBlock.Text = path ?? T("JsonLoadedShort");
        SummaryTextBlock.Text = string.Format(CultureInfo.CurrentCulture, T("Summary"), city, lineCount, stationCount);
    }

    private void UpdateInspector(
        MetroExportDocument? document,
        string? path,
        IReadOnlyList<string> loadWarnings,
        IReadOnlyList<string> renderWarnings)
    {
        if (document is null)
        {
            InspectorSummaryTextBlock.Text = T("InspectorEmpty");
            InspectorWarningsTextBlock.Text = string.Join(Environment.NewLine, loadWarnings.Concat(renderWarnings).Where(warning => !string.IsNullOrWhiteSpace(warning)));
            DiagnosticsPathTextBlock.Text = string.Empty;
            _diagnosticsPath = null;
            OpenDiagnosticsButton.IsEnabled = false;
            LinesDataGrid.ItemsSource = Array.Empty<LineInspectorRow>();
            StationsDataGrid.ItemsSource = Array.Empty<StationInspectorRow>();
            return;
        }

        ExportDataInspection inspection = ExportDataInspector.Inspect(document, path, loadWarnings, renderWarnings, T);
        InspectorSummaryTextBlock.Text = inspection.Summary;
        InspectorWarningsTextBlock.Text = inspection.Warnings.Count == 0 ? string.Empty : string.Join(Environment.NewLine, inspection.Warnings);
        _diagnosticsPath = inspection.DiagnosticsPath;
        OpenDiagnosticsButton.IsEnabled = _diagnosticsPath is not null;
        DiagnosticsPathTextBlock.Text = _diagnosticsPath is null
            ? T("DiagnosticsMissing")
            : string.Format(CultureInfo.CurrentCulture, T("DiagnosticsFound"), _diagnosticsPath);
        LinesDataGrid.ItemsSource = inspection.Lines;
        StationsDataGrid.ItemsSource = inspection.Stations;
    }

    private string BuildDefaultSvgFileName()
    {
        string layout = GetLayoutModeText(ReadSelectedLayoutMode());
        string baseName = string.IsNullOrWhiteSpace(_jsonPath)
            ? "metro-diagram"
            : Path.GetFileNameWithoutExtension(_jsonPath);
        return $"{baseName}.{layout}.svg";
    }

    private static string GetLayoutModeText(SvgLayoutMode layoutMode)
    {
        return layoutMode switch
        {
            SvgLayoutMode.SchematicLite => "schematic-lite",
            SvgLayoutMode.SchematicV2 => "schematic-v2",
            SvgLayoutMode.SchematicMap => "schematic-map",
            _ => "geographic"
        };
    }

    private void RefreshDefaultExportState(bool showStatus)
    {
        _defaultExportPath = FindDefaultExportPath();
        OpenDefaultButton.IsEnabled = _defaultExportPath is not null;

        if (showStatus)
        {
            SetStatus(_defaultExportPath is null
                ? T("DefaultMissing")
                : string.Format(CultureInfo.CurrentCulture, T("DefaultFound"), _defaultExportPath));
        }
    }

    private static string? FindDefaultExportPath()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string[] candidates =
        [
            @"D:\CS2MetroDiagram\metro-export.json",
            string.IsNullOrWhiteSpace(documents)
                ? string.Empty
                : Path.Combine(documents, "CS2MetroDiagram", "metro-export.json")
        ];

        return candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
    }

    private string? GetInitialOpenDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LastJsonPath))
        {
            string? folder = Path.GetDirectoryName(_settings.LastJsonPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                return folder;
            }
        }

        if (!string.IsNullOrWhiteSpace(_defaultExportPath))
        {
            string? folder = Path.GetDirectoryName(_defaultExportPath);
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
            {
                return folder;
            }
        }

        if (Directory.Exists(@"D:\CS2MetroDiagram"))
        {
            return @"D:\CS2MetroDiagram";
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string documentsExportFolder = string.IsNullOrWhiteSpace(documents)
            ? string.Empty
            : Path.Combine(documents, "CS2MetroDiagram");
        return Directory.Exists(documentsExportFolder) ? documentsExportFolder : null;
    }

    private string GetPreferredExportFolder(bool createIfMissing)
    {
        List<string> candidates = [];

        if (!string.IsNullOrWhiteSpace(_defaultExportPath))
        {
            string? folder = Path.GetDirectoryName(_defaultExportPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                candidates.Add(folder);
            }
        }

        if (!string.IsNullOrWhiteSpace(_jsonPath))
        {
            string? folder = Path.GetDirectoryName(_jsonPath);
            if (!string.IsNullOrWhiteSpace(folder))
            {
                candidates.Add(folder);
            }
        }

        candidates.Add(@"D:\CS2MetroDiagram");

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (!string.IsNullOrWhiteSpace(documents))
        {
            candidates.Add(Path.Combine(documents, "CS2MetroDiagram"));
        }

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        string fallback = candidates.Last();
        if (createIfMissing)
        {
            Directory.CreateDirectory(fallback);
        }

        return fallback;
    }

    private void ApplySettingsToUi(ViewerSettings settings)
    {
        _suppressUiEvents = true;
        try
        {
            SelectComboBoxItem(LayoutComboBox, NormalizeLayoutMode(settings.LayoutMode));
            SelectComboBoxItem(LanguageComboBox, NormalizeLanguage(settings.Language));
            SelectComboBoxItem(SizePresetComboBox, NormalizeSizePreset(settings.SizePreset));
            SelectComboBoxItem(PreviewZoomComboBox, NormalizePreviewZoom(settings.PreviewZoom));
            WidthTextBox.Text = settings.Width.ToString(CultureInfo.InvariantCulture);
            HeightTextBox.Text = settings.Height.ToString(CultureInfo.InvariantCulture);
            LegendWidthTextBox.Text = settings.LegendWidth.ToString(CultureInfo.InvariantCulture);
            PaddingTextBox.Text = settings.Padding.ToString(CultureInfo.InvariantCulture);
            LineWidthTextBox.Text = settings.LineWidth.ToString(CultureInfo.InvariantCulture);
            StationRadiusTextBox.Text = settings.StationRadius.ToString(CultureInfo.InvariantCulture);
            LabelFontSizeTextBox.Text = settings.LabelFontSize.ToString(CultureInfo.InvariantCulture);
            GridSizeTextBox.Text = settings.GridSize.ToString(CultureInfo.InvariantCulture);
            ShowNonImportantStationLabelsCheckBox.IsChecked = !settings.HideGenericStationLabels;
            VirtualTransferHintsCheckBox.IsChecked = settings.EnableVirtualTransferHints;
            HideCrowdedCheckBox.IsChecked = settings.HideCrowdedLabels;
            AlwaysInterchangesCheckBox.IsChecked = settings.AlwaysShowInterchanges;
            AlwaysTerminalsCheckBox.IsChecked = settings.AlwaysShowTerminals;
            UsePathPointsCheckBox.IsChecked = settings.UsePathPoints;
            SimplifyPathPointsCheckBox.IsChecked = settings.PathPointSimplificationEnabled;
            PathSimplificationToleranceTextBox.Text = settings.PathPointSimplificationTolerance.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _suppressUiEvents = false;
        }
    }

    private void ApplyLanguage()
    {
        _language = ReadSelectedLanguage();
        Title = $"{T("WindowTitle")} {MetroDiagramAppInfo.Version}";
        OpenButton.Content = T("OpenJson");
        OpenDefaultButton.Content = T("OpenDefaultExport");
        OpenFolderButton.Content = T("OpenExportFolder");
        ResetButton.Content = T("ResetDefaults");
        RefreshButton.Content = T("RefreshPreview");
        SaveButton.Content = T("SaveSvg");
        OpenDiagnosticsButton.Content = T("OpenDiagnostics");
        LayoutLabelTextBlock.Text = T("Layout");
        SizePresetLabelTextBlock.Text = T("SizePreset");
        SizePresetCustomItem.Content = T("SizeCustom");
        SizePresetCompactItem.Content = T("SizeCompact");
        SizePresetStandardItem.Content = T("SizeStandard");
        SizePresetPosterItem.Content = T("SizePoster");
        SizePresetUltraItem.Content = T("SizeUltra");
        LanguageLabelTextBlock.Text = T("Language");
        PreviewZoomLabelTextBlock.Text = T("PreviewZoom");
        PreviewZoomFitWidthItem.Content = T("PreviewZoomFitWidth");
        WidthLabelTextBlock.Text = T("Width");
        HeightLabelTextBlock.Text = T("Height");
        LegendLabelTextBlock.Text = T("Legend");
        PaddingLabelTextBlock.Text = T("Padding");
        LineLabelTextBlock.Text = T("Line");
        StationLabelTextBlock.Text = T("Station");
        LabelFontLabelTextBlock.Text = T("Label");
        GridLabelTextBlock.Text = T("Grid");
        ShowNonImportantStationLabelsCheckBox.Content = T("ShowNonImportantLabels");
        VirtualTransferHintsCheckBox.Content = T("VirtualTransferHints");
        HideCrowdedCheckBox.Content = T("HideCrowded");
        AlwaysInterchangesCheckBox.Content = T("AlwaysInterchanges");
        AlwaysTerminalsCheckBox.Content = T("AlwaysTerminals");
        UsePathPointsCheckBox.Content = T("UsePathPoints");
        SimplifyPathPointsCheckBox.Content = T("SimplifyPathPoints");
        PathToleranceLabelTextBlock.Text = T("PathTolerance");
        MapPreviewTabItem.Header = T("MapPreviewTab");
        ExportDataTabItem.Header = T("ExportDataTab");
        InspectorHeadingTextBlock.Text = T("InspectorHeading");
        LinesHeadingTextBlock.Text = T("LinesHeading");
        StationsHeadingTextBlock.Text = T("StationsHeading");
        LineColorColumn.Header = T("LineColorColumn");
        LineNameColumn.Header = T("LineNameColumn");
        LineModeColumn.Header = T("LineModeColumn");
        LineStopsColumn.Header = T("LineStopsColumn");
        LinePathPointsColumn.Header = T("LinePathPointsColumn");
        LinePathSourcesColumn.Header = T("LinePathSourcesColumn");
        LineTerminiColumn.Header = T("LineTerminiColumn");
        StationNameColumn.Header = T("StationNameColumn");
        StationIdColumn.Header = T("StationIdColumn");
        StationLinesColumn.Header = T("StationLinesColumn");
        StationInterchangeColumn.Header = T("StationInterchangeColumn");
        StationPositionColumn.Header = T("StationPositionColumn");
        UpdateSummary(_document, _jsonPath);
        UpdateInspector(_document, _jsonPath, _loadWarnings, _renderWarnings);
    }

    private void TrySaveCurrentSettings(bool showError)
    {
        try
        {
            _settings = BuildSettingsFromUi();
            ViewerSettingsStore.Save(_settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (showError)
            {
                SetError(string.Format(CultureInfo.CurrentCulture, T("SettingsSaveFailed"), ex.Message));
            }
        }
    }

    private ViewerSettings BuildSettingsFromUi()
    {
        return new ViewerSettings
        {
            LastJsonPath = _jsonPath ?? _settings.LastJsonPath,
            Language = _language,
            LayoutMode = GetLayoutModeText(ReadSelectedLayoutMode()),
            SizePreset = ReadSelectedSizePreset(),
            PreviewZoom = ReadSelectedPreviewZoom(),
            Width = ReadIntOrDefault(WidthTextBox, _settings.Width),
            Height = ReadIntOrDefault(HeightTextBox, _settings.Height),
            LegendWidth = ReadIntOrDefault(LegendWidthTextBox, _settings.LegendWidth),
            Padding = ReadIntOrDefault(PaddingTextBox, _settings.Padding),
            LineWidth = ReadDoubleOrDefault(LineWidthTextBox, _settings.LineWidth),
            StationRadius = ReadDoubleOrDefault(StationRadiusTextBox, _settings.StationRadius),
            LabelFontSize = ReadDoubleOrDefault(LabelFontSizeTextBox, _settings.LabelFontSize),
            GridSize = ReadDoubleOrDefault(GridSizeTextBox, _settings.GridSize),
            HideGenericStationLabels = ShowNonImportantStationLabelsCheckBox.IsChecked != true,
            EnableVirtualTransferHints = VirtualTransferHintsCheckBox.IsChecked == true,
            HideCrowdedLabels = HideCrowdedCheckBox.IsChecked == true,
            AlwaysShowInterchanges = AlwaysInterchangesCheckBox.IsChecked == true,
            AlwaysShowTerminals = AlwaysTerminalsCheckBox.IsChecked == true,
            UsePathPoints = UsePathPointsCheckBox.IsChecked == true,
            PathPointSimplificationEnabled = SimplifyPathPointsCheckBox.IsChecked == true,
            PathPointSimplificationTolerance = ReadDoubleOrDefault(PathSimplificationToleranceTextBox, _settings.PathPointSimplificationTolerance)
        };
    }

    private static void SelectComboBoxItem(ComboBox comboBox, string tag)
    {
        foreach (object item in comboBox.Items)
        {
            if (item is ComboBoxItem comboBoxItem
                && string.Equals(comboBoxItem.Tag?.ToString(), tag, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = comboBoxItem;
                return;
            }
        }
    }

    private static int ReadIntOrDefault(TextBox textBox, int fallback)
    {
        return int.TryParse(textBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) && value > 0
            ? value
            : fallback;
    }

    private static double ReadDoubleOrDefault(TextBox textBox, double fallback)
    {
        return double.TryParse(textBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0
            ? value
            : fallback;
    }

    private static string NormalizeLanguage(string? language)
    {
        return string.Equals(language, "zh", StringComparison.Ordinal) ? "zh" : "en";
    }

    private static string NormalizeLayoutMode(string? layoutMode)
    {
        return layoutMode switch
        {
            "schematic-lite" => "schematic-v2",
            "schematic-v2" => "schematic-v2",
            "schematic-map" => "schematic-map",
            _ => "geographic"
        };
    }

    private static string NormalizeSizePreset(string? preset)
    {
        return preset switch
        {
            "compact" => "compact",
            "standard" => "standard",
            "poster" => "poster",
            "ultra" => "ultra",
            _ => "custom"
        };
    }

    private static string NormalizePreviewZoom(string? previewZoom)
    {
        return previewZoom switch
        {
            "fit-width" => "fit-width",
            "50" => "50",
            "75" => "75",
            "150" => "150",
            "200" => "200",
            "300" => "300",
            _ => "100"
        };
    }

    private void ApplySelectedSizePresetToUi()
    {
        string preset = ReadSelectedSizePreset();
        SvgRenderSizePreset? renderPreset = preset switch
        {
            "compact" => SvgRenderSizePreset.Compact,
            "standard" => SvgRenderSizePreset.Standard,
            "poster" => SvgRenderSizePreset.Poster,
            "ultra" => SvgRenderSizePreset.Ultra,
            _ => null
        };

        if (!renderPreset.HasValue)
        {
            return;
        }

        SvgRenderSize size = SvgRenderSizePresets.Get(renderPreset.Value);
        _suppressUiEvents = true;
        try
        {
            WidthTextBox.Text = size.Width.ToString(CultureInfo.InvariantCulture);
            HeightTextBox.Text = size.Height.ToString(CultureInfo.InvariantCulture);
            LegendWidthTextBox.Text = "240";
            PaddingTextBox.Text = "80";
        }
        finally
        {
            _suppressUiEvents = false;
        }
    }

    private string T(string key)
    {
        return ViewerResources.Text(_language, key);
    }

    private void SetStatus(string message)
    {
        StatusTextBlock.Text = message;
    }

    private void SetError(string message)
    {
        ErrorTextBlock.Text = message;
    }

    private void ClearError()
    {
        ErrorTextBlock.Text = string.Empty;
    }

    protected override void OnClosed(EventArgs e)
    {
        TrySaveCurrentSettings(showError: false);
        TryDeletePreviewHtml();
        base.OnClosed(e);
    }

    private void TryDeletePreviewHtml()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_previewHtmlPath) && File.Exists(_previewHtmlPath))
            {
                File.Delete(_previewHtmlPath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void NormalizeRenderLayoutInputs(int width, int height, ref int legendWidth, ref int padding)
    {
        int normalizedLegendWidth = ViewerRenderSettingSanitizer.NormalizeLegendWidth(legendWidth, width);
        int normalizedPadding = ViewerRenderSettingSanitizer.NormalizePadding(padding, width, height);
        if (normalizedLegendWidth == legendWidth && normalizedPadding == padding)
        {
            return;
        }

        legendWidth = normalizedLegendWidth;
        padding = normalizedPadding;

        _suppressUiEvents = true;
        try
        {
            LegendWidthTextBox.Text = normalizedLegendWidth.ToString(CultureInfo.InvariantCulture);
            PaddingTextBox.Text = normalizedPadding.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _suppressUiEvents = false;
        }
    }

}
