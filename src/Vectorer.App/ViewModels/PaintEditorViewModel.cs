using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Styling;
using Vectorer.App.Localization;
using GradientStop = Vectorer.Abstractions.Styling.GradientStop;

namespace Vectorer.App.ViewModels;

public sealed class ColorPresetViewModel(ColorPreset preset)
{
    public ColorPreset Preset { get; } = preset;
    public Color Color => Color.FromArgb(Preset.Color.Alpha, Preset.Color.Red, Preset.Color.Green, Preset.Color.Blue);
    public IBrush Brush => new SolidColorBrush(Color);
    public string Name => Preset.Name ?? Preset.Color.ToString();
}

/// <summary>Edits one paint target; preset colors are snapshots, independent of shape styles.</summary>
public sealed partial class PaintEditorViewModel : ObservableObject
{
    private readonly MainWindowViewModel _workspace;
    private readonly bool _stroke;
    private bool _synchronizing;
    private bool _applying;

    public AppLocalization L10n => _workspace.L10n;
    public bool SupportsTexture => !_stroke;
    public ObservableCollection<ColorPresetViewModel> Palette { get; } = [];
    [ObservableProperty] private int _modeIndex = 1;
    [ObservableProperty] private bool _isMixed;
    [ObservableProperty] private Color _color = Colors.Black;
    [ObservableProperty] private IReadOnlyList<GradientStop> _gradientStops = new[] { new GradientStop(0, SKColors.Black), new GradientStop(1, SKColors.White) };
    [ObservableProperty] private int _selectedStopIndex;
    [ObservableProperty] private decimal _selectedStopOffset;
    [ObservableProperty] private decimal _startX;
    [ObservableProperty] private decimal _startY;
    [ObservableProperty] private decimal _endX = 100;
    [ObservableProperty] private decimal _endY;
    [ObservableProperty] private bool _usesObjectBoundingBox = true;
    [ObservableProperty] private decimal _gradientOpacity = 100;
    [ObservableProperty] private bool _isEditingPalette;

    public bool IsGradient => ModeIndex == 2;
    public bool CanDeleteSelectedStop => IsGradient && GradientStops.Count > 2 && SelectedStopIndex >= 0 && SelectedStopIndex < GradientStops.Count;

    internal PaintEditorViewModel(MainWindowViewModel workspace, bool stroke)
    {
        _workspace = workspace;
        _stroke = stroke;
    }

    public void BeginEdit() => _workspace.BeginPaintEdit();
    public void EndEdit() => _workspace.FinishPaintEdit();

    internal void Synchronize(IReadOnlyList<ShapeFill?> paints)
    {
        SyncPalette();
        if (_applying || paints.Count == 0) return;
        _synchronizing = true;
        try
        {
            var first = paints[0];
            IsMixed = paints.Skip(1).Any(p => !PaintEquals(first, p));
            var mode = PaintMode(first);
            ModeIndex = paints.All(p => PaintMode(p) == mode) ? mode : -1;
            if (first is LinearGradientFill gradient)
            {
                GradientStops = gradient.Stops;
                SelectedStopIndex = Math.Clamp(SelectedStopIndex, 0, Math.Max(0, GradientStops.Count - 1));
                UsesObjectBoundingBox = gradient.UseObjectBoundingBox;
                var factor = UsesObjectBoundingBox ? 100m : 1m;
                StartX = SafeDecimal(gradient.Start.X) * factor;
                StartY = SafeDecimal(gradient.Start.Y) * factor;
                EndX = SafeDecimal(gradient.End.X) * factor;
                EndY = SafeDecimal(gradient.End.Y) * factor;
                GradientOpacity = SafeDecimal(gradient.Opacity) * 100;
                SyncSelectedStop();
            }
            else if (first is SolidColorFill solid) Color = ToColor(solid.Color);
            else if (first is TextureFill texture) Color = ToColor(texture.PrimaryColor);
        }
        finally { _synchronizing = false; }
        NotifyGradientProperties();
    }

