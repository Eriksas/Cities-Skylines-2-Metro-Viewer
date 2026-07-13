using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI.Widgets;
using MetroDiagram.Core.Exporting;
using System;
using System.Collections.Generic;

namespace CS2_Metro
{
    [FileLocation(nameof(CS2_Metro))]
    [SettingsUIGroupOrder(kInterfaceGroup, kFolderGroup, kExportGroup)]
    [SettingsUIShowGroupName(kInterfaceGroup, kFolderGroup, kExportGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kInterfaceGroup = "Interface";
        public const string kFolderGroup = "ExportFolder";
        public const string kExportGroup = "Export";
        public const string LanguageAuto = "auto";
        public const string LanguageEnglish = "en";
        public const string LanguageChinese = "zh-HANS";
        public const string PreviewLayoutSchematic = "schematic";
        public const string PreviewLayoutGeographic = "geographic";

        private string m_InterfaceLanguage = LanguageAuto;

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kInterfaceGroup)]
        [SettingsUIDropdown(typeof(Setting), nameof(GetInterfaceLanguageItems))]
        public string InterfaceLanguage
        {
            get => m_InterfaceLanguage;
            set
            {
                string normalized = NormalizeInterfaceLanguage(value);
                if (string.Equals(m_InterfaceLanguage, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                m_InterfaceLanguage = normalized;
                Mod.NotifyInterfaceLanguageChanged();
            }
        }

        public DropdownItem<string>[] GetInterfaceLanguageItems()
        {
            return new[]
            {
                new DropdownItem<string>
                {
                    value = LanguageAuto,
                    displayName = GetOptionLabelLocaleID("InterfaceLanguage.Auto")
                },
                new DropdownItem<string>
                {
                    value = LanguageEnglish,
                    displayName = GetOptionLabelLocaleID("InterfaceLanguage.English")
                },
                new DropdownItem<string>
                {
                    value = LanguageChinese,
                    displayName = GetOptionLabelLocaleID("InterfaceLanguage.Chinese")
                }
            };
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

        [SettingsUIHidden]
        public string InGamePreviewLayout { get; set; } = PreviewLayoutGeographic;

        [SettingsUIHidden]
        public bool InGamePreviewGeographicDefaultApplied { get; set; }

        [SettingsUIHidden]
        public bool InGamePreviewShowGenericStationNames { get; set; }

        [SettingsUIHidden]
        public bool InGamePreviewHideCrowdedLabels { get; set; } = true;

        [SettingsUISection(kSection, kExportGroup)]
        [SettingsUIButton]
        public bool ExportRealMetroJson
        {
            set
            {
                RealMetroJsonExporter.ExportRealMetroJson(Mod.UpdateSystem);
            }
        }

        public override void SetDefaults()
        {
            InterfaceLanguage = LanguageAuto;
            ExportDirectory = ExportDirectoryResolver.GetDocumentsExportDirectory();
            InGamePreviewLayout = PreviewLayoutGeographic;
            InGamePreviewGeographicDefaultApplied = false;
            InGamePreviewShowGenericStationNames = false;
            InGamePreviewHideCrowdedLabels = true;
        }

        internal bool ShouldUseChinese(string localeId)
        {
            if (string.Equals(InterfaceLanguage, LanguageChinese, StringComparison.Ordinal))
            {
                return true;
            }

            if (string.Equals(InterfaceLanguage, LanguageEnglish, StringComparison.Ordinal))
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(localeId)
                && localeId.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeInterfaceLanguage(string value)
        {
            if (string.Equals(value, LanguageEnglish, StringComparison.Ordinal)
                || string.Equals(value, LanguageChinese, StringComparison.Ordinal))
            {
                return value;
            }

            return LanguageAuto;
        }
    }

    public sealed class ModLocaleSource : IDictionarySource
    {
        private readonly Setting m_Setting;
        private readonly string m_LocaleId;

        public ModLocaleSource(Setting setting, string localeId)
        {
            m_Setting = setting;
            m_LocaleId = localeId;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return m_Setting.ShouldUseChinese(m_LocaleId)
                ? ReadChineseEntries()
                : ReadEnglishEntries();
        }

        public void Unload()
        {
        }

        private Dictionary<string, string> ReadEnglishEntries()
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "CS2 Metro Diagram" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kInterfaceGroup), "Interface" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kFolderGroup), "Export Folder" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kExportGroup), "Export" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.InterfaceLanguage)), "Language" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.InterfaceLanguage)), "Choose this mod's options language. Auto follows the current game language without changing the language of the game itself." },
                { m_Setting.GetOptionLabelLocaleID("InterfaceLanguage.Auto"), "Auto (follow game)" },
                { m_Setting.GetOptionLabelLocaleID("InterfaceLanguage.English"), "English" },
                { m_Setting.GetOptionLabelLocaleID("InterfaceLanguage.Chinese"), "Chinese (Simplified)" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportDirectory)), "Export folder" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportDirectory)), "Paste a full folder path, or click a preset below. The latest JSON export, diagnostics, and timestamped snapshots will be written here. Leave blank to use Documents\\CS2MetroDiagram." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseDocumentsExportFolder)), "Use Documents folder" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseDocumentsExportFolder)), "Set export folder to Documents\\CS2MetroDiagram." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseDesktopExportFolder)), "Use Desktop folder" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseDesktopExportFolder)), "Set export folder to Desktop\\CS2MetroDiagram." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseDDriveExportFolder)), "Use D:\\CS2MetroDiagram" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseDDriveExportFolder)), "Set export folder to D:\\CS2MetroDiagram. Use this only if the D: drive exists." },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportRealMetroJson)), "Export Real Metro JSON" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportRealMetroJson)), "Exports your city's metro/subway network to JSON. Open the JSON in the CS2 Metro Diagram Viewer to render the map. No SVG preview is generated in-game." }
            };
        }

        private Dictionary<string, string> ReadChineseEntries()
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "CS2 地铁线路图" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "主要设置" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kInterfaceGroup), "界面" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kFolderGroup), "导出文件夹" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kExportGroup), "导出" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.InterfaceLanguage)), "语言" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.InterfaceLanguage)), "选择本模组设置页使用的语言。自动模式会跟随当前游戏语言，但不会改变游戏本身的语言。" },
                { m_Setting.GetOptionLabelLocaleID("InterfaceLanguage.Auto"), "自动（跟随游戏）" },
                { m_Setting.GetOptionLabelLocaleID("InterfaceLanguage.English"), "English" },
                { m_Setting.GetOptionLabelLocaleID("InterfaceLanguage.Chinese"), "简体中文" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportDirectory)), "导出文件夹" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportDirectory)), "粘贴完整文件夹路径，或点击下方常用位置。最新 JSON、诊断文件和带时间戳的快照都会写入这里。留空时使用 Documents\\CS2MetroDiagram。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseDocumentsExportFolder)), "使用文档文件夹" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseDocumentsExportFolder)), "将导出文件夹设为 Documents\\CS2MetroDiagram。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseDesktopExportFolder)), "使用桌面文件夹" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseDesktopExportFolder)), "将导出文件夹设为 Desktop\\CS2MetroDiagram。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.UseDDriveExportFolder)), "使用 D:\\CS2MetroDiagram" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.UseDDriveExportFolder)), "将导出文件夹设为 D:\\CS2MetroDiagram。仅在电脑存在 D: 盘时使用。" },
                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ExportRealMetroJson)), "导出真实地铁 JSON" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ExportRealMetroJson)), "把当前城市的地铁线路导出为 JSON。随后可在 CS2 地铁线路图 Viewer 中打开并生成线路图；游戏内不会生成 SVG 预览。" }
            };
        }
    }
}
