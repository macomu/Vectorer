using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Core.Commands;

namespace Vectorer.App.ViewModels;

public sealed record ColorSwatch(string Value)
{
    public Avalonia.Media.IBrush Brush => Avalonia.Media.Brush.Parse(Value);
}

public sealed partial class MainWindowViewModel
{
    public IReadOnlyList<ColorSwatch> ColorSwatches { get; } = new[] { "#FFFFFF", "#0F172A", "#64748B", "#EF4444", "#F97316", "#EAB308", "#22C55E", "#14B8A6", "#0EA5E9", "#3B82F6", "#8B5CF6", "#EC4899" }.Select(c => new ColorSwatch(c)).ToArray();
    [RelayCommand] private void SetFillColor(string value) => FillColor = value;
    [RelayCommand] private void SetStrokeColor(string value) => StrokeColor = value;
    private bool _syncingEditors;
    public IReadOnlyList<string> DashOptions { get; } = ["実線", "破線", "点線", "一点鎖線"];
    public bool HasParametricPolygon => SelectedNodes.Count == 1 && SelectedNodes[0] is PolygonShapeNode { PolygonKind: not PolygonKind.Freeform };
    public string StyleTarget => SelectedNodes.OfType<ShapeNode>().Any() ? "選択図形" : "次の図形";

    private void SyncSelectionEditors()
    {
        if (_syncingEditors) return;
        _syncingEditors = true;
        try
        {
            var styles = SelectedNodes.OfType<ShapeNode>().Select(n => n.Style).ToArray();
            if (styles.Length == 0) styles = [DefaultCreationStyle];
            var fills = styles.Select(s => s.Fill is SolidColorFill solid ? solid.Color.ToString() : s.Fill is null ? "none" : "texture").Distinct().ToArray();
            FillColor = fills.Length == 1 ? fills[0] : "";
            var strokes = styles.Select(s => s.Stroke is { IsVisible: true } stroke ? stroke.Color.ToString() : "none").Distinct().ToArray();
            StrokeColor = strokes.Length == 1 ? strokes[0] : "";
            var widths = styles.Select(s => (decimal)(s.Stroke?.Width ?? 0)).Distinct().ToArray();
            StrokeWidth = widths.Length == 1 ? widths[0] : null;
            var dashes = styles.Select(s => DashName(s.Stroke?.DashArray)).Distinct().ToArray();
            StrokeDash = dashes.Length == 1 ? dashes[0] : null;
            if (SelectedNodes.Count == 1)
            {
                if (SelectedNodes[0] is PolygonShapeNode p && p.PolygonKind != PolygonKind.Freeform)
                {
                    PolygonSides = p.PointCount;
                    OuterRadius = (decimal)p.OuterRadius;
                    InnerRatio = p.OuterRadius > 0 ? (decimal)(p.InnerRadius / p.OuterRadius) : 0.45m;
                    PolygonAngle = (decimal)p.StartAngleDegrees;
                }
                if (SelectedNodes[0] is TextShapeNode text)
                {
                    FontFamily = text.FontFamily; FontSize = (decimal)text.FontSize; TextBold = text.IsBold;
                    TextItalic = text.IsItalic; TextAlignment = text.TextAlign; LetterSpacing = (decimal)text.LetterSpacing;
                }
                if (SelectedNodes[0] is RectangleShapeNode rect) CornerRadius = (decimal)rect.CornerRadiusX;
                if (SelectedNodes[0] is ArcShapeNode arc) { ArcStart = (decimal)arc.StartAngleDegrees; ArcSweep = (decimal)arc.SweepAngleDegrees; ArcMode = arc.CloseMode; }
            }
            SyncTransformFields();
            OnPropertyChanged(nameof(HasParametricPolygon));
            OnPropertyChanged(nameof(IsRectangleTool));
            OnPropertyChanged(nameof(StyleTarget));
        }
        finally { _syncingEditors = false; }
    }

    private void EditStyle(Action<ShapeStyle> edit, string description)
    {
        if (_syncingEditors || IsInteracting) return;
        edit(DefaultCreationStyle);
        var selected = GetEditableNodes().Select(e => e.Node).OfType<ShapeNode>().Where(n => SelectedNodes.Contains(n)).ToArray();
        if (selected.Length > 0) ExecuteCommand(new StyleEditCommand(selected, edit, description));
        else RequestRedraw();
    }

