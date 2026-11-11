using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Core;
using Vectorer.Core.Commands;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty] private decimal _geometryX;
    [ObservableProperty] private decimal _geometryY;
    [ObservableProperty] private decimal _geometryWidth = 100;
    [ObservableProperty] private decimal _geometryHeight = 100;
    [ObservableProperty] private decimal _radiusX = 50;
    [ObservableProperty] private decimal _radiusY = 50;
    [ObservableProperty] private decimal _cornerRadiusY;
    [ObservableProperty] private bool _linkCorners = true;
    [ObservableProperty] private string _ellipseMode = "楕円";
    [ObservableProperty] private string _polygonMode = "正多角形";
    public IReadOnlyList<string> EllipseModes { get; } = ["楕円", "扇形", "弦", "開弧"];
    public IReadOnlyList<string> PolygonModes { get; } = ["正多角形", "星形"];
    public bool HasRectangle => SelectedNodes.Count == 1 && SelectedNodes[0] is RectangleShapeNode;
    public bool HasRadialShape => SelectedNodes.Count == 1 && SelectedNodes[0] is EllipseShapeNode or ArcShapeNode;
    public bool HasShapeDetails => HasRectangle || HasRadialShape || HasParametricPolygon;
    public bool HasStarPolygon => SelectedNodes.Count == 1 && SelectedNodes[0] is PolygonShapeNode { PolygonKind: PolygonKind.ConcaveStar };
    public bool ShowTransformInputs => HasSelection && !IsNodeTool && !IsTextTool && !HasRadialShape && !HasParametricPolygon;
    public bool ShowRadialInputs => HasRadialShape && !IsNodeTool;
    public bool ShowPolygonInputs => HasParametricPolygon && !IsNodeTool;

    private void SyncShapeDetails()
    {
        if (SelectedNodes.Count == 1)
        {
            switch (SelectedNodes[0])
            {
                case RectangleShapeNode r:
                    GeometryX = (decimal)r.Rect.Left; GeometryY = (decimal)r.Rect.Top;
                    GeometryWidth = (decimal)r.Rect.Width; GeometryHeight = (decimal)r.Rect.Height;
                    CornerRadiusY = (decimal)r.CornerRadiusY;
                    break;
                case EllipseShapeNode e:
                    GeometryX = (decimal)e.Center.X; GeometryY = (decimal)e.Center.Y;
                    RadiusX = (decimal)e.RadiusX; RadiusY = (decimal)e.RadiusY; EllipseMode = "楕円";
                    break;
                case ArcShapeNode a:
                    GeometryX = (decimal)a.Center.X; GeometryY = (decimal)a.Center.Y;
                    RadiusX = (decimal)a.RadiusX; RadiusY = (decimal)a.RadiusY;
                    EllipseMode = a.CloseMode switch { ArcCloseMode.PieSlice => "扇形", ArcCloseMode.Chord => "弦", _ => "開弧" };
                    break;
                case PolygonShapeNode p:
                    GeometryX = (decimal)p.Center.X; GeometryY = (decimal)p.Center.Y;
                    PolygonMode = p.PolygonKind == PolygonKind.ConcaveStar ? "星形" : "正多角形";
                    break;
            }
        }
        OnPropertyChanged(nameof(HasRectangle)); OnPropertyChanged(nameof(HasRadialShape)); OnPropertyChanged(nameof(HasShapeDetails));
        OnPropertyChanged(nameof(HasStarPolygon));
        NotifyPrimaryInputs();
    }
    private void NotifyPrimaryInputs()
    { OnPropertyChanged(nameof(ShowTransformInputs)); OnPropertyChanged(nameof(ShowRadialInputs)); OnPropertyChanged(nameof(ShowPolygonInputs)); }

    [RelayCommand] private void ResetCorners()
    {
        if (SelectedNodes.Count == 1 && SelectedNodes[0] is RectangleShapeNode rect)
        {
            var x = rect.CornerRadiusX; var y = rect.CornerRadiusY;
            ExecuteCommand(new DelegateDocumentCommand("Reset corners", () => rect.CornerRadiusX = rect.CornerRadiusY = 0,
                () => { rect.CornerRadiusX = x; rect.CornerRadiusY = y; }));
        }
        else { _syncingEditors = true; CornerRadius = CornerRadiusY = 0; _syncingEditors = false; }
    }
    [RelayCommand] private void ApplyShapeDetails()
    {
        if (IsInteracting || !HasShapeDetails) return;
        var entry = GetEditableEntries().FirstOrDefault(e => e.Node == SelectedNodes[0]);
        if (entry.Node is not ShapeNode source) return;
        var edited = (ShapeNode)SceneNodeCloner.Clone(source, true);
        var center = new SKPoint((float)GeometryX, (float)GeometryY);
        switch (edited)
        {
            case RectangleShapeNode r:
                if (GeometryWidth <= 0 || GeometryHeight <= 0) return;
                r.Rect = SKRect.Create(center.X, center.Y, (float)GeometryWidth, (float)GeometryHeight);
                break;
            case EllipseShapeNode or ArcShapeNode:
                if (RadiusX <= 0 || RadiusY <= 0) return;
                ShapeNode replacement = EllipseMode == "楕円"
                    ? new EllipseShapeNode { Center = center, RadiusX = (float)RadiusX, RadiusY = (float)RadiusY }
                    : new ArcShapeNode { Center = center, RadiusX = (float)RadiusX, RadiusY = (float)RadiusY,
                        StartAngleDegrees = (float)ArcStart, SweepAngleDegrees = (float)ArcSweep,
                        CloseMode = EllipseMode switch { "扇形" => ArcCloseMode.PieSlice, "弦" => ArcCloseMode.Chord, _ => ArcCloseMode.Open } };
                replacement.Id = edited.Id; replacement.Name = edited.Name; replacement.Transform = edited.Transform;
                replacement.Style = edited.Style; replacement.Opacity = edited.Opacity; replacement.Filter = edited.Filter;
                replacement.BlendMode = edited.BlendMode; replacement.ShaderEffect = edited.ShaderEffect;
                replacement.IsVisible = edited.IsVisible; replacement.IsLocked = edited.IsLocked;
                foreach (var (key, value) in edited.CustomAttributes) replacement.CustomAttributes[key] = value;
                edited = replacement;
                break;
            case PolygonShapeNode p:
                p.Center = center; p.PolygonKind = PolygonMode == "星形" ? PolygonKind.ConcaveStar : PolygonKind.RegularConvex;
                p.RegeneratePoints();
                break;
        }
        ExecuteCommand(new DelegateDocumentCommand("Shape geometry", () => entry.Container.Children[entry.Index] = edited,
            () => entry.Container.Children[entry.Index] = source));
        SelectNodes([edited]);
    }
}
