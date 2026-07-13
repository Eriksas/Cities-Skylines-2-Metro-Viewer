using Colossal.UI.Binding;
using Game;
using Game.SceneFlow;
using Game.UI;
using MetroDiagram.Core.Exporting;
using MetroDiagram.Engine;
using System;
using System.Globalization;
using System.Text;
using UnityEngine.Scripting;

namespace CS2_Metro
{
    /// <summary>
    /// Owns the in-game preview lifecycle. UI callbacks only queue work; snapshot
    /// capture and all ECS access run from OnUpdate on the game thread.
    /// </summary>
    public sealed partial class InGamePreviewUISystem : UISystemBase
    {
        internal const string BindingGroup = "CS2MetroPreview";
        private const string LogPrefix = "[CS2MetroPreview]";
        private const int PreviewCanvasWidth = 1800;
        private const int PreviewCanvasHeight = 1100;

        private ValueBinding<bool> m_PanelOpenBinding;
        private ValueBinding<string> m_StateJsonBinding;
        private ValueBinding<string> m_SvgBinding;

        private MetroNetworkSnapshotCapture m_Capture;
        private string m_CurrentSvg = string.Empty;
        private string m_Status = "idle";
        private string m_Notice = "open-panel";
        private string m_Error = string.Empty;
        private string m_LastPath = string.Empty;
        private string m_UpdatedAt = string.Empty;
        private PortableLayoutMode m_LayoutMode = PortableLayoutMode.Geographic;
        private bool m_ShowGenericStationNames;
        private bool m_HideCrowdedLabels = true;
        private bool m_PanelOpen;
        private bool m_SettingsInitialized;
        private string m_LastInterfaceLanguage = string.Empty;
        private bool m_RefreshRequested;
        private bool m_RenderRequested;
        private bool m_ExportJsonRequested;
        private bool m_SaveSvgRequested;
        private int m_RefreshCount;
        private int m_OpenCount;
        private int m_CoalescedRequestCount;
        private long m_LastCaptureMilliseconds;
        private long m_LastRenderMilliseconds;
        private long m_LastRendererMilliseconds;
        private bool m_LastRenderWasCacheHit;
        private int m_RenderCacheEntries;

        public override GameMode gameMode => GameMode.All;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            AddBinding(m_PanelOpenBinding = new ValueBinding<bool>(BindingGroup, "panelOpen", false));
            AddBinding(m_StateJsonBinding = new ValueBinding<string>(BindingGroup, "stateJson", BuildStateJson()));
            AddBinding(m_SvgBinding = new ValueBinding<string>(BindingGroup, "svg", string.Empty));
            AddBinding(new TriggerBinding<bool>(BindingGroup, "setPanelOpen", SetPanelOpen));
            AddBinding(new TriggerBinding(BindingGroup, "refresh", RequestRefresh));
            AddBinding(new TriggerBinding<string>(BindingGroup, "setLayout", SetLayout));
            AddBinding(new TriggerBinding<bool>(BindingGroup, "setShowGenericStationNames", SetShowGenericStationNames));
            AddBinding(new TriggerBinding<bool>(BindingGroup, "setHideCrowdedLabels", SetHideCrowdedLabels));
            AddBinding(new TriggerBinding(BindingGroup, "exportJson", RequestExportJson));
            AddBinding(new TriggerBinding(BindingGroup, "saveSvg", RequestSaveSvg));

            Mod.log.Info(LogPrefix + "[Lifecycle] Preview controller bindings created (Phase 7G).");
        }

        [Preserve]
        protected override void OnUpdate()
        {
            base.OnUpdate();
            EnsureSettingsInitialized();
            PublishInterfaceLanguageChangeIfNeeded();

            if (m_ExportJsonRequested)
            {
                m_ExportJsonRequested = false;
                ProcessExportJson();
                return;
            }

            if (m_RefreshRequested)
            {
                m_RefreshRequested = false;
                ProcessRefresh();
                return;
            }

            if (m_RenderRequested)
            {
                m_RenderRequested = false;
                ProcessRender();
                return;
            }

            if (m_SaveSvgRequested)
            {
                m_SaveSvgRequested = false;
                ProcessSaveSvg();
            }
        }

