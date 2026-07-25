using System;

namespace MetroDiagram.Engine
{
    /// <summary>
    /// Canonical per-line transport mode strings shared by the exporter, the
    /// display-family resolvers, and both renderers so they never drift on
    /// spelling. The value rides on the free-form <c>mode</c> field already
    /// present end to end (<see cref="MetroSnapshotLine.Mode"/>); anything
    /// unrecognized is treated as <see cref="Metro"/>.
    /// </summary>
    public static class MetroLineModes
    {
        public const string Metro = "metro";
        public const string Tram = "tram";
        public const string Train = "train";

        /// <summary>
        /// Normalizes an arbitrary mode string to a known value, defaulting to
        /// <see cref="Metro"/>. Case-insensitive; blank/unknown becomes metro.
        /// </summary>
        public static string Normalize(string? mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return Metro;
            }

            string trimmed = mode!.Trim();
            if (trimmed.Equals(Tram, StringComparison.OrdinalIgnoreCase))
            {
                return Tram;
            }

            if (trimmed.Equals(Train, StringComparison.OrdinalIgnoreCase))
            {
                return Train;
            }

            return Metro;
        }

        /// <summary>True for tram and intercity-train (i.e. not the metro default).</summary>
        public static bool IsNonMetro(string? mode)
        {
            return !string.Equals(Normalize(mode), Metro, StringComparison.Ordinal);
        }
    }
}
