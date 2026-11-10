using SkiaSharp;
using Vectorer.Abstractions.Styling;

namespace Vectorer.Abstractions.Documents;

/// <summary>
/// Represents a complete Vectorer SVG document containing layers, metadata, and canvas settings.
/// </summary>
public sealed class SvgDocument
{
    public string Title { get; set; } = "Untitled";
    public float Width { get; set; } = 800f;
    public float Height { get; set; } = 600f;
    public SKRect ViewBox { get; set; } = new(0, 0, 800, 600);
    public SKColor BackgroundColor { get; set; } = SKColors.Transparent;
    public List<LayerNode> Layers { get; } = [];

    public static SvgDocument CreateDefault(float width = 800f, float height = 600f)
    {
        var doc = new SvgDocument
        {
            Width = width,
            Height = height,
            ViewBox = new SKRect(0, 0, width, height)
        };
        doc.Layers.Add(new LayerNode { Name = "Layer 1" });
        return doc;
    }

    public SceneNode? FindNodeById(string id)
    {
        foreach (var layer in Layers)
        {
            if (layer.Id == id)
            {
                return layer;
            }

            var found = layer.FindNodeById(id);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }
}

/// <summary>
/// Base class for all elements in the SVG scene graph.
/// </summary>
public abstract class SceneNode
{
    public string Id { get; set; } = $"node_{Guid.NewGuid():N}"[..13];
    public string Name { get; set; } = "Element";
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public float Opacity { get; set; } = 1.0f;
    public SKMatrix Transform { get; set; } = SKMatrix.CreateIdentity();
    public LayerBlendMode BlendMode { get; set; } = LayerBlendMode.Normal;
    public FilterSettings? Filter { get; set; }
    public ShaderEffectDescriptor? ShaderEffect { get; set; }
    public Dictionary<string, string> CustomAttributes { get; } = [];

    /// <summary>
    /// Computes the bounding box of the node in world (document) coordinates.
    /// </summary>
    public abstract SKRect GetWorldBounds();
}

/// <summary>
/// Base class for container nodes that hold child nodes (Layers and Groups).
/// </summary>
public abstract class ContainerNode : SceneNode
{
    public List<SceneNode> Children { get; } = [];

    public SceneNode? FindNodeById(string id)
    {
        foreach (var child in Children)
        {
            if (child.Id == id)
            {
                return child;
            }

            if (child is ContainerNode container)
            {
                var found = container.FindNodeById(id);
                if (found is not null)
                {
                    return found;
                }
            }
        }

        return null;
    }

    public override SKRect GetWorldBounds()
    {
        var bounds = SKRect.Empty;
        foreach (var child in Children.Where(c => c.IsVisible))
        {
            var childBounds = child.GetWorldBounds();
            if (!childBounds.IsEmpty)
            {
                bounds = bounds.IsEmpty ? childBounds : SKRect.Union(bounds, childBounds);
            }
        }

        return Transform.IsIdentity || bounds.IsEmpty ? bounds : Transform.MapRect(bounds);
    }
}

/// <summary>
/// Represents an editor layer supporting blend modes, filters, and batch export.
/// </summary>
public sealed class LayerNode : ContainerNode
{
    public LayerNode()
    {
        Name = "Layer";
    }
}

/// <summary>
/// Represents a group of SVG elements (&lt;g&gt;).
/// </summary>
public sealed class GroupNode : ContainerNode
{
    public GroupNode()
    {
        Name = "Group";
    }
}

/// <summary>
/// Base class for drawable vector shapes.
/// </summary>
public abstract class ShapeNode : SceneNode
{
    public ShapeStyle Style { get; set; } = new();

    /// <summary>
    /// Builds a new <see cref="SKPath"/> in local coordinates. Caller is responsible for disposing it.
    /// </summary>
    public abstract SKPath BuildLocalPath();

    /// <summary>
    /// Builds a new <see cref="SKPath"/> transformed by <see cref="SceneNode.Transform"/>. Caller must dispose.
    /// </summary>
    public SKPath BuildTransformedPath()
    {
        var path = BuildLocalPath();
        if (!Transform.IsIdentity)
        {
            path.Transform(Transform);
        }

        return path;
    }

    public override SKRect GetWorldBounds()
    {
        using var path = BuildTransformedPath();
        var bounds = path.TightBounds;
        if (Style.Stroke is { IsVisible: true } stroke)
        {
            bounds.Inflate(stroke.Width * 0.5f, stroke.Width * 0.5f);
        }

        return bounds;
    }
}

/// <summary>
/// Represents a rectangle or rounded rectangle shape.
/// </summary>
public sealed class RectangleShapeNode : ShapeNode
{
    public SKRect Rect { get; set; } = new(50, 50, 250, 180);
    public float CornerRadiusX { get; set; }
    public float CornerRadiusY { get; set; }

    public RectangleShapeNode()
    {
        Name = "Rectangle";
    }

