using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Services;
using Vectorer.Abstractions.Styling;
using Vectorer.Rendering.Export;
using Vectorer.Rendering.Shaders;
using Vectorer.SamplePlugin;
using Xunit;

namespace Vectorer.Rendering.Tests;

public sealed class RenderingAndPngExportTests
{
    [Fact]
    public void RenderDocument_AppliesNestedTransformsWithoutLosingGeometry()
    {
        var document = SvgDocument.CreateDefault(200, 200);
        document.Layers[0].Transform = SKMatrix.CreateTranslation(50, 20);
        var group = new GroupNode { Transform = SKMatrix.CreateScale(2, 2) };
        var shape = new RectangleShapeNode { Rect = new SKRect(0, 0, 20, 20), Transform = SKMatrix.CreateTranslation(10, 15) };
        shape.Style.Fill = new SolidColorFill(SKColors.Red);
        shape.Style.Stroke = null;
        group.Children.Add(shape);
        document.Layers[0].Children.Add(group);
        using var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);
        var renderer = new SkiaDocumentRenderer();
        for (var i = 0; i < 3; i++)
        {
            canvas.Clear(SKColors.Transparent);
            renderer.RenderDocument(document, canvas);
            Assert.Equal(SKColors.Red, bitmap.GetPixel(80, 60));
            Assert.Equal(0, bitmap.GetPixel(10, 10).Alpha);
        }
        Assert.Equal(SKMatrix.CreateTranslation(10, 15), shape.Transform);
    }

    [Fact]
    public void ExportDocumentToBytes_RespectsTargetDimensions()
    {
        var doc = SvgDocument.CreateDefault(400, 300);
        doc.Layers[0].Children.Add(new EllipseShapeNode
        {
            Center = new SKPoint(200, 150),
            RadiusX = 90,
            RadiusY = 90
        });

        var shaderRegistry = new ShaderEffectRegistry();
        shaderRegistry.Register(new SunsetWaveShaderProvider());
        var renderer = new SkiaDocumentRenderer(shaderRegistry);
        var exporter = new PngExportService(renderer);

        var pngBytes = exporter.ExportDocumentToBytes(doc, new PngExportOptions(TargetWidth: 800, TargetHeight: 600));
        Assert.NotEmpty(pngBytes);

        using var bitmap = SKBitmap.Decode(pngBytes);
        Assert.NotNull(bitmap);
        Assert.Equal(800, bitmap.Width);
        Assert.Equal(600, bitmap.Height);
    }

    [Fact]
    public void ExportSelectionToBytes_ExportsOnlySelectedObjectWithSpecifiedSize()
    {
        var star = PolygonShapeNode.CreateConcaveStar(new SKPoint(150, 150), 80f, 35f, 6);
        star.ShaderEffect = new ShaderEffectDescriptor(SampleVectorerPlugin.SunsetWaveShaderId);

        var shaderRegistry = new ShaderEffectRegistry();
        shaderRegistry.Register(new SunsetWaveShaderProvider());
        var renderer = new SkiaDocumentRenderer(shaderRegistry);
        var exporter = new PngExportService(renderer);

        var pngBytes = exporter.ExportSelectionToBytes([star], new PngExportOptions(TargetWidth: 256, TargetHeight: 256));
        Assert.NotEmpty(pngBytes);

        using var bitmap = SKBitmap.Decode(pngBytes);
        Assert.NotNull(bitmap);
        Assert.Equal(256, bitmap.Width);
        Assert.Equal(256, bitmap.Height);
    }

    [Fact]
    public async Task ExportLayersBatchAsync_ExportsEachVisibleLayerToSeparatePngFile()
    {
        var doc = new SvgDocument { Width = 300, Height = 200 };
        var layer1 = new LayerNode { Name = "Background", BlendMode = LayerBlendMode.Normal };
        layer1.Children.Add(new RectangleShapeNode { Rect = new SKRect(10, 10, 290, 190) });

        var layer2 = new LayerNode { Name = "Foreground Add", BlendMode = LayerBlendMode.Add };
        layer2.Children.Add(new EllipseShapeNode { Center = new SKPoint(150, 100), RadiusX = 60, RadiusY = 60 });

        doc.Layers.Add(layer1);
        doc.Layers.Add(layer2);

        var renderer = new SkiaDocumentRenderer();
        var exporter = new PngExportService(renderer);

        var tempDir = Path.Combine(Path.GetTempPath(), $"vectorer_batch_test_{Guid.NewGuid():N}");
        try
        {
            var files = await exporter.ExportLayersBatchAsync(
                doc,
                tempDir,
                filePrefix: "batch",
                visibleLayersOnly: true,
                options: new PngExportOptions(TargetWidth: 600, TargetHeight: 400));

            Assert.Equal(2, files.Count);
            foreach (var file in files)
            {
                Assert.True(File.Exists(file));
                using var bmp = SKBitmap.Decode(file);
                Assert.Equal(600, bmp.Width);
                Assert.Equal(400, bmp.Height);
            }
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
