using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.App.ViewModels;

namespace Vectorer.App.Tools;

/// <summary>UI-independent pointer contract in document coordinates. Tools own transient edits.</summary>
public interface ICanvasTool
{
    bool IsInteracting { get; }
    ShapeNode? PreviewShape { get; }
    SKMatrix PreviewParent { get; }
    SKRect? Marquee { get; }
    void Begin(SKPoint point, bool shift);
    void Move(SKPoint point, bool shift, bool alt);
    void End();
    void Cancel();
}

public abstract class CanvasToolBase(MainWindowViewModel workspace) : ICanvasTool
{
    protected MainWindowViewModel Workspace { get; } = workspace;
    public virtual bool IsInteracting => false;
    public virtual ShapeNode? PreviewShape { get; protected set; }
    public virtual SKMatrix PreviewParent { get; protected set; } = SKMatrix.CreateIdentity();
    public virtual SKRect? Marquee { get; protected set; }
    public abstract void Begin(SKPoint point, bool shift);
    public virtual void Move(SKPoint point, bool shift, bool alt) { }
    public virtual void End() { }
    public virtual void Cancel() { }
}

/// <summary>Viewport pan is handled by the control without changing document coordinates.</summary>
public sealed class PanTool : ICanvasTool
{
    public bool IsInteracting => false;
    public ShapeNode? PreviewShape => null;
    public SKMatrix PreviewParent => SKMatrix.CreateIdentity();
    public SKRect? Marquee => null;
    public void Begin(SKPoint point, bool shift) { }
    public void Move(SKPoint point, bool shift, bool alt) { }
    public void End() { }
    public void Cancel() { }
}
