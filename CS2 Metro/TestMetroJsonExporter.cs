using MetroDiagram.Core.Exporting;

namespace CS2_Metro
{
    // Resolves the shared export directory used by the real metro JSON exporter.
    // (The former in-game "Export Test Metro JSON" developer option was removed
    // for release; only export-directory resolution is still needed here.)
    public static class TestMetroJsonExporter
    {
        public static string GetDefaultExportDirectory()
        {
            return ExportDirectoryResolver.GetConfiguredOrDefaultExportDirectory(Mod.Settings?.ExportDirectory);
        }
    }
}
