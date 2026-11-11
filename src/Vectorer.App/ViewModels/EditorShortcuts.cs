using Avalonia.Input;
using Vectorer.App.Tools;
using Vectorer.Core.Geometry;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    /// <summary>Called only after native text/numeric controls have had priority.</summary>
    public bool TryHandleEditorShortcut(Key key, KeyModifiers modifiers)
    {
        if (IsBusy) return false;
        if (key == Key.Escape) { HandleEscape(); return true; }
        if (key == Key.Enter && _canvasTool is BezierPenTool pen) { pen.Finish(); return true; }
        if (IsInteracting) return false;
        var shift = modifiers.HasFlag(KeyModifiers.Shift);
        if (modifiers.HasFlag(KeyModifiers.Control))
        {
            if (modifiers.HasFlag(KeyModifiers.Alt))
            {
                if (key != Key.C || shift) return false;
                StrokeToPath(); return true;
            }
            if (shift && key is not (Key.Z or Key.C or Key.X or Key.D or Key.U or Key.I or Key.R or Key.N or Key.OemSemicolon or Key.OemPlus or Key.Add)) return false;
            switch (key)
            {
                case Key.Z: if (shift) Redo(); else Undo(); break;
                case Key.Y: Redo(); break;
                case Key.OemPlus: case Key.Add: ZoomIn(); break;
                case Key.OemMinus: case Key.Subtract: ZoomOut(); break;
                case Key.D1: ResetZoom(); break;
                case Key.D0: FitCanvas(); break;
                case Key.OemQuotes: ToggleGrid(); break;
                case Key.OemSemicolon when shift: ToggleSnap(); break;
                case Key.C when shift: ObjectToPath(); break;
                case Key.C: CopySelectionCommand.Execute(null); break;
                case Key.X when shift: BooleanXor(); break;
                case Key.X: CutSelectionCommand.Execute(null); break;
                case Key.V: PasteCommand.Execute(null); break;
                case Key.D when shift: BooleanDifferenceActiveLayerShapes(); break;
                case Key.D: DuplicateSelection(); break;
                case Key.U when shift: BooleanUnionActiveLayerShapes(); break;
                case Key.I when shift: BooleanIntersection(); break;
                case Key.R when shift: BooleanReverseDifference(); break;
                case Key.A: SelectAll(); break;
                case Key.N when shift: NewLayer(); break;
                case Key.E: if (CanMergeLayer) MergeLayerDown(); break;
                case Key.G: if (shift) UngroupSelection(); else GroupSelection(); break;
                case Key.K: TogglePathClosed(); break;
                case Key.L: SimplifyPath(); break;
                default: return false;
            }
        }
        else if (modifiers is KeyModifiers.None or KeyModifiers.Shift)
        {
            switch (key)
            {
                case Key.Home: ChangeOrder("front"); break;
                case Key.End: ChangeOrder("back"); break;
                case Key.PageUp: ChangeOrder("forward"); break;
                case Key.PageDown: ChangeOrder("backward"); break;
                case Key.H when shift: FlipHorizontal(); break;
                case Key.V when shift: FlipVertical(); break;
                case Key.Multiply: case Key.D8 when shift: ChooseTool(DrawingTool.Star); break;
                case Key.N: case Key.A: ChooseTool(DrawingTool.Node); break;
                case Key.B: ChooseTool(DrawingTool.Bezier); break;
                case Key.T: ChooseTool(DrawingTool.Text); break;
                case Key.Enter: EnterGroup(); break;
                case Key.V: case Key.S: ChooseTool(DrawingTool.Select); break;
                case Key.R: ChooseTool(DrawingTool.Rectangle); break;
                case Key.E: ChooseTool(DrawingTool.Ellipse); break;
                case Key.Y: ChooseTool(DrawingTool.Polygon); break;
                case Key.P: ChooseTool(DrawingTool.Freehand); break;
                case Key.H: ChooseTool(DrawingTool.Pan); break;
                case Key.Delete: case Key.Back: DeleteSelection(); break;
                default: return false;
            }
        }
        else return false;
        return true;
    }
}
