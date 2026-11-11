using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Services;
using Vectorer.Rendering.Export;

namespace Vectorer.App.ViewModels;

public enum PngExportMode
{
    Document,
    Selection,
    LayersBatch
}

public sealed partial class PngExportViewModel : ObservableObject, IDisposable
{
    private readonly PngExportService _exportService;
    private bool _updatingDimensions;

    public SvgDocument Document { get; }
    public IReadOnlyList<SceneNode> SelectedNodes { get; }

    [ObservableProperty] private PngExportMode _mode = PngExportMode.Document;
    [ObservableProperty] private int _targetWidth;
    [ObservableProperty] private int _targetHeight;
    [ObservableProperty] private bool _lockAspect = true;
    [ObservableProperty] private float? _activePresetScale = 1.0f;
    [ObservableProperty] private bool _transparentBackground = true;
    [ObservableProperty] private string _batchFilePrefix = "export";
    [ObservableProperty] private bool _visibleLayersOnly = true;
    [ObservableProperty] private Bitmap? _previewBitmap;

    public float BaseWidth { get; private set; }
    public float BaseHeight { get; private set; }
    public SKRect? SelectionBounds { get; private set; }
    public byte[] PreviewPngBytes { get; private set; } = [];

    public bool IsDocumentMode
    {
        get => Mode == PngExportMode.Document;
        set { if (value) Mode = PngExportMode.Document; }
    }

    public bool IsSelectionMode
    {
        get => Mode == PngExportMode.Selection;
        set { if (value) Mode = PngExportMode.Selection; }
    }

    public bool IsLayersBatchMode
    {
        get => Mode == PngExportMode.LayersBatch;
        set { if (value) Mode = PngExportMode.LayersBatch; }
    }

    public bool IsCustomScale => ActivePresetScale is null;

    public int ExportableLayerCount => VisibleLayersOnly
        ? Document.Layers.Count(l => l.IsVisible)
        : Document.Layers.Count;

    public bool CanExport => Mode switch
    {
        PngExportMode.Document => BaseWidth > 0f && BaseHeight > 0f && TargetWidth > 0 && TargetHeight > 0,
        PngExportMode.Selection => SelectionBounds is { Width: > 0f, Height: > 0f } && TargetWidth > 0 && TargetHeight > 0,
        PngExportMode.LayersBatch => ExportableLayerCount > 0 && BaseWidth > 0f && BaseHeight > 0f && TargetWidth > 0 && TargetHeight > 0 && !string.IsNullOrWhiteSpace(BatchFilePrefix),
        _ => false
    };

    public string BaseDimensionsLabel => Mode == PngExportMode.Selection && SelectionBounds is null
        ? "出力可能な選択要素がありません"
        : $"{MathF.Round(BaseWidth)} × {MathF.Round(BaseHeight)} px";

    public string ScaleStatusLabel => ActivePresetScale is { } preset
        ? $"{preset.ToString("0.##", CultureInfo.InvariantCulture)}x"
        : BaseWidth > 0f
            ? $"カスタム ({(TargetWidth / BaseWidth).ToString("0.##", CultureInfo.InvariantCulture)}x)"
            : "カスタム";

    public string OutputSizeLabel
    {
        get
        {
            if (!CanExport)
            {
                if (Mode == PngExportMode.Selection)
                {
                    return "出力可能な選択要素がありません";
                }
                if (Mode == PngExportMode.LayersBatch && ExportableLayerCount == 0)
                {
                    return "出力対象レイヤーがありません";
                }
                return "出力設定が無効です";
            }

            return $"出力サイズ: {TargetWidth} × {TargetHeight} px";
        }
    }

