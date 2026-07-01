using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace CS2_Metro
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(CS2_Metro)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public static UpdateSystem UpdateSystem { get; private set; }

        public static Setting Settings { get; private set; }

        private Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));
            UpdateSystem = updateSystem;

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
            }

            m_Setting = new Setting(this);
            Settings = m_Setting;
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));
            AssetDatabase.global.LoadSettings(nameof(CS2_Metro), m_Setting, new Setting(this));

            log.Info($"Test metro JSON export directory: {TestMetroJsonExporter.GetDefaultExportDirectory()}");
            log.Info($"Transport debug dump directory: {TransportDebugDumpExporter.GetDefaultExportDirectory()}");
            log.Info($"Real metro JSON export directory: {RealMetroJsonExporter.GetDefaultExportDirectory()}");
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));

            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }

            Settings = null;
            UpdateSystem = null;
        }
    }
}