        [Preserve]
        protected override void OnDestroy()
        {
            InGamePreviewRenderService.Clear();
            MetroNetworkSnapshotService.Clear();
            Mod.log.Info(string.Format(
                CultureInfo.InvariantCulture,
                LogPrefix + "[Lifecycle] Preview controller disposed; panelOpen={0}, openCount={1}, refreshCount={2}, coalescedRequests={3}.",
                m_PanelOpen,
                m_OpenCount,
                m_RefreshCount,
                m_CoalescedRequestCount));
            base.OnDestroy();
        }

        private void EnsureSettingsInitialized()
        {
            if (m_SettingsInitialized || Mod.Settings == null)
            {
                return;
            }

            bool applyGeographicDefault = !Mod.Settings.InGamePreviewGeographicDefaultApplied;
            m_LayoutMode = applyGeographicDefault
                || string.Equals(
                    Mod.Settings.InGamePreviewLayout,
                    Setting.PreviewLayoutGeographic,
                    StringComparison.Ordinal)
                ? PortableLayoutMode.Geographic
                : PortableLayoutMode.SchematicAnneal;
            m_ShowGenericStationNames = Mod.Settings.InGamePreviewShowGenericStationNames;
            m_HideCrowdedLabels = Mod.Settings.InGamePreviewHideCrowdedLabels;
            m_LastInterfaceLanguage = Mod.Settings.InterfaceLanguage ?? Setting.LanguageAuto;
            m_SettingsInitialized = true;

            if (applyGeographicDefault)
            {
                Mod.Settings.InGamePreviewLayout = Setting.PreviewLayoutGeographic;
                Mod.Settings.InGamePreviewGeographicDefaultApplied = true;
                try
                {
                    Mod.Settings.ApplyAndSave();
                    Mod.log.Info(LogPrefix + "[Settings] Applied the geographic in-game preview default migration.");
                }
                catch (Exception ex)
                {
                    Mod.log.Warn(LogPrefix + "[Settings] Could not persist the geographic preview default migration: " + ex.Message);
                }
            }

            PublishState();
        }

        private void PublishInterfaceLanguageChangeIfNeeded()
        {
            if (!m_SettingsInitialized || Mod.Settings == null)
            {
                return;
            }

            string interfaceLanguage = Mod.Settings.InterfaceLanguage ?? Setting.LanguageAuto;
            if (string.Equals(m_LastInterfaceLanguage, interfaceLanguage, StringComparison.Ordinal))
            {
                return;
            }

            m_LastInterfaceLanguage = interfaceLanguage;
            PublishState();
        }

