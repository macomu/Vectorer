using SkiaSharp;

namespace Vectorer.Core.Geometry;

public enum PathNodeKind { Corner, Smooth, Symmetric }

public sealed class PathAnchor(SKPoint position)
{
    public SKPoint Position { get; set; } = position;
    public SKPoint? Incoming { get; set; }
    public SKPoint? Outgoing { get; set; }
    public PathNodeKind Kind { get; set; }
    public PathAnchor Clone() => new(Position) { Incoming = Incoming, Outgoing = Outgoing, Kind = Kind };
    public void MoveTo(SKPoint position)
    {
        var delta = position - Position;
        if (Incoming is { } a) Incoming = a + delta;
        if (Outgoing is { } b) Outgoing = b + delta;
        Position = position;
    }
    public void MoveHandle(bool incoming, SKPoint point)
    {
        var opposite = incoming ? Outgoing : Incoming;
        if (incoming) Incoming = point; else Outgoing = point;
        if (Kind == PathNodeKind.Corner) return;
        var vector = Position - point;
        if (Kind == PathNodeKind.Smooth && opposite is { } other)
        {
            var length = Length(vector);
            if (length > 0) vector = new SKPoint(vector.X * Length(other - Position) / length, vector.Y * Length(other - Position) / length);
        }
        if (incoming) Outgoing = Position + vector; else Incoming = Position + vector;
    }
    public static float Length(SKPoint point) => MathF.Sqrt(point.X * point.X + point.Y * point.Y);
}

public sealed class PathContour
{
    public List<PathAnchor> Anchors { get; } = [];
    public bool IsClosed { get; set; }
}

/// <summary>Editable cubic contours. Quadratic segments are converted exactly to cubic segments.</summary>
public sealed class EditablePath
{
    public List<PathContour> Contours { get; } = [];
    public SKPathFillType FillType { get; set; } = SKPathFillType.Winding;

    public static EditablePath Parse(string data)
    {
        using var path = string.IsNullOrWhiteSpace(data) ? new SKPath() : SKPath.ParseSvgPathData(data) ?? new SKPath();
        var result = new EditablePath { FillType = path.FillType };
        using var iterator = path.CreateRawIterator();
        var points = new SKPoint[4];
        PathContour? contour = null;
        SKPathVerb verb;
        while ((verb = iterator.Next(points)) != SKPathVerb.Done)
        {
            if (verb == SKPathVerb.Move)
            { contour = new PathContour(); contour.Anchors.Add(new(points[0])); result.Contours.Add(contour); continue; }
            if (contour is null || contour.Anchors.Count == 0) continue;
            var previous = contour.Anchors[^1];
            switch (verb)
            {
                case SKPathVerb.Line: contour.Anchors.Add(new(points[1])); break;
                case SKPathVerb.Cubic:
                    previous.Outgoing = points[1];
                    contour.Anchors.Add(new(points[3]) { Incoming = points[2] });
                    break;
                case SKPathVerb.Quad:
                    previous.Outgoing = Lerp(points[0], points[1], 2f / 3);
                    contour.Anchors.Add(new(points[2]) { Incoming = Lerp(points[2], points[1], 2f / 3) });
                    break;
                case SKPathVerb.Conic:
                    // SVG elliptical arcs use rational quadratics; sample those contours for editing.
                    var weight = iterator.ConicWeight();
                    for (var i = 1; i <= 16; i++)
                    {
                        var t = i / 16f;
                        var a = (1 - t) * (1 - t); var b = 2 * weight * t * (1 - t); var c = t * t;
                        contour.Anchors.Add(new(new SKPoint((a * points[0].X + b * points[1].X + c * points[2].X) / (a + b + c),
                            (a * points[0].Y + b * points[1].Y + c * points[2].Y) / (a + b + c))));
                    }
                    break;
                case SKPathVerb.Close:
                    contour.IsClosed = true;
                    if (contour.Anchors.Count > 1 && PathAnchor.Length(contour.Anchors[0].Position - contour.Anchors[^1].Position) < 0.0001f)
                    {
                        contour.Anchors[0].Incoming = contour.Anchors[^1].Incoming;
                        contour.Anchors.RemoveAt(contour.Anchors.Count - 1);
                    }
                    break;
            }
        }
        return result;
    }

