using System;
using System.Collections.Generic;
using System.Linq;

namespace MetroDiagram.Engine
{
    public sealed class IntercityRailPatternOptions
    {
        private const double RouteWidthRatio = 0.72;

        public double MarkLength { get; set; }

        public double GapLength { get; set; }

        public double MarkWidth { get; set; }

        public static double ResolveRouteBaseWidth(double routeWidth)
        {
            if (!IntercityGeographicGeometry.IsFinite(routeWidth) || routeWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(routeWidth));
            }

            return Math.Max(5.5, routeWidth * RouteWidthRatio);
        }

        public static double ResolveLegendBaseWidth(double sampleWidth)
        {
            if (!IntercityGeographicGeometry.IsFinite(sampleWidth) || sampleWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleWidth));
            }

            return Math.Max(3.5, sampleWidth * RouteWidthRatio);
        }

        public static IntercityRailPatternOptions FromBaseWidth(double baseWidth)
        {
            if (!IntercityGeographicGeometry.IsFinite(baseWidth) || baseWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(baseWidth));
            }

            return new IntercityRailPatternOptions
            {
                MarkLength = Math.Max(6, baseWidth * 0.72),
                GapLength = Math.Max(7, baseWidth * 0.92),
                MarkWidth = Math.Max(1.8, baseWidth * 0.32)
            };
        }
    }

    public readonly struct IntercityRailMark
    {
        public IntercityRailMark(
            IntercityCanvasPoint start,
            IntercityCanvasPoint end,
            double distanceAlongRoute)
        {
            Start = start;
            End = end;
            DistanceAlongRoute = distanceAlongRoute;
        }

        public IntercityCanvasPoint Start { get; }

        public IntercityCanvasPoint End { get; }

        public double DistanceAlongRoute { get; }
    }

    public static class IntercityRailPattern
    {
        public static IReadOnlyList<IntercityRailMark> BuildInlineMarks(
            IReadOnlyList<IntercityCanvasPoint> points,
            IntercityRailPatternOptions options)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            Validate(options);

            List<IntercityCanvasPoint> finitePoints = points
                .Where(point => IntercityGeographicGeometry.IsFinite(point.X)
                    && IntercityGeographicGeometry.IsFinite(point.Y))
                .ToList();
            if (finitePoints.Count < 2)
            {
                return Array.Empty<IntercityRailMark>();
            }

            List<double> segmentLengths = new List<double>(finitePoints.Count - 1);
            double totalLength = 0;
            for (int i = 1; i < finitePoints.Count; i++)
            {
                double length = Distance(finitePoints[i - 1], finitePoints[i]);
                segmentLengths.Add(length);
                totalLength += length;
            }

            int markCount = (int)Math.Floor(
                (totalLength + options.GapLength)
                / (options.MarkLength + options.GapLength));
            if (markCount <= 0)
            {
                return Array.Empty<IntercityRailMark>();
            }

            double usedLength = markCount * options.MarkLength
                + Math.Max(0, markCount - 1) * options.GapLength;
            double endMargin = Math.Max(0, (totalLength - usedLength) / 2);
            double firstCenter = endMargin + options.MarkLength / 2;
            double period = options.MarkLength + options.GapLength;
            List<IntercityRailMark> marks = new List<IntercityRailMark>(markCount);

            for (int i = 0; i < markCount; i++)
            {
                double distance = firstCenter + i * period;
                IntercityCanvasPoint center;
                IntercityCanvasPoint direction;
                if (!TrySample(finitePoints, segmentLengths, distance, out center, out direction))
                {
                    continue;
                }

                double halfLength = options.MarkLength / 2;
                marks.Add(new IntercityRailMark(
                    new IntercityCanvasPoint(
                        center.X - direction.X * halfLength,
                        center.Y - direction.Y * halfLength),
                    new IntercityCanvasPoint(
                        center.X + direction.X * halfLength,
                        center.Y + direction.Y * halfLength),
                    distance));
            }

            return marks;
        }

        private static bool TrySample(
            IReadOnlyList<IntercityCanvasPoint> points,
            IReadOnlyList<double> segmentLengths,
            double targetDistance,
            out IntercityCanvasPoint point,
            out IntercityCanvasPoint direction)
        {
            double traversed = 0;
            for (int i = 0; i < segmentLengths.Count; i++)
            {
                double length = segmentLengths[i];
                if (length <= 1e-9)
                {
                    continue;
                }

                if (targetDistance <= traversed + length + 1e-9)
                {
                    IntercityCanvasPoint start = points[i];
                    IntercityCanvasPoint end = points[i + 1];
                    double t = Math.Max(0, Math.Min(1, (targetDistance - traversed) / length));
                    point = new IntercityCanvasPoint(
                        start.X + (end.X - start.X) * t,
                        start.Y + (end.Y - start.Y) * t);
                    direction = new IntercityCanvasPoint(
                        (end.X - start.X) / length,
                        (end.Y - start.Y) / length);
                    return true;
                }

                traversed += length;
            }

            point = default(IntercityCanvasPoint);
            direction = default(IntercityCanvasPoint);
            return false;
        }

        private static double Distance(IntercityCanvasPoint first, IntercityCanvasPoint second)
        {
            double dx = second.X - first.X;
            double dy = second.Y - first.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static void Validate(IntercityRailPatternOptions options)
        {
            if (!IntercityGeographicGeometry.IsFinite(options.MarkLength)
                || options.MarkLength <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Rail mark length must be finite and positive.");
            }

            if (!IntercityGeographicGeometry.IsFinite(options.GapLength)
                || options.GapLength < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Rail mark gap must be finite and non-negative.");
            }

            if (!IntercityGeographicGeometry.IsFinite(options.MarkWidth)
                || options.MarkWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Rail mark width must be finite and positive.");
            }
        }
    }
}