    public PngExportViewModel(SvgDocument document, IReadOnlyList<SceneNode> selectedNodes, PngExportService exportService)
    {
        Document = document ?? throw new ArgumentNullException(nameof(document));
        SelectedNodes = selectedNodes ?? throw new ArgumentNullException(nameof(selectedNodes));
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));

        RecalculateBaseDimensionsAndScale(1.0f);
    }

    public PngExportOptions CreateExportOptions() => new(
        TargetWidth: Math.Max(1, TargetWidth),
        TargetHeight: Math.Max(1, TargetHeight),
        Scale: ActivePresetScale ?? (BaseWidth > 0f ? TargetWidth / BaseWidth : 1.0f),
        IncludeBackground: !TransparentBackground);

    [RelayCommand]
    public void ApplyScalePresetFromText(string? scaleText)
    {
        if (float.TryParse(scaleText, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) && scale > 0f)
        {
            ApplyScalePreset(scale);
        }
    }

    public void ApplyScalePreset(float scale)
    {
        if (scale <= 0f) return;
        _updatingDimensions = true;
        try
        {
            ActivePresetScale = scale;
            if (BaseWidth > 0f && BaseHeight > 0f)
            {
                TargetWidth = Math.Max(1, (int)MathF.Round(BaseWidth * scale));
                TargetHeight = Math.Max(1, (int)MathF.Round(BaseHeight * scale));
            }
        }
        finally
        {
            _updatingDimensions = false;
        }
        NotifyStateAndRefreshPreview();
    }

    partial void OnModeChanged(PngExportMode value)
    {
        OnPropertyChanged(nameof(IsDocumentMode));
        OnPropertyChanged(nameof(IsSelectionMode));
        OnPropertyChanged(nameof(IsLayersBatchMode));
        RecalculateBaseDimensionsAndScale(ActivePresetScale ?? 1.0f);
    }

    partial void OnTargetWidthChanged(int value)
    {
        if (_updatingDimensions) return;
        _updatingDimensions = true;
        try
        {
            var clamped = Math.Clamp(value, 1, 32768);
            if (TargetWidth != clamped) TargetWidth = clamped;
            if (LockAspect && BaseWidth > 0f && BaseHeight > 0f)
            {
                TargetHeight = Math.Max(1, (int)MathF.Round(clamped * (BaseHeight / BaseWidth)));
            }
            ActivePresetScale = null;
        }
        finally
        {
            _updatingDimensions = false;
        }
        NotifyStateAndRefreshPreview();
    }

    partial void OnTargetHeightChanged(int value)
    {
        if (_updatingDimensions) return;
        _updatingDimensions = true;
        try
        {
            var clamped = Math.Clamp(value, 1, 32768);
            if (TargetHeight != clamped) TargetHeight = clamped;
            if (LockAspect && BaseWidth > 0f && BaseHeight > 0f)
            {
                TargetWidth = Math.Max(1, (int)MathF.Round(clamped * (BaseWidth / BaseHeight)));
            }
            ActivePresetScale = null;
        }
        finally
        {
            _updatingDimensions = false;
        }
        NotifyStateAndRefreshPreview();
    }

    partial void OnLockAspectChanged(bool value)
    {
        if (value && BaseWidth > 0f && BaseHeight > 0f && TargetWidth > 0)
        {
            _updatingDimensions = true;
            try
            {
                TargetHeight = Math.Max(1, (int)MathF.Round(TargetWidth * (BaseHeight / BaseWidth)));
            }
            finally
            {
                _updatingDimensions = false;
            }
            NotifyStateAndRefreshPreview();
        }
    }

    partial void OnTransparentBackgroundChanged(bool value) => NotifyStateAndRefreshPreview();
    partial void OnVisibleLayersOnlyChanged(bool value) => NotifyStateAndRefreshPreview();
    partial void OnBatchFilePrefixChanged(string value) => NotifyStateAndRefreshPreview();

    private void RecalculateBaseDimensionsAndScale(float scale)
    {
        SelectionBounds = PngExportService.TryGetSelectionRenderBounds(Document, SelectedNodes);

        if (Mode == PngExportMode.Selection)
        {
            if (SelectionBounds is { Width: > 0f, Height: > 0f } sb)
            {
                BaseWidth = sb.Width;
                BaseHeight = sb.Height;
            }
            else
            {
                BaseWidth = 0f;
                BaseHeight = 0f;
            }
        }
        else
        {
            BaseWidth = Math.Max(1f, Document.Width);
            BaseHeight = Math.Max(1f, Document.Height);
        }

        _updatingDimensions = true;
        try
        {
            ActivePresetScale = scale;
            if (BaseWidth > 0f && BaseHeight > 0f)
            {
                TargetWidth = Math.Max(1, (int)MathF.Round(BaseWidth * scale));
                TargetHeight = Math.Max(1, (int)MathF.Round(BaseHeight * scale));
            }
            else
            {
                TargetWidth = 0;
                TargetHeight = 0;
            }
        }
        finally
        {
            _updatingDimensions = false;
        }

        NotifyStateAndRefreshPreview();
    }

    private void NotifyStateAndRefreshPreview()
    {
        OnPropertyChanged(nameof(BaseWidth));
        OnPropertyChanged(nameof(BaseHeight));
        OnPropertyChanged(nameof(BaseDimensionsLabel));
        OnPropertyChanged(nameof(IsCustomScale));
        OnPropertyChanged(nameof(ScaleStatusLabel));
        OnPropertyChanged(nameof(ExportableLayerCount));
        OnPropertyChanged(nameof(CanExport));
        OnPropertyChanged(nameof(OutputSizeLabel));
        RefreshPreview();
    }

    public byte[] RenderExportPngBytes()
    {
        if (!CanExport)
        {
            return [];
        }

        var options = CreateExportOptions();
        return Mode switch
        {
            PngExportMode.Selection => _exportService.ExportSelectionToBytes(Document, SelectedNodes, options),
            _ => _exportService.ExportDocumentToBytes(Document, options)
        };
    }

    public void RefreshPreview()
    {
        var previous = PreviewBitmap;
        if (!CanExport || BaseWidth <= 0f || BaseHeight <= 0f)
        {
            PreviewPngBytes = [];
            PreviewBitmap = null;
            previous?.Dispose();
            return;
        }

        const int maxPreviewDimension = 260;
        var aspect = (float)TargetWidth / Math.Max(1, TargetHeight);
        int previewW, previewH;
        if (aspect >= 1f)
        {
            previewW = Math.Clamp(TargetWidth, 1, maxPreviewDimension);
            previewH = Math.Max(1, (int)MathF.Round(previewW / aspect));
        }
        else
        {
            previewH = Math.Clamp(TargetHeight, 1, maxPreviewDimension);
            previewW = Math.Max(1, (int)MathF.Round(previewH * aspect));
        }

        var previewOptions = new PngExportOptions(
            TargetWidth: previewW,
            TargetHeight: previewH,
            IncludeBackground: !TransparentBackground);

        var contentBytes = Mode == PngExportMode.Selection
            ? _exportService.ExportSelectionToBytes(Document, SelectedNodes, previewOptions)
            : _exportService.ExportDocumentToBytes(Document, previewOptions);

        if (contentBytes.Length == 0)
        {
            PreviewPngBytes = [];
            PreviewBitmap = null;
            previous?.Dispose();
            return;
        }

        using var renderedBmp = SKBitmap.Decode(contentBytes);
        using var surface = SKSurface.Create(new SKImageInfo(previewW, previewH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        DrawCheckerboard(canvas, previewW, previewH);
        if (renderedBmp is not null)
        {
            canvas.DrawBitmap(renderedBmp, 0, 0);
        }

        using var snapshot = surface.Snapshot();
        using var pngData = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        PreviewPngBytes = pngData.ToArray();
        try
        {
            using var ms = new MemoryStream(PreviewPngBytes);
            PreviewBitmap = new Bitmap(ms);
        }
        catch (InvalidOperationException)
        {
            PreviewBitmap = null;
        }
        previous?.Dispose();
    }

    private static void DrawCheckerboard(SKCanvas canvas, int width, int height)
    {
        const int cellSize = 10;
        using var darkPaint = new SKPaint { Color = new SKColor(30, 41, 59), Style = SKPaintStyle.Fill };
        using var lightPaint = new SKPaint { Color = new SKColor(51, 65, 85), Style = SKPaintStyle.Fill };
        canvas.Clear(darkPaint.Color);
        for (var y = 0; y < height; y += cellSize)
        {
            for (var x = 0; x < width; x += cellSize)
            {
                if (((x / cellSize) + (y / cellSize)) % 2 == 0)
                {
                    canvas.DrawRect(x, y, cellSize, cellSize, lightPaint);
                }
            }
        }
    }

    public void Dispose()
    {
        PreviewBitmap?.Dispose();
        PreviewBitmap = null;
    }
}
