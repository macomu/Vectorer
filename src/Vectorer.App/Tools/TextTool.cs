using SkiaSharp;
using Vectorer.App.ViewModels;

namespace Vectorer.App.Tools;

public sealed class TextTool(MainWindowViewModel workspace) : CanvasToolBase(workspace)
{
    public override void Begin(SKPoint point, bool shift) => Workspace.BeginTextEdit(point);
}
