using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using MetroDiagram.Core;
using MetroDiagram.Core.Exporting;
using MetroDiagram.Core.Loading;
using MetroDiagram.Core.Models;
using MetroDiagram.Rendering;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace MetroDiagram.Viewer;

public partial class MainWindow : Window
{
    private static readonly TimeSpan ManualEditFinalPreviewDelay = TimeSpan.FromMilliseconds(120);
    private readonly MetroSvgRenderer _renderer = new();
    private readonly DispatcherTimer _manualEditPreviewRefreshTimer;
    private MetroExportDocument? _document;
    private string? _jsonPath;
    private string? _currentSvg;
    private string? _defaultExportPath;
    private string? _diagnosticsPath;
    private string? _pendingPreviewHtml;
    private LayoutOverrideDocument? _layoutOverrides;
    private string? _layoutOverridePath;
    private string? _selectedOverrideKind;
    private string? _selectedOverrideStationId;
    private IReadOnlyList<string> _selectedOverrideStationIds = [];
    private readonly Stack<string?> _undoStack = new();
    private readonly Stack<string?> _redoStack = new();
    // Last known preview scroll offset, reported by the injected script. Re-applied
    // after every preview reload so manual edits do not bounce the view back to
    // the top-left corner. Reset when a different document is loaded.
    private double _lastPreviewScrollX;
    private double _lastPreviewScrollY;
    // Station cx/cy points parsed from the current preview SVG; rebuilt lazily
    // whenever _currentSvg changes (reference comparison).
    private Dictionary<string, ViewerSvgPoint>? _svgStationPointCache;
    private string? _svgStationPointCacheSource;
    private bool _renderInFlight;
    private bool _renderQueued;
    private long _statusStamp;
    private static readonly JsonSerializerOptions ManualEditSnapshotOptions = new() { IncludeFields = false };
    private ViewerSettings _settings = new();
    private string _language = "en";
    private bool _uiReady;
    private bool _suppressUiEvents;
    private bool _previewRenderIsDirty;
    private bool _previewBrowserReady;
    private IReadOnlyList<string> _loadWarnings = [];
    private IReadOnlyList<string> _renderWarnings = [];

