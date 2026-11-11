using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;

namespace Vectorer.App.ViewModels;

public sealed record CanvasPreset(string Name, decimal Width, decimal Height)
{
    public override string ToString() => Name;
}
public sealed partial class DocumentSettingsViewModel : ObservableObject
{
    [ObservableProperty] private decimal _width;
    [ObservableProperty] private decimal _height;
    [ObservableProperty] private string _title = "Untitled";
    [ObservableProperty] private bool _transparent;
    [ObservableProperty] private string _background = "#FFFFFF";
    public IReadOnlyList<CanvasPreset> Presets { get; } = [new("カスタム", 0, 0), new("64 × 64", 64, 64), new("256 × 256", 256, 256), new("512 × 512", 512, 512), new("HD", 1280, 720), new("FHD", 1920, 1080), new("4K", 3840, 2160), new("A4 縦 (96dpi)", 794, 1123), new("A4 横 (96dpi)", 1123, 794)];
    public bool IsValid => Width is > 0 and <= 100000 && Height is > 0 and <= 100000 && (Transparent || SKColor.TryParse(Background, out _));
    public SKColor BackgroundColor => Transparent ? SKColors.Transparent : SKColor.Parse(Background);
    public void ApplyPreset(CanvasPreset? preset) { if (preset is { Width: > 0 }) { Width = preset.Width; Height = preset.Height; } }
    [RelayCommand] private void SwapDimensions() => (Width, Height) = (Height, Width);
    partial void OnWidthChanged(decimal value) => OnPropertyChanged(nameof(IsValid));
    partial void OnHeightChanged(decimal value) => OnPropertyChanged(nameof(IsValid));
    partial void OnTransparentChanged(bool value) => OnPropertyChanged(nameof(IsValid));
    partial void OnBackgroundChanged(string value) => OnPropertyChanged(nameof(IsValid));
}
