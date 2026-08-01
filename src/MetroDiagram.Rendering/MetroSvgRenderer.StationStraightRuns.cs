using System;
using System.Collections.Generic;
using MetroDiagram.Core.Models;

namespace MetroDiagram.Rendering;

public sealed partial class MetroSvgRenderer
{
    // Keep plain stops out of the bends.
    //
    // A line that changes direction *at* a station puts a small dot in the crook
    // of the bend, and that is one of the things that separates our output from a
    // drawn map: rmp bends at invisible Virtual nodes, so a corner never lands on
    // a stop. Measured after the octilinear snap, 56-68% of our drawn corners
    // still sit on a station.
    //
    // Not all of them are worth moving. A corner at an *interchange* is ordinary
    // in real maps -- the interchange symbol is large enough to own the bend and
    // it reads as deliberate. It is the plain stop, a small dot wedged in the
    // angle, that looks like an accident. Splitting the corpus that way: of the
    // 15-21 station corners per map, only 3-10 are plain stops.
    //
    // Moving one costs a corner. With the layout exactly octilinear, the two runs
    // meeting at a station intersect *at* that station, so leaving it means
    // leaving one of the two run lines -- and a single inserted point cannot get
    // back on the other without going off-grid. Two can: carry the arrival
    // heading past the station, then close the offset with a 45-degree connector
    // that lands back on the departure run. One corner becomes two, both mid-edge,
    // and both still exactly on the grid.
    //
    // This is display geometry only. `data-points` keeps the route as the layout
    // produced it, so the Viewer's handles and the tests never see the inserted
    // bends.

    /// <summary>
    /// How far the line carries the arrival heading past the station.
    ///
    /// Kept short deliberately. A 45-degree turn has no clean detour on an
    /// eight-heading grid -- there is no heading between two adjacent ones, so
    /// the corner cannot be cut and the line has to overshoot and come back,
    /// which leaves a bump of about 0.7 times this distance. At 1.6x the stroke
    /// that bump was obvious; at 0.9x it reads as a slight shoulder. Ninety-degree
    /// turns do have a clean cut and are unaffected by the choice.
    /// </summary>
    private const double StraightRunStubWidthFactor = 0.9;

    /// <summary>Below this the vertex is not a bend a reader would notice.</summary>
    private const double StraightRunMinTurnDegrees = 20.0;

    /// <summary>
    /// Above this the station keeps its corner. A near-reversal has no room for a
    /// detour that does not read as a hairpin, and a sharp turn at a station is a
    /// real metro-map idiom anyway.
    /// </summary>
    private const double StraightRunMaxTurnDegrees = 100.0;

    /// <summary>The detour may not eat more than this share of the outgoing edge.</summary>
    private const double StraightRunMaxLegFraction = 0.45;

    private const double StraightRunEpsilon = 1e-6;

    /// <summary>
    /// Returns the geometry to draw, with plain stops lifted out of their bends.
    /// Returns <paramref name="points"/> unchanged when the pass is off or has
    /// nothing to move.
    /// </summary>
    private static IReadOnlyList<SvgPoint> BuildStationStraightRuns(
        IReadOnlyList<SvgPoint> points,
        Dictionary<string, SvgPoint> stationPoints,
        Dictionary<string, MetroStation> stationsById,
        SvgRenderOptions options)
    {
        if (!options.EnableStationStraightRuns || points.Count < 3)
        {
            return points;
        }

        double stub = SvgVisualStyle.From(options).BaseRouteWidth * StraightRunStubWidthFactor;
        List<SvgPoint> drawn = new(points.Count) { points[0] };

        for (int i = 1; i < points.Count - 1; i++)
        {
            SvgPoint previous = drawn[^1];
            SvgPoint vertex = points[i];
            SvgPoint next = points[i + 1];

            if (!ShouldLiftStopOutOfBend(previous, vertex, next, stationPoints, stationsById))
            {
                drawn.Add(vertex);
                continue;
            }

            List<SvgPoint>? detour = BuildBendDetour(previous, vertex, next, stub);
            if (detour is null)
            {
                drawn.Add(vertex);
                continue;
            }

            drawn.Add(vertex);
            drawn.AddRange(detour);
        }

        drawn.Add(points[^1]);
        return drawn;
    }

