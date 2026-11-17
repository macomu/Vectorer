using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Services;
using Vectorer.Abstractions.Styling;

namespace Vectorer.Rendering.Export;

/// <summary>
/// Exports SVG documents, selected nodes, or individual layers in batch as PNG images with customizable dimensions.
/// </summary>
public sealed class PngExportService(IDocumentRenderer renderer) : IPngExportService
{
    public byte[] ExportDocumentToBytes(SvgDocument document, PngExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        options ??= new PngExportOptions();

        var sourceWidth = Math.Max(1f, document.Width);
        var sourceHeight = Math.Max(1f, document.Height);
        var (outWidth, outHeight, scaleX, scaleY) = CalculateOutputSize(sourceWidth, sourceHeight, options);

        var imageInfo = new SKImageInfo(outWidth, outHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo) ??
            throw new InvalidOperationException($"Cannot allocate a {outWidth} × {outHeight} PNG surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var opacityPaint = SkiaDocumentRenderer.CreateDocumentOpacityPaint(document);
        if (opacityPaint is not null) canvas.SaveLayer(opacityPaint);
        canvas.Scale(scaleX, scaleY);

        if (options.IncludeBackground && document.BackgroundColor.Alpha > 0)
        {
            using var bgPaint = new SKPaint { Color = document.BackgroundColor, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, document.Width, document.Height, bgPaint);
        }

        canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(0, 0, document.Width, document.Height));
            var viewport = document.GetViewportTransform();
            canvas.Concat(ref viewport);
            foreach (var layer in document.Layers.Where(l => l.IsVisible))
            {
                renderer.RenderLayer(layer, canvas);
            }
        }
        finally
        {
            canvas.Restore();
        }

