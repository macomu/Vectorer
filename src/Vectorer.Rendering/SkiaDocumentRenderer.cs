using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Services;
using Vectorer.Abstractions.Styling;
using Vectorer.Rendering.Shaders;

namespace Vectorer.Rendering;

/// <summary>
/// Renders <see cref="SvgDocument"/> and <see cref="SceneNode"/> hierarchies onto a <see cref="SKCanvas"/>,
/// supporting layer blend modes (Overlay, Add, Subtract, etc.), Gaussian blur filters, texture fills,
/// outlines, and per-object SkiaSharp runtime shaders.
/// </summary>
public sealed class SkiaDocumentRenderer(ShaderEffectRegistry? shaderRegistry = null) : IDocumentRenderer
{
    public ShaderEffectRegistry ShaderRegistry { get; } = shaderRegistry ?? new ShaderEffectRegistry();

    public void RenderDocument(SvgDocument document, SKCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(canvas);

        if (document.BackgroundColor.Alpha > 0)
        {
            using var bgPaint = new SKPaint { Color = document.BackgroundColor, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, document.Width, document.Height, bgPaint);
        }

        foreach (var layer in document.Layers)
        {
            if (layer.IsVisible)
            {
                RenderNode(layer, canvas);
            }
        }
    }

    public void RenderLayer(LayerNode layer, SKCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(canvas);
        if (layer.IsVisible)
        {
            RenderNode(layer, canvas);
        }
    }

