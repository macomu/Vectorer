using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;

namespace Vectorer.Core.Geometry;

public static class ShapePathConverter
{
    /// <summary>Converts local geometry, retaining the transform and presentation properties.</summary>
    public static PathShapeNode? Convert(ShapeNode source, bool strokeOnly = false)
    {
        using var path = source.BuildLocalPath();
        using var outline = new SKPath();
        var style = source.Style.Clone();
        if (strokeOnly)
        {
            if (source.Style.Stroke is not { IsVisible: true } stroke) return null;
            using var dash = stroke.DashArray is { Length: > 0 } array ? SKPathEffect.CreateDash(array, stroke.DashPhase) : null;
            using var paint = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = stroke.Width,
                StrokeCap = stroke.Cap, StrokeJoin = stroke.Join, StrokeMiter = stroke.MiterLimit, PathEffect = dash };
            if (!paint.GetFillPath(path, outline) || outline.IsEmpty) return null;
            style.Fill = new SolidColorFill(stroke.Color);
            style.Stroke = null;
        }
        var result = new PathShapeNode((strokeOnly ? outline : path).ToSvgPathData())
        {
            FillType = (strokeOnly ? outline : path).FillType,
            NodeKinds = !strokeOnly && source is PathShapeNode existing ? existing.NodeKinds : "",
            Id = source.Id, Name = source.Name, Transform = source.Transform, Style = style,
            IsLocked = source.IsLocked, IsVisible = source.IsVisible, Opacity = source.Opacity,
            BlendMode = source.BlendMode, Filter = source.Filter, ShaderEffect = source.ShaderEffect
        };
        foreach (var (key, value) in source.CustomAttributes) result.CustomAttributes[key] = value;
        return result;
    }
}
