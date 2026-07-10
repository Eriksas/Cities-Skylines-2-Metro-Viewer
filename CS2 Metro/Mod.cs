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
        private bool m_OptionsRegistered;
        private static bool s_LocalizationReady;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));
            UpdateSystem = updateSystem;

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
            }

            try
            {
                m_Setting = new Setting(this);
                Settings = m_Setting;
                AssetDatabase.global.LoadSettings(nameof(CS2_Metro), m_Setting, new Setting(this));

                // Keep the official mod-template order: the options page must be
                // registered BEFORE locale sources are added. AddSource eagerly
                // reads the setting's locale IDs through the registered options
                // machinery; in the reverse order every AddSource throws and the
                // failure used to take the exporter down with it (beta.2 bug).
                m_Setting.RegisterInOptionsUI();
                m_OptionsRegistered = true;

                try
                {
                    RegisterLocalizationSources();
                }
                catch (Exception ex)
                {
                    // Localized option labels are cosmetic. Never let them block
                    // the exporter; the page falls back to raw English keys.
                    log.Warn($"CS2 Metro Diagram localization unavailable, continuing without it: {ex.Message}");
                }

                log.Info($"Real metro JSON export directory: {RealMetroJsonExporter.GetDefaultExportDirectory()}");
            }
            catch (Exception ex)
            {
                log.Error($"CS2 Metro Diagram failed to initialize: {ex}");
                CleanupRegistrations();
                throw;
            }
        }

        internal static void NotifyInterfaceLanguageChanged()
        {
            if (!s_LocalizationReady || GameManager.instance?.localizationManager == null)
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
            var localizationManager = GameManager.instance?.localizationManager;
            if (localizationManager == null)
            {
                throw new InvalidOperationException("The CS2 localization manager is not available yet.");
            }

            var localeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            IEnumerable<string> supportedLocales = localizationManager.GetSupportedLocales();
            if (supportedLocales != null)
            {
                foreach (string localeId in supportedLocales)
                {
                    if (!string.IsNullOrWhiteSpace(localeId))
                    {
                        localeIds.Add(localeId);
                    }
                }
            }

            if (localeIds.Count == 0)
            {
                throw new InvalidOperationException("CS2 reported no supported localization locales.");
            }

            foreach (string localeId in localeIds)
            {
                try
                {
                    var source = new ModLocaleSource(m_Setting, localeId);
                    localizationManager.AddSource(localeId, source);
                    m_LocaleRegistrations.Add(new LocaleRegistration(localeId, source));
                }
                catch (Exception ex)
                {
                    log.Warn($"Could not register CS2 Metro Diagram localization for '{localeId}': {ex.Message}");
                }
            }

            if (m_LocaleRegistrations.Count == 0)
            {
                throw new InvalidOperationException("No CS2 Metro Diagram localization source could be registered.");
            }

            s_LocalizationReady = true;
            try
            {
                localizationManager.ReloadActiveLocale();
            }
            catch (Exception ex)
            {
                log.Warn($"Could not reload the active locale after registering CS2 Metro Diagram: {ex.Message}");
            }

            log.Info($"Registered CS2 Metro Diagram localization for {m_LocaleRegistrations.Count} supported locale(s).");
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));

            CleanupRegistrations();
        }

        private void CleanupRegistrations()
        {
            s_LocalizationReady = false;

            var localizationManager = GameManager.instance?.localizationManager;
            if (localizationManager != null)
            {
                foreach (LocaleRegistration registration in m_LocaleRegistrations)
                {
                    try
                    {
                        localizationManager.RemoveSource(registration.LocaleId, registration.Source);
                    }
                    catch (Exception ex)
                    {
                        log.Warn($"Could not remove CS2 Metro Diagram localization for '{registration.LocaleId}': {ex.Message}");
                    }
                }
            }

            m_LocaleRegistrations.Clear();

            if (m_OptionsRegistered && m_Setting != null)
            {
                try
                {
                    m_Setting.UnregisterInOptionsUI();
                }
                catch (Exception ex)
                {
                    log.Warn($"Could not unregister CS2 Metro Diagram options: {ex.Message}");
                }
            }

            m_OptionsRegistered = false;
            m_Setting = null;
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
