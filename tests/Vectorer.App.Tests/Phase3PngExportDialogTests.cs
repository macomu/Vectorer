using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.App.ViewModels;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class Phase3PngExportDialogTests
{
    private static MainWindowViewModel CreateWorkspace()
    {
        var vm = new MainWindowViewModel();
        vm.CurrentDocument.Width = 860f;
        vm.CurrentDocument.Height = 540f;
        vm.CurrentDocument.ViewBox = new SKRect(0, 0, 860, 540);
        vm.CurrentDocument.Layers.Clear();
        var layer = new LayerNode { Name = "Layer 1" };
        vm.CurrentDocument.Layers.Add(layer);
        vm.ActiveLayer = layer;
        vm.SelectNodes([]);
        return vm;
    }

    [Fact]
    public void SelectionMode_ExportsHorizontalLineWithRoundCapsAndBlurAndDisablesEmptySelection()
    {
        var vm = CreateWorkspace();

        // Empty selection disables Selection export
        using (var emptyExportVm = vm.CreatePngExportViewModel())
        {
            emptyExportVm.Mode = PngExportMode.Selection;
            Assert.False(emptyExportVm.CanExport);
            Assert.Equal("出力可能な選択要素がありません", emptyExportVm.OutputSizeLabel);
        }

        // Horizontal line (height 0 in geometry, 4px stroke with round caps -> 104x4 painted bounds)
        var line = new PathShapeNode("M 10 50 L 110 50");
        line.Style.Fill = null;
        line.Style.Stroke = new StrokeStyle(SKColors.Red, Width: 4f, Cap: SKStrokeCap.Round, Join: SKStrokeJoin.Round);
        vm.ActiveLayer!.Children.Add(line);
        vm.SelectNodes([line]);

        using (var lineExportVm = vm.CreatePngExportViewModel())
        {
            lineExportVm.Mode = PngExportMode.Selection;
            Assert.True(lineExportVm.CanExport);
            Assert.Equal(104f, lineExportVm.BaseWidth, 1);
            Assert.Equal(4f, lineExportVm.BaseHeight, 1);
            Assert.Equal(104, lineExportVm.TargetWidth);
            Assert.Equal(4, lineExportVm.TargetHeight);
            Assert.NotEmpty(lineExportVm.PreviewPngBytes);

            var pngBytes = lineExportVm.RenderExportPngBytes();
            Assert.NotEmpty(pngBytes);
            using var decoded = SKBitmap.Decode(pngBytes);
            Assert.Equal(104, decoded.Width);
            Assert.Equal(4, decoded.Height);
            // Center of line and rounded cap tips are painted within the cropped PNG
            Assert.True(decoded.GetPixel(52, 2).Alpha > 200);
            Assert.True(decoded.GetPixel(1, 2).Alpha > 50);
            Assert.True(decoded.GetPixel(102, 2).Alpha > 50);
        }

        // Adding Gaussian blur (sigma = 2) expands painted bounds by 3*sigma = 6px per side -> 116x16
        line.Filter = new FilterSettings(2f, 2f);
        using (var blurExportVm = vm.CreatePngExportViewModel())
        {
            blurExportVm.Mode = PngExportMode.Selection;
            Assert.True(blurExportVm.CanExport);
            Assert.Equal(116, blurExportVm.TargetWidth);
            Assert.Equal(16, blurExportVm.TargetHeight);
        }
    }

    [Fact]
    public void ScalePresetsAndManualDimensions_SynchronizeWithAspectLockAndExactOutputResolution()
    {
        var vm = CreateWorkspace();
        var rect = new RectangleShapeNode { Rect = new SKRect(50, 50, 300, 250) };
        vm.ActiveLayer!.Children.Add(rect);

        using var exportVm = vm.CreatePngExportViewModel();
        Assert.Equal(PngExportMode.Document, exportVm.Mode);
        Assert.Equal(860f, exportVm.BaseWidth);
        Assert.Equal(540f, exportVm.BaseHeight);
        Assert.Equal(860, exportVm.TargetWidth);
        Assert.Equal(540, exportVm.TargetHeight);
        Assert.Equal("1x", exportVm.ScaleStatusLabel);

        // 2x preset
        exportVm.ApplyScalePreset(2f);
        Assert.Equal(1720, exportVm.TargetWidth);
        Assert.Equal(1080, exportVm.TargetHeight);
        Assert.Equal("2x", exportVm.ScaleStatusLabel);
        Assert.Equal("出力サイズ: 1720 × 1080 px", exportVm.OutputSizeLabel);

        // 0.5x preset always recalculates from BaseWidth/BaseHeight (860x540), not previous TargetWidth
        exportVm.ApplyScalePreset(0.5f);
        Assert.Equal(430, exportVm.TargetWidth);
        Assert.Equal(270, exportVm.TargetHeight);
        Assert.Equal("0.5x", exportVm.ScaleStatusLabel);

        // Manual width entry with LockAspect=true updates height proportionally and switches to custom scale
        exportVm.LockAspect = true;
        exportVm.TargetWidth = 1290;
        Assert.Equal(810, exportVm.TargetHeight);
        Assert.True(exportVm.IsCustomScale);
        Assert.Equal("カスタム (1.5x)", exportVm.ScaleStatusLabel);
        Assert.Equal("出力サイズ: 1290 × 810 px", exportVm.OutputSizeLabel);

        var bytes = exportVm.RenderExportPngBytes();
        using var bmp = SKBitmap.Decode(bytes);
        Assert.Equal(1290, bmp.Width);
        Assert.Equal(810, bmp.Height);

        // LayersBatch mode disables export when all layers are hidden and VisibleLayersOnly is true
        vm.ActiveLayer.IsVisible = false;
        exportVm.Mode = PngExportMode.LayersBatch;
        exportVm.VisibleLayersOnly = true;
        Assert.Equal(0, exportVm.ExportableLayerCount);
        Assert.False(exportVm.CanExport);

        exportVm.VisibleLayersOnly = false;
        Assert.Equal(1, exportVm.ExportableLayerCount);
        Assert.True(exportVm.CanExport);
    }
}
