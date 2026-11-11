using SkiaSharp;

namespace Vectorer.Core.Geometry;

/// <summary>Fits cubic segments to adaptively flattened contours, retaining endpoints and sharp corners.</summary>
public static class BezierPathFitter
{
    public static void Simplify(EditablePath model, float tolerance)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(tolerance, 0);
        foreach (var contour in model.Contours)
        {
            var samples = EditablePath.Flatten(contour, tolerance / 8);
            if (samples.Count < 3) continue;
            // Keep the original sampling for tangent estimation; RDP supplies stable corner candidates.
            var corners = new SortedSet<int> { 0, samples.Count - 1 };
            for (var i = 1; i < samples.Count - 1; i++)
            {
                var a = Unit(samples[i] - samples[i - 1]); var b = Unit(samples[i + 1] - samples[i]);
                if (Dot(a, b) < 0.5f) corners.Add(i);
            }
            if (contour.IsClosed) corners.Add(samples.Count / 2);
            var output = new List<PathAnchor> { new(samples[0]) };
            var breaks = corners.ToArray();
            for (var i = 1; i < breaks.Length; i++) Fit(samples, breaks[i - 1], breaks[i], tolerance * 0.7f, output, 0);
            if (contour.IsClosed && output.Count > 1)
            { output[0].Incoming = output[^1].Incoming; output.RemoveAt(output.Count - 1); }
            foreach (var anchor in output)
                if (anchor.Incoming is { } incoming && anchor.Outgoing is { } outgoing)
                {
                    var a = Unit(incoming - anchor.Position); var b = Unit(outgoing - anchor.Position);
                    if (Dot(a, b) < -0.9999f) anchor.Kind = PathNodeKind.Smooth;
                }
            contour.Anchors.Clear(); contour.Anchors.AddRange(output);
        }
    }

    private static void Fit(List<SKPoint> points, int start, int end, float tolerance, List<PathAnchor> output, int depth, SKPoint? leftHint = null, SKPoint? rightHint = null)
    {
        if (end <= start) return;
        if (end == start + 1) { output.Add(new(points[end])); return; }
        var p0 = points[start]; var p3 = points[end];
        var left = leftHint ?? Unit(points[start + 1] - p0); var right = rightHint ?? Unit(points[end - 1] - p3);
        var length = new float[end - start + 1];
        for (var i = 1; i < length.Length; i++) length[i] = length[i - 1] + PathAnchor.Length(points[start + i] - points[start + i - 1]);
        if (length[^1] < 0.00001f) { output.Add(new(p3)); return; }
        float aa = 0, ab = 0, bb = 0, ar = 0, br = 0;
        for (var i = 1; i < length.Length - 1; i++)
        {
            var t = length[i] / length[^1]; var u = 1 - t;
            var a = Scale(left, 3 * u * u * t); var b = Scale(right, 3 * u * t * t);
            var baseline = Scale(p0, u * u * u + 3 * u * u * t) + Scale(p3, t * t * t + 3 * u * t * t);
            var residual = points[start + i] - baseline;
            aa += Dot(a, a); ab += Dot(a, b); bb += Dot(b, b); ar += Dot(a, residual); br += Dot(b, residual);
        }
        var determinant = aa * bb - ab * ab;
        var alpha = determinant > 0.000001f ? (ar * bb - br * ab) / determinant : length[^1] / 3;
        var beta = determinant > 0.000001f ? (br * aa - ar * ab) / determinant : length[^1] / 3;
        if (alpha < 0 || beta < 0 || alpha > length[^1] * 2 || beta > length[^1] * 2) alpha = beta = length[^1] / 3;
        var aNode = new PathAnchor(p0) { Outgoing = p0 + Scale(left, alpha) };
        var bNode = new PathAnchor(p3) { Incoming = p3 + Scale(right, beta) };
        var error = 0f; var split = (start + end) / 2;
        for (var i = 1; i < length.Length - 1; i++)
        {
            var distance = PathAnchor.Length(EditablePath.Evaluate(aNode, bNode, length[i] / length[^1]) - points[start + i]);
            if (distance > error) { error = distance; split = start + i; }
        }
        // Also test the reverse direction, preventing bulges between sparse samples.
        var steps = Math.Clamp((int)MathF.Ceiling(length[^1] / MathF.Max(tolerance, 0.01f)), 16, 2048);
        for (var step = 1; error <= tolerance && step < steps; step++)
        {
            var point = EditablePath.Evaluate(aNode, bNode, step / (float)steps);
            var nearest = float.MaxValue;
            for (var j = start; j < end; j++) nearest = Math.Min(nearest, SegmentDistance(point, points[j], points[j + 1]));
            error = Math.Max(error, nearest);
        }
        if (error <= tolerance)
        {
            output[^1].Outgoing = aNode.Outgoing; output.Add(bNode); return;
        }
        if (depth >= 20)
        { for (var i = start + 1; i <= end; i++) output.Add(new(points[i])); return; }
        split = Math.Clamp(split, start + 1, end - 1);
        var tangent = Unit(points[split + 1] - points[split - 1]);
        Fit(points, start, split, tolerance, output, depth + 1, left, Scale(tangent, -1));
        Fit(points, split, end, tolerance, output, depth + 1, tangent, right);
    }
    private static SKPoint Unit(SKPoint p) { var length = PathAnchor.Length(p); return length > 0 ? Scale(p, 1 / length) : new(1, 0); }
    private static float Dot(SKPoint a, SKPoint b) => a.X * b.X + a.Y * b.Y;
    private static SKPoint Scale(SKPoint p, float scale) => new(p.X * scale, p.Y * scale);
    private static float SegmentDistance(SKPoint p, SKPoint a, SKPoint b)
    { var v = b - a; var squared = Dot(v, v); return PathAnchor.Length(p - EditablePath.Lerp(a, b, squared > 0 ? Math.Clamp(Dot(p - a, v) / squared, 0, 1) : 0)); }
}
