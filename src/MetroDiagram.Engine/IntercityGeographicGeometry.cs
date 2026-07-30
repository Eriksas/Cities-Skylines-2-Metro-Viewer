using System;
using System.Collections.Generic;
using System.Linq;

namespace MetroDiagram.Engine
{
    public struct GeographicPoint : IEquatable<GeographicPoint>
    {
        public GeographicPoint(double x, double z)
        {
            X = x;
            Z = z;
        }

        public double X { get; set; }

        public double Z { get; set; }

        public bool IsFinite => IntercityGeographicGeometry.IsFinite(X)
            && IntercityGeographicGeometry.IsFinite(Z);

        public bool Equals(GeographicPoint other)
        {
            return X.Equals(other.X) && Z.Equals(other.Z);
        }

        public override bool Equals(object? obj)
        {
            return obj is GeographicPoint other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Z.GetHashCode();
            }
        }

        public static bool operator ==(GeographicPoint left, GeographicPoint right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(GeographicPoint left, GeographicPoint right)
        {
            return !left.Equals(right);
        }
    }

    public readonly struct GeographicEnvelope
    {
        public GeographicEnvelope(double minX, double minZ, double maxX, double maxZ)
        {
            MinX = minX;
            MinZ = minZ;
            MaxX = maxX;
            MaxZ = maxZ;
        }

        public double MinX { get; }

        public double MinZ { get; }

        public double MaxX { get; }

        public double MaxZ { get; }

        public double Width => Math.Max(0, MaxX - MinX);

        public double Height => Math.Max(0, MaxZ - MinZ);

        public bool Contains(GeographicPoint point, double epsilon = 0)
        {
            return point.X >= MinX - epsilon
                && point.X <= MaxX + epsilon
                && point.Z >= MinZ - epsilon
                && point.Z <= MaxZ + epsilon;
        }
    }

    public sealed class IntercityGeographicGeometryOptions
    {
        public double EnvelopeMarginRatio { get; set; } = 0.12;

        public double MinimumMargin { get; set; } = 250;

        public double DuplicatePointEpsilon { get; set; } = 0.01;
    }

    public readonly struct IntercityContinuationMarker
    {
        public IntercityContinuationMarker(GeographicPoint position, GeographicPoint outwardDirection)
        {
            Position = position;
            OutwardDirection = outwardDirection;
        }

        public GeographicPoint Position { get; }

        public GeographicPoint OutwardDirection { get; }
    }

    public sealed class IntercityClippedGeometry
    {
        public IntercityClippedGeometry(
            IReadOnlyList<IReadOnlyList<GeographicPoint>> polylines,
            IReadOnlyList<IntercityContinuationMarker> continuations,
            GeographicEnvelope envelope,
            int sourcePointCount,
            int clippedSegmentCount)
        {
            Polylines = polylines;
            Continuations = continuations;
            Envelope = envelope;
            SourcePointCount = sourcePointCount;
            ClippedSegmentCount = clippedSegmentCount;
        }

        public IReadOnlyList<IReadOnlyList<GeographicPoint>> Polylines { get; }

        public IReadOnlyList<IntercityContinuationMarker> Continuations { get; }

        public GeographicEnvelope Envelope { get; }

        public int SourcePointCount { get; }

        public int ClippedSegmentCount { get; }
    }

    public readonly struct IntercityCanvasViewport
    {
        public IntercityCanvasViewport(
            double width,
            double height,
            double paddingLeft,
            double paddingTop,
            double paddingRight,
            double paddingBottom)
        {
            Width = width;
            Height = height;
            PaddingLeft = paddingLeft;
            PaddingTop = paddingTop;
            PaddingRight = paddingRight;
            PaddingBottom = paddingBottom;
        }

        public double Width { get; }

        public double Height { get; }

        public double PaddingLeft { get; }

        public double PaddingTop { get; }

        public double PaddingRight { get; }

        public double PaddingBottom { get; }
    }