    public override SKPath BuildLocalPath()
    {
        var path = new SKPath();
        if (CornerRadiusX > 0f || CornerRadiusY > 0f)
        {
            path.AddRoundRect(Rect, CornerRadiusX, CornerRadiusY);
        }
        else
        {
            path.AddRect(Rect);
        }

        return path;
    }
}

/// <summary>
/// Represents a circle or ellipse shape.
/// </summary>
public sealed class EllipseShapeNode : ShapeNode
{
    public SKPoint Center { get; set; } = new(200, 200);
    public float RadiusX { get; set; } = 80f;
    public float RadiusY { get; set; } = 80f;

    public EllipseShapeNode()
    {
        Name = "Ellipse";
    }

    public override SKPath BuildLocalPath()
    {
        var path = new SKPath();
        path.AddOval(new SKRect(
            Center.X - RadiusX,
            Center.Y - RadiusY,
            Center.X + RadiusX,
            Center.Y + RadiusY));
        return path;
    }
}

/// <summary>
/// Represents a convex or concave polygon shape defined by a list of vertices.
/// Includes helper factories for regular convex polygons and star/concave polygons.
/// </summary>
public sealed class PolygonShapeNode : ShapeNode
{
    public List<SKPoint> Points { get; set; } = [];
    public bool IsClosed { get; set; } = true;

    public PolygonShapeNode()
    {
        Name = "Polygon";
    }

    public static PolygonShapeNode CreateRegularConvex(SKPoint center, float radius, int sides, float startAngleDegrees = -90f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 3);
        var node = new PolygonShapeNode { Name = $"Convex Polygon ({sides})" };
        for (var i = 0; i < sides; i++)
        {
            var angleRad = (startAngleDegrees + (360f * i / sides)) * MathF.PI / 180f;
            node.Points.Add(new SKPoint(
                center.X + (radius * MathF.Cos(angleRad)),
                center.Y + (radius * MathF.Sin(angleRad))));
        }

        return node;
    }

    public static PolygonShapeNode CreateConcaveStar(SKPoint center, float outerRadius, float innerRadius, int pointsCount, float startAngleDegrees = -90f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pointsCount, 3);
        var node = new PolygonShapeNode { Name = $"Concave Star ({pointsCount})" };
        var totalVertices = pointsCount * 2;
        for (var i = 0; i < totalVertices; i++)
        {
            var r = (i % 2 == 0) ? outerRadius : innerRadius;
            var angleRad = (startAngleDegrees + (360f * i / totalVertices)) * MathF.PI / 180f;
            node.Points.Add(new SKPoint(
                center.X + (r * MathF.Cos(angleRad)),
                center.Y + (r * MathF.Sin(angleRad))));
        }

        return node;
    }

    public override SKPath BuildLocalPath()
    {
        var path = new SKPath();
        if (Points.Count > 0)
        {
            path.AddPoly(Points.ToArray(), IsClosed);
        }

        return path;
    }
}

/// <summary>
/// How an arc path should be closed.
/// </summary>
public enum ArcCloseMode
{
    Open,
    PieSlice,
    Chord
}

/// <summary>
/// Represents an elliptical or circular arc / pie sector shape.
/// </summary>
public sealed class ArcShapeNode : ShapeNode
{
    public SKPoint Center { get; set; } = new(200, 200);
    public float RadiusX { get; set; } = 90f;
    public float RadiusY { get; set; } = 90f;
    public float StartAngleDegrees { get; set; } = 0f;
    public float SweepAngleDegrees { get; set; } = 240f;
    public ArcCloseMode CloseMode { get; set; } = ArcCloseMode.PieSlice;

    public ArcShapeNode()
    {
        Name = "Arc";
    }

    public override SKPath BuildLocalPath()
    {
        var path = new SKPath();
        var oval = new SKRect(
            Center.X - RadiusX,
            Center.Y - RadiusY,
            Center.X + RadiusX,
            Center.Y + RadiusY);

        switch (CloseMode)
        {
            case ArcCloseMode.PieSlice:
                path.MoveTo(Center);
                path.ArcTo(oval, StartAngleDegrees, SweepAngleDegrees, false);
                path.Close();
                break;
            case ArcCloseMode.Chord:
                path.AddArc(oval, StartAngleDegrees, SweepAngleDegrees);
                path.Close();
                break;
            default:
                path.AddArc(oval, StartAngleDegrees, SweepAngleDegrees);
                break;
        }

        return path;
    }
}

/// <summary>
/// Represents an arbitrary vector path (e.g., from SVG path data or boolean path operations).
/// </summary>
public sealed class PathShapeNode : ShapeNode
{
    public string SvgPathData { get; set; } = string.Empty;

    public PathShapeNode()
    {
        Name = "Path";
    }

    public PathShapeNode(string svgPathData)
    {
        Name = "Path";
        SvgPathData = svgPathData;
    }

    public override SKPath BuildLocalPath()
    {
        if (string.IsNullOrWhiteSpace(SvgPathData))
        {
            return new SKPath();
        }

        return SKPath.ParseSvgPathData(SvgPathData) ?? new SKPath();
    }
}
