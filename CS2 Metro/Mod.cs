using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;
using System;
using System.Collections.Generic;

namespace CS2_Metro
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(CS2_Metro)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public static UpdateSystem UpdateSystem { get; private set; }

        public static Setting Settings { get; private set; }

        private Setting m_Setting;
        private readonly List<LocaleRegistration> m_LocaleRegistrations = new List<LocaleRegistration>();
        private static bool s_LocalizationReady;

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
            AssetDatabase.global.LoadSettings(nameof(CS2_Metro), m_Setting, new Setting(this));
            m_Setting.RegisterInOptionsUI();
            RegisterLocalizationSources();

            log.Info($"Real metro JSON export directory: {RealMetroJsonExporter.GetDefaultExportDirectory()}");
        }

        internal static void NotifyInterfaceLanguageChanged()
        {
            if (!s_LocalizationReady || GameManager.instance == null)
            {
                return;
            }

            try
            {
                GameManager.instance.localizationManager.ReloadActiveLocale();
                log.Info($"CS2 Metro Diagram options language changed to: {Settings?.InterfaceLanguage ?? Setting.LanguageAuto}");
            }
            catch (Exception ex)
            {
                log.Warn($"Could not refresh CS2 Metro Diagram options localization: {ex.Message}");
            }
        }

        private void RegisterLocalizationSources()
        {
            var localizationManager = GameManager.instance.localizationManager;
            var localeIds = new HashSet<string>(localizationManager.GetSupportedLocales(), StringComparer.OrdinalIgnoreCase)
            {
                "en-US",
                "zh-HANS",
                "zh-CN"
            };

            foreach (string localeId in localeIds)
            {
                var source = new ModLocaleSource(m_Setting, localeId);
                localizationManager.AddSource(localeId, source);
                m_LocaleRegistrations.Add(new LocaleRegistration(localeId, source));
            }

            s_LocalizationReady = true;
            localizationManager.ReloadActiveLocale();
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));

            s_LocalizationReady = false;
            if (GameManager.instance != null)
            {
                foreach (LocaleRegistration registration in m_LocaleRegistrations)
                {
                    GameManager.instance.localizationManager.RemoveSource(registration.LocaleId, registration.Source);
                }
            }

            m_LocaleRegistrations.Clear();

            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }

            Settings = null;
            UpdateSystem = null;
        }

        private sealed class LocaleRegistration
        {
            public LocaleRegistration(string localeId, ModLocaleSource source)
            {
                LocaleId = localeId;
                Source = source;
            }

            public string LocaleId { get; }

            public ModLocaleSource Source { get; }
        }
    }
}