    private static decimal SafeDecimal(float value) => float.IsFinite(value) ? (decimal)Math.Clamp(value, -1e20f, 1e20f) : 0;
    private static int PaintMode(ShapeFill? fill) => fill switch { null => 0, SolidColorFill => 1, LinearGradientFill => 2, TextureFill => 3, _ => 0 };

    internal static bool PaintEquals(ShapeFill? left, ShapeFill? right)
    {
        if (left is LinearGradientFill a && right is LinearGradientFill b)
            return a.Stops.SequenceEqual(b.Stops) && (a with { Stops = b.Stops }) == b;
        return Equals(left, right);
    }

    private void Apply(Func<ShapeFill?, ShapeFill?> change, string description)
    {
        if (_synchronizing) return;
        _applying = true;
        try { _workspace.EditPaint(_stroke, change, description); }
        finally { _applying = false; }
        // Read back clamped/normalized values without triggering another mutation.
        _workspace.SyncPaintEditors();
    }

    private LinearGradientFill NewGradient(ShapeFill? previous)
    {
        var color = previous is SolidColorFill solid ? solid.Color : ToSkColor(Color);
        return new LinearGradientFill(new[] { new GradientStop(0, color), new GradientStop(1, color.WithAlpha(0)) }, new SKPoint(0, 0), new SKPoint(1, 0));
    }