    private static (double X, double Y, double Length) Direction(SvgPoint from, SvgPoint to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        return length <= StraightRunEpsilon ? (0, 0, 0) : (dx / length, dy / length, length);
    }

    private static double TurnDegrees(SvgPoint from, SvgPoint vertex, SvgPoint to)
    {
        (double X, double Y, double Length) back = Direction(vertex, from);
        (double X, double Y, double Length) forward = Direction(vertex, to);
        if (back.Length <= StraightRunEpsilon || forward.Length <= StraightRunEpsilon)
        {
            return 0;
        }

        double dot = Math.Clamp((back.X * forward.X) + (back.Y * forward.Y), -1, 1);
        return 180 - (Math.Acos(dot) * 180 / Math.PI);
    }

    private static bool ShouldLiftStopOutOfBend(
        SvgPoint previous,
        SvgPoint vertex,
        SvgPoint next,
        Dictionary<string, SvgPoint> stationPoints,
        Dictionary<string, MetroStation> stationsById)
    {
        double turn = TurnDegrees(previous, vertex, next);
        if (turn < StraightRunMinTurnDegrees || turn > StraightRunMaxTurnDegrees)
        {
            return false;
        }

        if (NearestStationId(vertex, stationPoints) is not string id
            || !stationsById.TryGetValue(id, out MetroStation? station))
        {
            return false;
        }

        // Interchanges keep their corners: the symbol is large enough to own the
        // bend, and moving it would only trade a readable junction for a detour.
        return !IsInterchange(station);
    }

    /// <summary>
    /// Two grid points that carry the arrival heading past the station and then
    /// rejoin the departure run: the stub end, and the 45-degree connector's
    /// landing. Null when the outgoing edge is too short to hold them.
    /// </summary>
    private static List<SvgPoint>? BuildBendDetour(SvgPoint previous, SvgPoint vertex, SvgPoint next, double stub)
    {
        (double X, double Y, double Length) arrival = Direction(previous, vertex);
        (double X, double Y, double Length) departure = Direction(vertex, next);
        if (arrival.Length <= StraightRunEpsilon || departure.Length <= StraightRunEpsilon)
        {
            return null;
        }

        double reach = Math.Min(stub, departure.Length * StraightRunMaxLegFraction);
        if (reach <= StraightRunEpsilon)
        {
            return null;
        }

        SvgPoint stubEnd = new(
            vertex.X + (arrival.X * reach),
            vertex.Y + (arrival.Y * reach));

        // Perpendicular offset of the stub end from the departure run's line.
        double normalX = -departure.Y;
        double normalY = departure.X;
        double offset = ((stubEnd.X - vertex.X) * normalX) + ((stubEnd.Y - vertex.Y) * normalY);
        if (Math.Abs(offset) <= StraightRunEpsilon)
        {
            return null;                       // already collinear; nothing to bend around
        }

        // Close that offset with the heading 45 degrees off the departure run, on
        // the side that moves back towards it. On an octilinear layout that pair
        // is always axis + diagonal, so the connector stays exactly on the grid.
        double sign = offset > 0 ? -1 : 1;
        double connectorX = (departure.X * Math.Sqrt(0.5)) + (normalX * sign * Math.Sqrt(0.5));
        double connectorY = (departure.Y * Math.Sqrt(0.5)) + (normalY * sign * Math.Sqrt(0.5));
        double connectorNormal = (connectorX * normalX) + (connectorY * normalY);
        if (Math.Abs(connectorNormal) <= StraightRunEpsilon)
        {
            return null;
        }

        double travel = -offset / connectorNormal;
        if (travel <= StraightRunEpsilon)
        {
            return null;
        }

        SvgPoint landing = new(
            stubEnd.X + (connectorX * travel),
            stubEnd.Y + (connectorY * travel));

        // Both inserted points have to sit inside the outgoing edge, or the detour
        // overshoots the next station instead of rejoining before it.
        double along = ((landing.X - vertex.X) * departure.X) + ((landing.Y - vertex.Y) * departure.Y);
        if (along <= StraightRunEpsilon || along >= departure.Length * StraightRunMaxLegFraction * 2)
        {
            return null;
        }

        return [stubEnd, landing];
    }
}