        private void SetPanelOpen(bool open)
        {
            if (m_PanelOpen != open)
            {
                m_PanelOpen = open;
                m_PanelOpenBinding.Update(open);
                if (open)
                {
                    m_OpenCount++;
                }

                Mod.log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    LogPrefix + "[Lifecycle] Preview panel {0}; openCount={1}.",
                    open ? "opened" : "closed",
                    m_OpenCount));
            }

            if (!open)
            {
                DiscardRequest(ref m_RefreshRequested);
                DiscardRequest(ref m_RenderRequested);
                DiscardRequest(ref m_SaveSvgRequested);
                return;
            }

            if (m_Capture == null || string.IsNullOrWhiteSpace(m_CurrentSvg))
            {
                RequestRefresh();
            }
            else
            {
                SetState("ready", "cached-preview", string.Empty);
            }
        }

        private void RequestRefresh()
        {
            QueueRequest(ref m_RefreshRequested);
            SetState("loading", "capturing-city", string.Empty);
        }

        private void SetLayout(string layout)
        {
            PortableLayoutMode requested = string.Equals(layout, Setting.PreviewLayoutGeographic, StringComparison.OrdinalIgnoreCase)
                ? PortableLayoutMode.Geographic
                : PortableLayoutMode.SchematicAnneal;
            if (m_LayoutMode == requested)
            {
                return;
            }

            m_LayoutMode = requested;
            PersistPreviewSettings();
            QueueRenderForSettingChange();
        }

        private void SetShowGenericStationNames(bool show)
        {
            if (m_ShowGenericStationNames == show)
            {
                return;
            }

            m_ShowGenericStationNames = show;
            PersistPreviewSettings();
            QueueRenderForSettingChange();
        }

        private void SetHideCrowdedLabels(bool hide)
        {
            if (m_HideCrowdedLabels == hide)
            {
                return;
            }

            m_HideCrowdedLabels = hide;
            PersistPreviewSettings();
            QueueRenderForSettingChange();
        }

        private void QueueRenderForSettingChange()
        {
            if (m_Capture == null)
            {
                if (m_PanelOpen)
                {
                    RequestRefresh();
                }
                else
                {
                    PublishState();
                }

                return;
            }

            QueueRequest(ref m_RenderRequested);
            SetState("rendering", "applying-options", string.Empty);
        }

        private void RequestExportJson()
        {
            QueueRequest(ref m_ExportJsonRequested);
            SetState("exporting", "exporting-json", string.Empty);
        }

        private void RequestSaveSvg()
        {
            QueueRequest(ref m_SaveSvgRequested);
            SetState("saving", "saving-svg", string.Empty);
        }

        private void ProcessRefresh()
        {
            // Refresh captures and renders the latest options, so an older queued option render is redundant.
            DiscardRequest(ref m_RenderRequested);

            if (!IsPlayableCityAvailable())
            {
                ClearPreview("no-city", "load-city", string.Empty);
                return;
            }

            try
            {
                Mod.log.Info(LogPrefix + "[Capture] City snapshot capture started.");
                m_Capture = MetroNetworkSnapshotService.Capture(
                    Mod.UpdateSystem,
                    RealMetroJsonExporter.GetJsonPath(),
                    RealMetroJsonExporter.GetDiagnosticsPath());
                m_RefreshCount++;
                m_LastCaptureMilliseconds = m_Capture.CaptureMilliseconds;
                Mod.log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    LogPrefix + "[Capture] City snapshot capture completed; revision={0}, lines={1}, stations={2}, captureMs={3}.",
                    m_Capture.Snapshot.Revision,
                    m_Capture.Snapshot.Lines.Count,
                    m_Capture.Snapshot.Stations.Count,
                    m_LastCaptureMilliseconds));

                if (m_Capture.Snapshot.Lines.Count == 0)
                {
                    ClearPreview("no-metro", "no-metro-lines", string.Empty);
                    return;
                }

                RenderCurrent("preview-refreshed");
            }
            catch (Exception ex)
            {
                Mod.log.Error(LogPrefix + "[Capture] In-game metro preview capture failed: " + ex);
                ClearPreview("error", "capture-failed", ex.Message);
            }
        }

        private void ProcessRender()
        {
            if (m_Capture == null)
            {
                RequestRefresh();
                return;
            }

            try
            {
                RenderCurrent("options-applied");
            }
            catch (Exception ex)
            {
                Mod.log.Error(LogPrefix + "[Render] In-game metro preview render failed: " + ex);
                SetState("error", "render-failed", ex.Message);
            }
        }

        private void ProcessExportJson()
        {
            if (!IsPlayableCityAvailable())
            {
                SetState("no-city", "load-city", string.Empty);
                return;
            }

            try
            {
                Mod.log.Info(LogPrefix + "[Export] JSON export action started.");
                bool succeeded = RealMetroJsonExporter.ExportRealMetroJson(Mod.UpdateSystem);
                if (!succeeded)
                {
                    SetState("error", "json-export-failed", "The exporter reported a failure. Check the game log and diagnostics file.");
                    return;
                }

                MetroNetworkSnapshotCapture latest;
                if (MetroNetworkSnapshotService.TryGetLatest(out latest))
                {
                    m_Capture = latest;
                    m_LastCaptureMilliseconds = latest.CaptureMilliseconds;
                }

                m_LastPath = RealMetroJsonExporter.GetJsonPath();
                if (m_Capture == null || m_Capture.Snapshot.Lines.Count == 0)
                {
                    ClearPreview("no-metro", "json-exported-no-metro", string.Empty);
                    m_LastPath = RealMetroJsonExporter.GetJsonPath();
                    PublishState();
                    return;
                }

                // The export capture is now authoritative and RenderCurrent applies the latest UI options.
                DiscardRequest(ref m_RefreshRequested);
                DiscardRequest(ref m_RenderRequested);
                RenderCurrent("json-exported");
            }
            catch (Exception ex)
            {
                Mod.log.Error(LogPrefix + "[Export] In-game JSON export action failed: " + ex);
                SetState("error", "json-export-failed", ex.Message);
            }
        }

        private void ProcessSaveSvg()
        {
            if (m_Capture == null || string.IsNullOrWhiteSpace(m_CurrentSvg))
            {
                SetState("error", "no-svg-to-save", "Refresh the preview before saving SVG.");
                return;
            }

            try
            {
                string exportDirectory = Mod.Settings == null
                    ? RealMetroJsonExporter.GetDefaultExportDirectory()
                    : Mod.Settings.GetResolvedExportDirectory();
                SvgSnapshotWriteResult result = SvgSnapshotFileWriter.Write(
                    exportDirectory,
                    m_Capture.Snapshot.CityName,
                    m_CurrentSvg,
                    DateTime.Now);
                m_LastPath = result.LatestPath;
                SetState("ready", "svg-saved", string.Empty);
                Mod.log.Info(string.Format(
                    LogPrefix + "[Save] In-game metro SVG saved. Latest: {0}. Snapshot: {1}.",
                    result.LatestPath,
                    result.SnapshotPath));
            }
            catch (Exception ex)
            {
                Mod.log.Error(LogPrefix + "[Save] In-game SVG save failed: " + ex);
                SetState("error", "svg-save-failed", ex.Message);
            }
        }

        private void RenderCurrent(string notice)
        {
            InGamePreviewRenderResponse response = InGamePreviewRenderService.Render(
                m_Capture.Snapshot,
                m_LayoutMode,
                m_ShowGenericStationNames,
                m_HideCrowdedLabels);
            PortableRenderResult result = response.Result;
            m_CurrentSvg = result.Svg;
            m_LastRenderMilliseconds = response.RequestElapsedMilliseconds;
            m_LastRendererMilliseconds = result.ElapsedMilliseconds;
            m_LastRenderWasCacheHit = response.WasCacheHit;
            m_RenderCacheEntries = response.CacheEntryCount;
            m_SvgBinding.Update(m_CurrentSvg);
            SetState("ready", notice, string.Empty);
            Mod.log.Info(string.Format(
                CultureInfo.InvariantCulture,
                LogPrefix + "[Render] In-game metro preview ready. Revision={0}, layout={1}, lines={2}, stations={3}, requestMs={4}, rendererMs={5}, cacheHit={6}, cacheEntries={7}, svgChars={8}.",
                result.SnapshotRevision,
                GetLayoutToken(),
                result.LineCount,
                result.StationCount,
                response.RequestElapsedMilliseconds,
                result.ElapsedMilliseconds,
                response.WasCacheHit,
                response.CacheEntryCount,
                m_CurrentSvg.Length));
        }

        private void ClearPreview(string status, string notice, string error)
        {
            DiscardRequest(ref m_RenderRequested);
            DiscardRequest(ref m_SaveSvgRequested);
            m_Capture = null;
            m_CurrentSvg = string.Empty;
            m_LastCaptureMilliseconds = 0;
            m_LastRenderMilliseconds = 0;
            m_LastRendererMilliseconds = 0;
            m_LastRenderWasCacheHit = false;
            m_SvgBinding.Update(string.Empty);
            SetState(status, notice, error);
        }

        private bool IsPlayableCityAvailable()
        {
            return Mod.UpdateSystem != null
                && GameManager.instance != null
                && GameManager.instance.gameMode == GameMode.Game
                && !GameManager.instance.isGameLoading;
        }

        private void PersistPreviewSettings()
        {
            if (Mod.Settings == null)
            {
                return;
            }

            try
            {
                Mod.Settings.InGamePreviewLayout = GetLayoutToken();
                Mod.Settings.InGamePreviewGeographicDefaultApplied = true;
                Mod.Settings.InGamePreviewShowGenericStationNames = m_ShowGenericStationNames;
                Mod.Settings.InGamePreviewHideCrowdedLabels = m_HideCrowdedLabels;
                Mod.Settings.ApplyAndSave();
            }
            catch (Exception ex)
            {
                Mod.log.Warn(LogPrefix + "[Settings] Could not persist in-game preview settings: " + ex.Message);
            }
        }

        private string GetLayoutToken()
        {
            return m_LayoutMode == PortableLayoutMode.Geographic
                ? Setting.PreviewLayoutGeographic
                : Setting.PreviewLayoutSchematic;
        }

        private void QueueRequest(ref bool requestFlag)
        {
            if (requestFlag)
            {
                m_CoalescedRequestCount++;
            }

            requestFlag = true;
        }

        private void DiscardRequest(ref bool requestFlag)
        {
            if (!requestFlag)
            {
                return;
            }

            requestFlag = false;
            m_CoalescedRequestCount++;
        }

        private void SetState(string status, string notice, string error)
        {
            m_Status = status;
            m_Notice = notice;
            m_Error = error ?? string.Empty;
            m_UpdatedAt = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            PublishState();
        }

        private void PublishState()
        {
            m_StateJsonBinding.Update(BuildStateJson());
        }

        private string BuildStateJson()
        {
            MetroNetworkSnapshot snapshot = m_Capture == null ? null : m_Capture.Snapshot;
            StringBuilder builder = new StringBuilder(640);
            builder.Append('{');
            AppendJsonString(builder, "status", m_Status);
            AppendJsonString(builder, "notice", m_Notice);
            AppendJsonString(builder, "error", m_Error);
            AppendJsonString(builder, "cityName", snapshot == null ? string.Empty : snapshot.CityName);
            AppendJsonString(builder, "layout", GetLayoutToken());
            AppendJsonString(builder, "snapshotRevision", snapshot == null ? string.Empty : snapshot.Revision);
            AppendJsonString(builder, "updatedAt", m_UpdatedAt);
            AppendJsonString(builder, "lastPath", m_LastPath);
            AppendJsonString(builder, "interfaceLanguage", Mod.Settings == null
                ? Setting.LanguageAuto
                : Mod.Settings.InterfaceLanguage);
            AppendJsonNumber(builder, "lineCount", snapshot == null ? 0 : snapshot.Lines.Count);
            AppendJsonNumber(builder, "stationCount", snapshot == null ? 0 : snapshot.Stations.Count);
            AppendJsonNumber(builder, "refreshCount", m_RefreshCount);
            AppendJsonNumber(builder, "openCount", m_OpenCount);
            AppendJsonNumber(builder, "coalescedRequests", m_CoalescedRequestCount);
            AppendJsonNumber(builder, "captureMs", m_LastCaptureMilliseconds);
            AppendJsonNumber(builder, "renderMs", m_LastRenderMilliseconds);
            AppendJsonNumber(builder, "rendererMs", m_LastRendererMilliseconds);
            AppendJsonNumber(builder, "renderCacheEntries", m_RenderCacheEntries);
            AppendJsonNumber(builder, "svgLength", m_CurrentSvg == null ? 0 : m_CurrentSvg.Length);
            AppendJsonNumber(builder, "canvasWidth", PreviewCanvasWidth);
            AppendJsonNumber(builder, "canvasHeight", PreviewCanvasHeight);
            AppendJsonBoolean(builder, "showGenericStationNames", m_ShowGenericStationNames);
            AppendJsonBoolean(builder, "hideCrowdedLabels", m_HideCrowdedLabels);
            AppendJsonBoolean(builder, "renderCacheHit", m_LastRenderWasCacheHit);
            AppendJsonBoolean(builder, "hasSvg", !string.IsNullOrWhiteSpace(m_CurrentSvg));
            AppendJsonBoolean(builder, "busy", IsBusyStatus(m_Status));
            if (builder[builder.Length - 1] == ',')
            {
                builder.Length--;
            }

            builder.Append('}');
            return builder.ToString();
        }

        private static bool IsBusyStatus(string status)
        {
            return status == "loading" || status == "rendering" || status == "exporting" || status == "saving";
        }

        private static void AppendJsonString(StringBuilder builder, string name, string value)
        {
            builder.Append('\"').Append(name).Append("\":\"");
            AppendEscapedJson(builder, value ?? string.Empty);
            builder.Append("\",");
        }

        private static void AppendJsonNumber(StringBuilder builder, string name, long value)
        {
            builder.Append('\"').Append(name).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture)).Append(',');
        }

        private static void AppendJsonBoolean(StringBuilder builder, string name, bool value)
        {
            builder.Append('\"').Append(name).Append("\":").Append(value ? "true" : "false").Append(',');
        }

        private static void AppendEscapedJson(StringBuilder builder, string value)
        {
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '\"': builder.Append("\\\""); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < ' ')
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            builder.Append(character);
                        }
                        break;
                }
            }
        }

        [Preserve]
        public InGamePreviewUISystem()
        {
        }
    }
}
