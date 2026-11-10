using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Services;

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
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scaleX, scaleY);

        if (options.IncludeBackground && document.BackgroundColor.Alpha > 0)
        {
            using var bgPaint = new SKPaint { Color = document.BackgroundColor, Style = SKPaintStyle.Fill };
            canvas.DrawRect(0, 0, document.Width, document.Height, bgPaint);
        }

        foreach (var layer in document.Layers.Where(l => l.IsVisible))
        {
            renderer.RenderLayer(layer, canvas);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
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

    public byte[] ExportSelectionToBytes(IReadOnlyList<SceneNode> selectedNodes, PngExportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(selectedNodes);
        options ??= new PngExportOptions();

        var combinedBounds = SKRect.Empty;
        foreach (var node in selectedNodes.Where(n => n.IsVisible))
        {
            var b = node.GetWorldBounds();
            if (!b.IsEmpty)
            {
                combinedBounds = combinedBounds.IsEmpty ? b : SKRect.Union(combinedBounds, b);
            }
        }

        if (combinedBounds.IsEmpty)
        {
            combinedBounds = new SKRect(0, 0, 100, 100);
        }

        if (options.Padding > 0f)
        {
            combinedBounds.Inflate(options.Padding, options.Padding);
        }

        var sourceWidth = Math.Max(1f, combinedBounds.Width);
        var sourceHeight = Math.Max(1f, combinedBounds.Height);
        var (outWidth, outHeight, scaleX, scaleY) = CalculateOutputSize(sourceWidth, sourceHeight, options);

        var imageInfo = new SKImageInfo(outWidth, outHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo);
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale(scaleX, scaleY);
        canvas.Translate(-combinedBounds.Left, -combinedBounds.Top);

        renderer.RenderNodes(selectedNodes, canvas);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    public async Task ExportSelectionToFileAsync(
        IReadOnlyList<SceneNode> selectedNodes,
        string filePath,
        PngExportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var bytes = ExportSelectionToBytes(selectedNodes, options);
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

        for (var i = 0; i < document.Layers.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layer = document.Layers[i];
            if (visibleLayersOnly && !layer.IsVisible)
            {
                continue;
            }

            var imageInfo = new SKImageInfo(outWidth, outHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scaleX, scaleY);

            var wasVisible = layer.IsVisible;
            layer.IsVisible = true;
            try
            {
                renderer.RenderLayer(layer, canvas);
            }
            finally
            {
                layer.IsVisible = wasVisible;
            }

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);

            var safeName = SanitizeFileName(layer.Name);
            var fileName = $"{filePrefix}_{i + 1:D2}_{safeName}.png";
            var fullPath = Path.Combine(outputDirectory, fileName);

            await File.WriteAllBytesAsync(fullPath, data.ToArray(), cancellationToken);
            exportedFiles.Add(fullPath);
        }

        return exportedFiles;
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
