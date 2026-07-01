using System;
using System.IO;

namespace MetroDiagram.Core.Exporting
{
    public static class ExportDirectoryResolver
    {
        public const string AppFolderName = "CS2MetroDiagram";
        public const string DocumentsPreset = "documents";
        public const string DesktopPreset = "desktop";
        public const string DDrivePreset = "d-drive";

        public static string GetConfiguredOrDefaultExportDirectory(string configuredDirectory)
        {
            return ResolveExportDirectory(
                configuredDirectory,
                GetKnownFolderExportDirectory(Environment.SpecialFolder.MyDocuments),
                GetKnownFolderExportDirectory(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Path.GetTempPath());
        }

        public static string GetDocumentsExportDirectory()
        {
            return ResolveExportDirectory(DocumentsPreset);
        }

        public static string GetDesktopExportDirectory()
        {
            return ResolveExportDirectory(DesktopPreset);
        }

        public static string GetDDriveExportDirectory()
        {
            return @"D:\CS2MetroDiagram";
        }

        public static string ResolveExportDirectory(
            string configuredDirectory,
            string documentsExportDirectory,
            string desktopExportDirectory,
            string userProfileDirectory,
            string tempDirectory)
        {
            string fallbackDirectory = FirstNonEmpty(
                documentsExportDirectory,
                CombineIfBaseExists(userProfileDirectory, AppFolderName),
                CombineIfBaseExists(tempDirectory, AppFolderName),
                Path.Combine(Path.GetTempPath(), AppFolderName));

            string expanded = NormalizeConfiguredDirectory(configuredDirectory);
            if (string.IsNullOrWhiteSpace(expanded))
            {
                return fallbackDirectory;
            }

            if (expanded.Equals(DocumentsPreset, StringComparison.OrdinalIgnoreCase))
            {
                return fallbackDirectory;
            }

            if (expanded.Equals(DesktopPreset, StringComparison.OrdinalIgnoreCase))
            {
                return FirstNonEmpty(desktopExportDirectory, fallbackDirectory);
            }

            if (expanded.Equals(DDrivePreset, StringComparison.OrdinalIgnoreCase))
            {
                return GetDDriveExportDirectory();
            }

            if (!Path.IsPathRooted(expanded))
            {
                expanded = Path.Combine(fallbackDirectory, expanded);
            }

            return GetFullPathOrOriginal(expanded);
        }

        public static string ResolveExportDirectory(string configuredDirectory)
        {
            return GetConfiguredOrDefaultExportDirectory(configuredDirectory);
        }

        private static string GetKnownFolderExportDirectory(Environment.SpecialFolder specialFolder)
        {
            string folder = Environment.GetFolderPath(specialFolder);
            return CombineIfBaseExists(folder, AppFolderName);
        }

        private static string NormalizeConfiguredDirectory(string configuredDirectory)
        {
            if (string.IsNullOrWhiteSpace(configuredDirectory))
            {
                return string.Empty;
            }

            string trimmed = configuredDirectory.Trim();
            if (trimmed.Length >= 2
                && ((trimmed[0] == '"' && trimmed[trimmed.Length - 1] == '"')
                    || (trimmed[0] == '\'' && trimmed[trimmed.Length - 1] == '\'')))
            {
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Trim();
            }

            return Environment.ExpandEnvironmentVariables(trimmed);
        }

        private static string CombineIfBaseExists(string baseDirectory, string childDirectory)
        {
            return string.IsNullOrWhiteSpace(baseDirectory)
                ? string.Empty
                : Path.Combine(baseDirectory, childDirectory);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return GetFullPathOrOriginal(value);
                }
            }

            return Path.Combine(Path.GetTempPath(), AppFolderName);
        }

        private static string GetFullPathOrOriginal(string value)
        {
            try
            {
                return Path.GetFullPath(value);
            }
            catch
            {
                return value;
            }
        }

    }
}