    partial void OnFillColorChanged(string value)
    {
        if (_syncingEditors || string.IsNullOrWhiteSpace(value) || value == "texture") return;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) EditStyle(s => s.Fill = null, "Remove fill");
        else if (SKColor.TryParse(value, out var color)) EditStyle(s => s.Fill = new SolidColorFill(color), "Fill color");
    }
    partial void OnStrokeColorChanged(string value)
    {
        if (_syncingEditors || string.IsNullOrWhiteSpace(value)) return;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) EditStyle(s => s.Stroke = null, "Remove stroke");
        else if (SKColor.TryParse(value, out var color)) EditStyle(s => s.Stroke = (s.Stroke ?? new StrokeStyle(color)) with { Color = color }, "Stroke color");
    }
    partial void OnStrokeWidthChanged(decimal? value)
    {
        if (value is >= 0 and <= 1000) EditStyle(s => s.Stroke = (s.Stroke ?? new StrokeStyle(SKColors.Black)) with { Width = (float)value.Value }, "Stroke width");
    }
    partial void OnStrokeDashChanged(string? value)
    {
        if (value is null) return;
        float[]? dash = value switch { "破線" => [8, 4], "点線" => [1, 4], "一点鎖線" => [8, 4, 1, 4], _ => null };
        EditStyle(s => s.Stroke = (s.Stroke ?? new StrokeStyle(SKColors.Black)) with { DashArray = dash }, "Stroke dash");
    }
    private static string DashName(float[]? dash) => dash is null ? "実線" : dash.SequenceEqual(new float[] { 8, 4 }) ? "破線"
        : dash.SequenceEqual(new float[] { 1, 4 }) ? "点線" : dash.SequenceEqual(new float[] { 8, 4, 1, 4 }) ? "一点鎖線" : "カスタム";

    partial void OnPolygonSidesChanged(decimal value) => EditPolygon(p => p.PointCount = Math.Clamp((int)value, 3, 64));
    partial void OnOuterRadiusChanged(decimal value) => EditPolygon(p => { var ratio = p.OuterRadius > 0 ? p.InnerRadius / p.OuterRadius : 0.45f; p.OuterRadius = Math.Max(0, (float)value); p.InnerRadius = p.OuterRadius * ratio; });
    partial void OnInnerRatioChanged(decimal value) => EditPolygon(p => p.InnerRadius = p.OuterRadius * Math.Clamp((float)value, 0.05f, 0.95f));
    partial void OnPolygonAngleChanged(decimal value) => EditPolygon(p => p.StartAngleDegrees = (float)value);
    private void EditPolygon(Action<PolygonShapeNode> edit)
    {
        if (_syncingEditors || IsInteracting || !HasParametricPolygon) return;
        var p = (PolygonShapeNode)SelectedNodes[0];
        var before = (p.PointCount, p.OuterRadius, p.InnerRadius, p.StartAngleDegrees);
        edit(p);
        var after = (p.PointCount, p.OuterRadius, p.InnerRadius, p.StartAngleDegrees);
        void Apply((int Count, float Outer, float Inner, float Angle) state)
        { p.PointCount = state.Count; p.OuterRadius = state.Outer; p.InnerRadius = state.Inner; p.StartAngleDegrees = state.Angle; p.RegeneratePoints(); }
        ExecuteCommand(new DelegateDocumentCommand("Polygon parameters", () => Apply(after), () => Apply(before)));
    }
    partial void OnCornerRadiusChanged(decimal value)
    {
        if (_syncingEditors || IsInteracting || SelectedNodes.Count != 1 || SelectedNodes[0] is not RectangleShapeNode rect) return;
        var before = (rect.CornerRadiusX, rect.CornerRadiusY);
        ExecuteCommand(new DelegateDocumentCommand("Rectangle corners",
            () => rect.CornerRadiusX = rect.CornerRadiusY = Math.Max(0, (float)value),
            () => { rect.CornerRadiusX = before.CornerRadiusX; rect.CornerRadiusY = before.CornerRadiusY; }));
    }

    partial void OnArcStartChanged(decimal value) => EditArc();
    partial void OnArcSweepChanged(decimal value) => EditArc();
    partial void OnArcModeChanged(ArcCloseMode value) => EditArc();
    private void EditArc()
    {
        if (_syncingEditors || IsInteracting || SelectedNodes.Count != 1 || SelectedNodes[0] is not ArcShapeNode arc) return;
        var before = (arc.StartAngleDegrees, arc.SweepAngleDegrees, arc.CloseMode);
        var after = ((float)ArcStart, (float)ArcSweep, ArcMode);
        void Apply((float Start, float Sweep, ArcCloseMode Mode) s) { arc.StartAngleDegrees = s.Start; arc.SweepAngleDegrees = s.Sweep; arc.CloseMode = s.Mode; }
        ExecuteCommand(new DelegateDocumentCommand("Arc parameters", () => Apply(after), () => Apply(before)));
    }

    [RelayCommand] private void ClearFill() => FillColor = "none";
    [RelayCommand] private void ClearStroke() => StrokeColor = "none";
}