    public readonly struct IntercityCanvasPoint
    {
        public IntercityCanvasPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }
    }

    public readonly struct IntercityProjectedContinuation
    {
        public IntercityProjectedContinuation(
            IntercityCanvasPoint position,
            IntercityCanvasPoint outwardDirection)
        {
            Position = position;
            OutwardDirection = outwardDirection;
        }

        public IntercityCanvasPoint Position { get; }

        public IntercityCanvasPoint OutwardDirection { get; }
    }

    public sealed class IntercityProjectedGeometry
    {
        public IntercityProjectedGeometry(
            IReadOnlyList<IReadOnlyList<IntercityCanvasPoint>> polylines,
            IReadOnlyList<IntercityProjectedContinuation> continuations,
            double scale,
            double offsetX,
            double offsetY)
        {
            Polylines = polylines;
            Continuations = continuations;
            Scale = scale;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public IReadOnlyList<IReadOnlyList<IntercityCanvasPoint>> Polylines { get; }

        public IReadOnlyList<IntercityProjectedContinuation> Continuations { get; }

        public double Scale { get; }

        public double OffsetX { get; }

        public double OffsetY { get; }
    }

    public static class IntercityGeographicGeometry
    {
        public static GeographicEnvelope CreateCityEnvelope(
            IReadOnlyList<GeographicPoint> cityAnchors,
            IntercityGeographicGeometryOptions? options = null)
        {
            if (cityAnchors == null)
            {
                throw new ArgumentNullException(nameof(cityAnchors));
            }

            options = options ?? new IntercityGeographicGeometryOptions();
            ValidateOptions(options);

            List<GeographicPoint> finiteAnchors = cityAnchors.Where(point => point.IsFinite).ToList();
            if (finiteAnchors.Count == 0)
            {
                throw new ArgumentException("At least one finite city anchor is required.", nameof(cityAnchors));
            }

            return CreateEnvelope(finiteAnchors, options);
        }

        public static IntercityClippedGeometry ClipToCityEnvelope(
            IReadOnlyList<GeographicPoint> routePoints,
            IReadOnlyList<GeographicPoint> cityAnchors,
            IntercityGeographicGeometryOptions? options = null)
        {
            if (routePoints == null)
            {
                throw new ArgumentNullException(nameof(routePoints));
            }

            if (cityAnchors == null)
            {
                throw new ArgumentNullException(nameof(cityAnchors));
            }

            options = options ?? new IntercityGeographicGeometryOptions();
            ValidateOptions(options);

            GeographicEnvelope envelope = CreateCityEnvelope(cityAnchors, options);
            List<IReadOnlyList<GeographicPoint>> polylines = new List<IReadOnlyList<GeographicPoint>>();
            List<IntercityContinuationMarker> continuations = new List<IntercityContinuationMarker>();
            List<GeographicPoint>? current = null;
            int clippedSegmentCount = 0;

            for (int i = 1; i < routePoints.Count; i++)
            {
                GeographicPoint start = routePoints[i - 1];
                GeographicPoint end = routePoints[i];
                if (!start.IsFinite || !end.IsFinite || Distance(start, end) <= options.DuplicatePointEpsilon)
                {
                    CompleteCurrentPolyline(polylines, ref current);
                    continue;
                }

                GeographicPoint clippedStart;
                GeographicPoint clippedEnd;
                double startT;
                double endT;
                if (!TryClipSegment(start, end, envelope, out clippedStart, out clippedEnd, out startT, out endT))
                {
                    CompleteCurrentPolyline(polylines, ref current);
                    continue;
                }

                bool clippedAtStart = startT > 1e-9;
                bool clippedAtEnd = endT < 1 - 1e-9;
                if (clippedAtStart || clippedAtEnd)
                {
                    clippedSegmentCount++;
                }

                if (current == null)
                {
                    current = new List<GeographicPoint>();
                }

                if (current.Count > 0
                    && Distance(current[current.Count - 1], clippedStart) > options.DuplicatePointEpsilon)
                {
                    CompleteCurrentPolyline(polylines, ref current);
                    current = new List<GeographicPoint>();
                }

                AppendDistinct(current, clippedStart, options.DuplicatePointEpsilon);
                AppendDistinct(current, clippedEnd, options.DuplicatePointEpsilon);

                if (clippedAtStart)
                {
                    AddContinuation(
                        continuations,
                        new IntercityContinuationMarker(
                            clippedStart,
                            Normalize(start.X - clippedStart.X, start.Z - clippedStart.Z)),
                        options.DuplicatePointEpsilon);
                }

                if (clippedAtEnd)
                {
                    AddContinuation(
                        continuations,
                        new IntercityContinuationMarker(
                            clippedEnd,
                            Normalize(end.X - clippedEnd.X, end.Z - clippedEnd.Z)),
                        options.DuplicatePointEpsilon);
                    CompleteCurrentPolyline(polylines, ref current);
                }
            }

            CompleteCurrentPolyline(polylines, ref current);

            return new IntercityClippedGeometry(
                polylines,
                continuations,
                envelope,
                routePoints.Count,
                clippedSegmentCount);
        }

        public static IntercityProjectedGeometry Project(
            IntercityClippedGeometry geometry,
            IntercityCanvasViewport viewport)
        {
            if (geometry == null)
            {
                throw new ArgumentNullException(nameof(geometry));
            }

            if (!IsFinite(viewport.Width)
                || !IsFinite(viewport.Height)
                || viewport.Width <= 0
                || viewport.Height <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(viewport),
                    "Canvas width and height must be finite and positive.");
            }

            double availableWidth = viewport.Width - viewport.PaddingLeft - viewport.PaddingRight;
            double availableHeight = viewport.Height - viewport.PaddingTop - viewport.PaddingBottom;
            if (!IsFinite(availableWidth)
                || !IsFinite(availableHeight)
                || availableWidth <= 0
                || availableHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(viewport), "Canvas padding leaves no drawable area.");
            }

            double sourceWidth = Math.Max(geometry.Envelope.Width, 1);
            double sourceHeight = Math.Max(geometry.Envelope.Height, 1);
            double scale = Math.Min(availableWidth / sourceWidth, availableHeight / sourceHeight);
            double scaledWidth = geometry.Envelope.Width * scale;
            double scaledHeight = geometry.Envelope.Height * scale;
            double offsetX = viewport.PaddingLeft + (availableWidth - scaledWidth) / 2;
            double offsetY = viewport.PaddingTop + (availableHeight - scaledHeight) / 2;

            Func<GeographicPoint, IntercityCanvasPoint> projectPoint = point =>
                new IntercityCanvasPoint(
                    offsetX + (point.X - geometry.Envelope.MinX) * scale,
                    offsetY + (geometry.Envelope.MaxZ - point.Z) * scale);

            List<IReadOnlyList<IntercityCanvasPoint>> polylines = geometry.Polylines
                .Select(polyline => (IReadOnlyList<IntercityCanvasPoint>)polyline.Select(projectPoint).ToList())
                .ToList();
            List<IntercityProjectedContinuation> continuations = geometry.Continuations
                .Select(marker => new IntercityProjectedContinuation(
                    projectPoint(marker.Position),
                    NormalizeCanvas(marker.OutwardDirection)))
                .ToList();

            return new IntercityProjectedGeometry(polylines, continuations, scale, offsetX, offsetY);
        }

        internal static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static GeographicEnvelope CreateEnvelope(
            IReadOnlyList<GeographicPoint> anchors,
            IntercityGeographicGeometryOptions options)
        {
            double minX = anchors.Min(point => point.X);
            double maxX = anchors.Max(point => point.X);
            double minZ = anchors.Min(point => point.Z);
            double maxZ = anchors.Max(point => point.Z);
            double width = maxX - minX;
            double height = maxZ - minZ;
            double diagonal = Math.Sqrt(width * width + height * height);
            double margin = Math.Max(options.MinimumMargin, diagonal * options.EnvelopeMarginRatio);

            return new GeographicEnvelope(minX - margin, minZ - margin, maxX + margin, maxZ + margin);
        }

        private static bool TryClipSegment(
            GeographicPoint start,
            GeographicPoint end,
            GeographicEnvelope envelope,
            out GeographicPoint clippedStart,
            out GeographicPoint clippedEnd,
            out double startT,
            out double endT)
        {
            double dx = end.X - start.X;
            double dz = end.Z - start.Z;
            startT = 0;
            endT = 1;

            if (!Clip(-dx, start.X - envelope.MinX, ref startT, ref endT)
                || !Clip(dx, envelope.MaxX - start.X, ref startT, ref endT)
                || !Clip(-dz, start.Z - envelope.MinZ, ref startT, ref endT)
                || !Clip(dz, envelope.MaxZ - start.Z, ref startT, ref endT))
            {
                clippedStart = default(GeographicPoint);
                clippedEnd = default(GeographicPoint);
                return false;
            }

            clippedStart = new GeographicPoint(start.X + dx * startT, start.Z + dz * startT);
            clippedEnd = new GeographicPoint(start.X + dx * endT, start.Z + dz * endT);
            return true;
        }

        private static bool Clip(double p, double q, ref double startT, ref double endT)
        {
            if (Math.Abs(p) <= 1e-12)
            {
                return q >= 0;
            }

            double ratio = q / p;
            if (p < 0)
            {
                if (ratio > endT)
                {
                    return false;
                }

                startT = Math.Max(startT, ratio);
            }
            else
            {
                if (ratio < startT)
                {
                    return false;
                }

                endT = Math.Min(endT, ratio);
            }

            return true;
        }

        private static void AppendDistinct(
            List<GeographicPoint> points,
            GeographicPoint point,
            double epsilon)
        {
            if (points.Count == 0 || Distance(points[points.Count - 1], point) > epsilon)
            {
                points.Add(point);
            }
        }

        private static void CompleteCurrentPolyline(
            List<IReadOnlyList<GeographicPoint>> polylines,
            ref List<GeographicPoint>? current)
        {
            if (current != null && current.Count >= 2)
            {
                polylines.Add(current);
            }

            current = null;
        }

        private static void AddContinuation(
            List<IntercityContinuationMarker> continuations,
            IntercityContinuationMarker marker,
            double epsilon)
        {
            if (marker.OutwardDirection == default(GeographicPoint))
            {
                return;
            }

            bool duplicate = continuations.Any(
                existing => Distance(existing.Position, marker.Position) <= epsilon);
            if (!duplicate)
            {
                continuations.Add(marker);
            }
        }

        private static GeographicPoint Normalize(double x, double z)
        {
            double length = Math.Sqrt(x * x + z * z);
            return length <= 1e-12
                ? default(GeographicPoint)
                : new GeographicPoint(x / length, z / length);
        }

        private static IntercityCanvasPoint NormalizeCanvas(GeographicPoint direction)
        {
            double length = Math.Sqrt(
                direction.X * direction.X + direction.Z * direction.Z);
            return length <= 1e-12
                ? default(IntercityCanvasPoint)
                : new IntercityCanvasPoint(direction.X / length, -direction.Z / length);
        }

        private static double Distance(GeographicPoint first, GeographicPoint second)
        {
            double dx = first.X - second.X;
            double dz = first.Z - second.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        }

        private static void ValidateOptions(IntercityGeographicGeometryOptions options)
        {
            if (!IsFinite(options.EnvelopeMarginRatio) || options.EnvelopeMarginRatio < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Envelope margin ratio must be finite and non-negative.");
            }

            if (!IsFinite(options.MinimumMargin) || options.MinimumMargin < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Minimum margin must be finite and non-negative.");
            }

            if (!IsFinite(options.DuplicatePointEpsilon) || options.DuplicatePointEpsilon < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options),
                    "Duplicate point epsilon must be finite and non-negative.");
            }
        }
    }
}
