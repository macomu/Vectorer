using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Core.Commands;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private PaintEditorViewModel? _fillPaintEditor;
    private PaintEditorViewModel? _strokePaintEditor;
    public PaintEditorViewModel FillPaintEditor => _fillPaintEditor ??= new(this, stroke: false);
    public PaintEditorViewModel StrokePaintEditor => _strokePaintEditor ??= new(this, stroke: true);

    private sealed class PaintEditSession(SvgDocument document, ShapeNode[] shapes, ShapeStyle defaultStyle)
    {
        public SvgDocument Document { get; } = document;
        public (ShapeNode Shape, ShapeStyle Before)[] Shapes { get; } = shapes.Select(s => (s, s.Style.Clone())).ToArray();
        public ShapeStyle DefaultBefore { get; } = defaultStyle.Clone();
        public bool HasChanges => Shapes.Any(s => !StylesEqual(s.Before, s.Shape.Style));
    }

    private PaintEditSession? _paintGesture;

    internal void SyncPaintEditors()
    {
        SyncColorSwatches();
        var styles = SelectedNodes.OfType<ShapeNode>().Select(n => n.Style).ToArray();
        if (styles.Length == 0) styles = [DefaultCreationStyle];
        FillPaintEditor.Synchronize(styles.Select(s => s.Fill).ToArray());
        StrokePaintEditor.Synchronize(styles.Select(s => s.Stroke is { } stroke ? stroke.Paint ?? new SolidColorFill(stroke.Color) : null).ToArray());
    }

    internal void EditPaint(bool stroke, Func<ShapeFill?, ShapeFill?> change, string description)
    {
        EditStyle(style =>
        {
            if (!stroke) style.Fill = change(style.Fill);
            else
            {
                var previous = style.Stroke is { } outline ? outline.Paint ?? new SolidColorFill(outline.Color) : null;
                var paint = change(previous);
                style.Stroke = paint switch
                {
                    null => null,
                    SolidColorFill solid => EnsureStroke(style.Stroke) with { Color = solid.Color, Paint = null },
                    _ => EnsureStroke(style.Stroke) with { Paint = paint }
                };
            }
        }, description);
    }

    internal void BeginPaintEdit()
    {
        if (_paintGesture is not null || IsInteracting || IsBusy) return;
        var shapes = GetEditableNodes().Select(e => e.Node).OfType<ShapeNode>().Where(SelectedNodes.Contains).ToArray();
        _paintGesture = new PaintEditSession(CurrentDocument, shapes, DefaultCreationStyle);
    }

    /// <summary>Commits a live color/stop drag as one reversible operation.</summary>
    public void FinishPaintEdit()
    {
        var session = _paintGesture;
        if (session is null) return;
        _paintGesture = null;
        if (!ReferenceEquals(session.Document, CurrentDocument) || !session.HasChanges)
        {
            NotifyDocumentState();
            return;
        }
        var changes = session.Shapes.Select(s => (s.Shape, s.Before, After: s.Shape.Style.Clone())).ToArray();
        var defaultAfter = DefaultCreationStyle.Clone();
        ExecuteCommand(new DelegateDocumentCommand("Edit paint", () =>
        {
            foreach (var change in changes) change.Shape.Style = change.After.Clone();
            DefaultCreationStyle.Fill = defaultAfter.Fill;
            DefaultCreationStyle.Stroke = defaultAfter.Stroke;
        }, () =>
        {
            foreach (var change in changes) change.Shape.Style = change.Before.Clone();
            DefaultCreationStyle.Fill = session.DefaultBefore.Fill;
            DefaultCreationStyle.Stroke = session.DefaultBefore.Stroke;
        }));
    }

    internal void EditPalette(Action<List<ColorPreset>> edit, string description)
    {
        if (IsInteracting || IsBusy) return;
        FinishPaintEdit();
        var document = CurrentDocument;
        var before = document.ColorPresets.ToArray();
        var edited = before.ToList();
        edit(edited);
        var after = edited.ToArray();
        if (before.SequenceEqual(after)) return;
        void Apply(ColorPreset[] presets)
        {
            document.ColorPresets.Clear();
            document.ColorPresets.AddRange(presets);
        }
        ExecuteCommand(new DelegateDocumentCommand(description, () => Apply(after), () => Apply(before)));
    }

    private static bool StylesEqual(ShapeStyle a, ShapeStyle b) => PaintEditorViewModel.PaintEquals(a.Fill, b.Fill)
        && (a.Stroke is { } first && b.Stroke is { } second
            ? PaintEditorViewModel.PaintEquals(first.Paint, second.Paint) && (first with { Paint = second.Paint }) == second
            : Equals(a.Stroke, b.Stroke));
}