    public void RenderNodes(IEnumerable<SceneNode> nodes, SKCanvas canvas)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(canvas);
        foreach (var node in nodes)
        {
            if (node.IsVisible)
            {
                RenderNode(node, canvas);
            }
        }
    }

    public void RenderNode(SceneNode node, SKCanvas canvas)
    {
        if (!node.IsVisible || node.Opacity <= 0f)
        {
            return;
        }

        var requiresLayerSave = node.Opacity < 0.999f
            || node.BlendMode != LayerBlendMode.Normal
            || (node.Filter is { HasActiveFilter: true });

        SKPaint? layerPaint = null;
        SKImageFilter? imageFilter = null;

        try
        {
            if (requiresLayerSave)
            {
                layerPaint = new SKPaint
                {
                    Color = new SKColor(255, 255, 255, (byte)Math.Clamp(node.Opacity * 255f, 0f, 255f)),
                    BlendMode = ToSkiaBlendMode(node.BlendMode),
                    IsAntialias = true
                };

                if (node.Filter is { HasActiveFilter: true } filter)
                {
                    imageFilter = SKImageFilter.CreateBlur(filter.BlurSigmaX, filter.BlurSigmaY);
                    layerPaint.ImageFilter = imageFilter;
                }

                canvas.SaveLayer(layerPaint);
            }
            else
            {
                canvas.Save();
            }

            if (!node.Transform.IsIdentity)
            {
                canvas.Concat(ref UnsafeTransformRef(node));
            }

            switch (node)
            {
                case ContainerNode container:
                    foreach (var child in container.Children)
                    {
                        RenderNode(child, canvas);
                    }
                    break;

                case ShapeNode shape:
                    RenderShape(shape, canvas);
                    break;
            }
        }
        finally
        {
            canvas.Restore();
            imageFilter?.Dispose();
            layerPaint?.Dispose();
        }
    }

    private void RenderShape(ShapeNode shape, SKCanvas canvas)
    {
        using var path = shape.BuildLocalPath();
        if (path.IsEmpty)
        {
            return;
        }

        var bounds = path.TightBounds;

        // 1. Fill or Per-Object Runtime Shader
        if (shape.ShaderEffect is not null)
        {
            using var customShader = ShaderRegistry.CreateShaderForDescriptor(shape.ShaderEffect, bounds);
            if (customShader is not null)
            {
                using var shaderPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Shader = customShader,
                    IsAntialias = true
                };
                canvas.DrawPath(path, shaderPaint);
            }
        }
        else if (shape.Style.Fill is not null)
        {
            RenderShapeFill(shape.Style.Fill, path, canvas);
        }

        // 2. Outline (Stroke)
        if (shape.Style.Stroke is { IsVisible: true } stroke)
        {
            using var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
                Color = stroke.Color,
                StrokeWidth = stroke.Width,
                StrokeCap = stroke.Cap,
                StrokeJoin = stroke.Join,
                StrokeMiter = stroke.MiterLimit,
                IsAntialias = true
            };

            SKPathEffect? dashEffect = null;
            try
            {
                if (stroke.DashArray is { Length: >= 2 })
                {
                    dashEffect = SKPathEffect.CreateDash(stroke.DashArray, stroke.DashPhase);
                    strokePaint.PathEffect = dashEffect;
                }

                canvas.DrawPath(path, strokePaint);
            }
            finally
            {
                dashEffect?.Dispose();
            }
        }
    }

    private static void RenderShapeFill(ShapeFill fill, SKPath path, SKCanvas canvas)
    {
        switch (fill)
        {
            case SolidColorFill solid:
            {
                using var fillPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Color = solid.Color,
                    IsAntialias = true
                };
                canvas.DrawPath(path, fillPaint);
                break;
            }
            case TextureFill texture:
            {
                using var shader = CreateTexturePatternShader(texture);
                using var fillPaint = new SKPaint
                {
                    Style = SKPaintStyle.Fill,
                    Shader = shader,
                    IsAntialias = true
                };
                canvas.DrawPath(path, fillPaint);
                break;
            }
        }
    }

    private static SKShader CreateTexturePatternShader(TextureFill texture)
    {
        var tileSize = Math.Max(4, (int)MathF.Round(texture.Scale));
        using var bitmap = new SKBitmap(tileSize, tileSize);
        using (var tileCanvas = new SKCanvas(bitmap))
        {
            tileCanvas.Clear(texture.SecondaryColor);
            using var primaryPaint = new SKPaint
            {
                Color = texture.PrimaryColor,
                IsAntialias = true,
                Style = SKPaintStyle.Fill
            };

            var half = tileSize * 0.5f;
            switch (texture.PatternKind)
            {
                case TexturePatternKind.Checkerboard:
                    tileCanvas.DrawRect(0, 0, half, half, primaryPaint);
                    tileCanvas.DrawRect(half, half, half, half, primaryPaint);
                    break;

                case TexturePatternKind.DiagonalStripes:
                    primaryPaint.Style = SKPaintStyle.Stroke;
                    primaryPaint.StrokeWidth = Math.Max(1.5f, tileSize * 0.2f);
                    tileCanvas.DrawLine(-half, half, half, -half, primaryPaint);
                    tileCanvas.DrawLine(0, tileSize, tileSize, 0, primaryPaint);
                    tileCanvas.DrawLine(half, tileSize + half, tileSize + half, half, primaryPaint);
                    break;

                case TexturePatternKind.Crosshatch:
                    primaryPaint.Style = SKPaintStyle.Stroke;
                    primaryPaint.StrokeWidth = Math.Max(1f, tileSize * 0.12f);
                    tileCanvas.DrawLine(0, half, tileSize, half, primaryPaint);
                    tileCanvas.DrawLine(half, 0, half, tileSize, primaryPaint);
                    break;

                case TexturePatternKind.Dots:
                default:
                    tileCanvas.DrawCircle(half, half, Math.Max(1.5f, tileSize * 0.25f), primaryPaint);
                    break;
            }
        }

        var localMatrix = Math.Abs(texture.AngleDegrees) > 0.01f
            ? SKMatrix.CreateRotationDegrees(texture.AngleDegrees)
            : SKMatrix.CreateIdentity();

        return SKShader.CreateBitmap(bitmap, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, localMatrix);
    }

    public static SKBlendMode ToSkiaBlendMode(LayerBlendMode blendMode) => blendMode switch
    {
        LayerBlendMode.Normal => SKBlendMode.SrcOver,
        LayerBlendMode.Multiply => SKBlendMode.Multiply,
        LayerBlendMode.Screen => SKBlendMode.Screen,
        LayerBlendMode.Overlay => SKBlendMode.Overlay,
        LayerBlendMode.Add => SKBlendMode.Plus,
        LayerBlendMode.Subtract => SKBlendMode.Exclusion,
        LayerBlendMode.Difference => SKBlendMode.Difference,
        LayerBlendMode.Darken => SKBlendMode.Darken,
        LayerBlendMode.Lighten => SKBlendMode.Lighten,
        _ => SKBlendMode.SrcOver
    };

    private static ref SKMatrix UnsafeTransformRef(SceneNode node)
    {
        var matrix = node.Transform;
        node.Transform = matrix;
        return ref System.Runtime.CompilerServices.Unsafe.AsRef(in matrix);
    }
}