    partial void OnModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsGradient));
        if (_synchronizing || value < 0 || value > (SupportsTexture ? 3 : 2)) return;
        Apply(previous => value switch
        {
            0 => null,
            1 => previous is SolidColorFill ? previous : new SolidColorFill(previous is LinearGradientFill gradient && gradient.Stops.Count > 0 ? gradient.Stops[Math.Clamp(SelectedStopIndex, 0, gradient.Stops.Count - 1)].Color : ToSkColor(Color)),
            2 => previous is LinearGradientFill ? previous : NewGradient(previous),
            _ => previous is TextureFill ? previous : new TextureFill(TexturePatternKind.Checkerboard, ToSkColor(Color), SKColors.White)
        }, "Paint type");
        NotifyGradientProperties();
    }

    partial void OnColorChanged(Color value)
    {
        ApplyColor(value);
    }

    private void ApplyColor(Color value)
    {
        var color = ToSkColor(value);
        Apply(previous => previous switch
        {
            LinearGradientFill gradient when gradient.Stops.Count > 0 => gradient with
            {
                Stops = gradient.Stops.Select((stop, i) => i == Math.Clamp(SelectedStopIndex, 0, gradient.Stops.Count - 1) ? stop with { Color = color } : stop).ToArray()
            },
            TextureFill texture => texture with { PrimaryColor = color },
            _ => new SolidColorFill(color)
        }, "Paint color");
    }

    partial void OnGradientStopsChanged(IReadOnlyList<GradientStop> value)
    {
        if (_synchronizing || value.Count < 2 || value.Any(s => !float.IsFinite(s.Offset))) return;
        // Sorting is stable, so coincident stops retain their intentional hard edge order.
        var stops = value.Select(s => s with { Offset = Math.Clamp(s.Offset, 0, 1) }).OrderBy(s => s.Offset).ToArray();
        Apply(previous => (previous as LinearGradientFill ?? NewGradient(previous)) with { Stops = stops }, "Gradient stops");
        NotifyGradientProperties();
    }

    partial void OnSelectedStopIndexChanged(int value)
    {
        var wasSyncing = _synchronizing;
        _synchronizing = true;
        try { SyncSelectedStop(); }
        finally { _synchronizing = wasSyncing; }
        NotifyGradientProperties();
    }

    private void SyncSelectedStop()
    {
        if (SelectedStopIndex < 0 || SelectedStopIndex >= GradientStops.Count) return;
        var stop = GradientStops[SelectedStopIndex];
        Color = ToColor(stop.Color);
        SelectedStopOffset = SafeDecimal(stop.Offset) * 100;
    }

    partial void OnSelectedStopOffsetChanged(decimal value)
    {
        if (_synchronizing || SelectedStopIndex < 0 || SelectedStopIndex >= GradientStops.Count || value is < 0 or > 100) return;
        var selected = SelectedStopIndex;
        var indexed = GradientStops.Select((stop, index) => (Stop: index == selected ? stop with { Offset = (float)(value / 100) } : stop, Index: index)).OrderBy(s => s.Stop.Offset).ToArray();
        _synchronizing = true;
        try { SelectedStopIndex = Array.FindIndex(indexed, s => s.Index == selected); }
        finally { _synchronizing = false; }
        GradientStops = indexed.Select(s => s.Stop).ToArray();
    }

    partial void OnStartXChanged(decimal value) => ChangeGeometry(g => g with { Start = new SKPoint(Coordinate(value), g.Start.Y) });
    partial void OnStartYChanged(decimal value) => ChangeGeometry(g => g with { Start = new SKPoint(g.Start.X, Coordinate(value)) });
    partial void OnEndXChanged(decimal value) => ChangeGeometry(g => g with { End = new SKPoint(Coordinate(value), g.End.Y) });
    partial void OnEndYChanged(decimal value) => ChangeGeometry(g => g with { End = new SKPoint(g.End.X, Coordinate(value)) });
    private float Coordinate(decimal value) => (float)(UsesObjectBoundingBox ? value / 100 : value);
    private void ChangeGeometry(Func<LinearGradientFill, LinearGradientFill> change)
    {
        if (!IsGradient) return;
        Apply(previous => change(previous as LinearGradientFill ?? NewGradient(previous)), "Gradient geometry");
    }

    partial void OnUsesObjectBoundingBoxChanged(bool value) => ChangeGeometry(g => g with { UseObjectBoundingBox = value });
    partial void OnGradientOpacityChanged(decimal value)
    {
        if (value is >= 0 and <= 100) ChangeGeometry(g => g with { Opacity = (float)(value / 100) });
    }

    [RelayCommand(CanExecute = nameof(CanDeleteSelectedStop))]
    private void DeleteSelectedStop()
    {
        var index = SelectedStopIndex;
        var stops = GradientStops.Where((_, i) => i != index).ToArray();
        _synchronizing = true;
        try { SelectedStopIndex = Math.Min(index, stops.Length - 1); }
        finally { _synchronizing = false; }
        GradientStops = stops;
    }

    [RelayCommand] private void SelectPreset(ColorPresetViewModel? preset)
    {
        if (preset is not null && !IsEditingPalette) ApplyColor(preset.Color);
    }
    [RelayCommand] private void AddPreset() => _workspace.EditPalette(presets => presets.Add(new ColorPreset(ToSkColor(Color))), "Add color preset");
    [RelayCommand] private void TogglePaletteEditing() => IsEditingPalette = !IsEditingPalette;
    [RelayCommand] private void RemovePreset(ColorPresetViewModel? preset)
    {
        if (preset is null) return;
        var index = Palette.IndexOf(preset);
        if (index >= 0) _workspace.EditPalette(presets => presets.RemoveAt(index), "Remove color preset");
    }
    [RelayCommand] private void MovePresetUp(ColorPresetViewModel? preset) => MovePreset(preset, -1);
    [RelayCommand] private void MovePresetDown(ColorPresetViewModel? preset) => MovePreset(preset, 1);
    private void MovePreset(ColorPresetViewModel? preset, int direction)
    {
        if (preset is null) return;
        var index = Palette.IndexOf(preset);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= Palette.Count) return;
        _workspace.EditPalette(presets => (presets[index], presets[target]) = (presets[target], presets[index]), "Reorder color presets");
    }

    internal void SyncPalette()
    {
        var presets = _workspace.CurrentDocument.ColorPresets;
        if (Palette.Select(p => p.Preset).SequenceEqual(presets)) return;
        Palette.Clear();
        foreach (var preset in presets) Palette.Add(new ColorPresetViewModel(preset));
    }

    private void NotifyGradientProperties()
    {
        OnPropertyChanged(nameof(IsGradient));
        OnPropertyChanged(nameof(CanDeleteSelectedStop));
        DeleteSelectedStopCommand.NotifyCanExecuteChanged();
    }

    private static Color ToColor(SKColor color) => Color.FromArgb(color.Alpha, color.Red, color.Green, color.Blue);
    private static SKColor ToSkColor(Color color) => new(color.R, color.G, color.B, color.A);
}
