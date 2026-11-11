using SkiaSharp;
using Vectorer.Abstractions.Documents;

namespace Vectorer.Core.Geometry;

public enum DrawingTool { Select, Rectangle, Ellipse, Arc, Polygon, Star, Freehand, Pan }

/// <summary>Document-space geometry shared by the interactive editor. No UI dependencies.</summary>
public static class CanvasGeometry
{
    public static SKPoint Snap(SKPoint point, float spacing = 20) => new(
        MathF.Round(point.X / spacing) * spacing, MathF.Round(point.Y / spacing) * spacing);

    public static SKRect DragBounds(SKPoint start, SKPoint end, bool square, bool fromCenter)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (square)
        {
            var size = MathF.Max(MathF.Abs(dx), MathF.Abs(dy));
            dx = MathF.CopySign(size, dx);
            dy = MathF.CopySign(size, dy);
        }
        var opposite = fromCenter ? new SKPoint(start.X - dx, start.Y - dy) : start;
        return new SKRect(MathF.Min(opposite.X, start.X + dx), MathF.Min(opposite.Y, start.Y + dy),
            MathF.Max(opposite.X, start.X + dx), MathF.Max(opposite.Y, start.Y + dy));
    }

    public static ShapeNode CreateShape(DrawingTool tool, SKPoint start, SKPoint end,
        bool square = false, bool fromCenter = false, float cornerRadius = 0, int sides = 5,
        float innerRatio = 0.45f, float startAngle = 0, float sweepAngle = 270,
        ArcCloseMode closeMode = ArcCloseMode.PieSlice)
    {
        var bounds = DragBounds(start, end, square, fromCenter);
        var center = new SKPoint(bounds.MidX, bounds.MidY);
        var radius = MathF.Sqrt(MathF.Pow(end.X - start.X, 2) + MathF.Pow(end.Y - start.Y, 2));
        var angle = MathF.Atan2(end.Y - start.Y, end.X - start.X) * 180 / MathF.PI;
        return tool switch
        {
            DrawingTool.Rectangle => new RectangleShapeNode { Rect = bounds, CornerRadiusX = cornerRadius, CornerRadiusY = cornerRadius },
            DrawingTool.Ellipse => new EllipseShapeNode { Center = center, RadiusX = bounds.Width / 2, RadiusY = bounds.Height / 2 },
            DrawingTool.Arc => new ArcShapeNode { Center = center, RadiusX = bounds.Width / 2, RadiusY = bounds.Height / 2,
                StartAngleDegrees = startAngle, SweepAngleDegrees = sweepAngle, CloseMode = closeMode },
            DrawingTool.Polygon => PolygonShapeNode.CreateRegularConvex(start, radius, Math.Clamp(sides, 3, 64), angle),
            DrawingTool.Star => PolygonShapeNode.CreateConcaveStar(start, radius, radius * Math.Clamp(innerRatio, 0.05f, 0.95f), Math.Clamp(sides, 3, 64), angle),
            _ => throw new ArgumentOutOfRangeException(nameof(tool))
        };
    }

    public static IEnumerable<(SceneNode Node, SKMatrix Parent)> EditableNodes(SvgDocument document)
    {
        foreach (var layer in document.Layers.Where(l => l.IsVisible && !l.IsLocked && l.Opacity > 0))
            foreach (var node in layer.Children.Where(n => n.IsVisible && !n.IsLocked && n.Opacity > 0))
                if (layer.Transform.TryInvert(out _))
                    yield return (node, layer.Transform);
    }

    public static SKPath BuildWorldPath(SceneNode node, SKMatrix parent)
    {
        var world = SKMatrix.Concat(parent, node.Transform);
        if (node is ShapeNode shape)
        {
            var path = shape.BuildLocalPath();
            path.Transform(world);
            return path;
        }
        var result = new SKPath();
        if (node is ContainerNode container)
            foreach (var child in container.Children.Where(c => c.IsVisible && c.Opacity > 0))
            {
                using var path = BuildWorldPath(child, world);
                result.AddPath(path);
            }
        return result;
    }

    public static bool HitTest(SceneNode node, SKMatrix parent, SKPoint point, float tolerance)
    {
        var world = SKMatrix.Concat(parent, node.Transform);
        if (node is ContainerNode container)
            return container.Children.Where(c => c.IsVisible && c.Opacity > 0).Reverse()
                .Any(c => HitTest(c, world, point, tolerance));
        if (node is not ShapeNode shape || !world.TryInvert(out var inverse)) return false;
        var local = inverse.MapPoint(point);
        using var path = shape.BuildLocalPath();
        if (shape.Style.Fill is not null && path.Contains(local.X, local.Y)) return true;
        using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = shape.Style.Stroke?.Width ?? 0,
            StrokeCap = shape.Style.Stroke?.Cap ?? SKStrokeCap.Round, StrokeJoin = shape.Style.Stroke?.Join ?? SKStrokeJoin.Round };
        using var outline = new SKPath();
        if (shape.Style.Stroke is { IsVisible: true } && paint.GetFillPath(path, outline))
        {
            outline.Transform(world);
            if (outline.Contains(point.X, point.Y)) return true;
        }
        // A small screen-space tolerance also makes open and hairline paths selectable.
        path.Transform(world);
        using var hitPaint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = tolerance * 2 };
        using var hitOutline = new SKPath();
        return hitPaint.GetFillPath(path, hitOutline) && hitOutline.Contains(point.X, point.Y);
    }

    public static SKMatrix ApplyWorldTransform(SKMatrix original, SKMatrix parent, SKMatrix delta)
    {
        if (!parent.TryInvert(out var inverse)) return original;
        return SKMatrix.Concat(inverse, SKMatrix.Concat(delta, SKMatrix.Concat(parent, original)));
    }
}
