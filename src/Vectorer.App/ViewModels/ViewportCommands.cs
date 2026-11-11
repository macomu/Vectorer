using CommunityToolkit.Mvvm.Input;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    public event Action? FitCanvasRequested;
    [RelayCommand] private void FitCanvas() { if (!IsInteracting) FitCanvasRequested?.Invoke(); }
    [RelayCommand] private void ToggleGrid() => ShowGrid = !ShowGrid;
    [RelayCommand] private void ToggleSnap() => SnapToGrid = !SnapToGrid;
}
