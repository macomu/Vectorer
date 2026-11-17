using System.Globalization;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.App.Localization;
using Vectorer.App.ViewModels;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class Phase3FillAndStrokeInspectorTests
{
    private static MainWindowViewModel CreateWorkspace()
    {
        var vm = new MainWindowViewModel(new AppLocalization(CultureInfo.GetCultureInfo("ja-JP"), CultureInfo.InvariantCulture));
        vm.CurrentDocument.Layers.Clear();
        var layer = new LayerNode { Name = "Layer 1" };
        vm.CurrentDocument.Layers.Add(layer);
        vm.ActiveLayer = layer;
        vm.SelectNodes([]);
        return vm;
    }

    [Fact]
    public void TextureFillDetails_SwitchKindEditPropertiesAndSupportUndoRedo()
    {
        var vm = CreateWorkspace();
        var rect = new RectangleShapeNode { Rect = new SKRect(10, 10, 110, 90) };
        rect.Style.Fill = new SolidColorFill(new SKColor(255, 0, 0));
        vm.ActiveLayer!.Children.Add(rect);
        vm.SelectNodes([rect]);

        Assert.Equal("単色", vm.FillKind);
        Assert.True(vm.IsSolidFill);
        Assert.False(vm.IsTextureFill);

        vm.FillKind = "テクスチャ";
        Assert.True(vm.IsTextureFill);
        var tex = Assert.IsType<TextureFill>(rect.Style.Fill);
        Assert.Equal(new SKColor(255, 0, 0), tex.PrimaryColor);

        vm.TexturePattern = TexturePatternKind.Crosshatch;
        vm.TexturePrimaryColor = "#22C55E";
        vm.TextureSecondaryColor = "#0F172A";
        vm.TextureScale = 28m;
        vm.TextureAngle = 45m;
        vm.FillOpacity = 50m;

        tex = Assert.IsType<TextureFill>(rect.Style.Fill);
        Assert.Equal(TexturePatternKind.Crosshatch, tex.PatternKind);
        Assert.Equal(new SKColor(0x22, 0xC5, 0x5E, 128), tex.PrimaryColor);
        Assert.Equal(new SKColor(0x0F, 0x17, 0x2A), tex.SecondaryColor);
        Assert.Equal(28f, tex.Scale);
        Assert.Equal(45f, tex.AngleDegrees);

        // DefaultCreationStyle also receives the updated texture settings
        var defaultTex = Assert.IsType<TextureFill>(vm.DefaultCreationStyle.Fill);
        Assert.Equal(TexturePatternKind.Crosshatch, defaultTex.PatternKind);
        Assert.Equal(28f, defaultTex.Scale);

        // Undo step by step back to solid fill (7 edits: FillKind, Pattern, Primary, Secondary, Scale, Angle, Opacity)
        for (var i = 0; i < 7; i++)
        {
            vm.UndoCommand.Execute(null);
        }

        var restoredSolid = Assert.IsType<SolidColorFill>(rect.Style.Fill);
        Assert.Equal(new SKColor(255, 0, 0), restoredSolid.Color);
    }

    [Fact]
    public void StrokeDetails_CapJoinMiterOpacityCustomDashAndPhaseSupportUndoRedo()
    {
        var vm = CreateWorkspace();
        var shape = new RectangleShapeNode();
        shape.Style.Stroke = new StrokeStyle(new SKColor(255, 255, 255), 4f, SKStrokeCap.Butt, SKStrokeJoin.Miter, 4f);
        vm.ActiveLayer!.Children.Add(shape);
        vm.SelectNodes([shape]);

        Assert.Equal(SKStrokeCap.Butt, vm.StrokeCap);
        Assert.Equal(SKStrokeJoin.Miter, vm.StrokeJoin);
        Assert.Equal(4m, vm.StrokeMiterLimit);
        Assert.Equal(100m, vm.StrokeOpacity);
        Assert.Equal("実線", vm.StrokeDash);

        vm.StrokeCap = SKStrokeCap.Square;
        vm.StrokeJoin = SKStrokeJoin.Bevel;
        vm.StrokeMiterLimit = 10m;
        vm.StrokeOpacity = 50m;
        vm.CustomDashArray = "12, 6, 3";
        vm.StrokeDashPhase = 4m;

        Assert.NotNull(shape.Style.Stroke);
        Assert.Equal(SKStrokeCap.Square, shape.Style.Stroke!.Cap);
        Assert.Equal(SKStrokeJoin.Bevel, shape.Style.Stroke.Join);
        Assert.Equal(10f, shape.Style.Stroke.MiterLimit);
        Assert.Equal(128, shape.Style.Stroke.Color.Alpha);
        // Odd-length dash array "12, 6, 3" repeats to even length per SVG/Skia spec
        Assert.Equal(new[] { 12f, 6f, 3f, 12f, 6f, 3f }, shape.Style.Stroke.DashArray);
        Assert.Equal(4f, shape.Style.Stroke.DashPhase);
        Assert.Equal("カスタム", vm.StrokeDash);

        vm.UndoCommand.Execute(null);
        Assert.Equal(0f, shape.Style.Stroke!.DashPhase);
        vm.UndoCommand.Execute(null);
        Assert.Null(shape.Style.Stroke!.DashArray);
    }

    [Fact]
    public void MixedSelection_DisplaysNullForDifferingAttributesAndUpdatesOnlyEditedProperty()
    {
        var vm = CreateWorkspace();
        var a = new RectangleShapeNode();
        a.Style.Fill = new TextureFill(TexturePatternKind.Checkerboard, SKColors.Red, SKColors.Black, Scale: 12f, AngleDegrees: 0f);
        a.Style.Stroke = new StrokeStyle(SKColors.Red, 2f, SKStrokeCap.Butt, SKStrokeJoin.Miter, 4f, [8f, 4f]);

        var b = new RectangleShapeNode();
        b.Style.Fill = new TextureFill(TexturePatternKind.Dots, SKColors.Blue, SKColors.White, Scale: 24f, AngleDegrees: 30f);
        b.Style.Stroke = new StrokeStyle(SKColors.Blue, 6f, SKStrokeCap.Round, SKStrokeJoin.Bevel, 8f, [1f, 4f]);

        vm.ActiveLayer!.Children.AddRange([a, b]);
        vm.SelectNodes([a, b]);

        Assert.Equal("テクスチャ", vm.FillKind);
        Assert.Null(vm.TexturePattern);
        Assert.Equal("", vm.TexturePrimaryColor);
        Assert.Null(vm.TextureScale);
        Assert.Null(vm.StrokeCap);
        Assert.Null(vm.StrokeJoin);
        Assert.Null(vm.StrokeDash);
        Assert.Equal("", vm.CustomDashArray);

        // Edit only TextureScale -> both shapes get Scale=32, preserving their distinct PatternKind and colors
        vm.TextureScale = 32m;
        var texA = Assert.IsType<TextureFill>(a.Style.Fill);
        var texB = Assert.IsType<TextureFill>(b.Style.Fill);
        Assert.Equal(32f, texA.Scale);
        Assert.Equal(32f, texB.Scale);
        Assert.Equal(TexturePatternKind.Checkerboard, texA.PatternKind);
        Assert.Equal(TexturePatternKind.Dots, texB.PatternKind);
        Assert.Equal(SKColors.Red, texA.PrimaryColor);
        Assert.Equal(SKColors.Blue, texB.PrimaryColor);

        // Edit only StrokeCap -> both shapes get Square cap, preserving their distinct widths, joins, and dashes
        vm.StrokeCap = SKStrokeCap.Square;
        Assert.Equal(SKStrokeCap.Square, a.Style.Stroke!.Cap);
        Assert.Equal(SKStrokeCap.Square, b.Style.Stroke!.Cap);
        Assert.Equal(2f, a.Style.Stroke.Width);
        Assert.Equal(6f, b.Style.Stroke.Width);
        Assert.Equal(SKStrokeJoin.Miter, a.Style.Stroke.Join);
        Assert.Equal(SKStrokeJoin.Bevel, b.Style.Stroke.Join);

        vm.UndoCommand.Execute(null);
        Assert.Equal(SKStrokeCap.Butt, a.Style.Stroke!.Cap);
        Assert.Equal(SKStrokeCap.Round, b.Style.Stroke!.Cap);

        vm.UndoCommand.Execute(null);
        Assert.Equal(12f, Assert.IsType<TextureFill>(a.Style.Fill).Scale);
        Assert.Equal(24f, Assert.IsType<TextureFill>(b.Style.Fill).Scale);
    }
}
