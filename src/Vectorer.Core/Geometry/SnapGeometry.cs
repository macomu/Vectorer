using SkiaSharp;

namespace Vectorer.Core.Geometry;

public static class SnapGeometry
{
    public static SKPoint? Nearest(SKPoint point, IEnumerable<SKPoint> candidates, float tolerance)
    {
        SKPoint? result = null;
        var best = tolerance;
        foreach (var candidate in candidates)
        {
            var distance = PathAnchor.Length(candidate - point);
            if (distance <= best) { best = distance; result = candidate; }
        }
        return result;
    }
}
