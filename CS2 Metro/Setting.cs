using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;
using MetroDiagram.Core.Exporting;
using System.Collections.Generic;

namespace CS2_Metro
{
    [FileLocation(nameof(CS2_Metro))]
    [SettingsUIGroupOrder(kFolderGroup, kExportGroup, kDebugGroup)]
    [SettingsUIShowGroupName(kFolderGroup, kExportGroup, kDebugGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kFolderGroup = "ExportFolder";
        public const string kExportGroup = "Export";
        public const string kDebugGroup = "Debug";

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kFolderGroup)]
        public string ExportDirectory { get; set; } = string.Empty;

        [SettingsUISection(kSection, kFolderGroup)]
        [SettingsUIButton]
        public bool UseDocumentsExportFolder
        {
            set
            {
                ExportDirectory = ExportDirectoryResolver.GetDocumentsExportDirectory();
                Mod.log.Info($"CS2 Metro Diagram export folder preset selected: Documents -> {ExportDirectory}");
            }
        }

        [SettingsUISection(kSection, kFolderGroup)]
        [SettingsUIButton]
        public bool UseDesktopExportFolder
        {
            set
            {
                ExportDirectory = ExportDirectoryResolver.GetDesktopExportDirectory();
                Mod.log.Info($"CS2 Metro Diagram export folder preset selected: Desktop -> {ExportDirectory}");
            }
        }

        [SettingsUISection(kSection, kFolderGroup)]
        [SettingsUIButton]
        public bool UseDDriveExportFolder
        {
            set
            {
                ExportDirectory = ExportDirectoryResolver.GetDDriveExportDirectory();
                Mod.log.Info($"CS2 Metro Diagram export folder preset selected: D drive -> {ExportDirectory}");
            }
        }

        public string GetResolvedExportDirectory()
        {
            return ExportDirectoryResolver.GetConfiguredOrDefaultExportDirectory(ExportDirectory);
        }

        [SettingsUISection(kSection, kExportGroup)]
        [SettingsUIButton]
        public bool ExportTestMetroJson
        {
            set
            {
                TestMetroJsonExporter.ExportTestMetroJson();
            }
        }

        [SettingsUISection(kSection, kExportGroup)]
        [SettingsUIButton]
        public bool ExportRealMetroJson
        {
            set
            {
                RealMetroJsonExporter.ExportRealMetroJson(Mod.UpdateSystem);
            }
        }

        [SettingsUISection(kSection, kDebugGroup)]
        [SettingsUIButton]
        public bool ExportTransportDebugDump
        {
            set
            {
                TransportDebugDumpExporter.ExportDebugDump(Mod.UpdateSystem);
            }
        }

        public override void SetDefaults()
        {
            ExportDirectory = ExportDirectoryResolver.GetDocumentsExportDirectory();
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "CS2 Metro Diagram" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kFolderGroup), "Export Folder" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kExportGroup), "Export" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kDebugGroup), "Debug" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportDirectory)), "Export folder" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportDirectory)), "Paste a full folder path, or click a preset below. Latest JSON, diagnostics, snapshots, test exports, and debug dumps will be written here. Leave blank to use Documents\\CS2MetroDiagram." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseDocumentsExportFolder)), "Use Documents folder" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseDocumentsExportFolder)), "Set export folder to Documents\\CS2MetroDiagram." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseDesktopExportFolder)), "Use Desktop folder" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseDesktopExportFolder)), "Set export folder to Desktop\\CS2MetroDiagram." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseDDriveExportFolder)), "Use D:\\CS2MetroDiagram" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseDDriveExportFolder)), "Set export folder to D:\\CS2MetroDiagram. Use this only if the D: drive exists." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportTestMetroJson)), "Export Test Metro JSON" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportTestMetroJson)), "Writes a static sample metro.json file for testing the offline SVG pipeline." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportRealMetroJson)), "Export Real Metro JSON" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportRealMetroJson)), "Writes a narrow real metro export from current CS2 transport line data. No SVG preview is generated in-game." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportTransportDebugDump)), "Export Transport Debug Dump" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportTransportDebugDump)), "Writes transport-related ECS diagnostics, including metro track geometry debug files, for manual analysis. This does not export a real metro diagram." }
            };
        }

        public void Unload()
        {
        }
    }
}
