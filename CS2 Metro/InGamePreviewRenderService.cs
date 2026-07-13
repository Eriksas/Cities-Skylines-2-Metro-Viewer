using MetroDiagram.Engine;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CS2_Metro
{
    internal sealed class InGamePreviewRenderResponse
    {
        public InGamePreviewRenderResponse(
            PortableRenderResult result,
            bool wasCacheHit,
            int cacheEntryCount,
            long requestElapsedMilliseconds)
        {
            Result = result ?? throw new ArgumentNullException(nameof(result));
            WasCacheHit = wasCacheHit;
            CacheEntryCount = cacheEntryCount;
            RequestElapsedMilliseconds = requestElapsedMilliseconds;
        }

        public PortableRenderResult Result { get; }

        public bool WasCacheHit { get; }

        public int CacheEntryCount { get; }

        public long RequestElapsedMilliseconds { get; }
    }

    internal static class InGamePreviewRenderService
    {
        internal const int MaxCacheEntries = 4;
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, PortableRenderResult> Cache = new Dictionary<string, PortableRenderResult>(StringComparer.Ordinal);
        private static readonly List<string> CacheOrder = new List<string>();

        public static InGamePreviewRenderResponse Render(
            MetroNetworkSnapshot snapshot,
            PortableLayoutMode layoutMode,
            bool showGenericStationNames,
            bool hideCrowdedLabels)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            Stopwatch requestTimer = Stopwatch.StartNew();
            string cacheKey = string.Format(
                "{0}|{1}|{2}|{3}",
                snapshot.Revision,
                layoutMode,
                showGenericStationNames,
                hideCrowdedLabels);

            lock (Sync)
            {
                PortableRenderResult cached;
                if (Cache.TryGetValue(cacheKey, out cached))
                {
                    TouchCacheKey(cacheKey);
                    requestTimer.Stop();
                    return new InGamePreviewRenderResponse(
                        cached,
                        true,
                        Cache.Count,
                        requestTimer.ElapsedMilliseconds);
                }
            }

            PortableRenderOptions options = layoutMode == PortableLayoutMode.Geographic
                ? PortableRenderProfiles.CreateInGameGeographic(showGenericStationNames, hideCrowdedLabels)
                : PortableRenderProfiles.CreateInGameSchematic(showGenericStationNames, hideCrowdedLabels);
            PortableRenderResult rendered = new PortableMetroSvgRenderer().Render(snapshot, options);

            lock (Sync)
            {
                bool wasCacheHit = Cache.ContainsKey(cacheKey);
                if (!Cache.ContainsKey(cacheKey))
                {
                    Cache.Add(cacheKey, rendered);
                    CacheOrder.Add(cacheKey);
                    while (CacheOrder.Count > MaxCacheEntries)
                    {
                        string oldestKey = CacheOrder[0];
                        CacheOrder.RemoveAt(0);
                        Cache.Remove(oldestKey);
                    }
                }

                TouchCacheKey(cacheKey);
                requestTimer.Stop();
                return new InGamePreviewRenderResponse(
                    Cache[cacheKey],
                    wasCacheHit,
                    Cache.Count,
                    requestTimer.ElapsedMilliseconds);
            }
        }

        private static void TouchCacheKey(string cacheKey)
        {
            CacheOrder.Remove(cacheKey);
            CacheOrder.Add(cacheKey);
        }

        public static void Clear()
        {
            lock (Sync)
            {
                Cache.Clear();
                CacheOrder.Clear();
            }
        }
    }
}
