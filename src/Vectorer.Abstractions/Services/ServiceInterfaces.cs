using SkiaSharp;
using Vectorer.Abstractions.Documents;

namespace Vectorer.Abstractions.Services;

/// <summary>
/// Reads and writes <see cref="SvgDocument"/> instances to/from SVG XML streams or strings.
/// </summary>
public interface ISvgSerializer
{
    string SerializeToString(SvgDocument document);
    void SaveToStream(SvgDocument document, Stream outputStream);
    SvgDocument DeserializeFromString(string svgContent);
    SvgDocument LoadFromStream(Stream inputStream);
}

/// <summary>
/// Renders a <see cref="SvgDocument"/> or subset of <see cref="SceneNode"/>s onto a SkiaSharp canvas.
/// </summary>
public interface IDocumentRenderer
{
    void RenderDocument(SvgDocument document, SKCanvas canvas);
    void RenderLayer(LayerNode layer, SKCanvas canvas);
    void RenderNodes(IEnumerable<SceneNode> nodes, SKCanvas canvas);
}

/// <summary>
/// Configuration for exporting PNG images with explicit target dimensions or scaling.
/// </summary>
public sealed record PngExportOptions(
    int? TargetWidth = null,
    int? TargetHeight = null,
    float Scale = 1.0f,
    bool IncludeBackground = true,
    float Padding = 0f);

/// <summary>
/// Exports SVG documents, selected nodes, or individual layers in batch as PNG images.
/// </summary>
public interface IPngExportService
{
    /// <summary>
    /// Exports the entire document to a PNG byte array with optional target size.
    /// </summary>
    byte[] ExportDocumentToBytes(SvgDocument document, PngExportOptions? options = null);

    /// <summary>
    /// Exports the entire document to a PNG file.
    /// </summary>
    Task ExportDocumentToFileAsync(SvgDocument document, string filePath, PngExportOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports only the selected nodes cropped to their combined bounds (with optional target size).
    /// </summary>
    byte[] ExportSelectionToBytes(IReadOnlyList<SceneNode> selectedNodes, PngExportOptions? options = null);

    /// <summary>
    /// Exports only the selected nodes to a PNG file.
    /// </summary>
    Task ExportSelectionToFileAsync(IReadOnlyList<SceneNode> selectedNodes, string filePath, PngExportOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch exports each layer in the document as an individual PNG file into the target directory.
    /// Returns the list of generated file paths.
    /// </summary>
    Task<IReadOnlyList<string>> ExportLayersBatchAsync(
        SvgDocument document,
        string outputDirectory,
        string filePrefix = "layer",
        bool visibleLayersOnly = true,
        PngExportOptions? options = null,
        CancellationToken cancellationToken = default);
}
