using System.Text;
using Avalonia.Media;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.App.Services;
using Vectorer.App.ViewModels;
using Xunit;
using GradientStop = Vectorer.Abstractions.Styling.GradientStop;

namespace Vectorer.App.Tests;

public sealed class PaintEditingTests
{
    private static (MainWindowViewModel Workspace, RectangleShapeNode Shape) CreateWorkspace()
    {
        var workspace = new MainWindowViewModel();
        workspace.CurrentDocument.Layers.Clear();
        var layer = new LayerNode();
        workspace.CurrentDocument.Layers.Add(layer);
        workspace.ActiveLayer = layer;
        var shape = new RectangleShapeNode();
        shape.Style.Fill = new SolidColorFill(SKColors.Red);
        layer.Children.Add(shape);
        workspace.SelectNodes([shape]);
        return (workspace, shape);
    }

    private static LinearGradientFill Gradient(SKColor start, SKColor end) => new(new[] { new GradientStop(0, start), new GradientStop(1, end) }, new SKPoint(0, 0), new SKPoint(1, 0));

    [Fact]
    public void ColorDrag_PreviewsImmediatelyAndCommitsOneUndoOperation()
    {
        var (vm, shape) = CreateWorkspace();
        var editor = vm.FillPaintEditor;
        editor.BeginEdit();
        editor.Color = Colors.Blue;
        Assert.Equal(SKColors.Blue, Assert.IsType<SolidColorFill>(shape.Style.Fill).Color);
        Assert.True(vm.HasUnsavedChanges);
        editor.Color = Colors.Green;
        editor.EndEdit();

        vm.UndoCommand.Execute(null);
        Assert.Equal(SKColors.Red, Assert.IsType<SolidColorFill>(shape.Style.Fill).Color);
        Assert.False(vm.HasUnsavedChanges);
        vm.RedoCommand.Execute(null);
        Assert.Equal(new SKColor(0, 128, 0), Assert.IsType<SolidColorFill>(shape.Style.Fill).Color);
    }

