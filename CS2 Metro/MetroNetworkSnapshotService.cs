using Game;
using MetroDiagram.Engine;
using System;
using System.Diagnostics;

namespace CS2_Metro
{
    internal sealed class MetroNetworkSnapshotCapture
    {
        public MetroNetworkSnapshotCapture(
            MetroNetworkSnapshot snapshot,
            string diagnostics,
            long captureMilliseconds = 0,
            DateTime? capturedAtUtc = null)
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Diagnostics = diagnostics ?? string.Empty;
            CaptureMilliseconds = captureMilliseconds;
            CapturedAtUtc = capturedAtUtc ?? DateTime.UtcNow;
        }

        public MetroNetworkSnapshot Snapshot { get; }

        public string Diagnostics { get; }

        public long CaptureMilliseconds { get; internal set; }

        public DateTime CapturedAtUtc { get; internal set; }
    }

    /// <summary>
    /// The single game-thread capture boundary used by file export and preview.
    /// Rendering consumes Snapshot only and never reaches back into ECS.
    /// </summary>
    internal static class MetroNetworkSnapshotService
    {
        private static readonly object Sync = new object();
        private static MetroNetworkSnapshotCapture _latest;

        public static MetroNetworkSnapshotCapture Capture(UpdateSystem updateSystem, string jsonPath, string diagnosticsPath)
        {
            Stopwatch timer = Stopwatch.StartNew();
            MetroNetworkSnapshotCapture capture = RealMetroJsonExporter.CaptureSnapshotCore(updateSystem, jsonPath, diagnosticsPath);
            timer.Stop();
            capture.CaptureMilliseconds = timer.ElapsedMilliseconds;
            capture.CapturedAtUtc = DateTime.UtcNow;
            lock (Sync)
            {
                _latest = capture;
            }

            return capture;
        }

        public static bool TryGetLatest(out MetroNetworkSnapshotCapture capture)
        {
            lock (Sync)
            {
                capture = _latest;
                return capture != null;
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                _latest = null;
            }
        }
    }
}
