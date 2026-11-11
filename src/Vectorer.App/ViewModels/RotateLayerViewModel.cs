using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.ViewModels;

public enum LayerRotationCenterMode
{
    CanvasCenter,
    LayerBoundsCenter
}

public sealed record LayerRotationCenterOption(LayerRotationCenterMode Mode, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class RotateLayerViewModel : ObservableObject
{
    private readonly MainWindowViewModel _workspace;
    private readonly LayerNode _layer;
    private readonly SKMatrix _originalTransform;

    [ObservableProperty] private decimal _degrees;
    [ObservableProperty] private LayerRotationCenterOption _selectedCenterOption;
    [ObservableProperty] private bool _livePreview = true;

    public IReadOnlyList<LayerRotationCenterOption> CenterOptions { get; } =
    [
        new(LayerRotationCenterMode.CanvasCenter, "キャンバスの中心"),
        new(LayerRotationCenterMode.LayerBoundsCenter, "レイヤー内要素のバウンディングボックス中心")
    ];

    public LayerRotationCenterMode CenterMode
    {
        get => SelectedCenterOption.Mode;
        set
        {
            var match = CenterOptions.FirstOrDefault(o => o.Mode == value);
            if (match is not null)
            {
                SelectedCenterOption = match;
            }
        }
    }

    public RotateLayerViewModel(MainWindowViewModel workspace, LayerNode layer, decimal initialDegrees = 0m)
    {
        _workspace = workspace;
        _layer = layer;
        _originalTransform = layer.Transform;
        _selectedCenterOption = CenterOptions[0];
        _degrees = Math.Clamp(initialDegrees, -360m, 360m);
        UpdatePreview();
    }

    public SKPoint GetRotationCenter()
    {
        if (CenterMode == LayerRotationCenterMode.LayerBoundsCenter)
        {
            var layerWorld = SKMatrix.Concat(_workspace.CurrentDocument.GetViewportTransform(), _originalTransform);
            var bounds = ComputeLayerElementsBounds(_layer, layerWorld);
            if (!bounds.IsEmpty && bounds.Width >= 0 && bounds.Height >= 0)
            {
                return new SKPoint(bounds.MidX, bounds.MidY);
            }
        }

        return new SKPoint(_workspace.CurrentDocument.Width * 0.5f, _workspace.CurrentDocument.Height * 0.5f);
    }

    public SKMatrix ComputeRotatedTransform()
    {
        var center = GetRotationCenter();
        var delta = SKMatrix.CreateRotationDegrees((float)Degrees, center.X, center.Y);
        return CanvasGeometry.ApplyWorldTransform(_originalTransform, _workspace.CurrentDocument.GetViewportTransform(), delta);
    }

    partial void OnDegreesChanged(decimal value) => UpdatePreview();
    partial void OnSelectedCenterOptionChanged(LayerRotationCenterOption value)
    {
        OnPropertyChanged(nameof(CenterMode));
        UpdatePreview();
    }
    partial void OnLivePreviewChanged(bool value) => UpdatePreview();

    private void UpdatePreview()
    {
        if (LivePreview)
        {
            _layer.Transform = ComputeRotatedTransform();
        }
        else
        {
            _layer.Transform = _originalTransform;
        }
        _workspace.RequestRedraw(false);
    }

    public void CancelPreview()
    {
        _layer.Transform = _originalTransform;
        _workspace.RequestRedraw(false);
    }

    public void Commit()
    {
        _layer.Transform = _originalTransform;
        var after = ComputeRotatedTransform();
        if (after == _originalTransform)
        {
            _workspace.RequestRedraw(false);
            return;
        }

        _workspace.ExecuteCommand(new DelegateDocumentCommand(
            "Rotate layer",
            () => _layer.Transform = after,
            () => _layer.Transform = _originalTransform));
    }

    private static SKRect ComputeLayerElementsBounds(ContainerNode container, SKMatrix parentWorld)
    {
        var combined = SKRect.Empty;
        var hasAny = false;

        void Visit(ContainerNode current, SKMatrix currentWorld)
        {
            foreach (var child in current.Children)
            {
                if (!child.IsVisible) continue;
                switch (child)
                {
                    case ContainerNode group:
                        Visit(group, SKMatrix.Concat(currentWorld, group.Transform));
                        break;
                    case ShapeNode shape:
                    {
                        using var path = CanvasGeometry.BuildWorldPath(shape, currentWorld);
                        if (!path.IsEmpty)
                        {
                            var b = path.TightBounds;
                            combined = !hasAny ? b : SKRect.Union(combined, b);
                            hasAny = true;
                        }
                        break;
                    }
                }
            }
        }

        Visit(container, parentWorld);
        return hasAny ? combined : SKRect.Empty;
    }
}
