using System.Diagnostics;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.App.ViewModels;
using Vectorer.Rendering;
using Vectorer.Svg;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class LargeDocumentPerformanceTests
{
    [Fact]
    public void LargeDocument_RoundTripsRendersAndReusesSnapGeometryDuringPreview()
    {
        const int count = 5000;
        var document = SvgDocument.CreateDefault(1000, 500);
        for (var i = 0; i < count; i++)
        {
            var x = i % 100 * 10; var y = i / 100 * 10;
            var rectangle = new RectangleShapeNode { Rect = SKRect.Create(x, y, 6, 6) };
            rectangle.Style.Fill = new SolidColorFill(SKColors.Red); rectangle.Style.Stroke = null;
            document.Layers[0].Children.Add(rectangle);
        }
        var serializer = new SvgSerializer(); var stopwatch = Stopwatch.StartNew();
        var xml = serializer.SerializeToString(document); var serializeMs = stopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Restart(); var restored = serializer.DeserializeFromString(xml); var parseMs = stopwatch.Elapsed.TotalMilliseconds;
        Assert.Equal(count, restored.Layers[0].Children.Count);
        using var bitmap = new SKBitmap(1000, 500); using var canvas = new SKCanvas(bitmap); var renderer = new SkiaDocumentRenderer();
        renderer.RenderDocument(restored, canvas); stopwatch.Restart();
        for (var i = 0; i < 3; i++) { canvas.Clear(SKColors.Transparent); renderer.RenderDocument(restored, canvas); }
        var renderMs = stopwatch.Elapsed.TotalMilliseconds / 3;
        Assert.Equal(SKColors.Red, bitmap.GetPixel(2, 2)); Assert.Equal(0, bitmap.GetPixel(8, 8).Alpha);
        var vm = new MainWindowViewModel { CurrentDocument = restored, ActiveLayer = restored.Layers[0], SnapToGrid = true }; vm.SelectNodes([]);
        stopwatch.Restart(); vm.SnapPoint(new(51, 51)); var buildSnapMs = stopwatch.Elapsed.TotalMilliseconds;
        stopwatch.Restart(); for (var i = 0; i < 100; i++) { vm.SnapPoint(new(51, 51)); vm.RequestRedraw(false); }
        var previewSnapMs = stopwatch.Elapsed.TotalMilliseconds / 100;
        // This is a reproducible measurement, not a machine-dependent timing assertion.
        Console.WriteLine($"[PERF] shapes={count}; svgBytes={System.Text.Encoding.UTF8.GetByteCount(xml)}; serializeMs={serializeMs:F2}; parseMs={parseMs:F2}; renderMs={renderMs:F2}; snapBuildMs={buildSnapMs:F2}; previewSnapMs={previewSnapMs:F2}");
        Assert.Equal(new SKPoint(50, 50), vm.SnapPoint(new(51, 51)));
    }
}
