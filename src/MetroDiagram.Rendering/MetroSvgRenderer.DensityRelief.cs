using System;
using System.Collections.Generic;
using System.Linq;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    // Give the dense centre more room and let the sparse edge give some back.
    //
    // A real network is not evenly dense: downtown stations sit a couple of
    // hundred metres apart while suburban ones are kilometres apart, and drawing
    // that faithfully means the centre of the diagram is unreadable while the
    // rim is empty. Measured over 23 networks, local station spacing at the
    // sparse end is 4.4x the spacing at the dense end, and up to 6.9x.
    //
    // This is the one classic answer to that: draw the centre at a larger
    // effective scale than the rim, the way the London tube map has since 1933.
    // The map here is radial and monotone -- r' = R * (r/R)^exponent with the
    // exponent below one -- which matters for two reasons beyond simplicity:
    //
    //   * it is a homeomorphism, so it cannot introduce a crossing, cannot
    //     reorder two stations radially, and cannot turn the map inside out;
    //   * it is a *global* reshaping rather than a per-edge correction, so it
    //     does not re-open the trade the annealer already priced between edge
    //     length, crossings and clearance. Trying to fix density edge by edge is
    //     what failed in round 7 of the layout study.
    //
    // It runs before the octilinear snap, not after: the relief moves stations
    // off their headings, and the snap is what puts them back. Doing it in the
    // other order fights constraints that are already established.

    /// <summary>
    /// Radial exponent. One leaves the layout untouched; lower values expand the
    /// centre at the rim's expense.
    /// </summary>
    private const double DensityReliefExponent = 0.8;

    /// <summary>
    /// Stations closer than this share of the median spacing count as crowded
    /// when locating the centre to expand around.
    /// </summary>
    private const int DensityReliefNeighbour = 3;

    /// <summary>
    /// Networks whose spacing spread is already below this are left alone.
    ///
    /// Relief is a treatment for uneven density, and applying it to a network
    /// that is already even buys nothing and costs the distortion anyway. The two
    /// networks it hurt worst in the corpus -- 安岭市 at 6% crowded going to 47%,
    /// and 谢菲尔德-0622 -- were the two with the *least* spread to begin with
    /// (2.25x and 2.68x against a corpus mean of 4.38x).
    /// </summary>
    private const double DensityReliefMinimumSpread = 3.0;

    private const double DensityReliefEpsilon = 1e-9;

    /// <summary>
    /// Returns the layout with its dense core expanded, or the input unchanged
    /// when the pass is off or the network is too small to have a core.
    /// </summary>
    private static Dictionary<string, SvgPoint> ApplyDensityRelief(
        Dictionary<string, SvgPoint> points,
        SvgRenderOptions options,
        List<string> warnings)
    {
        if (!options.EnableDensityRelief || points.Count < 8)
        {
            return points;
        }

        double exponent = Math.Clamp(options.DensityReliefExponent, 0.4, 1.0);
        if (Math.Abs(exponent - 1) < 1e-6)
        {
            return points;
        }

        string[] ids = points.Keys.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        SvgPoint[] positions = ids.Select(id => points[id]).ToArray();

        double[] spacing = LocalSpacing(positions);
        double spread = SpacingSpread(spacing);
        if (spread < DensityReliefMinimumSpread)
        {
            warnings.Add($"Density relief skipped: spacing spread {Format(spread)}x is already even.");
            return points;
        }

        SvgPoint centre = CrowdedCentre(positions, spacing);

        double maxRadius = positions.Max(point => Distance(point, centre));
        if (maxRadius < DensityReliefEpsilon)
        {
            return points;
        }

        Dictionary<string, SvgPoint> result = new(points.Count, StringComparer.Ordinal);
        double moved = 0;
        for (int i = 0; i < ids.Length; i++)
        {
            double dx = positions[i].X - centre.X;
            double dy = positions[i].Y - centre.Y;
            double radius = Math.Sqrt((dx * dx) + (dy * dy));
            if (radius < DensityReliefEpsilon)
            {
                result[ids[i]] = positions[i];
                continue;
            }

            double scaled = maxRadius * Math.Pow(radius / maxRadius, exponent);
            double factor = scaled / radius;
            SvgPoint moved_ = new(centre.X + (dx * factor), centre.Y + (dy * factor));
            moved += Distance(positions[i], moved_);
            result[ids[i]] = moved_;
        }

        double[] after = LocalSpacing(ids.Select(id => result[id]).ToArray());
        warnings.Add(
            $"Density relief: exponent {Format(exponent)}; local spacing spread "
            + $"{Format(spread)}x -> {Format(SpacingSpread(after))}x; "
            + $"stations moved {Format(moved / ids.Length)} mean.");
        return result;
    }

    /// <summary>Distance to the n-th nearest other station -- a local density probe.</summary>
    private static double[] LocalSpacing(SvgPoint[] positions)
    {
        double[] spacing = new double[positions.Length];
        List<double> distances = new(positions.Length);
        for (int i = 0; i < positions.Length; i++)
        {
            distances.Clear();
            for (int j = 0; j < positions.Length; j++)
            {
                if (i != j)
                {
                    distances.Add(Distance(positions[i], positions[j]));
                }
            }

            distances.Sort();
            spacing[i] = distances[Math.Min(DensityReliefNeighbour - 1, distances.Count - 1)];
        }

        return spacing;
    }

    private static double SpacingSpread(double[] spacing)
    {
        double[] sorted = (double[])spacing.Clone();
        Array.Sort(sorted);
        double dense = sorted[(int)(sorted.Length * 0.10)];
        double sparse = sorted[(int)(sorted.Length * 0.90)];
        return dense < DensityReliefEpsilon ? 0 : sparse / dense;
    }

    /// <summary>
    /// The point to expand around: the station centroid weighted by local
    /// crowding, so the dense part of the network pulls it in rather than the
    /// geometric middle of a lopsided map.
    /// </summary>
    private static SvgPoint CrowdedCentre(SvgPoint[] positions, double[] spacing)
    {
        double totalWeight = 0;
        double x = 0;
        double y = 0;
        for (int i = 0; i < positions.Length; i++)
        {
            double weight = 1.0 / Math.Max(spacing[i], DensityReliefEpsilon);
            totalWeight += weight;
            x += positions[i].X * weight;
            y += positions[i].Y * weight;
        }

        return totalWeight < DensityReliefEpsilon
            ? new SvgPoint(positions.Average(p => p.X), positions.Average(p => p.Y))
            : new SvgPoint(x / totalWeight, y / totalWeight);
    }
}