    [Fact]
    public void GradientDrag_ReturningToOriginalStateDoesNotDirtyDocument()
    {
        var (vm, shape) = CreateWorkspace();
        shape.Style.Fill = Gradient(SKColors.Red, SKColors.Blue);
        vm.SelectNodes([shape]);
        var editor = vm.FillPaintEditor;
        editor.BeginEdit();
        editor.SelectedStopOffset = 25;
        editor.SelectedStopOffset = 0;
        editor.EndEdit();
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void SelectedStopColorAndOverallOpacityAreIndependent()
    {
        var (vm, shape) = CreateWorkspace();
        shape.Style.Fill = Gradient(SKColors.Red, SKColors.Blue);
        vm.SelectNodes([shape]);
        var editor = vm.FillPaintEditor;
        editor.SelectedStopIndex = 1;
        editor.Color = Color.FromArgb(64, 10, 20, 30);
        vm.FillOpacity = 35;

        var gradient = Assert.IsType<LinearGradientFill>(shape.Style.Fill);
        Assert.Equal(SKColors.Red, gradient.Stops[0].Color);
        Assert.Equal(new SKColor(10, 20, 30, 64), gradient.Stops[1].Color);
        Assert.Equal(.35f, gradient.Opacity);
    }

    [Fact]
    public void StrokeGradient_PreservesWidthDashAndCanSwitchBackToSolid()
    {
        var (vm, shape) = CreateWorkspace();
        shape.Style.Stroke = new StrokeStyle(SKColors.Red, 8, DashArray: [6, 3]);
        vm.SelectNodes([shape]);
        var editor = vm.StrokePaintEditor;
        editor.ModeIndex = 2;
        editor.SelectedStopIndex = 1;
        editor.Color = Colors.Blue;
        vm.StrokeOpacity = 40;
        var stroke = shape.Style.Stroke!;
        Assert.Equal(8, stroke.Width);
        Assert.Equal(new[] { 6f, 3f }, stroke.DashArray);
        Assert.Equal(.4f, Assert.IsType<LinearGradientFill>(stroke.Paint).Opacity);
        editor.ModeIndex = 1;
        Assert.Null(shape.Style.Stroke!.Paint);
        Assert.Equal(SKColors.Blue, shape.Style.Stroke.Color);
    }

    [Fact]
    public void MixedGradients_ChangingOneCoordinatePreservesOtherProperties()
    {
        var (vm, first) = CreateWorkspace();
        first.Style.Fill = Gradient(SKColors.Red, SKColors.Blue);
        var second = new RectangleShapeNode();
        second.Style.Fill = Gradient(SKColors.Green, SKColors.White) with { End = new SKPoint(.5f, .8f), Opacity = .5f };
        vm.ActiveLayer!.Children.Add(second);
        vm.SelectNodes([first, second]);
        Assert.True(vm.FillPaintEditor.IsMixed);
        vm.FillPaintEditor.StartX = 25;
        var a = Assert.IsType<LinearGradientFill>(first.Style.Fill);
        var b = Assert.IsType<LinearGradientFill>(second.Style.Fill);
        Assert.Equal(.25f, a.Start.X);
        Assert.Equal(.25f, b.Start.X);
        Assert.Equal(new SKPoint(.5f, .8f), b.End);
        Assert.Equal(SKColors.Green, b.Stops[0].Color);
        Assert.Equal(.5f, b.Opacity);
    }

    [Fact]
    public void Presets_AreDocumentEditsWithUndoAndSnapshotApplication()
    {
        var (vm, shape) = CreateWorkspace();
        var editor = vm.FillPaintEditor;
        var initialCount = editor.Palette.Count;
        editor.AddPresetCommand.Execute(null);
        Assert.Equal(initialCount + 1, vm.CurrentDocument.ColorPresets.Count);
        Assert.Equal(initialCount + 1, vm.StrokePaintEditor.Palette.Count);
        Assert.True(vm.HasUnsavedChanges);
        var added = editor.Palette[^1];
        editor.MovePresetUpCommand.Execute(added);
        Assert.Equal(SKColors.Red, vm.CurrentDocument.ColorPresets[^2].Color);
        editor.RemovePresetCommand.Execute(editor.Palette[^2]);
        Assert.Equal(initialCount, vm.CurrentDocument.ColorPresets.Count);
        Assert.Equal(SKColors.Red, Assert.IsType<SolidColorFill>(shape.Style.Fill).Color);
        vm.UndoCommand.Execute(null);
        Assert.Equal(initialCount + 1, vm.CurrentDocument.ColorPresets.Count);
    }

    [Fact]
    public void DuplicatePresets_CanRemoveTheActualSelectedEntry()
    {
        var (vm, _) = CreateWorkspace();
        vm.CurrentDocument.ColorPresets.Clear();
        vm.CurrentDocument.ColorPresets.Add(new ColorPreset(SKColors.Red));
        vm.CurrentDocument.ColorPresets.Add(new ColorPreset(SKColors.Blue));
        vm.CurrentDocument.ColorPresets.Add(new ColorPreset(SKColors.Red));
        vm.SelectNodes([]);
        vm.FillPaintEditor.RemovePresetCommand.Execute(vm.FillPaintEditor.Palette[2]);
        Assert.Equal(new[] { SKColors.Red, SKColors.Blue }, vm.CurrentDocument.ColorPresets.Select(p => p.Color));
    }

    [Fact]
    public void PaletteChangesWithoutSelectedShapesAreUndoable()
    {
        var vm = new MainWindowViewModel();
        var count = vm.CurrentDocument.ColorPresets.Count;
        vm.FillPaintEditor.AddPresetCommand.Execute(null);
        Assert.True(vm.HasUnsavedChanges);
        vm.UndoCommand.Execute(null);
        Assert.Equal(count, vm.CurrentDocument.ColorPresets.Count);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void SelectionChangeDuringDragFinishesPreviousShapeHistory()
    {
        var (vm, first) = CreateWorkspace();
        var second = new RectangleShapeNode();
        vm.ActiveLayer!.Children.Add(second);
        vm.FillPaintEditor.BeginEdit();
        vm.FillPaintEditor.Color = Colors.Blue;
        vm.SelectNodes([second]);
        vm.FillPaintEditor.EndEdit();
        vm.UndoCommand.Execute(null);
        Assert.Equal(SKColors.Red, Assert.IsType<SolidColorFill>(first.Style.Fill).Color);
    }

    [Fact]
    public void LockedSelectionDoesNotChangeItsPaint()
    {
        var (vm, shape) = CreateWorkspace();
        shape.IsLocked = true;
        vm.FillPaintEditor.BeginEdit();
        vm.FillPaintEditor.Color = Colors.Blue;
        vm.FillPaintEditor.EndEdit();
        Assert.Equal(SKColors.Red, Assert.IsType<SolidColorFill>(shape.Style.Fill).Color);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void UserSpaceCoordinatesRetainTheirUnits()
    {
        var (vm, shape) = CreateWorkspace();
        shape.Style.Fill = Gradient(SKColors.Red, SKColors.Blue) with { UseObjectBoundingBox = false, End = new SKPoint(250, 75) };
        vm.SelectNodes([shape]);
        Assert.Equal(250, vm.FillPaintEditor.EndX);
        vm.FillPaintEditor.EndX = 350;
        Assert.Equal(350, Assert.IsType<LinearGradientFill>(shape.Style.Fill).End.X);
    }

    [Fact]
    public void StrokeGradientDragReturningToOriginalDoesNotCreateHistory()
    {
        var (vm, shape) = CreateWorkspace();
        shape.Style.Stroke = new StrokeStyle(SKColors.White, Paint: Gradient(SKColors.Red, SKColors.Blue));
        vm.SelectNodes([shape]);
        var editor = vm.StrokePaintEditor;
        editor.BeginEdit();
        editor.Color = Colors.Green;
        editor.Color = Colors.Red;
        editor.EndEdit();
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void ApplyingIdenticalPresetDoesNotCreateHistoryButCanUnifyMixedSelection()
    {
        var (vm, first) = CreateWorkspace();
        vm.FillPaintEditor.AddPresetCommand.Execute(null);
        var preset = vm.FillPaintEditor.Palette[^1];
        // Removing the add operation gives a clean document while retaining the preset VM snapshot.
        vm.UndoCommand.Execute(null);
        vm.SelectNodes([first]);
        vm.FillPaintEditor.SelectPresetCommand.Execute(preset);
        Assert.False(vm.HasUnsavedChanges);
        var second = new RectangleShapeNode();
        second.Style.Fill = new SolidColorFill(SKColors.Blue);
        vm.ActiveLayer!.Children.Add(second);
        vm.SelectNodes([first, second]);
        vm.FillPaintEditor.SelectPresetCommand.Execute(preset);
        Assert.Equal(SKColors.Red, Assert.IsType<SolidColorFill>(second.Style.Fill).Color);
        Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public void StopCrossingAndInsertionPublishOrderDoesNotChangeNeighborColors()
    {
        var (vm, shape) = CreateWorkspace();
        shape.Style.Fill = Gradient(SKColors.Red, SKColors.Blue) with
        {
            Stops = new[] { new GradientStop(0, SKColors.Red), new GradientStop(.3f, SKColors.Green), new GradientStop(1, SKColors.Blue) }
        };
        vm.SelectNodes([shape]);
        var editor = vm.FillPaintEditor;
        editor.SelectedStopIndex = 1;
        editor.BeginEdit();
        // Match the bar's publication sequence: stops first, then selected index.
        editor.GradientStops = new[] { new GradientStop(0, SKColors.Red), new GradientStop(1, SKColors.Blue), new GradientStop(1, SKColors.Green) };
        editor.SelectedStopIndex = 2;
        editor.EndEdit();
        Assert.Equal(Colors.Green, editor.Color);
        Assert.Equal(new[] { SKColors.Red, SKColors.Blue, SKColors.Green }, Assert.IsType<LinearGradientFill>(shape.Style.Fill).Stops.Select(s => s.Color));
        vm.UndoCommand.Execute(null);
        Assert.Equal(new[] { SKColors.Red, SKColors.Green, SKColors.Blue }, Assert.IsType<LinearGradientFill>(shape.Style.Fill).Stops.Select(s => s.Color));
    }

    private sealed class FileDialogs : IEditorFileDialogs
    {
        public string Input { get; set; } = "<svg xmlns='http://www.w3.org/2000/svg' width='100' height='100'/>";
        public MemoryStream Output { get; private set; } = new();
        public Task<bool> ConfirmDiscardAsync() => Task.FromResult(true);
        public Task<EditorFile?> OpenSvgAsync() => Task.FromResult<EditorFile?>(new EditorFile("test.svg", () => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(Input))), () => throw new NotSupportedException()));
        public Task<EditorFile?> SaveSvgAsync(string suggestedName) => Task.FromResult<EditorFile?>(new EditorFile("test.svg", () => throw new NotSupportedException(), () =>
        {
            Output = new MemoryStream();
            return Task.FromResult<Stream>(Output);
        }));
    }

    [Fact]
    public async Task SaveDuringGestureFinishesHistoryAndWritesFinalPreview()
    {
        var (vm, shape) = CreateWorkspace();
        var dialogs = new FileDialogs();
        vm.FileDialogs = dialogs;
        vm.FillPaintEditor.BeginEdit();
        vm.FillPaintEditor.Color = Colors.Blue;
        await vm.SaveDocumentCommand.ExecuteAsync(null);
        vm.FillPaintEditor.EndEdit();
        Assert.False(vm.HasUnsavedChanges);
        var saved = new Vectorer.Svg.SvgSerializer().DeserializeFromString(Encoding.UTF8.GetString(dialogs.Output.ToArray()));
        Assert.Equal(SKColors.Blue, Assert.IsType<SolidColorFill>(Assert.IsType<RectangleShapeNode>(saved.Layers[0].Children[0]).Style.Fill).Color);
        vm.UndoCommand.Execute(null);
        Assert.Equal(SKColors.Red, Assert.IsType<SolidColorFill>(shape.Style.Fill).Color);
        Assert.True(vm.HasUnsavedChanges);
    }

    [Fact]
    public async Task OpeningDocumentsReplacesBothEditorsPalettesIncludingExplicitEmpty()
    {
        var vm = new MainWindowViewModel();
        var dialogs = new FileDialogs();
        vm.FileDialogs = dialogs;
        var document = SvgDocument.CreateDefault();
        document.ColorPresets.Clear();
        document.ColorPresets.Add(new ColorPreset(new SKColor(10, 20, 30, 40), "Sample"));
        var serializer = new Vectorer.Svg.SvgSerializer();
        dialogs.Input = serializer.SerializeToString(document);
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        Assert.Single(vm.FillPaintEditor.Palette);
        Assert.Single(vm.StrokePaintEditor.Palette);
        Assert.Equal("Sample", vm.FillPaintEditor.Palette[0].Name);
        Assert.Equal(Color.FromArgb(40, 10, 20, 30), vm.FillPaintEditor.Palette[0].Color);
        document.ColorPresets.Clear();
        dialogs.Input = serializer.SerializeToString(document);
        await vm.OpenDocumentCommand.ExecuteAsync(null);
        Assert.Empty(vm.FillPaintEditor.Palette);
        Assert.Empty(vm.StrokePaintEditor.Palette);
    }

    [Fact]
    public void HiddenTextureEditorsCannotConvertSolidOrGradientDuringBindingInitialization()
    {
        var (vm, shape) = CreateWorkspace();
        void UpdateHiddenTextureFields()
        {
            // Avalonia's selection control publishes a transient value while its DataContext attaches.
            vm.TexturePattern = TexturePatternKind.Crosshatch;
            vm.TexturePrimaryColor = "#00FF00";
            vm.TextureSecondaryColor = "#FFFFFF";
            vm.TextureScale = 24;
            vm.TextureAngle = 45;
        }
        UpdateHiddenTextureFields();
        Assert.Equal(SKColors.Red, Assert.IsType<SolidColorFill>(shape.Style.Fill).Color);
        Assert.IsType<SolidColorFill>(vm.DefaultCreationStyle.Fill);
        Assert.False(vm.HasUnsavedChanges);

        shape.Style.Fill = Gradient(SKColors.Red, SKColors.Blue);
        vm.SelectNodes([shape]);
        vm.TexturePattern = TexturePatternKind.Dots;
        vm.TexturePrimaryColor = "#FFFF00";
        vm.TextureScale = 32;
        Assert.IsType<LinearGradientFill>(shape.Style.Fill);
        Assert.False(vm.HasUnsavedChanges);
    }

    [Fact]
    public void TextureSwatchesFollowDocumentPaletteAndDoNotRebuildDuringColorEdits()
    {
        var vm = new MainWindowViewModel();
        var swatches = vm.ColorSwatches;
        var notifications = 0;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(vm.ColorSwatches)) notifications++; };
        vm.FillPaintEditor.Color = Color.FromArgb(40, 10, 20, 30);
        Assert.Same(swatches, vm.ColorSwatches);
        Assert.Equal(0, notifications);
        vm.FillPaintEditor.AddPresetCommand.Execute(null);
        Assert.NotSame(swatches, vm.ColorSwatches);
        Assert.Equal(vm.CurrentDocument.ColorPresets.Count, vm.ColorSwatches.Count);
        Assert.Equal(new SKColor(10, 20, 30, 40).ToString(), vm.ColorSwatches[^1].Value);
        Assert.Equal(1, notifications);
        vm.UndoCommand.Execute(null);
        Assert.Equal(swatches.Count, vm.ColorSwatches.Count);
        Assert.Equal(2, notifications);
    }

}