    public MainWindow()
    {
        InitializeComponent();
        _ = InitializePreviewBrowserAsync();
        _manualEditPreviewRefreshTimer = new DispatcherTimer
        {
            Interval = ManualEditFinalPreviewDelay
        };
        _manualEditPreviewRefreshTimer.Tick += ManualEditPreviewRefreshTimer_Tick;

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

    private async Task InitializePreviewBrowserAsync()
    {
        try
        {
            await PreviewBrowser.EnsureCoreWebView2Async();
            PreviewBrowser.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled = false;
            PreviewBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;
            PreviewBrowser.CoreWebView2.WebMessageReceived += PreviewBrowser_WebMessageReceived;
            _previewBrowserReady = true;

            if (!string.IsNullOrWhiteSpace(_pendingPreviewHtml))
            {
                PreviewBrowser.CoreWebView2.NavigateToString(_pendingPreviewHtml);
            }
        }
        catch (Exception ex)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("RenderFailed"), ex.Message));
        }
    }

    private void PreviewBrowser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(e.WebMessageAsJson);
            JsonElement root = document.RootElement;
            string? type = ReadMessageString(root, "type");
            string? stationId = ReadMessageString(root, "stationId");
            IReadOnlyList<string> stationIds = ReadMessageStringList(root, "stationIds");
            if (stationIds.Count == 0 && !string.IsNullOrWhiteSpace(stationId))
            {
                stationIds = [stationId!];
            }

            if (string.IsNullOrWhiteSpace(type))
            {
                return;
            }

            switch (type)
            {
                case "stationSelected":
                    if (!string.IsNullOrWhiteSpace(stationId))
                    {
                        SelectManualEditTarget("station", stationId);
                    }
                    break;
                case "labelSelected":
                    if (!string.IsNullOrWhiteSpace(stationId))
                    {
                        SelectManualEditTarget("label", stationId);
                    }
                    break;
                case "segmentSelected":
                    SelectManualEditSegment(stationIds);
                    break;
                case "selectionCleared":
                    ClearManualEditSelection();
                    break;
                case "previewScrolled":
                    if (TryReadMessageDouble(root, "x", out double scrollX) &&
                        TryReadMessageDouble(root, "y", out double scrollY))
                    {
                        _lastPreviewScrollX = scrollX;
                        _lastPreviewScrollY = scrollY;
                    }
                    break;
                case "stationDragged":
                    if (!string.IsNullOrWhiteSpace(stationId) &&
                        TryReadMessageDouble(root, "deltaX", out double stationDeltaX) &&
                        TryReadMessageDouble(root, "deltaY", out double stationDeltaY))
                    {
                        ApplyStationDragOverride(stationId, stationDeltaX, stationDeltaY);
                    }
                    break;
                case "labelDragged":
                    if (!string.IsNullOrWhiteSpace(stationId) &&
                        TryReadMessageDouble(root, "deltaX", out double labelDeltaX) &&
                        TryReadMessageDouble(root, "deltaY", out double labelDeltaY))
                    {
                        ApplyLabelDragOverride(stationId, labelDeltaX, labelDeltaY);
                    }
                    break;
                case "segmentDragged":
                    if (TryReadMessageDouble(root, "deltaX", out double segmentDeltaX) &&
                        TryReadMessageDouble(root, "deltaY", out double segmentDeltaY))
                    {
                        ApplySegmentDragOverride(stationIds, segmentDeltaX, segmentDeltaY);
                    }
                    break;
                case "bendChanged":
                    if (stationIds.Count >= 2 &&
                        TryReadMessageDouble(root, "bendX", out double bendX) &&
                        TryReadMessageDouble(root, "bendY", out double bendY))
                    {
                        ApplyBendOverride(stationIds[0], stationIds[1], bendX, bendY);
                    }
                    break;
            }
        }
        catch (JsonException)
        {
        }
    }

    private static string? ReadMessageString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryReadMessageDouble(JsonElement root, string propertyName, out double result)
    {
        result = 0;
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetDouble(out result);
        }

        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    }

    private static IReadOnlyList<string> ReadMessageStringList(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Select(text => text!)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?
                .Split(['|', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Distinct(StringComparer.Ordinal)
                .ToList() ?? [];
        }

        return [];
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
        _manualEditPreviewRefreshTimer.Stop();
        RenderPreview();
    }

    private async void SaveSvg_Click(object sender, RoutedEventArgs e)
    {
        if (_previewRenderIsDirty && _document is not null)
        {
            _manualEditPreviewRefreshTimer.Stop();
            RenderPreview();
        }

        // Renders run on a background thread; wait for the latest result so the
        // export never captures a stale map.
        while (_renderInFlight || (_previewRenderIsDirty && _document is not null))
        {
            await Task.Delay(50);
        }

        if (string.IsNullOrWhiteSpace(_currentSvg))
        {
            SetError(T("NoSvgToSave"));
            return;
        }

        string format = ReadSelectedSaveFormat();
        SaveFileDialog dialog = new()
        {
            Filter = "SVG (*.svg)|*.svg|PNG (*.png)|*.png|PDF (*.pdf)|*.pdf",
            FilterIndex = format switch { "png" => 2, "pdf" => 3, _ => 1 },
            DefaultExt = "." + format,
            Title = T("SaveSvgTitle"),
            FileName = Path.ChangeExtension(BuildDefaultSvgFileName(), "." + format)
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

        string svg = _currentSvg!;
        string path = dialog.FileName;
        string extension = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            SetStatus(string.Format(CultureInfo.CurrentCulture, T("Exporting"), path));
            await Task.Run(() =>
            {
                switch (extension)
                {
                    case ".png":
                        MetroDiagram.Export.SvgDocumentExporter.ExportPng(svg, path);
                        break;
                    case ".pdf":
                        MetroDiagram.Export.SvgDocumentExporter.ExportPdf(svg, path);
                        break;
                    default:
                        File.WriteAllText(path, svg, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                        break;
                }
            });
            SetStatus(string.Format(CultureInfo.CurrentCulture, T("SvgSaved"), path));
            ClearError();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
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

    private string ReadSelectedSaveFormat()
    {
        string? tag = (SaveFormatComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return tag is "png" or "pdf" ? tag : "svg";
    }

    private void SaveFormatComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        TrySaveCurrentSettings(showError: false);
    }

    private void ManualEditControl_Changed(object sender, RoutedEventArgs e)
    {
        if (!_uiReady || _suppressUiEvents)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(_currentSvg))
        {
            WritePreviewHtml(_currentSvg);
        }

        UpdateManualEditButtons();
        SetStatus(ManualEditCheckBox.IsChecked == true
            ? string.Format(CultureInfo.CurrentCulture, T("ManualEditEnabled"), T(GetManualEditModeResourceKey(ReadManualEditMode())))
            : T("ManualEditDisabled"));
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
                _layoutOverrides = null;
                _layoutOverridePath = null;
                ClearManualEditSelection();
                SaveButton.IsEnabled = false;
                SetManualEditEnabled(false);
                ClearPreview(T("JsonCouldNotLoad"));
                UpdateSummary(null, null);
                UpdateInspector(null, null, [], []);
                SetError(string.Format(CultureInfo.CurrentCulture, T("InvalidJson"), string.Join(Environment.NewLine, loadResult.Errors)));
                SetStatus(T("JsonLoadFailed"));
                return;
            }

            _document = loadResult.Document;
            _jsonPath = path;
            List<string> loadWarnings = loadResult.Warnings.ToList();
            _layoutOverrides = TryLoadLayoutOverrides(path, loadWarnings, out _layoutOverridePath);
            ResetUndoHistory();
            _lastPreviewScrollX = 0;
            _lastPreviewScrollY = 0;
            _loadWarnings = loadWarnings;
            _renderWarnings = [];
            FileTextBlock.Text = path;
            ClearManualEditSelection();
            SetManualEditEnabled(true);
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
            _layoutOverrides = null;
            _layoutOverridePath = null;
            ClearManualEditSelection();
            _loadWarnings = [ex.Message];
            _renderWarnings = [];
            SaveButton.IsEnabled = false;
            SetManualEditEnabled(false);
            UpdateSummary(null, null);
            UpdateInspector(null, null, _loadWarnings, _renderWarnings);
            SetError(string.Format(CultureInfo.CurrentCulture, T("InvalidJson"), ex.Message));
            SetStatus(T("JsonLoadFailed"));
        }
    }

    // Renders run on a background thread so the annealer and schematic passes do
    // not freeze the UI. Requests coalesce: at most one render is in flight, one
    // more can be queued, and only the newest request's options are used. A status
    // stamp keeps a caller's own status text (e.g. "edit undone") from being
    // clobbered by the render-completion status when the caller set it later.
    private async void RenderPreview()
    {
        if (_document is null)
        {
            SetError(T("RenderFirst"));
            return;
        }

        if (_renderInFlight)
        {
            _renderQueued = true;
            return;
        }

        _renderInFlight = true;
        try
        {
            do
            {
                _renderQueued = false;
                MetroExportDocument document = _document;
                try
                {
                    SvgRenderOptions options = ReadRenderOptions();
                    long statusStampAtRequest = _statusStamp;
                    SvgRenderResult renderResult = await Task.Run(() => _renderer.Render(document, options));
                    if (!ReferenceEquals(_document, document))
                    {
                        // A different export was loaded while rendering; its own
                        // load path already requested a fresh render.
                        continue;
                    }

                    _currentSvg = renderResult.Svg;
                    _renderWarnings = renderResult.Warnings;
                    _previewRenderIsDirty = false;
                    SaveButton.IsEnabled = true;
                    WritePreviewHtml(renderResult.Svg);
                    MainContentTabControl.SelectedItem = MapPreviewTabItem;
                    UpdateInspector(_document, _jsonPath, _loadWarnings, _renderWarnings);
                    ClearError();
                    if (_statusStamp == statusStampAtRequest)
                    {
                        SetStatus(renderResult.Warnings.Count == 0
                            ? string.Format(CultureInfo.CurrentCulture, T("Rendered"), GetLayoutModeText(options.LayoutMode))
                            : string.Format(CultureInfo.CurrentCulture, T("RenderedWarnings"), string.Join(" | ", renderResult.Warnings)));
                    }

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
            while (_renderQueued);
        }
        finally
        {
            _renderInFlight = false;
        }
    }

    private void MarkPreviewDirty()
    {
        _previewRenderIsDirty = true;
    }

    private void ScheduleManualEditPreviewRefresh()
    {
        _manualEditPreviewRefreshTimer.Stop();
        _manualEditPreviewRefreshTimer.Interval = ManualEditFinalPreviewDelay;
        _manualEditPreviewRefreshTimer.Start();
    }

    private void ManualEditPreviewRefreshTimer_Tick(object? sender, EventArgs e)
    {
        _manualEditPreviewRefreshTimer.Stop();
        if (!_previewRenderIsDirty || _document is null)
        {
            return;
        }

        RenderPreview();
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
            PathPointSimplificationTolerance = pathSimplificationTolerance,
            // Deep copy: the render now runs on a background thread while the UI
            // thread keeps mutating _layoutOverrides on further edits.
            LayoutOverrides = DeserializeOverridesSnapshot(SerializeOverridesSnapshot(_layoutOverrides))
        };
    }

    private LayoutOverrideDocument? TryLoadLayoutOverrides(
        string jsonPath,
        List<string> loadWarnings,
        out string? overridePath)
    {
        if (LayoutOverrideLoader.TryLoadDefaultSidecar(jsonPath, out LayoutOverrideDocument? overrides, out overridePath, out string? error))
        {
            if (overrides is not null && !overrides.IsEmpty)
            {
                loadWarnings.Add(string.Format(CultureInfo.CurrentCulture, T("LayoutOverridesLoaded"), overridePath));
            }

            return overrides;
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            loadWarnings.Add(string.Format(CultureInfo.CurrentCulture, T("LayoutOverridesFailed"), overridePath ?? LayoutOverrideLoader.GetDefaultSidecarPath(jsonPath), error));
        }

        return null;
    }

    private void ApplyStationDragOverride(string? stationId, double deltaX, double deltaY)
    {
        if (_document is null || string.IsNullOrWhiteSpace(_jsonPath) || string.IsNullOrWhiteSpace(stationId))
        {
            return;
        }

        if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < 0.5)
        {
            return;
        }

        try
        {
            PushUndoSnapshot();
            _layoutOverrides ??= new LayoutOverrideDocument();
            _layoutOverridePath ??= LayoutOverrideLoader.GetDefaultSidecarPath(_jsonPath);

            ApplyStationOverrideDeltaCore(stationId, deltaX, deltaY);

            SaveOrDeleteLayoutOverrides();
            SelectManualEditTarget("station", stationId, showStatus: false);
            MarkPreviewDirty();
            ScheduleManualEditPreviewRefresh();
            SetStatus(string.Format(CultureInfo.CurrentCulture, T("StationOverrideSaved"), GetStationDisplayName(stationId), _layoutOverridePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("StationOverrideSaveFailed"), ex.Message));
        }
    }

    private void ApplySegmentDragOverride(IReadOnlyList<string> stationIds, double deltaX, double deltaY)
    {
        if (_document is null || string.IsNullOrWhiteSpace(_jsonPath))
        {
            return;
        }

        List<string> normalizedStationIds = NormalizeStationIds(stationIds);
        if (normalizedStationIds.Count == 0)
        {
            return;
        }

        if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < 0.5)
        {
            return;
        }

        try
        {
            PushUndoSnapshot();
            _layoutOverrides ??= new LayoutOverrideDocument();
            _layoutOverridePath ??= LayoutOverrideLoader.GetDefaultSidecarPath(_jsonPath);

            foreach (string stationId in normalizedStationIds)
            {
                ApplyStationOverrideDeltaCore(stationId, deltaX, deltaY);
            }

            SaveOrDeleteLayoutOverrides();
            SelectManualEditSegment(normalizedStationIds, showStatus: false);
            MarkPreviewDirty();
            ScheduleManualEditPreviewRefresh();
            SetStatus(string.Format(CultureInfo.CurrentCulture, T("SegmentOverrideSaved"), normalizedStationIds.Count, _layoutOverridePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("StationOverrideSaveFailed"), ex.Message));
        }
    }

    private void ApplyBendOverride(string stationIdA, string stationIdB, double x, double y)
    {
        if (_document is null || string.IsNullOrWhiteSpace(_jsonPath)
            || string.IsNullOrWhiteSpace(stationIdA) || string.IsNullOrWhiteSpace(stationIdB)
            || string.Equals(stationIdA, stationIdB, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            PushUndoSnapshot();
            _layoutOverrides ??= new LayoutOverrideDocument();
            _layoutOverridePath ??= LayoutOverrideLoader.GetDefaultSidecarPath(_jsonPath);

            string key = LayoutOverrideDocument.BendEdgeKey(stationIdA.Trim(), stationIdB.Trim());
            _layoutOverrides.Bends[key] = new BendLayoutOverride { X = x, Y = y };

            SaveOrDeleteLayoutOverrides();
            SelectManualEditSegment([stationIdA, stationIdB], showStatus: false);
            MarkPreviewDirty();
            ScheduleManualEditPreviewRefresh();
            SetStatus(string.Format(
                CultureInfo.CurrentCulture,
                T("BendOverrideSaved"),
                GetStationDisplayName(stationIdA),
                GetStationDisplayName(stationIdB)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("StationOverrideSaveFailed"), ex.Message));
        }
    }

    private void ApplyStationOverrideDeltaCore(string stationId, double deltaX, double deltaY)
    {
        _layoutOverrides ??= new LayoutOverrideDocument();
        if (!_layoutOverrides.Stations.TryGetValue(stationId, out StationLayoutOverride? stationOverride))
        {
            stationOverride = new StationLayoutOverride();
            _layoutOverrides.Stations[stationId] = stationOverride;
        }

        stationOverride.Enabled = true;
        if (stationOverride.X is not null || stationOverride.Y is not null)
        {
            if (stationOverride.X is not null)
            {
                stationOverride.X += deltaX;
            }
            else
            {
                stationOverride.Dx = (stationOverride.Dx ?? 0) + deltaX;
            }

            if (stationOverride.Y is not null)
            {
                stationOverride.Y += deltaY;
            }
            else
            {
                stationOverride.Dy = (stationOverride.Dy ?? 0) + deltaY;
            }
        }
        else
        {
            stationOverride.Dx = (stationOverride.Dx ?? 0) + deltaX;
            stationOverride.Dy = (stationOverride.Dy ?? 0) + deltaY;
        }
    }

    private void ApplyLabelDragOverride(string? stationId, double deltaX, double deltaY)
    {
        if (_document is null || string.IsNullOrWhiteSpace(_jsonPath) || string.IsNullOrWhiteSpace(stationId))
        {
            return;
        }

        if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) < 0.5)
        {
            return;
        }

        try
        {
            PushUndoSnapshot();
            _layoutOverrides ??= new LayoutOverrideDocument();
            _layoutOverridePath ??= LayoutOverrideLoader.GetDefaultSidecarPath(_jsonPath);

            if (!_layoutOverrides.Labels.TryGetValue(stationId, out LabelLayoutOverride? labelOverride))
            {
                labelOverride = new LabelLayoutOverride();
                _layoutOverrides.Labels[stationId] = labelOverride;
            }

            labelOverride.Hidden = false;
            labelOverride.Position = "manual";
            if (labelOverride.X is not null || labelOverride.Y is not null)
            {
                if (labelOverride.X is not null)
                {
                    labelOverride.X += deltaX;
                }
                else
                {
                    labelOverride.Dx = (labelOverride.Dx ?? 0) + deltaX;
                }

                if (labelOverride.Y is not null)
                {
                    labelOverride.Y += deltaY;
                }
                else
                {
                    labelOverride.Dy = (labelOverride.Dy ?? 0) + deltaY;
                }
            }
            else
            {
                labelOverride.Dx = (labelOverride.Dx ?? 0) + deltaX;
                labelOverride.Dy = (labelOverride.Dy ?? 0) + deltaY;
            }

            SaveOrDeleteLayoutOverrides();
            SelectManualEditTarget("label", stationId, showStatus: false);
            MarkPreviewDirty();
            ScheduleManualEditPreviewRefresh();
            SetStatus(string.Format(CultureInfo.CurrentCulture, T("LabelOverrideSaved"), GetStationDisplayName(stationId), _layoutOverridePath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("LabelOverrideSaveFailed"), ex.Message));
        }
    }

    private void ResetSelectedOverride_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedOverrideKind)
            || (string.IsNullOrWhiteSpace(_selectedOverrideStationId) && _selectedOverrideStationIds.Count == 0))
        {
            SetStatus(T("ManualEditNoSelection"));
            return;
        }

        if (string.Equals(_selectedOverrideKind, "segment", StringComparison.Ordinal))
        {
            ResetManualEditSegmentOverrides(_selectedOverrideStationIds);
            return;
        }

        ResetManualEditOverride(_selectedOverrideKind, _selectedOverrideStationId!);
    }

    private void AlignHorizontal_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectedAlignment(horizontal: true);
    }

    private void AlignVertical_Click(object sender, RoutedEventArgs e)
    {
        ApplySelectedAlignment(horizontal: false);
    }

    private void ToggleSelectedLabel_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || string.IsNullOrWhiteSpace(_jsonPath) || string.IsNullOrWhiteSpace(_selectedOverrideStationId))
        {
            SetStatus(T("ManualEditNoSelection"));
            return;
        }

        try
        {
            string stationId = _selectedOverrideStationId;
            PushUndoSnapshot();
            _layoutOverrides ??= new LayoutOverrideDocument();
            _layoutOverridePath ??= LayoutOverrideLoader.GetDefaultSidecarPath(_jsonPath);

            if (!_layoutOverrides.Labels.TryGetValue(stationId, out LabelLayoutOverride? labelOverride))
            {
                labelOverride = new LabelLayoutOverride();
                _layoutOverrides.Labels[stationId] = labelOverride;
            }

            bool hide = labelOverride.Hidden != true;
            labelOverride.Hidden = hide;
            if (!hide && labelOverride.X is null && labelOverride.Y is null && labelOverride.Dx is null && labelOverride.Dy is null)
            {
                _layoutOverrides.Labels.Remove(stationId);
            }

            SaveOrDeleteLayoutOverrides();
            SelectManualEditTarget("label", stationId, showStatus: false);
            SetStatus(string.Format(
                CultureInfo.CurrentCulture,
                hide ? T("LabelOverrideHidden") : T("LabelOverrideShown"),
                GetStationDisplayName(stationId)));
            RenderPreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("LabelOverrideSaveFailed"), ex.Message));
        }
    }

    // Cycle the selected station's label side: auto -> left -> right -> top ->
    // bottom -> auto. Picking a side clears any manual drag offset so the label
    // lands exactly on that candidate slot; "auto" returns to the renderer's
    // own overlap-avoiding choice.
    private static readonly string[] LabelSideCycle = ["left", "right", "top", "bottom"];

    private void LabelSide_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null || string.IsNullOrWhiteSpace(_jsonPath) || string.IsNullOrWhiteSpace(_selectedOverrideStationId))
        {
            SetStatus(T("ManualEditNoSelection"));
            return;
        }

        try
        {
            string stationId = _selectedOverrideStationId;
            PushUndoSnapshot();
            _layoutOverrides ??= new LayoutOverrideDocument();
            _layoutOverridePath ??= LayoutOverrideLoader.GetDefaultSidecarPath(_jsonPath);

            _layoutOverrides.Labels.TryGetValue(stationId, out LabelLayoutOverride? labelOverride);
            string? current = labelOverride?.Position;
            int currentIndex = Array.FindIndex(LabelSideCycle, side => string.Equals(side, current, StringComparison.OrdinalIgnoreCase));
            string? next = currentIndex switch
            {
                -1 => LabelSideCycle[0],
                _ when currentIndex + 1 < LabelSideCycle.Length => LabelSideCycle[currentIndex + 1],
                _ => null
            };

            if (next is null)
            {
                if (labelOverride is not null)
                {
                    labelOverride.Position = null;
                    if (labelOverride.X is null && labelOverride.Y is null
                        && labelOverride.Dx is null && labelOverride.Dy is null
                        && labelOverride.Hidden != true)
                    {
                        _layoutOverrides.Labels.Remove(stationId);
                    }
                }
            }
            else
            {
                if (labelOverride is null)
                {
                    labelOverride = new LabelLayoutOverride();
                    _layoutOverrides.Labels[stationId] = labelOverride;
                }

                labelOverride.Position = next;
                labelOverride.X = null;
                labelOverride.Y = null;
                labelOverride.Dx = null;
                labelOverride.Dy = null;
                labelOverride.Hidden = false;
            }

            SaveOrDeleteLayoutOverrides();
            SelectManualEditTarget("label", stationId, showStatus: false);
            SetStatus(string.Format(
                CultureInfo.CurrentCulture,
                T("LabelSideSet"),
                GetStationDisplayName(stationId),
                T(next is null ? "LabelSideAuto" : $"LabelSide{char.ToUpperInvariant(next[0])}{next[1..]}")));
            MarkPreviewDirty();
            ScheduleManualEditPreviewRefresh();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("LabelOverrideSaveFailed"), ex.Message));
        }
    }

    private void ClearOverrides_Click(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            this,
            T("ClearOverridesConfirm"),
            T("ClearOverrides"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            PushUndoSnapshot();
            _layoutOverrides = null;
            if (!string.IsNullOrWhiteSpace(_layoutOverridePath) && File.Exists(_layoutOverridePath))
            {
                File.Delete(_layoutOverridePath);
            }

            ClearManualEditSelection();
            UpdateManualEditButtons();
            SetStatus(T("OverridesCleared"));
            RenderPreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("OverridesClearFailed"), ex.Message));
        }
    }

    private void OpenOverrides_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_layoutOverridePath))
            {
                if (string.IsNullOrWhiteSpace(_jsonPath))
                {
                    return;
                }

                _layoutOverridePath = LayoutOverrideLoader.GetDefaultSidecarPath(_jsonPath);
            }

            if (!File.Exists(_layoutOverridePath))
            {
                SaveOrDeleteLayoutOverrides(forceWriteEmpty: true);
            }

            Process.Start(new ProcessStartInfo(_layoutOverridePath)
            {
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("OpenOverridesFailed"), ex.Message));
        }
    }

    private void ResetManualEditOverride(string kind, string stationId)
    {
        if (_layoutOverrides is null)
        {
            SetStatus(T("ManualEditNoOverride"));
            return;
        }

        string? snapshot = SerializeOverridesSnapshot(_layoutOverrides);
        bool removed = string.Equals(kind, "label", StringComparison.Ordinal)
            ? _layoutOverrides.Labels.Remove(stationId)
            : _layoutOverrides.Stations.Remove(stationId);
        if (!removed)
        {
            SetStatus(T("ManualEditNoOverride"));
            return;
        }

        PushUndoSnapshotState(snapshot);
        try
        {
            SaveOrDeleteLayoutOverrides();
            SelectManualEditTarget(kind, stationId, showStatus: false);
            SetStatus(string.Format(CultureInfo.CurrentCulture, T("ManualEditOverrideReset"), GetStationDisplayName(stationId)));
            RenderPreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("OverridesSaveFailed"), ex.Message));
        }
    }

    private void ResetManualEditSegmentOverrides(IReadOnlyList<string> stationIds)
    {
        if (_layoutOverrides is null)
        {
            SetStatus(T("ManualEditNoOverride"));
            return;
        }

        List<string> normalizedStationIds = NormalizeStationIds(stationIds);
        string? snapshot = SerializeOverridesSnapshot(_layoutOverrides);
        bool removed = false;
        foreach (string stationId in normalizedStationIds)
        {
            removed |= _layoutOverrides.Stations.Remove(stationId);
        }

        if (normalizedStationIds.Count >= 2)
        {
            removed |= _layoutOverrides.Bends.Remove(
                LayoutOverrideDocument.BendEdgeKey(normalizedStationIds[0], normalizedStationIds[1]));
        }

        if (!removed)
        {
            SetStatus(T("ManualEditNoOverride"));
            return;
        }

        PushUndoSnapshotState(snapshot);
        try
        {
            SaveOrDeleteLayoutOverrides();
            SelectManualEditSegment(normalizedStationIds, showStatus: false);
            SetStatus(string.Format(CultureInfo.CurrentCulture, T("ManualEditSegmentOverrideReset"), normalizedStationIds.Count));
            RenderPreview();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("OverridesSaveFailed"), ex.Message));
        }
    }

    private void ApplySelectedAlignment(bool horizontal)
    {
        if (_document is null || string.IsNullOrWhiteSpace(_jsonPath))
        {
            SetStatus(T("ManualEditNoSelection"));
            return;
        }

        if (string.Equals(_selectedOverrideKind, "segment", StringComparison.Ordinal)
            && _selectedOverrideStationIds.Count >= 2)
        {
            ApplySegmentEndpointAlignment(_selectedOverrideStationIds, horizontal);
            return;
        }

        if (string.IsNullOrWhiteSpace(_selectedOverrideStationId))
        {
            SetStatus(T("ManualEditNoSelection"));
            return;
        }

        ApplySingleStationAlignment(_selectedOverrideStationId, horizontal);
    }

    private void ApplySegmentEndpointAlignment(IReadOnlyList<string> stationIds, bool horizontal)
    {
        List<string> normalizedStationIds = NormalizeStationIds(stationIds);
        List<(string StationId, ViewerSvgPoint Point)> points = [];
        foreach (string stationId in normalizedStationIds)
        {
            if (TryReadStationSvgPoint(stationId, out ViewerSvgPoint point))
            {
                points.Add((stationId, point));
            }
        }

        if (points.Count < 2)
        {
            SetStatus(T("ManualEditAlignmentNoPreviewPoint"));
            return;
        }

        double target = horizontal
            ? points.Average(point => point.Point.Y)
            : points.Average(point => point.Point.X);
        List<(string StationId, double DeltaX, double DeltaY)> deltas = [];
        foreach ((string stationId, ViewerSvgPoint point) in points)
        {
            deltas.Add(horizontal
                ? (stationId, 0, target - point.Y)
                : (stationId, target - point.X, 0));
        }

        ApplyStationAlignmentDeltas(deltas, string.Format(
            CultureInfo.CurrentCulture,
            horizontal ? T("SegmentAlignedHorizontal") : T("SegmentAlignedVertical"),
            points.Count));
    }

    private void ApplySingleStationAlignment(string stationId, bool horizontal)
    {
        if (!TryReadStationSvgPoint(stationId, out ViewerSvgPoint selectedPoint)
            || !TryFindNearestConnectedStationPoint(stationId, selectedPoint, out string? neighborId, out ViewerSvgPoint neighborPoint))
        {
            SetStatus(T("ManualEditAlignmentNoNeighbor"));
            return;
        }

        double deltaX = horizontal ? 0 : neighborPoint.X - selectedPoint.X;
        double deltaY = horizontal ? neighborPoint.Y - selectedPoint.Y : 0;
        ApplyStationAlignmentDeltas(
            [(stationId, deltaX, deltaY)],
            string.Format(
                CultureInfo.CurrentCulture,
                horizontal ? T("StationAlignedHorizontal") : T("StationAlignedVertical"),
                GetStationDisplayName(stationId),
                GetStationDisplayName(neighborId)));
    }

    private void ApplyStationAlignmentDeltas(
        IReadOnlyList<(string StationId, double DeltaX, double DeltaY)> deltas,
        string status)
    {
        List<(string StationId, double DeltaX, double DeltaY)> meaningfulDeltas = deltas
            .Where(delta => Math.Sqrt(delta.DeltaX * delta.DeltaX + delta.DeltaY * delta.DeltaY) >= 0.1)
            .ToList();
        if (meaningfulDeltas.Count == 0)
        {
            SetStatus(T("ManualEditAlignmentNoChange"));
            return;
        }

        try
        {
            PushUndoSnapshot();
            _layoutOverrides ??= new LayoutOverrideDocument();
            _layoutOverridePath ??= LayoutOverrideLoader.GetDefaultSidecarPath(_jsonPath!);
            foreach ((string stationId, double deltaX, double deltaY) in meaningfulDeltas)
            {
                ApplyStationOverrideDeltaCore(stationId, deltaX, deltaY);
            }

            SaveOrDeleteLayoutOverrides();
            MarkPreviewDirty();
            RenderPreview();
            SetStatus(status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("StationOverrideSaveFailed"), ex.Message));
        }
    }

    private void SelectManualEditTarget(string kind, string stationId, bool showStatus = true)
    {
        _selectedOverrideKind = string.Equals(kind, "label", StringComparison.Ordinal) ? "label" : "station";
        _selectedOverrideStationId = stationId;
        _selectedOverrideStationIds = [stationId];
        UpdateManualEditButtons();
        if (showStatus)
        {
            SetStatus(string.Format(
                CultureInfo.CurrentCulture,
                _selectedOverrideKind == "label" ? T("ManualEditLabelSelected") : T("ManualEditStationSelected"),
                GetStationDisplayName(stationId)));
        }
    }

    private void SelectManualEditSegment(IReadOnlyList<string> stationIds, bool showStatus = true)
    {
        List<string> normalizedStationIds = NormalizeStationIds(stationIds);
        if (normalizedStationIds.Count == 0)
        {
            return;
        }

        _selectedOverrideKind = "segment";
        _selectedOverrideStationId = normalizedStationIds[0];
        _selectedOverrideStationIds = normalizedStationIds;
        UpdateManualEditButtons();
        if (showStatus)
        {
            SetStatus(string.Format(CultureInfo.CurrentCulture, T("ManualEditSegmentSelected"), normalizedStationIds.Count));
        }
    }

    private void ClearManualEditSelection()
    {
        _selectedOverrideKind = null;
        _selectedOverrideStationId = null;
        _selectedOverrideStationIds = [];
        UpdateManualEditButtons();
    }

    private void SetManualEditEnabled(bool enabled)
    {
        _suppressUiEvents = true;
        try
        {
            ManualEditCheckBox.IsEnabled = enabled;
            ManualEditModeComboBox.IsEnabled = enabled;
            if (!enabled)
            {
                ManualEditCheckBox.IsChecked = false;
            }
        }
        finally
        {
            _suppressUiEvents = false;
        }

        UpdateManualEditButtons();
    }

    private void UpdateManualEditButtons()
    {
        bool hasDocument = _document is not null;
        bool hasSelection = hasDocument && (_selectedOverrideStationIds.Count > 0 || !string.IsNullOrWhiteSpace(_selectedOverrideStationId));
        bool selectedSegment = string.Equals(_selectedOverrideKind, "segment", StringComparison.Ordinal);
        bool hasOverrides = _layoutOverrides?.IsEmpty == false;
        bool selectedLabelHidden = hasSelection
            && !selectedSegment
            && _layoutOverrides?.Labels.TryGetValue(_selectedOverrideStationId!, out LabelLayoutOverride? labelOverride) == true
            && labelOverride.Hidden == true;

        ResetSelectedOverrideButton.IsEnabled = hasSelection
            && _layoutOverrides is not null
            && (selectedSegment
                ? _selectedOverrideStationIds.Any(stationId => _layoutOverrides.Stations.ContainsKey(stationId))
                : string.Equals(_selectedOverrideKind, "label", StringComparison.Ordinal)
                    ? _layoutOverrides.Labels.ContainsKey(_selectedOverrideStationId!)
                    : _layoutOverrides.Stations.ContainsKey(_selectedOverrideStationId!));
        AlignHorizontalButton.IsEnabled = hasSelection && !string.Equals(_selectedOverrideKind, "label", StringComparison.Ordinal);
        AlignVerticalButton.IsEnabled = AlignHorizontalButton.IsEnabled;
        ToggleSelectedLabelButton.IsEnabled = hasSelection && !selectedSegment;
        ToggleSelectedLabelButton.Content = selectedLabelHidden ? T("ShowSelectedLabel") : T("HideSelectedLabel");
        LabelSideButton.IsEnabled = hasSelection && !selectedSegment;
        ClearOverridesButton.IsEnabled = hasOverrides || (!string.IsNullOrWhiteSpace(_layoutOverridePath) && File.Exists(_layoutOverridePath));
        OpenOverridesButton.IsEnabled = hasDocument;
        UndoButton.IsEnabled = hasDocument && _undoStack.Count > 0;
        RedoButton.IsEnabled = hasDocument && _redoStack.Count > 0;
        // The edit sub-toolbar occupies a full row, so it only exists while editing.
        ManualEditToolbarCard.Visibility = ManualEditCheckBox.IsChecked == true
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    // ---- Manual-edit undo/redo (snapshots of the whole override document) ----

    private static string? SerializeOverridesSnapshot(LayoutOverrideDocument? doc)
    {
        return doc is null ? null : JsonSerializer.Serialize(doc, ManualEditSnapshotOptions);
    }

    private static LayoutOverrideDocument? DeserializeOverridesSnapshot(string? json)
    {
        return string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<LayoutOverrideDocument>(json, ManualEditSnapshotOptions);
    }

    // Call immediately before mutating the override document, so the pre-edit
    // state can be restored by Undo. Clears the redo history (new branch).
    private void PushUndoSnapshot()
    {
        PushUndoSnapshotState(SerializeOverridesSnapshot(_layoutOverrides));
    }

    // For mutations that may be a no-op: capture the snapshot first, then push
    // it only after confirming a change actually happened.
    private void PushUndoSnapshotState(string? snapshot)
    {
        _undoStack.Push(snapshot);
        _redoStack.Clear();
        UpdateManualEditButtons();
    }

    private void ResetUndoHistory()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    private void UndoManualEdit()
    {
        if (_undoStack.Count == 0)
        {
            SetStatus(T("NothingToUndo"));
            return;
        }

        _redoStack.Push(SerializeOverridesSnapshot(_layoutOverrides));
        _layoutOverrides = DeserializeOverridesSnapshot(_undoStack.Pop());
        ApplyUndoRedoState(T("EditUndone"));
    }

    private void RedoManualEdit()
    {
        if (_redoStack.Count == 0)
        {
            SetStatus(T("NothingToRedo"));
            return;
        }

        _undoStack.Push(SerializeOverridesSnapshot(_layoutOverrides));
        _layoutOverrides = DeserializeOverridesSnapshot(_redoStack.Pop());
        ApplyUndoRedoState(T("EditRedone"));
    }

    private void ApplyUndoRedoState(string statusText)
    {
        // The current selection may no longer have an override; drop it so the
        // action buttons re-evaluate cleanly.
        _selectedOverrideKind = null;
        _selectedOverrideStationId = null;
        _selectedOverrideStationIds = [];

        try
        {
            SaveOrDeleteLayoutOverrides();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SetError(string.Format(CultureInfo.CurrentCulture, T("StationOverrideSaveFailed"), ex.Message));
            return;
        }

        RenderPreview();
        UpdateManualEditButtons();
        SetStatus(statusText);
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        UndoManualEdit();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        RedoManualEdit();
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_document is null
            || (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != System.Windows.Input.ModifierKeys.Control)
        {
            return;
        }

        bool shift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift;
        if (e.Key == System.Windows.Input.Key.Z && !shift)
        {
            UndoManualEdit();
            e.Handled = true;
        }
        else if (e.Key == System.Windows.Input.Key.Y || (e.Key == System.Windows.Input.Key.Z && shift))
        {
            RedoManualEdit();
            e.Handled = true;
        }
    }

    private string ReadManualEditMode()
    {
        string? tag = (ManualEditModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return tag switch
        {
            "label" => "label",
            "segment" => "segment",
            "bend" => "bend",
            _ => "station"
        };
    }

    private static string GetManualEditModeResourceKey(string mode)
    {
        return mode switch
        {
            "label" => "ManualEditLabels",
            "segment" => "ManualEditSegments",
            "bend" => "ManualEditBends",
            _ => "ManualEditStations"
        };
    }

    private static List<string> NormalizeStationIds(IEnumerable<string> stationIds)
    {
        return stationIds
            .Where(stationId => !string.IsNullOrWhiteSpace(stationId))
            .Select(stationId => stationId.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private bool TryReadStationSvgPoint(string stationId, out ViewerSvgPoint point)
    {
        point = default;
        if (string.IsNullOrWhiteSpace(_currentSvg))
        {
            return false;
        }

        // One regex pass per rendered SVG; group operations (align/reset over many
        // stations) then look points up in O(1) instead of rescanning the document.
        if (!ReferenceEquals(_svgStationPointCacheSource, _currentSvg))
        {
            Dictionary<string, ViewerSvgPoint> points = new(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(_currentSvg, "<circle\\b[^>]*>", RegexOptions.IgnoreCase))
            {
                string tag = match.Value;
                string? currentStationId = ReadSvgStringAttribute(tag, "data-station-id");
                if (string.IsNullOrWhiteSpace(currentStationId) || points.ContainsKey(currentStationId!))
                {
                    continue;
                }

                double? x = ReadSvgLengthAttribute(tag, "cx");
                double? y = ReadSvgLengthAttribute(tag, "cy");
                if (x is not null && y is not null)
                {
                    points[currentStationId!] = new ViewerSvgPoint(x.Value, y.Value);
                }
            }

            _svgStationPointCache = points;
            _svgStationPointCacheSource = _currentSvg;
        }

        return _svgStationPointCache!.TryGetValue(stationId, out point);
    }

    private bool TryFindNearestConnectedStationPoint(
        string stationId,
        ViewerSvgPoint selectedPoint,
        out string neighborId,
        out ViewerSvgPoint neighborPoint)
    {
        neighborId = string.Empty;
        neighborPoint = default;
        HashSet<string> neighborIds = new(StringComparer.Ordinal);
        foreach (MetroLine line in _document?.Network?.Lines ?? [])
        {
            IReadOnlyList<string> stops = line.Stops ?? [];
            for (int index = 0; index < stops.Count; index++)
            {
                if (!string.Equals(stops[index], stationId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (index > 0 && !string.IsNullOrWhiteSpace(stops[index - 1]))
                {
                    neighborIds.Add(stops[index - 1]);
                }

                if (index + 1 < stops.Count && !string.IsNullOrWhiteSpace(stops[index + 1]))
                {
                    neighborIds.Add(stops[index + 1]);
                }
            }
        }

        double bestDistance = double.MaxValue;
        foreach (string candidateId in neighborIds)
        {
            if (!TryReadStationSvgPoint(candidateId, out ViewerSvgPoint candidatePoint))
            {
                continue;
            }

            double distance = Distance(selectedPoint, candidatePoint);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                neighborId = candidateId;
                neighborPoint = candidatePoint;
            }
        }

        return !string.IsNullOrWhiteSpace(neighborId);
    }

    private static double Distance(ViewerSvgPoint first, ViewerSvgPoint second)
    {
        double deltaX = second.X - first.X;
        double deltaY = second.Y - first.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private string GetStationDisplayName(string stationId)
    {
        string? name = _document?.Network?.Stations?
            .FirstOrDefault(station => string.Equals(station.Id, stationId, StringComparison.Ordinal))
            ?.Name;
        return string.IsNullOrWhiteSpace(name) ? stationId : name!;
    }

    private void SaveOrDeleteLayoutOverrides(bool forceWriteEmpty = false)
    {
        if (string.IsNullOrWhiteSpace(_jsonPath))
        {
            return;
        }

        _layoutOverridePath ??= LayoutOverrideLoader.GetDefaultSidecarPath(_jsonPath);
        if (_layoutOverrides is null && forceWriteEmpty)
        {
            _layoutOverrides = new LayoutOverrideDocument();
        }

        if (_layoutOverrides is null || (_layoutOverrides.IsEmpty && !forceWriteEmpty))
        {
            if (File.Exists(_layoutOverridePath))
            {
                File.Delete(_layoutOverridePath);
            }

            _layoutOverrides = null;
            UpdateManualEditButtons();
            return;
        }

        _layoutOverrides ??= new LayoutOverrideDocument();
        LayoutOverrideLoader.SaveToFile(_layoutOverridePath, _layoutOverrides);
        UpdateManualEditButtons();
    }

    private SvgLayoutMode ReadSelectedLayoutMode()
    {
        string? tag = (LayoutComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        return tag switch
        {
            "schematic-map" => SvgLayoutMode.SchematicMap,
            "schematic-anneal" => SvgLayoutMode.SchematicAnneal,
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
        bool enableManualEditing = ManualEditCheckBox.IsChecked == true && _document is not null;
        // Re-highlight the current station/segment selection after each render so a
        // multi-selected group stays visible (and re-draggable) across the reload.
        IReadOnlyList<string> selectedForHighlight =
            string.Equals(_selectedOverrideKind, "label", StringComparison.Ordinal)
                ? []
                : _selectedOverrideStationIds;
        _pendingPreviewHtml = BuildPreviewHtml(svg, ReadSelectedPreviewZoom(), enableManualEditing, ReadManualEditMode(), selectedForHighlight, _lastPreviewScrollX, _lastPreviewScrollY);
        if (_previewBrowserReady && PreviewBrowser.CoreWebView2 is not null)
        {
            PreviewBrowser.CoreWebView2.NavigateToString(_pendingPreviewHtml);
        }
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

    private readonly record struct ViewerSvgPoint(double X, double Y);

    private static string BuildPreviewHtml(string svg, string previewZoom, bool enableManualEditing, string manualEditMode, IReadOnlyList<string> selectedStationIds, double restoreScrollX, double restoreScrollY)
    {
        SvgPixelSize svgSize = ReadSvgPixelSize(svg);
        bool fitPage = string.Equals(previewZoom, "fit-page", StringComparison.Ordinal);
        bool fitWidth = string.Equals(previewZoom, "fit-width", StringComparison.Ordinal);
        int zoomPercent = ParsePreviewZoomPercent(previewZoom);
        double displayedWidth = fitWidth || fitPage ? svgSize.Width : svgSize.Width * zoomPercent / 100;
        double displayedHeight = fitWidth || fitPage ? svgSize.Height : svgSize.Height * zoomPercent / 100;
        string widthText = displayedWidth.ToString("0.###", CultureInfo.InvariantCulture);
        string heightText = displayedHeight.ToString("0.###", CultureInfo.InvariantCulture);
        string svgWidthText = svgSize.Width.ToString("0.###", CultureInfo.InvariantCulture);
        string svgHeightText = svgSize.Height.ToString("0.###", CultureInfo.InvariantCulture);
        const string sheetCss = "box-shadow: 0 1px 3px rgba(16, 24, 40, 0.10), 0 8px 24px rgba(16, 24, 40, 0.08); border-radius: 4px; background: white;";
        string svgCss = fitWidth || fitPage
            ? $"svg {{ display: block; width: 100%; max-width: 100%; height: auto; margin: 0 auto; {sheetCss} }}"
            : string.Create(CultureInfo.InvariantCulture, $"svg {{ display: block; width: {widthText}px; height: {heightText}px; max-width: none; margin: 0; {sheetCss} }}");
        bool editStations = enableManualEditing && string.Equals(manualEditMode, "station", StringComparison.Ordinal);
        bool editLabels = enableManualEditing && string.Equals(manualEditMode, "label", StringComparison.Ordinal);
        bool editSegments = enableManualEditing && string.Equals(manualEditMode, "segment", StringComparison.Ordinal);
        bool editBends = enableManualEditing && string.Equals(manualEditMode, "bend", StringComparison.Ordinal);
        string dragCss = enableManualEditing
            ? "    circle.station[data-station-id], text.station-label[data-station-id] { user-select: none; -ms-user-select: none; } circle.station-interchange-inner { pointer-events: none; }"
                + (editStations ? " circle.station[data-station-id] { cursor: move; }" : string.Empty)
                + (editLabels ? " text.station-label[data-station-id] { cursor: move; }" : string.Empty)
                + (editSegments ? " polyline.route, polyline.schematic-v2-parallel-corridor, polyline.product-line { cursor: move; }" : string.Empty)
                + (editBends ? " polyline.route, polyline.schematic-v2-parallel-corridor, polyline.product-line { cursor: crosshair; }" : string.Empty)
            : string.Empty;
        string previewScript = BuildPreviewFocusScript(fitPage, fitWidth, zoomPercent, svgWidthText, svgHeightText, restoreScrollX, restoreScrollY);
        string manualEditScript = BuildManualEditScript(editStations, editLabels, editSegments, editBends, selectedStationIds);

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
            "    ::-webkit-scrollbar { width: 10px; height: 10px; }",
            "    ::-webkit-scrollbar-track { background: transparent; }",
            "    ::-webkit-scrollbar-thumb { background: #c3ccd7; border-radius: 6px; border: 2px solid #f4f6f8; }",
            "    ::-webkit-scrollbar-thumb:hover { background: #a8b4c2; }",
            "    ::-webkit-scrollbar-corner { background: transparent; }",
            $"    {svgCss}",
            dragCss,
            "  </style>",
            previewScript,
            manualEditScript,
            "</head>",
            "<body>",
            "<div id=\"preview-frame\" class=\"preview-frame\">",
            svg,
            "</div>",
            "</body>",
            "</html>"
        ]);
    }

    private static string BuildPreviewFocusScript(bool fitPage, bool fitWidth, int zoomPercent, string svgWidthText, string svgHeightText, double restoreScrollX, double restoreScrollY)
    {
        string fitPageText = fitPage ? "true" : "false";
        string fitWidthText = fitWidth ? "true" : "false";
        string zoomScaleText = (zoomPercent / 100.0).ToString("0.###", CultureInfo.InvariantCulture);
        string scrollXText = restoreScrollX.ToString("0.##", CultureInfo.InvariantCulture);
        string scrollYText = restoreScrollY.ToString("0.##", CultureInfo.InvariantCulture);

        return string.Join(Environment.NewLine,
        [
            "  <script>",
            "    (function () {",
            $"      var fitPage = {fitPageText};",
            $"      var fitWidth = {fitWidthText};",
            $"      var zoomScale = {zoomScaleText};",
            $"      var fallbackSvgWidth = {svgWidthText};",
            $"      var fallbackSvgHeight = {svgHeightText};",
            $"      var restoreScrollX = {scrollXText};",
            $"      var restoreScrollY = {scrollYText};",
            "      var initialScrollApplied = false;",
            "      var scrollReportTimer = null;",
            "      function reportScroll() {",
            "        try {",
            "          if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {",
            "            window.chrome.webview.postMessage({ type: 'previewScrolled', x: String(window.pageXOffset || 0), y: String(window.pageYOffset || 0) });",
            "          }",
            "        } catch (ex) { }",
            "      }",
            "      function onPreviewScroll() {",
            "        if (scrollReportTimer) { window.clearTimeout(scrollReportTimer); }",
            "        scrollReportTimer = window.setTimeout(reportScroll, 120);",
            "      }",
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
            "        // Header, footer legend, and network content all count: the preview",
            "        // must show the complete sheet, not a network-only crop.",
            "        var ids = ['transit-map-header', 'routes', 'route-badges', 'stations', 'labels', 'virtual-transfer-hints', 'legend'];",
            "        var state = { hasBox: false, left: 0, top: 0, right: 0, bottom: 0 };",
            "        for (var i = 0; i < ids.length; i++) {",
            "          var element = document.getElementById(ids[i]);",
            "          if (!element || !element.getBBox) { continue; }",
            "          try { addBox(element.getBBox(), state); } catch (e) { }",
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
            "        if (fitPage) {",
            "          var pageWidth = Math.max(320, frame.clientWidth - 32);",
            "          var pageHeight = Math.max(240, (window.innerHeight || document.documentElement.clientHeight || 600) - 48);",
            "          var pageScale = Math.min(pageWidth / Math.max(1, width), pageHeight / Math.max(1, height));",
            "          svg.style.width = Math.max(1, width * pageScale) + 'px';",
            "          svg.style.height = Math.max(1, height * pageScale) + 'px';",
            "        } else if (fitWidth) {",
            "          var availableWidth = Math.max(320, frame.clientWidth - 32);",
            "          var scale = availableWidth / Math.max(1, width);",
            "          svg.style.width = availableWidth + 'px';",
            "          svg.style.height = Math.max(1, height * scale) + 'px';",
            "        } else {",
            "          svg.style.width = Math.max(1, width * zoomScale) + 'px';",
            "          svg.style.height = Math.max(1, height * zoomScale) + 'px';",
            "        }",
            "        if (!initialScrollApplied) {",
            "          initialScrollApplied = true;",
            "          window.scrollTo(restoreScrollX, restoreScrollY);",
            "          window.setTimeout(function () { window.scrollTo(restoreScrollX, restoreScrollY); }, 0);",
            "        }",
            "      }",
            "      if (window.attachEvent) {",
            "        window.attachEvent('onload', applyPreviewFocus);",
            "        window.attachEvent('onresize', applyPreviewFocus);",
            "        window.attachEvent('onscroll', onPreviewScroll);",
            "      } else {",
            "        window.addEventListener('load', applyPreviewFocus, false);",
            "        window.addEventListener('resize', applyPreviewFocus, false);",
            "        window.addEventListener('scroll', onPreviewScroll, false);",
            "      }",
            "    }());",
            "  </script>"
        ]);
    }

    private static string BuildManualEditScript(bool editStations, bool editLabels, bool editSegments, bool editBends, IReadOnlyList<string> selectedStationIds)
    {
        if (!editStations && !editLabels && !editSegments && !editBends)
        {
            return string.Empty;
        }

        string editStationsText = editStations ? "true" : "false";
        string editLabelsText = editLabels ? "true" : "false";
        string editSegmentsText = editSegments ? "true" : "false";
        string editBendsText = editBends ? "true" : "false";
        string selectedIdsJson = "[" + string.Join(",", (selectedStationIds ?? []).Select(id => JsonSerializer.Serialize(id))) + "]";
        return string.Join(Environment.NewLine,
        [
            "  <script>",
            "    (function () {",
            $"      var editStations = {editStationsText};",
            $"      var editLabels = {editLabelsText};",
            $"      var editSegments = {editSegmentsText};",
            $"      var editBends = {editBendsText};",
            $"      var selectedStationIds = {selectedIdsJson};",
            "      var drag = null;",
            "      var nudge = null;",
            "      var nudgeTimer = null;",
            "      var routePointTolerance = 18.0;",
            "      var routeSegmentTolerance = 24.0;",
            "      var segmentEndpointStationTolerance = 54.0;",
            "      function hasClass(element, className) {",
            "        return (' ' + (element.getAttribute('class') || '') + ' ').indexOf(' ' + className + ' ') >= 0;",
            "      }",
            "      function parseCoordinate(value) {",
            "        var parsed = parseFloat(value);",
            "        return isNaN(parsed) ? 0 : parsed;",
            "      }",
            "      function svgPoint(svg, evt) {",
            "        if (!svg || !svg.createSVGPoint || !svg.getScreenCTM) { return null; }",
            "        var matrix = svg.getScreenCTM();",
            "        if (!matrix) { return null; }",
            "        var point = svg.createSVGPoint();",
            "        point.x = evt.clientX;",
            "        point.y = evt.clientY;",
            "        return point.matrixTransform(matrix.inverse());",
            "      }",
            "      function stationCircles(stationId) {",
            "        var circles = document.getElementsByTagName('circle');",
            "        var matches = [];",
            "        for (var i = 0; i < circles.length; i++) {",
            "          if (circles[i].getAttribute('data-station-id') === stationId) { matches.push(circles[i]); }",
            "        }",
            "        return matches;",
            "      }",
            "      function labelTexts(stationId) {",
            "        var texts = document.getElementsByTagName('text');",
            "        var matches = [];",
            "        for (var i = 0; i < texts.length; i++) {",
            "          if (texts[i].getAttribute('data-station-id') === stationId && hasClass(texts[i], 'station-label')) { matches.push(texts[i]); }",
            "        }",
            "        return matches;",
            "      }",
            "      function editableElements(kind, stationId) {",
            "        return kind === 'label' ? labelTexts(stationId) : stationCircles(stationId);",
            "      }",
            "      function attributeNames(kind) {",
            "        return kind === 'label' ? { x: 'x', y: 'y' } : { x: 'cx', y: 'cy' };",
            "      }",
            "      function routePolylines(svg) {",
            "        var polylines = svg ? svg.getElementsByTagName('polyline') : [];",
            "        var matches = [];",
            "        for (var i = 0; i < polylines.length; i++) {",
            "          var cls = ' ' + (polylines[i].getAttribute('class') || '') + ' ';",
            "          if (cls.indexOf(' route ') >= 0 || cls.indexOf(' express-decoration ') >= 0 || cls.indexOf(' schematic-v2-parallel-corridor ') >= 0 || cls.indexOf(' product-line ') >= 0) {",
            "            matches.push(polylines[i]);",
            "          }",
            "        }",
            "        return matches;",
            "      }",
            "      function routePointMatches(svg, x, y) {",
            "        var toleranceSquared = routePointTolerance * routePointTolerance;",
            "        var matches = [];",
            "        var polylines = routePolylines(svg);",
            "        for (var i = 0; i < polylines.length; i++) {",
            "          var points = polylines[i].points;",
            "          if (!points) { continue; }",
            "          for (var j = 0; j < points.numberOfItems; j++) {",
            "            var point = points.getItem(j);",
            "            var dx = point.x - x;",
            "            var dy = point.y - y;",
            "            if (dx * dx + dy * dy <= toleranceSquared) {",
            "              matches.push({ element: polylines[i], index: j, startX: point.x, startY: point.y });",
            "            }",
            "          }",
            "        }",
            "        return matches;",
            "      }",
            "      function stationPoints() {",
            "        var circles = document.getElementsByTagName('circle');",
            "        var stations = [];",
            "        for (var i = 0; i < circles.length; i++) {",
            "          var stationId = circles[i].getAttribute('data-station-id');",
            "          if (!stationId || !hasClass(circles[i], 'station')) { continue; }",
            "          stations.push({ stationId: stationId, x: parseCoordinate(circles[i].getAttribute('cx')), y: parseCoordinate(circles[i].getAttribute('cy')), element: circles[i] });",
            "        }",
            "        return stations;",
            "      }",
            "      function nearestStationToPoint(stations, point, maxDistance) {",
            "        var best = null;",
            "        var bestDistanceSquared = maxDistance * maxDistance;",
            "        for (var i = 0; i < stations.length; i++) {",
            "          var dx = stations[i].x - point.x;",
            "          var dy = stations[i].y - point.y;",
            "          var distanceSquared = dx * dx + dy * dy;",
            "          if (distanceSquared <= bestDistanceSquared) {",
            "            bestDistanceSquared = distanceSquared;",
            "            best = stations[i];",
            "          }",
            "        }",
            "        return best;",
            "      }",
            "      function uniqueStationIds(ids) {",
            "        var result = [];",
            "        for (var i = 0; i < ids.length; i++) {",
            "          if (!ids[i] || result.indexOf(ids[i]) >= 0) { continue; }",
            "          result.push(ids[i]);",
            "        }",
            "        return result;",
            "      }",
            "      function distanceToSegment(point, start, end) {",
            "        var vx = end.x - start.x;",
            "        var vy = end.y - start.y;",
            "        var lengthSquared = vx * vx + vy * vy;",
            "        if (lengthSquared <= 0.0001) { return { distance: Number.MAX_VALUE, x: start.x, y: start.y }; }",
            "        var t = ((point.x - start.x) * vx + (point.y - start.y) * vy) / lengthSquared;",
            "        if (t < 0) { t = 0; }",
            "        if (t > 1) { t = 1; }",
            "        var x = start.x + t * vx;",
            "        var y = start.y + t * vy;",
            "        var dx = point.x - x;",
            "        var dy = point.y - y;",
            "        return { distance: Math.sqrt(dx * dx + dy * dy), x: x, y: y };",
            "      }",
            "      function isRoutePolyline(element) {",
            "        if (!element || !element.tagName || element.tagName.toLowerCase() !== 'polyline') { return false; }",
            "        var cls = ' ' + (element.getAttribute('class') || '') + ' ';",
            "        return cls.indexOf(' route ') >= 0 || cls.indexOf(' schematic-v2-parallel-corridor ') >= 0 || cls.indexOf(' product-line ') >= 0;",
            "      }",
            "      function closestRouteSegment(svg, point, preferredElement) {",
            "        var best = null;",
            "        var candidates = preferredElement && isRoutePolyline(preferredElement) ? [preferredElement] : routePolylines(svg);",
            "        for (var i = 0; i < candidates.length; i++) {",
            "          var points = candidates[i].points;",
            "          if (!points || points.numberOfItems < 2) { continue; }",
            "          for (var j = 0; j + 1 < points.numberOfItems; j++) {",
            "            var first = points.getItem(j);",
            "            var second = points.getItem(j + 1);",
            "            var projection = distanceToSegment(point, first, second);",
            "            if (projection.distance > routeSegmentTolerance) { continue; }",
            "            if (!best || projection.distance < best.distance) {",
            "              best = { element: candidates[i], index: j, distance: projection.distance, start: { x: first.x, y: first.y }, end: { x: second.x, y: second.y } };",
            "            }",
            "          }",
            "        }",
            "        if (!best && preferredElement && isRoutePolyline(preferredElement)) {",
            "          return closestRouteSegment(svg, point, null);",
            "        }",
            "        return best;",
            "      }",
            "      function segmentEndpointStations(segment) {",
            "        var stations = stationPoints();",
            "        var first = nearestStationToPoint(stations, segment.start, segmentEndpointStationTolerance);",
            "        var second = nearestStationToPoint(stations, segment.end, segmentEndpointStationTolerance);",
            "        if (!first || !second || first.stationId === second.stationId) { return []; }",
            "        return uniqueStationIds([first.stationId, second.stationId]);",
            "      }",
            "      function bracketingStations(element, segmentIndex) {",
            "        var pts = element.points;",
            "        if (!pts) { return []; }",
            "        var stations = stationPoints();",
            "        var before = null, after = null;",
            "        for (var k = segmentIndex; k >= 0; k--) {",
            "          var pk = pts.getItem(k);",
            "          var s = nearestStationToPoint(stations, { x: pk.x, y: pk.y }, 8.0);",
            "          if (s) { before = s; break; }",
            "        }",
            "        for (var m = segmentIndex + 1; m < pts.numberOfItems; m++) {",
            "          var pm = pts.getItem(m);",
            "          var s2 = nearestStationToPoint(stations, { x: pm.x, y: pm.y }, 8.0);",
            "          if (s2) { after = s2; break; }",
            "        }",
            "        if (!before || !after || before.stationId === after.stationId) { return []; }",
            "        return [before.stationId, after.stationId];",
            "      }",
            "      function elementsForStationIds(stationIds, kind) {",
            "        var matches = [];",
            "        for (var i = 0; i < stationIds.length; i++) {",
            "          var elements = editableElements(kind, stationIds[i]);",
            "          for (var j = 0; j < elements.length; j++) { matches.push(elements[j]); }",
            "        }",
            "        return matches;",
            "      }",
            "      function routeSegmentPointMatches(segment) {",
            "        return [",
            "          { element: segment.element, index: segment.index, startX: segment.start.x, startY: segment.start.y },",
            "          { element: segment.element, index: segment.index + 1, startX: segment.end.x, startY: segment.end.y }",
            "        ];",
            "      }",
            "      function movePreview(matches, names, deltaX, deltaY) {",
            "        for (var i = 0; i < matches.length; i++) {",
            "          var element = matches[i];",
            "          var startX = parseFloat(element.getAttribute('data-drag-start-x'));",
            "          var startY = parseFloat(element.getAttribute('data-drag-start-y'));",
            "          if (isNaN(startX) || isNaN(startY)) { continue; }",
            "          element.setAttribute(names.x, String(startX + deltaX));",
            "          element.setAttribute(names.y, String(startY + deltaY));",
            "        }",
            "      }",
            "      function moveRoutePointPreview(matches, deltaX, deltaY) {",
            "        for (var i = 0; i < matches.length; i++) {",
            "          var points = matches[i].element.points;",
            "          if (!points || matches[i].index >= points.numberOfItems) { continue; }",
            "          var point = points.getItem(matches[i].index);",
            "          point.x = matches[i].startX + deltaX;",
            "          point.y = matches[i].startY + deltaY;",
            "        }",
            "      }",
            "      function moveDragPreview(currentDrag) {",
            "        movePreview(currentDrag.matches, currentDrag.names, currentDrag.deltaX, currentDrag.deltaY);",
            "        if (currentDrag.labelMatches && currentDrag.labelMatches.length) {",
            "          movePreview(currentDrag.labelMatches, { x: 'x', y: 'y' }, currentDrag.deltaX, currentDrag.deltaY);",
            "        }",
            "        if (currentDrag.routeMatches && currentDrag.routeMatches.length) {",
            "          moveRoutePointPreview(currentDrag.routeMatches, currentDrag.deltaX, currentDrag.deltaY);",
            "        }",
            "      }",
            "      function requestPreviewUpdate() {",
            "        if (!drag || drag.previewPending) { return; }",
            "        drag.previewPending = true;",
            "        var apply = function() {",
            "          if (drag) {",
            "            moveDragPreview(drag);",
            "            drag.lastPreviewAt = (new Date()).getTime();",
            "            drag.previewPending = false;",
            "          }",
            "        };",
            "        if (window.requestAnimationFrame) { window.requestAnimationFrame(apply); } else { window.setTimeout(apply, 16); }",
            "      }",
            "      function setDragStart(matches, names) {",
            "        for (var i = 0; i < matches.length; i++) {",
            "          matches[i].setAttribute('data-drag-start-x', matches[i].getAttribute(names.x));",
            "          matches[i].setAttribute('data-drag-start-y', matches[i].getAttribute(names.y));",
            "        }",
            "      }",
            "      function clearDragStart(matches) {",
            "        for (var i = 0; i < matches.length; i++) {",
            "          matches[i].removeAttribute('data-drag-start-x');",
            "          matches[i].removeAttribute('data-drag-start-y');",
            "        }",
            "      }",
            "      function svgDeltaFromClient(currentDrag, evt) {",
            "        var dx = evt.clientX - currentDrag.startClientX;",
            "        var dy = evt.clientY - currentDrag.startClientY;",
            "        return { x: dx * currentDrag.matrixA + dy * currentDrag.matrixC, y: dx * currentDrag.matrixB + dy * currentDrag.matrixD };",
            "      }",
            "      window.onerror = function() { return true; };",
            "      function postViewerMessage(message) {",
            "        try {",
            "          if (window.chrome && window.chrome.webview && window.chrome.webview.postMessage) {",
            "            window.chrome.webview.postMessage(message);",
            "          }",
            "        } catch (ex) { }",
            "      }",
            "      function externalSelect(kind, stationId) {",
            "        if (!stationId) { return; }",
            "        postViewerMessage({ type: kind === 'label' ? 'labelSelected' : 'stationSelected', stationId: String(stationId) });",
            "      }",
            "      function externalDrag(kind, stationId, deltaX, deltaY) {",
            "        if (!stationId) { return; }",
            "        var dx = String(Math.round(deltaX * 1000) / 1000);",
            "        var dy = String(Math.round(deltaY * 1000) / 1000);",
            "        postViewerMessage({ type: kind === 'label' ? 'labelDragged' : 'stationDragged', stationId: String(stationId), deltaX: dx, deltaY: dy });",
            "      }",
            "      function externalSegmentSelect(stationIds) {",
            "        if (!stationIds || !stationIds.length) { return; }",
            "        postViewerMessage({ type: 'segmentSelected', stationId: String(stationIds[0]), stationIds: stationIds.join('|') });",
            "      }",
            "      function externalSegmentDrag(stationIds, deltaX, deltaY) {",
            "        if (!stationIds || !stationIds.length) { return; }",
            "        var dx = String(Math.round(deltaX * 1000) / 1000);",
            "        var dy = String(Math.round(deltaY * 1000) / 1000);",
            "        postViewerMessage({ type: 'segmentDragged', stationId: String(stationIds[0]), stationIds: stationIds.join('|'), deltaX: dx, deltaY: dy });",
            "      }",
            "      function externalBend(stationIds, x, y) {",
            "        if (!stationIds || stationIds.length < 2) { return; }",
            "        postViewerMessage({ type: 'bendChanged', stationId: String(stationIds[0]), stationIds: stationIds.join('|'), bendX: String(Math.round(x * 1000) / 1000), bendY: String(Math.round(y * 1000) / 1000) });",
            "      }",
            "      function selectTarget(kind, stationId) {",
            "        externalSelect(kind, stationId);",
            "      }",
            "      function applySelectionHighlight() {",
            "        var circles = document.getElementsByTagName('circle');",
            "        for (var i = 0; i < circles.length; i++) {",
            "          var id = circles[i].getAttribute('data-station-id');",
            "          if (!id || !hasClass(circles[i], 'station')) { continue; }",
            "          if (selectedStationIds.indexOf(id) >= 0) {",
            "            var r = parseFloat(circles[i].getAttribute('r')); if (isNaN(r)) { r = 6; }",
            "            circles[i].style.stroke = '#ff2d95';",
            "            circles[i].style.strokeWidth = String(Math.max(2.5, r * 0.85));",
            "          } else {",
            "            circles[i].style.stroke = '';",
            "            circles[i].style.strokeWidth = '';",
            "          }",
            "        }",
            "      }",
            "      function broadcastSelection() {",
            "        if (selectedStationIds.length > 1) { externalSegmentSelect(selectedStationIds); }",
            "        else if (selectedStationIds.length === 1) { externalSelect('station', selectedStationIds[0]); }",
            "        else { postViewerMessage({ type: 'selectionCleared' }); }",
            "      }",
            "      function setSelection(ids) {",
            "        selectedStationIds = uniqueStationIds(ids || []);",
            "        applySelectionHighlight();",
            "        broadcastSelection();",
            "      }",
            "      function toggleSelection(id) {",
            "        if (!id) { return; }",
            "        var idx = selectedStationIds.indexOf(id);",
            "        var next = selectedStationIds.slice();",
            "        if (idx >= 0) { next.splice(idx, 1); } else { next.push(id); }",
            "        setSelection(next);",
            "      }",
            "      function buildNudgeTargets() {",
            "        var ids = selectedStationIds.slice();",
            "        if (!ids.length) { return null; }",
            "        var svg = document.getElementsByTagName('svg')[0];",
            "        if (!svg) { return null; }",
            "        var matches = elementsForStationIds(ids, 'station');",
            "        if (!matches.length) { return null; }",
            "        var labelMatches = elementsForStationIds(ids, 'label');",
            "        var routeMatches = [];",
            "        for (var i = 0; i < ids.length; i++) {",
            "          var c = stationCircles(ids[i])[0];",
            "          if (!c) { continue; }",
            "          var rm = routePointMatches(svg, parseCoordinate(c.getAttribute('cx')), parseCoordinate(c.getAttribute('cy')));",
            "          for (var j = 0; j < rm.length; j++) { routeMatches.push(rm[j]); }",
            "        }",
            "        setDragStart(matches, { x: 'cx', y: 'cy' });",
            "        setDragStart(labelMatches, { x: 'x', y: 'y' });",
            "        return { ids: ids, matches: matches, names: { x: 'cx', y: 'cy' }, labelMatches: labelMatches, routeMatches: routeMatches, deltaX: 0, deltaY: 0 };",
            "      }",
            "      function commitNudge() {",
            "        if (nudgeTimer) { window.clearTimeout(nudgeTimer); nudgeTimer = null; }",
            "        if (!nudge) { return; }",
            "        var pending = nudge; nudge = null;",
            "        clearDragStart(pending.matches);",
            "        if (pending.labelMatches && pending.labelMatches.length) { clearDragStart(pending.labelMatches); }",
            "        if (Math.abs(pending.deltaX) < 0.01 && Math.abs(pending.deltaY) < 0.01) { return; }",
            "        if (pending.ids.length > 1) { externalSegmentDrag(pending.ids, pending.deltaX, pending.deltaY); }",
            "        else { externalDrag('station', pending.ids[0], pending.deltaX, pending.deltaY); }",
            "      }",
            "      function applyNudge(dx, dy) {",
            "        if (!nudge) { nudge = buildNudgeTargets(); if (!nudge) { return; } }",
            "        nudge.deltaX += dx; nudge.deltaY += dy;",
            "        moveDragPreview(nudge);",
            "        if (nudgeTimer) { window.clearTimeout(nudgeTimer); }",
            "        nudgeTimer = window.setTimeout(commitNudge, 350);",
            "      }",
            "      function onNudgeKeyDown(evt) {",
            "        evt = evt || window.event;",
            "        if (!editStations || !selectedStationIds || !selectedStationIds.length || drag) { return; }",
            "        var key = evt.key;",
            "        var step = evt.shiftKey ? 24 : 4;",
            "        var dx = 0, dy = 0;",
            "        if (key === 'ArrowLeft' || key === 'Left') { dx = -step; }",
            "        else if (key === 'ArrowRight' || key === 'Right') { dx = step; }",
            "        else if (key === 'ArrowUp' || key === 'Up') { dy = -step; }",
            "        else if (key === 'ArrowDown' || key === 'Down') { dy = step; }",
            "        else { return; }",
            "        applyNudge(dx, dy);",
            "        if (evt.preventDefault) { evt.preventDefault(); } else { evt.returnValue = false; }",
            "      }",
            "      function beginDrag(evt) {",
            "        evt = evt || window.event;",
            "        if (nudge) { commitNudge(); return; }",
            "        var target = evt.target || evt.srcElement;",
            "        if (!target) { return; }",
            "        var tagName = target.tagName ? target.tagName.toLowerCase() : '';",
            "        var kind = null;",
            "        if (editStations && tagName === 'circle' && hasClass(target, 'station')) { kind = 'station'; }",
            "        if (editLabels && tagName === 'text' && hasClass(target, 'station-label')) { kind = 'label'; }",
            "        if (editSegments && tagName === 'polyline' && isRoutePolyline(target)) { kind = 'segment'; }",
            "        if (editBends && tagName === 'polyline' && isRoutePolyline(target)) { kind = 'bend'; }",
            "        if (!kind) { return; }",
            "        var svg = document.getElementsByTagName('svg')[0];",
            "        var start = svgPoint(svg, evt);",
            "        if (!start) { return; }",
            "        var matrix = svg.getScreenCTM().inverse();",
            "        if (kind === 'bend') {",
            "          var bendSegment = closestRouteSegment(svg, start, target);",
            "          if (!bendSegment) { return; }",
            "          var bendStationIds = bracketingStations(bendSegment.element, bendSegment.index);",
            "          if (bendStationIds.length < 2) { bendStationIds = segmentEndpointStations(bendSegment); }",
            "          if (bendStationIds.length < 2) { return; }",
            "          var bendPt = svg.createSVGPoint(); bendPt.x = start.x; bendPt.y = start.y;",
            "          var bendInsertIndex = bendSegment.index + 1;",
            "          try { bendSegment.element.points.insertItemBefore(bendPt, bendInsertIndex); } catch (bendEx) { return; }",
            "          drag = { kind: 'bend', stationIds: bendStationIds, bendElement: bendSegment.element, bendIndex: bendInsertIndex, svg: svg, curX: start.x, curY: start.y };",
            "          if (evt.preventDefault) { evt.preventDefault(); } else { evt.returnValue = false; }",
            "          return;",
            "        }",
            "        if (kind === 'segment') {",
            "          var segment = closestRouteSegment(svg, start, target);",
            "          if (!segment) { return; }",
            "          var segmentStationIds = segmentEndpointStations(segment);",
            "          if (segmentStationIds.length < 2) { return; }",
            "          var segmentStationMatches = elementsForStationIds(segmentStationIds, 'station');",
            "          var segmentLabelMatches = elementsForStationIds(segmentStationIds, 'label');",
            "          var segmentRouteMatches = routeSegmentPointMatches(segment);",
            "          if (!segmentStationMatches.length) { return; }",
            "          externalSegmentSelect(segmentStationIds);",
            "          setDragStart(segmentStationMatches, { x: 'cx', y: 'cy' });",
            "          setDragStart(segmentLabelMatches, { x: 'x', y: 'y' });",
            "          drag = { kind: kind, stationId: segmentStationIds[0], stationIds: segmentStationIds, svg: svg, matches: segmentStationMatches, names: { x: 'cx', y: 'cy' }, labelMatches: segmentLabelMatches, routeMatches: segmentRouteMatches, startX: start.x, startY: start.y, startClientX: evt.clientX, startClientY: evt.clientY, matrixA: matrix.a, matrixB: matrix.b, matrixC: matrix.c, matrixD: matrix.d, deltaX: 0, deltaY: 0, lastPreviewAt: 0, previewPending: false };",
            "          if (evt.preventDefault) { evt.preventDefault(); } else { evt.returnValue = false; }",
            "          return;",
            "        }",
            "        var stationId = target.getAttribute('data-station-id');",
            "        if (!stationId) { return; }",
            "        if (kind === 'station' && (evt.ctrlKey || evt.metaKey)) {",
            "          toggleSelection(stationId);",
            "          if (evt.preventDefault) { evt.preventDefault(); } else { evt.returnValue = false; }",
            "          return;",
            "        }",
            "        if (kind === 'station' && selectedStationIds.length > 1 && selectedStationIds.indexOf(stationId) >= 0) {",
            "          var groupIds = selectedStationIds.slice();",
            "          var groupStationMatches = elementsForStationIds(groupIds, 'station');",
            "          if (!groupStationMatches.length) { return; }",
            "          var groupLabelMatches = elementsForStationIds(groupIds, 'label');",
            "          var groupRouteMatches = [];",
            "          for (var gi = 0; gi < groupIds.length; gi++) {",
            "            var gc = stationCircles(groupIds[gi])[0];",
            "            if (!gc) { continue; }",
            "            var gm = routePointMatches(svg, parseCoordinate(gc.getAttribute('cx')), parseCoordinate(gc.getAttribute('cy')));",
            "            for (var gj = 0; gj < gm.length; gj++) { groupRouteMatches.push(gm[gj]); }",
            "          }",
            "          externalSegmentSelect(groupIds);",
            "          setDragStart(groupStationMatches, { x: 'cx', y: 'cy' });",
            "          setDragStart(groupLabelMatches, { x: 'x', y: 'y' });",
            "          drag = { kind: 'segment', stationId: groupIds[0], stationIds: groupIds, svg: svg, matches: groupStationMatches, names: { x: 'cx', y: 'cy' }, labelMatches: groupLabelMatches, routeMatches: groupRouteMatches, startX: start.x, startY: start.y, startClientX: evt.clientX, startClientY: evt.clientY, matrixA: matrix.a, matrixB: matrix.b, matrixC: matrix.c, matrixD: matrix.d, deltaX: 0, deltaY: 0, lastPreviewAt: 0, previewPending: false };",
            "          if (evt.preventDefault) { evt.preventDefault(); } else { evt.returnValue = false; }",
            "          return;",
            "        }",
            "        var matches = editableElements(kind, stationId);",
            "        if (!matches.length) { return; }",
            "        var names = attributeNames(kind);",
            "        var labelMatches = [];",
            "        var routeMatches = [];",
            "        if (kind === 'station') {",
            "          labelMatches = labelTexts(stationId);",
            "          setDragStart(labelMatches, { x: 'x', y: 'y' });",
            "          routeMatches = routePointMatches(svg, parseCoordinate(target.getAttribute('cx')), parseCoordinate(target.getAttribute('cy')));",
            "        }",
            "        if (kind === 'station') { setSelection([stationId]); } else { selectTarget(kind, stationId); }",
            "        setDragStart(matches, names);",
            "        drag = { kind: kind, stationId: stationId, svg: svg, matches: matches, names: names, labelMatches: labelMatches, routeMatches: routeMatches, startX: start.x, startY: start.y, startClientX: evt.clientX, startClientY: evt.clientY, matrixA: matrix.a, matrixB: matrix.b, matrixC: matrix.c, matrixD: matrix.d, deltaX: 0, deltaY: 0, lastPreviewAt: 0, previewPending: false };",
            "        if (evt.preventDefault) { evt.preventDefault(); } else { evt.returnValue = false; }",
            "      }",
            "      function updateDrag(evt) {",
            "        if (!drag) { return; }",
            "        evt = evt || window.event;",
            "        if (drag.kind === 'bend') {",
            "          var bp = svgPoint(drag.svg, evt);",
            "          if (bp) { drag.curX = bp.x; drag.curY = bp.y; var bpts = drag.bendElement.points; if (drag.bendIndex < bpts.numberOfItems) { var bit = bpts.getItem(drag.bendIndex); bit.x = bp.x; bit.y = bp.y; } }",
            "          if (evt.preventDefault) { evt.preventDefault(); } else { evt.returnValue = false; }",
            "          return;",
            "        }",
            "        var delta = svgDeltaFromClient(drag, evt);",
            "        drag.deltaX = delta.x;",
            "        drag.deltaY = delta.y;",
            "        var now = (new Date()).getTime();",
            "        if (!drag.lastPreviewAt || now - drag.lastPreviewAt >= 16) { requestPreviewUpdate(); }",
            "        if (evt.preventDefault) { evt.preventDefault(); } else { evt.returnValue = false; }",
            "      }",
            "      function neighborPointsForStation(routeMatches) {",
            "        var out = [];",
            "        if (!routeMatches) { return out; }",
            "        for (var i = 0; i < routeMatches.length; i++) {",
            "          var pts = routeMatches[i].element.points;",
            "          var idx = routeMatches[i].index;",
            "          if (!pts) { continue; }",
            "          if (idx - 1 >= 0) { var a = pts.getItem(idx - 1); out.push({ x: a.x, y: a.y }); }",
            "          if (idx + 1 < pts.numberOfItems) { var b = pts.getItem(idx + 1); out.push({ x: b.x, y: b.y }); }",
            "        }",
            "        return out;",
            "      }",
            "      function snapOctilinear(pos, neighbors) {",
            "        if (!neighbors || !neighbors.length) { return pos; }",
            "        var best = null, bestD = Infinity;",
            "        for (var i = 0; i < neighbors.length; i++) {",
            "          var ndx = neighbors[i].x - pos.x, ndy = neighbors[i].y - pos.y; var d = ndx * ndx + ndy * ndy;",
            "          if (d < bestD) { bestD = d; best = neighbors[i]; }",
            "        }",
            "        if (!best) { return pos; }",
            "        var dx = pos.x - best.x, dy = pos.y - best.y; var dist = Math.sqrt(dx * dx + dy * dy);",
            "        if (dist < 1) { return pos; }",
            "        var step = Math.PI / 4; var k = Math.round(Math.atan2(dy, dx) / step);",
            "        var dev = Math.abs(Math.atan2(dy, dx) - k * step); if (dev > Math.PI) { dev = 2 * Math.PI - dev; }",
            "        if (dev > 0.28) { return pos; }",
            "        var a = ((k % 8) + 8) % 8; var nx = pos.x, ny = pos.y;",
            "        if (a === 0 || a === 4) { ny = best.y; }",
            "        else if (a === 2 || a === 6) { nx = best.x; }",
            "        else { var m = (Math.abs(dx) + Math.abs(dy)) / 2; nx = best.x + (dx < 0 ? -m : m); ny = best.y + (dy < 0 ? -m : m); }",
            "        return { x: nx, y: ny };",
            "      }",
            "      function octilinearSnapDelta(completed) {",
            "        if (!completed.matches || !completed.matches.length) { return null; }",
            "        var station = completed.matches[0];",
            "        var sx = parseFloat(station.getAttribute('data-drag-start-x'));",
            "        var sy = parseFloat(station.getAttribute('data-drag-start-y'));",
            "        if (isNaN(sx) || isNaN(sy)) { return null; }",
            "        var newX = sx + completed.deltaX, newY = sy + completed.deltaY;",
            "        var snapped = snapOctilinear({ x: newX, y: newY }, neighborPointsForStation(completed.routeMatches));",
            "        if (Math.abs(snapped.x - newX) < 0.01 && Math.abs(snapped.y - newY) < 0.01) { return null; }",
            "        return { x: completed.deltaX + (snapped.x - newX), y: completed.deltaY + (snapped.y - newY) };",
            "      }",
            "      function endDrag(evt) {",
            "        if (!drag) { return; }",
            "        var completed = drag;",
            "        drag = null;",
            "        if (completed.kind === 'bend') {",
            "          externalBend(completed.stationIds, completed.curX, completed.curY);",
            "          return;",
            "        }",
            "        moveDragPreview(completed);",
            "        if (completed.kind === 'station') {",
            "          var snappedDelta = octilinearSnapDelta(completed);",
            "          if (snappedDelta) { completed.deltaX = snappedDelta.x; completed.deltaY = snappedDelta.y; moveDragPreview(completed); }",
            "        }",
            "        clearDragStart(completed.matches);",
            "        if (completed.labelMatches && completed.labelMatches.length) { clearDragStart(completed.labelMatches); }",
            "        if (Math.sqrt(completed.deltaX * completed.deltaX + completed.deltaY * completed.deltaY) < 0.5) { return; }",
            "        if (completed.kind === 'segment') {",
            "          externalSegmentDrag(completed.stationIds, completed.deltaX, completed.deltaY);",
            "        } else {",
            "          externalDrag(completed.kind, completed.stationId, completed.deltaX, completed.deltaY);",
            "        }",
            "      }",
            "      if (document.attachEvent) {",
            "        document.attachEvent('onmousedown', beginDrag);",
            "        document.attachEvent('onmousemove', updateDrag);",
            "        document.attachEvent('onmouseup', endDrag);",
            "      } else {",
            "        document.addEventListener('mousedown', beginDrag, false);",
            "        document.addEventListener('mousemove', updateDrag, false);",
            "        document.addEventListener('mouseup', endDrag, false);",
            "      }",
            "      if (document.addEventListener) { document.addEventListener('keydown', onNudgeKeyDown, false); }",
            "      else if (document.attachEvent) { document.attachEvent('onkeydown', onNudgeKeyDown); }",
            "      if (window.addEventListener) { window.addEventListener('load', applySelectionHighlight, false); }",
            "      else if (window.attachEvent) { window.attachEvent('onload', applySelectionHighlight); }",
            "      if (document.readyState === 'complete') { applySelectionHighlight(); }",
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

    private static string? ReadSvgStringAttribute(string svgTag, string attributeName)
    {
        Match match = Regex.Match(
            svgTag,
            $@"\b{Regex.Escape(attributeName)}\s*=\s*[""'](?<value>[^""']*)[""']",
            RegexOptions.IgnoreCase);
        return match.Success
            ? System.Net.WebUtility.HtmlDecode(match.Groups["value"].Value)
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
            SvgLayoutMode.SchematicV2 => "schematic-v2",
            SvgLayoutMode.SchematicMap => "schematic-map",
            SvgLayoutMode.SchematicAnneal => "schematic-anneal",
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
        string[] candidates =
        [
            @"D:\CS2MetroDiagram\metro-export.json",
            Path.Combine(ExportDirectoryResolver.GetDocumentsExportDirectory(), "metro-export.json"),
            Path.Combine(ExportDirectoryResolver.GetDesktopExportDirectory(), "metro-export.json")
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

        string documentsExportFolder = ExportDirectoryResolver.GetDocumentsExportDirectory();
        if (Directory.Exists(documentsExportFolder))
        {
            return documentsExportFolder;
        }

        string desktopExportFolder = ExportDirectoryResolver.GetDesktopExportDirectory();
        return Directory.Exists(desktopExportFolder) ? desktopExportFolder : null;
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
        candidates.Add(ExportDirectoryResolver.GetDocumentsExportDirectory());
        candidates.Add(ExportDirectoryResolver.GetDesktopExportDirectory());

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
            SelectComboBoxItem(SaveFormatComboBox, NormalizeSaveFormat(settings.SaveFormat));
        }
        finally
        {
            _suppressUiEvents = false;
        }
    }

    private static string NormalizeSaveFormat(string? format)
    {
        return format is "png" or "pdf" ? format : "svg";
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
        SaveFormatComboBox.ToolTip = T("SaveFormatToolTip");
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
        PreviewZoomFitPageItem.Content = T("PreviewZoomFitPage");
        PreviewZoomFitWidthItem.Content = T("PreviewZoomFitWidth");
        ManualEditCheckBox.Content = T("ManualEdit");
        ManualEditStationsItem.Content = T("ManualEditStations");
        ManualEditLabelsItem.Content = T("ManualEditLabels");
        ManualEditSegmentsItem.Content = T("ManualEditSegments");
        ManualEditBendsItem.Content = T("ManualEditBends");
        AlignHorizontalButton.Content = T("AlignHorizontal");
        AlignVerticalButton.Content = T("AlignVertical");
        ResetSelectedOverrideButton.Content = T("ResetSelectedOverride");
        ToggleSelectedLabelButton.Content = T("HideSelectedLabel");
        LabelSideButton.Content = T("LabelSide");
        ClearOverridesButton.Content = T("ClearOverrides");
        OpenOverridesButton.Content = T("OpenOverrides");
        UpdateManualEditButtons();
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
        AdvancedSettingsExpander.Header = T("AdvancedSettings");
        ManualEditingHeader.Text = T("ManualEditing");
        UndoButton.Content = T("Undo");
        RedoButton.Content = T("Redo");
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
            SaveFormat = ReadSelectedSaveFormat(),
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
            "schematic-anneal" => "schematic-anneal",
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
            "fit-page" => "fit-page",
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
        _statusStamp++;
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
        if (PreviewBrowser.CoreWebView2 is not null)
        {
            PreviewBrowser.CoreWebView2.WebMessageReceived -= PreviewBrowser_WebMessageReceived;
        }

        PreviewBrowser.Dispose();
        base.OnClosed(e);
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