    public SKPath BuildPath()
    {
        var path = new SKPath { FillType = FillType };
        foreach (var contour in Contours.Where(c => c.Anchors.Count > 0))
        {
            path.MoveTo(contour.Anchors[0].Position);
            var count = contour.IsClosed ? contour.Anchors.Count : contour.Anchors.Count - 1;
            for (var i = 0; i < count; i++)
            {
                var a = contour.Anchors[i]; var b = contour.Anchors[(i + 1) % contour.Anchors.Count];
                if (a.Outgoing is not null || b.Incoming is not null)
                    path.CubicTo(a.Outgoing ?? a.Position, b.Incoming ?? b.Position, b.Position);
                else path.LineTo(b.Position);
            }
            if (contour.IsClosed) path.Close();
        }
        return path;
    }

    public string ToSvgPathData() { using var path = BuildPath(); return path.ToSvgPathData(); }
    public EditablePath Clone()
    {
        var copy = new EditablePath { FillType = FillType };
        foreach (var contour in Contours)
        {
            var cloned = new PathContour { IsClosed = contour.IsClosed };
            cloned.Anchors.AddRange(contour.Anchors.Select(a => a.Clone())); copy.Contours.Add(cloned);
        }
        return copy;
    }
    public static SKPoint Lerp(SKPoint a, SKPoint b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    public static SKPoint Evaluate(PathAnchor a, PathAnchor b, float t)
    {
        if (a.Outgoing is null && b.Incoming is null) return Lerp(a.Position, b.Position, t);
        var p = Lerp(a.Position, a.Outgoing ?? a.Position, t);
        var q = Lerp(a.Outgoing ?? a.Position, b.Incoming ?? b.Position, t);
        var r = Lerp(b.Incoming ?? b.Position, b.Position, t);
        return Lerp(Lerp(p, q, t), Lerp(q, r, t), t);
    }

    public static PathAnchor SplitSegment(PathContour contour, int index, float t = 0.5f)
    {
        var a = contour.Anchors[index]; var b = contour.Anchors[(index + 1) % contour.Anchors.Count];
        PathAnchor added;
        if (a.Outgoing is null && b.Incoming is null) added = new(Lerp(a.Position, b.Position, t));
        else
        {
            var p = Lerp(a.Position, a.Outgoing ?? a.Position, t);
            var q = Lerp(a.Outgoing ?? a.Position, b.Incoming ?? b.Position, t);
            var r = Lerp(b.Incoming ?? b.Position, b.Position, t);
            var u = Lerp(p, q, t); var v = Lerp(q, r, t);
            added = new(Lerp(u, v, t)) { Incoming = u, Outgoing = v, Kind = PathNodeKind.Smooth };
            a.Outgoing = p; b.Incoming = r;
        }
        contour.Anchors.Insert(index + 1, added);
        return added;
    }

    public (PathContour Contour, int Segment, float T, float Distance)? NearestSegment(SKPoint point)
    {
        (PathContour Contour, int Segment, float T, float Distance)? best = null;
        foreach (var contour in Contours)
            for (var i = 0; i < (contour.IsClosed ? contour.Anchors.Count : contour.Anchors.Count - 1); i++)
                for (var step = 1; step < 100; step++)
                {
                    var t = step / 100f;
                    var distance = PathAnchor.Length(Evaluate(contour.Anchors[i], contour.Anchors[(i + 1) % contour.Anchors.Count], t) - point);
                    if (best is null || distance < best.Value.Distance) best = (contour, i, t, distance);
                }
        return best;
    }

    public void Cut(PathContour contour, int index)
    {
        if (contour.IsClosed)
        {
            var reordered = contour.Anchors.Skip(index).Concat(contour.Anchors.Take(index)).Select(a => a.Clone()).ToList();
            reordered.Add(reordered[0].Clone());
            contour.Anchors.Clear(); contour.Anchors.AddRange(reordered); contour.IsClosed = false;
        }
        else if (index > 0 && index < contour.Anchors.Count - 1)
        {
            var tail = new PathContour(); tail.Anchors.AddRange(contour.Anchors.Skip(index).Select(a => a.Clone()));
            contour.Anchors.RemoveRange(index + 1, contour.Anchors.Count - index - 1);
            Contours.Insert(Contours.IndexOf(contour) + 1, tail);
        }
    }

    public static List<SKPoint> Flatten(PathContour contour, float tolerance)
    {
        if (contour.Anchors.Count == 0) return [];
        var result = new List<SKPoint> { contour.Anchors[0].Position };
        static float DistanceToSegment(SKPoint p, SKPoint a, SKPoint b)
        {
            var v = b - a; var d = p - a; var length = v.X * v.X + v.Y * v.Y;
            var t = length == 0 ? 0 : Math.Clamp((d.X * v.X + d.Y * v.Y) / length, 0, 1);
            return PathAnchor.Length(p - Lerp(a, b, t));
        }
        for (var i = 0; i < (contour.IsClosed ? contour.Anchors.Count : contour.Anchors.Count - 1); i++)
        {
            var a = contour.Anchors[i]; var b = contour.Anchors[(i + 1) % contour.Anchors.Count];
            if (a.Outgoing is null && b.Incoming is null) { result.Add(b.Position); continue; }
            var stack = new Stack<(SKPoint A, SKPoint B, SKPoint C, SKPoint D, int Depth)>();
            stack.Push((a.Position, a.Outgoing ?? a.Position, b.Incoming ?? b.Position, b.Position, 0));
            while (stack.TryPop(out var c))
            {
                if (c.Depth >= 20 || MathF.Max(DistanceToSegment(c.B, c.A, c.D), DistanceToSegment(c.C, c.A, c.D)) <= MathF.Max(0.001f, tolerance))
                { result.Add(c.D); continue; }
                var p = Lerp(c.A, c.B, 0.5f); var q = Lerp(c.B, c.C, 0.5f); var r = Lerp(c.C, c.D, 0.5f);
                var u = Lerp(p, q, 0.5f); var v = Lerp(q, r, 0.5f); var middle = Lerp(u, v, 0.5f);
                stack.Push((middle, v, r, c.D, c.Depth + 1)); stack.Push((c.A, p, u, middle, c.Depth + 1));
            }
        }
        return result;
    }

    public static PathShapeNodeBuilder Builder(IReadOnlyList<SKPoint> points, float smoothing, bool closed) => new(points, smoothing, closed);
}

public sealed class PathShapeNodeBuilder(IReadOnlyList<SKPoint> points, float smoothing, bool closed)
{
    public string Build()
    {
        var simplified = Simplify(points, Math.Clamp(smoothing, 0, 100) / 20f);
        var model = new EditablePath(); var contour = new PathContour { IsClosed = closed };
        contour.Anchors.AddRange(simplified.Select(p => new PathAnchor(p))); model.Contours.Add(contour);
        if (smoothing > 0 && simplified.Count > 2)
        {
            var tension = Math.Clamp(smoothing / 100f, 0, 1) / 6;
            for (var i = 0; i < simplified.Count; i++)
            {
                var prev = simplified[closed ? (i + simplified.Count - 1) % simplified.Count : Math.Max(0, i - 1)];
                var next = simplified[closed ? (i + 1) % simplified.Count : Math.Min(simplified.Count - 1, i + 1)];
                var vector = new SKPoint((next.X - prev.X) * tension, (next.Y - prev.Y) * tension);
                contour.Anchors[i].Incoming = simplified[i] - vector;
                contour.Anchors[i].Outgoing = simplified[i] + vector;
            }
        }
        return model.ToSvgPathData();
    }

    public static List<SKPoint> Simplify(IReadOnlyList<SKPoint> input, float tolerance)
    {
        if (input.Count < 3 || tolerance <= 0) return input.ToList();
        var keep = new SortedSet<int> { 0, input.Count - 1 }; var stack = new Stack<(int, int)>(); stack.Push((0, input.Count - 1));
        while (stack.TryPop(out var range))
        {
            var start = input[range.Item1]; var end = input[range.Item2]; var v = end - start;
            var lengthSquared = v.X * v.X + v.Y * v.Y; var max = tolerance; var chosen = -1;
            for (var i = range.Item1 + 1; i < range.Item2; i++)
            {
                var d = input[i] - start;
                var t = lengthSquared == 0 ? 0 : Math.Clamp((d.X * v.X + d.Y * v.Y) / lengthSquared, 0, 1);
                var distance = PathAnchor.Length(input[i] - EditablePath.Lerp(start, end, t));
                if (distance > max) { max = distance; chosen = i; }
            }
            if (chosen < 0) continue;
            keep.Add(chosen); stack.Push((range.Item1, chosen)); stack.Push((chosen, range.Item2));
        }
        return keep.Select(i => input[i]).ToList();
    }
}
