using Vectorer.Abstractions.Documents;

namespace Vectorer.Core;

public static class SceneNodeCloner
{
    public static SceneNode Clone(SceneNode source, bool preserveIds = false)
    {
        SceneNode result = source switch
        {
            LayerNode => new LayerNode(), GroupNode => new GroupNode(),
            RectangleShapeNode r => new RectangleShapeNode { Rect = r.Rect, CornerRadiusX = r.CornerRadiusX, CornerRadiusY = r.CornerRadiusY },
            EllipseShapeNode e => new EllipseShapeNode { Center = e.Center, RadiusX = e.RadiusX, RadiusY = e.RadiusY },
            ArcShapeNode a => new ArcShapeNode { Center = a.Center, RadiusX = a.RadiusX, RadiusY = a.RadiusY, StartAngleDegrees = a.StartAngleDegrees, SweepAngleDegrees = a.SweepAngleDegrees, CloseMode = a.CloseMode },
            PolygonShapeNode p => new PolygonShapeNode { Points = p.Points.ToList(), IsClosed = p.IsClosed, PolygonKind = p.PolygonKind,
                Center = p.Center, PointCount = p.PointCount, OuterRadius = p.OuterRadius, InnerRadius = p.InnerRadius, StartAngleDegrees = p.StartAngleDegrees },
            PathShapeNode p => new PathShapeNode(p.SvgPathData) { NodeKinds = p.NodeKinds, FillType = p.FillType },
            TextShapeNode t => new TextShapeNode { Text = t.Text, Position = t.Position, FontFamily = t.FontFamily, FontSize = t.FontSize, IsBold = t.IsBold, IsItalic = t.IsItalic, TextAlign = t.TextAlign, LetterSpacing = t.LetterSpacing },
            _ => throw new NotSupportedException($"Unsupported node: {source.GetType().Name}")
        };
        if (preserveIds) result.Id = source.Id;
        result.Name = source.Name; result.IsVisible = source.IsVisible; result.IsLocked = source.IsLocked;
        result.Opacity = source.Opacity; result.Transform = source.Transform; result.BlendMode = source.BlendMode; result.Filter = source.Filter;
        result.ShaderEffect = source.ShaderEffect is { } effect ? effect with { Uniforms = effect.Uniforms?.ToDictionary(p => p.Key, p => p.Value) } : null;
        foreach (var (key, value) in source.CustomAttributes) result.CustomAttributes[key] = value;
        if (source is ShapeNode shape && result is ShapeNode clone)
        { clone.Style = shape.Style.Clone(); if (clone.Style.Stroke is { } stroke) clone.Style.Stroke = stroke with { DashArray = stroke.DashArray?.ToArray() }; }
        if (source is ContainerNode container && result is ContainerNode copy) copy.Children.AddRange(container.Children.Select(n => Clone(n, preserveIds)));
        return result;
    }
}