        if (opacityPaint is not null) canvas.Restore();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100) ??
            throw new InvalidOperationException("Cannot encode the rendered image as PNG.");
        return data.ToArray();
    }

    public async Task ExportDocumentToFileAsync(
        SvgDocument document,
        string filePath,
        PngExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = ExportDocumentToBytes(document, options);
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
    }

    public byte[] ExportSelectionToBytes(IReadOnlyList<SceneNode> selectedNodes, PngExportOptions? options = null) =>
        ExportSelectionToBytes(document: null, selectedNodes, options);

    public byte[] ExportSelectionToBytes(
        SvgDocument? document,
        IReadOnlyList<SceneNode> selectedNodes,
        PngExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(selectedNodes);
        options ??= new PngExportOptions();

        var bounds = TryGetSelectionRenderBounds(document, selectedNodes, options.Padding);
        if (bounds is not { Width: > 0f, Height: > 0f } validBounds)
        {
            return [];
        }

        var sourceWidth = Math.Max(1f, validBounds.Width);
        var sourceHeight = Math.Max(1f, validBounds.Height);
        var (outWidth, outHeight, scaleX, scaleY) = CalculateOutputSize(sourceWidth, sourceHeight, options);

        var imageInfo = new SKImageInfo(outWidth, outHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo) ??
            throw new InvalidOperationException($"Cannot allocate a {outWidth} × {outHeight} PNG surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var opacityPaint = SkiaDocumentRenderer.CreateDocumentOpacityPaint(document);
        if (opacityPaint is not null) canvas.SaveLayer(opacityPaint);

        if (options.IncludeBackground && document is { BackgroundColor.Alpha: > 0 })
        {
            using var bgPaint = new SKPaint { Color = document.BackgroundColor, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, outWidth, outHeight, bgPaint);
        }

        canvas.Scale(scaleX, scaleY);
        canvas.Translate(-validBounds.Left, -validBounds.Top);

        RenderSelectedNodesInWorldSpace(document, selectedNodes, canvas);

        if (opacityPaint is not null) canvas.Restore();
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100) ??
            throw new InvalidOperationException("Cannot encode the rendered image as PNG.");
        return data.ToArray();
    }

    public Task ExportSelectionToFileAsync(
        IReadOnlyList<SceneNode> selectedNodes,
        string filePath,
        PngExportOptions? options = null,
        CancellationToken cancellationToken = default) =>
        ExportSelectionToFileAsync(document: null, selectedNodes, filePath, options, cancellationToken);

    public async Task ExportSelectionToFileAsync(
        SvgDocument? document,
        IReadOnlyList<SceneNode> selectedNodes,
        string filePath,
        PngExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = ExportSelectionToBytes(document, selectedNodes, options);
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("No exportable selection bounds available.");
        }

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await File.WriteAllBytesAsync(filePath, bytes, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ExportLayersBatchAsync(
        SvgDocument document,
        string outputDirectory,
        string filePrefix = "layer",
        bool visibleLayersOnly = true,
        PngExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        options ??= new PngExportOptions();

        Directory.CreateDirectory(outputDirectory);
        var exportedFiles = new List<string>();

        var sourceWidth = Math.Max(1f, document.Width);
        var sourceHeight = Math.Max(1f, document.Height);
        var (outWidth, outHeight, scaleX, scaleY) = CalculateOutputSize(sourceWidth, sourceHeight, options);
        var viewport = document.GetViewportTransform();

        for (var i = 0; i < document.Layers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layer = document.Layers[i];
            if (visibleLayersOnly && !layer.IsVisible)
            {
                continue;
            }

            var imageInfo = new SKImageInfo(outWidth, outHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo) ??
                throw new InvalidOperationException($"Cannot allocate a {outWidth} × {outHeight} PNG surface.");
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            using var opacityPaint = SkiaDocumentRenderer.CreateDocumentOpacityPaint(document);
            if (opacityPaint is not null) canvas.SaveLayer(opacityPaint);
            canvas.Scale(scaleX, scaleY);

            if (options.IncludeBackground && document.BackgroundColor.Alpha > 0)
            {
                using var bgPaint = new SKPaint { Color = document.BackgroundColor, Style = SKPaintStyle.Fill };
                canvas.DrawRect(0, 0, document.Width, document.Height, bgPaint);
            }

            canvas.Save();
            var wasVisible = layer.IsVisible;
            layer.IsVisible = true;
            try
            {
                canvas.ClipRect(new SKRect(0, 0, document.Width, document.Height));
                canvas.Concat(ref viewport);
                renderer.RenderLayer(layer, canvas);
            }
            finally
            {
                layer.IsVisible = wasVisible;
                canvas.Restore();
            }

            if (opacityPaint is not null) canvas.Restore();
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100) ??
                throw new InvalidOperationException("Cannot encode the rendered image as PNG.");

            var safeName = SanitizeFileName(layer.Name);
            var fileName = $"{filePrefix}_{i + 1:D2}_{safeName}.png";
            var fullPath = Path.Combine(outputDirectory, fileName);

            await File.WriteAllBytesAsync(fullPath, data.ToArray(), cancellationToken);
            exportedFiles.Add(fullPath);
        }

        return exportedFiles;
    }

    /// <summary>
    /// Computes the exact canvas-space painted bounds of the selected elements, accounting for ancestor visibility,
    /// accumulated transforms, fills/shaders, stroke width/cap/join/miter, and Gaussian blur spread.
    /// Returns <see langword="null"/> when the selection is empty or produces no visible painted area.
    /// </summary>
    public static SKRect? TryGetSelectionRenderBounds(
        SvgDocument? document,
        IReadOnlyList<SceneNode> selectedNodes,
        float padding = 0f)
    {
        if (selectedNodes is null || selectedNodes.Count == 0)
        {
            return null;
        }

        var selectedSet = new HashSet<SceneNode>(selectedNodes);
        var visited = new HashSet<SceneNode>();
        SKRect? combined = null;

        if (document is not null)
        {
            var viewport = document.GetViewportTransform();
            foreach (var layer in document.Layers)
            {
                WalkDocumentHierarchy(
                    layer,
                    viewport,
                    inheritedShader: null,
                    inheritedBlurX: 0f,
                    inheritedBlurY: 0f,
                    isSelectionActive: false,
                    selectedSet,
                    visited,
                    ref combined);
            }
        }

        // Also handle standalone nodes that are not attached to the provided document
        foreach (var node in selectedNodes)
        {
            if (visited.Contains(node)) continue;
            WalkDocumentHierarchy(
                node,
                SKMatrix.CreateIdentity(),
                inheritedShader: null,
                inheritedBlurX: 0f,
                inheritedBlurY: 0f,
                isSelectionActive: true,
                selectedSet,
                visited,
                ref combined);
        }

        if (combined is not { Width: > 0f, Height: > 0f } result)
        {
            return null;
        }

        if (padding > 0f)
        {
            result.Inflate(padding, padding);
        }

        return result;
    }

    private static void WalkDocumentHierarchy(
        SceneNode node,
        SKMatrix parentWorld,
        ShaderEffectDescriptor? inheritedShader,
        float inheritedBlurX,
        float inheritedBlurY,
        bool isSelectionActive,
        HashSet<SceneNode> selectedSet,
        HashSet<SceneNode> visited,
        ref SKRect? combined)
    {
        if (!node.IsVisible || node.Opacity <= 0f)
        {
            return;
        }

        var world = SKMatrix.Concat(parentWorld, node.Transform);
        if (!world.TryInvert(out _))
        {
            return;
        }

        var activeShader = node.ShaderEffect ?? inheritedShader;
        var (scaleX, scaleY) = GetMatrixScale(world);
        var totalBlurX = inheritedBlurX + (node.Filter is { HasActiveFilter: true } f ? f.BlurSigmaX * scaleX : 0f);
        var totalBlurY = inheritedBlurY + (node.Filter is { HasActiveFilter: true } f2 ? f2.BlurSigmaY * scaleY : 0f);

        var selectedHere = isSelectionActive || selectedSet.Contains(node);
        if (selectedSet.Contains(node))
        {
            visited.Add(node);
        }

        switch (node)
        {
            case ContainerNode container:
                foreach (var child in container.Children)
                {
                    WalkDocumentHierarchy(
                        child,
                        world,
                        activeShader,
                        totalBlurX,
                        totalBlurY,
                        selectedHere,
                        selectedSet,
                        visited,
                        ref combined);
                }
                break;

            case ShapeNode shape when selectedHere:
            {
                if (TryComputeShapePaintedBounds(shape, world, activeShader, totalBlurX, totalBlurY) is { } shapeBounds)
                {
                    combined = combined is { } cur ? SKRect.Union(cur, shapeBounds) : shapeBounds;
                }
                break;
            }
        }
    }

    private static SKRect? TryComputeShapePaintedBounds(
        ShapeNode shape,
        SKMatrix shapeWorld,
        ShaderEffectDescriptor? activeShader,
        float totalBlurX,
        float totalBlurY)
    {
        using var localPath = shape.BuildLocalPath();
        if (localPath.IsEmpty)
        {
            return null;
        }

        var hasFill = activeShader is not null || PaintVisibility.IsVisible(shape.Style.Fill);

        var stroke = shape.Style.Stroke;
        var hasStroke = stroke is { IsVisible: true };

        if (!hasFill && !hasStroke)
        {
            return null;
        }

        SKRect? shapeBounds = null;

        if (hasFill)
        {
            using var worldFillPath = new SKPath(localPath);
            worldFillPath.Transform(shapeWorld);
            var fb = worldFillPath.TightBounds;
            if (fb.Width > 0f && fb.Height > 0f)
            {
                shapeBounds = fb;
            }
        }

        if (hasStroke && stroke is not null)
        {
            using var strokePaint = new SKPaint
            {
                Style = SKPaintStyle.Stroke,
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

                using var fillOutline = new SKPath();
                if (strokePaint.GetFillPath(localPath, fillOutline) && !fillOutline.IsEmpty)
                {
                    fillOutline.Transform(shapeWorld);
                    var sb = fillOutline.TightBounds;
                    if (sb.Width > 0f && sb.Height > 0f)
                    {
                        shapeBounds = shapeBounds is { } cur ? SKRect.Union(cur, sb) : sb;
                    }
                }
                else
                {
                    using var worldStrokePath = new SKPath(localPath);
                    worldStrokePath.Transform(shapeWorld);
                    var sb = worldStrokePath.TightBounds;
                    var (sx, sy) = GetMatrixScale(shapeWorld);
                    var halfW = stroke.Width * 0.5f;
                    sb.Inflate(halfW * sx, halfW * sy);
                    if (sb.Width > 0f && sb.Height > 0f)
                    {
                        shapeBounds = shapeBounds is { } cur ? SKRect.Union(cur, sb) : sb;
                    }
                }
            }
            finally
            {
                dashEffect?.Dispose();
            }
        }

        if (shapeBounds is not { Width: > 0f, Height: > 0f } validBounds)
        {
            return null;
        }

        if (totalBlurX > 0f || totalBlurY > 0f)
        {
            validBounds.Inflate(3f * totalBlurX, 3f * totalBlurY);
        }

        return validBounds;
    }

    private void RenderSelectedNodesInWorldSpace(
        SvgDocument? document,
        IReadOnlyList<SceneNode> selectedNodes,
        SKCanvas canvas)
    {
        var selectedSet = new HashSet<SceneNode>(selectedNodes);
        var rendered = new HashSet<SceneNode>();

        if (document is not null)
        {
            var viewport = document.GetViewportTransform();
            foreach (var layer in document.Layers)
            {
                RenderHierarchySelection(layer, viewport, selectedSet, rendered, canvas);
            }
        }

        foreach (var node in selectedNodes)
        {
            if (rendered.Contains(node) || !node.IsVisible || node.Opacity <= 0f) continue;
            renderer.RenderNodes([node], canvas);
        }
    }

    private void RenderHierarchySelection(
        SceneNode node,
        SKMatrix parentWorld,
        HashSet<SceneNode> selectedSet,
        HashSet<SceneNode> rendered,
        SKCanvas canvas)
    {
        if (!node.IsVisible || node.Opacity <= 0f)
        {
            return;
        }

        if (selectedSet.Contains(node))
        {
            rendered.Add(node);
            canvas.Save();
            try
            {
                if (!parentWorld.IsIdentity)
                {
                    canvas.Concat(ref parentWorld);
                }
                renderer.RenderNodes([node], canvas);
            }
            finally
            {
                canvas.Restore();
            }
            return;
        }

        if (node is ContainerNode container)
        {
            var nextWorld = SKMatrix.Concat(parentWorld, container.Transform);
            if (!nextWorld.TryInvert(out _)) return;

            var hasSelectedDescendant = container.Children.Any(c => ContainsAnySelected(c, selectedSet));
            if (!hasSelectedDescendant) return;

            var requiresAncestorFilter = container.Opacity < 0.999f
                || container.BlendMode != LayerBlendMode.Normal
                || container.Filter is { HasActiveFilter: true };

            SKPaint? layerPaint = null;
            SKImageFilter? imageFilter = null;
            try
            {
                if (requiresAncestorFilter)
                {
                    layerPaint = new SKPaint
                    {
                        Color = new SKColor(255, 255, 255, (byte)Math.Clamp(container.Opacity * 255f, 0f, 255f)),
                        BlendMode = SkiaDocumentRenderer.ToSkiaBlendMode(container.BlendMode),
                        IsAntialias = true
                    };
                    if (container.Filter is { HasActiveFilter: true } filter)
                    {
                        var (sx, sy) = GetMatrixScale(nextWorld);
                        imageFilter = SKImageFilter.CreateBlur(filter.BlurSigmaX * sx, filter.BlurSigmaY * sy);
                        layerPaint.ImageFilter = imageFilter;
                    }
                    canvas.SaveLayer(layerPaint);
                }

                foreach (var child in container.Children)
                {
                    RenderHierarchySelection(child, nextWorld, selectedSet, rendered, canvas);
                }
            }
            finally
            {
                if (requiresAncestorFilter)
                {
                    canvas.Restore();
                }
                imageFilter?.Dispose();
                layerPaint?.Dispose();
            }
        }
    }

    private static bool ContainsAnySelected(SceneNode node, HashSet<SceneNode> selectedSet)
    {
        if (selectedSet.Contains(node)) return true;
        return node is ContainerNode container && container.Children.Any(c => ContainsAnySelected(c, selectedSet));
    }

    private static (float ScaleX, float ScaleY) GetMatrixScale(SKMatrix m)
    {
        var sx = MathF.Sqrt(m.ScaleX * m.ScaleX + m.SkewY * m.SkewY);
        var sy = MathF.Sqrt(m.SkewX * m.SkewX + m.ScaleY * m.ScaleY);
        return (Math.Max(0.0001f, sx), Math.Max(0.0001f, sy));
    }

    private static (int OutWidth, int OutHeight, float ScaleX, float ScaleY) CalculateOutputSize(
        float sourceWidth,
        float sourceHeight,
        PngExportOptions options)
    {
        var scale = Math.Max(0.01f, options.Scale);
        var outWidth = options.TargetWidth ?? Math.Max(1, (int)MathF.Round(sourceWidth * scale));
        var outHeight = options.TargetHeight ?? Math.Max(1, (int)MathF.Round(sourceHeight * scale));
        var scaleX = outWidth / sourceWidth;
        var scaleY = outHeight / sourceHeight;
        return (outWidth, outHeight, scaleX, scaleY);
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unnamed";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '_' : c).ToArray();
        return new string(chars);
    }
}
