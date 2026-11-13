using System.Globalization;
using Vectorer.App.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
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
    private SvgDocument? _colorSwatchesDocument;
    private ColorPreset[] _colorSwatchPresetSnapshot = [];
    private IReadOnlyList<ColorSwatch> _colorSwatches = [];
    public IReadOnlyList<ColorSwatch> ColorSwatches
    {
        get
        {
            SyncColorSwatches(notify: false);
            return _colorSwatches;
        }
    }

    private void SyncColorSwatches(bool notify = true)
    {
        if (ReferenceEquals(_colorSwatchesDocument, CurrentDocument)
            && _colorSwatchPresetSnapshot.SequenceEqual(CurrentDocument.ColorPresets)) return;
        _colorSwatchesDocument = CurrentDocument;
        _colorSwatchPresetSnapshot = CurrentDocument.ColorPresets.ToArray();
        _colorSwatches = _colorSwatchPresetSnapshot.Select(p => new ColorSwatch(p.Color.ToString())).ToArray();
        if (notify) OnPropertyChanged(nameof(ColorSwatches));
    }
    public IReadOnlyList<string> FillKinds { get; } = ["なし", "単色", "グラデーション", "テクスチャ"];
    public IReadOnlyList<TexturePatternKind> TexturePatterns { get; } = [TexturePatternKind.Checkerboard, TexturePatternKind.DiagonalStripes, TexturePatternKind.Crosshatch, TexturePatternKind.Dots];
    public IReadOnlyList<SKStrokeCap> StrokeCaps { get; } = [SKStrokeCap.Butt, SKStrokeCap.Round, SKStrokeCap.Square];
    public IReadOnlyList<SKStrokeJoin> StrokeJoins { get; } = [SKStrokeJoin.Miter, SKStrokeJoin.Round, SKStrokeJoin.Bevel];
    public IReadOnlyList<string> DashOptions { get; } = ["実線", "破線", "点線", "一点鎖線", "カスタム"];

    [ObservableProperty] private string? _fillKind = "単色";
    [ObservableProperty] private decimal? _fillOpacity = 100m;
    [ObservableProperty] private TexturePatternKind? _texturePattern = TexturePatternKind.Checkerboard;
    [ObservableProperty] private string _texturePrimaryColor = "#38BDF8";
    [ObservableProperty] private string _textureSecondaryColor = "#0F172A";
    [ObservableProperty] private decimal? _textureScale = 16m;
    [ObservableProperty] private decimal? _textureAngle = 0m;

    [ObservableProperty] private decimal? _strokeOpacity = 100m;
    [ObservableProperty] private SKStrokeCap? _strokeCap = SKStrokeCap.Round;
    [ObservableProperty] private SKStrokeJoin? _strokeJoin = SKStrokeJoin.Round;
    [ObservableProperty] private decimal? _strokeMiterLimit = 4m;
    [ObservableProperty] private string _customDashArray = "";
    [ObservableProperty] private decimal? _strokeDashPhase = 0m;

    [RelayCommand] private void SetFillColor(string value) => FillColor = value;
    [RelayCommand] private void SetStrokeColor(string value) => StrokeColor = value;
    [RelayCommand] private void SetTexturePrimaryColor(string value) => TexturePrimaryColor = value;
    [RelayCommand] private void SetTextureSecondaryColor(string value) => TextureSecondaryColor = value;

    private bool _syncingEditors;
    public bool IsSolidFill => FillKind == "単色";
    public bool IsGradientFill => FillKind == "グラデーション";
    public bool IsTextureFill => FillKind == "テクスチャ";
    public bool HasParametricPolygon => SelectedNodes.Count == 1 && SelectedNodes[0] is PolygonShapeNode { PolygonKind: not PolygonKind.Freeform };
    public string StyleTarget => SelectedNodes.OfType<ShapeNode>().Any() ? L10n.Ui_SelectedShapes : L10n.Ui_NextShape;

    public string StyleTargetDisplay => L10n.Format(new LocalizedMessage(nameof(Strings.Ui_EditingTarget), StyleTarget));

    public string FillColorDisplay
    {
        get => FillColor switch
        {
            "texture" => L10n.Get(nameof(Strings.Choice_Texture)),
            "gradient" => L10n.Get("Paint_LinearGradient"),
            "none" => L10n.Ui_None,
            _ => FillColor
        };
        set => FillColor = value == L10n.Get(nameof(Strings.Choice_Texture)) ? "texture"
            : value == L10n.Get("Paint_LinearGradient") ? "gradient"
            : value == L10n.Ui_None ? "none" : value;
    }

    private void SyncSelectionEditors()
    {
        if (_syncingEditors) return;
        _syncingEditors = true;
        try
        {
            var styles = SelectedNodes.OfType<ShapeNode>().Select(n => n.Style).ToArray();
            if (styles.Length == 0) styles = [DefaultCreationStyle];

            var fillKinds = styles.Select(s => s.Fill switch
            {
                null => "なし",
                SolidColorFill => "単色",
                LinearGradientFill => "グラデーション",
                TextureFill => "テクスチャ",
                _ => "なし"
            }).Distinct().ToArray();
            FillKind = fillKinds.Length == 1 ? fillKinds[0] : null;

            var fills = styles.Select(s => s.Fill is SolidColorFill solid ? solid.Color.ToString() : s.Fill is null ? "none" : s.Fill is LinearGradientFill ? "gradient" : "texture").Distinct().ToArray();
            FillColor = fills.Length == 1 ? fills[0] : "";

            var fillOpacities = styles.Where(s => s.Fill is not null).Select(s => s.Fill switch
            {
                SolidColorFill solid => Math.Round(solid.Color.Alpha / 255m * 100m, 1),
                TextureFill tex => Math.Round(tex.PrimaryColor.Alpha / 255m * 100m, 1),
                LinearGradientFill gradient => Math.Round((decimal)gradient.Opacity * 100m, 1),
                _ => 100m
            }).Distinct().ToArray();
            FillOpacity = styles.All(s => s.Fill is null) ? 0m
                : fillOpacities.Length == 1 && styles.All(s => s.Fill is not null) ? fillOpacities[0] : null;

            var textures = styles.Select(s => s.Fill).OfType<TextureFill>().ToArray();
            if (textures.Length > 0)
            {
                var allTexture = textures.Length == styles.Length;
                var patterns = textures.Select(t => t.PatternKind).Distinct().ToArray();
                TexturePattern = allTexture && patterns.Length == 1 ? patterns[0] : null;
                var primaries = textures.Select(t => t.PrimaryColor.ToString()).Distinct().ToArray();
                TexturePrimaryColor = allTexture && primaries.Length == 1 ? primaries[0] : "";
                var secondaries = textures.Select(t => t.SecondaryColor.ToString()).Distinct().ToArray();
                TextureSecondaryColor = allTexture && secondaries.Length == 1 ? secondaries[0] : "";
                var scales = textures.Select(t => (decimal)t.Scale).Distinct().ToArray();
                TextureScale = allTexture && scales.Length == 1 ? scales[0] : null;
                var angles = textures.Select(t => (decimal)t.AngleDegrees).Distinct().ToArray();
                TextureAngle = allTexture && angles.Length == 1 ? angles[0] : null;
            }

            var strokes = styles.Select(s => s.Stroke is { } stroke ? stroke.Paint is LinearGradientFill ? "gradient" : stroke.Color.ToString() : "none").Distinct().ToArray();
            StrokeColor = strokes.Length == 1 ? strokes[0] : "";
            var widths = styles.Select(s => (decimal)(s.Stroke?.Width ?? 0)).Distinct().ToArray();
            StrokeWidth = widths.Length == 1 ? widths[0] : null;
            var dashes = styles.Select(s => DashName(s.Stroke?.DashArray)).Distinct().ToArray();
            StrokeDash = dashes.Length == 1 ? dashes[0] : null;

            var allHaveStroke = styles.All(s => s.Stroke is not null);
            var strokeOpacities = styles.Where(s => s.Stroke is not null).Select(s => s.Stroke!.Paint is LinearGradientFill gradient ? Math.Round((decimal)gradient.Opacity * 100m, 1) : Math.Round(s.Stroke.Color.Alpha / 255m * 100m, 1)).Distinct().ToArray();
            StrokeOpacity = styles.All(s => s.Stroke is null) ? 0m
                : allHaveStroke && strokeOpacities.Length == 1 ? strokeOpacities[0] : null;

            var caps = styles.Where(s => s.Stroke is not null).Select(s => s.Stroke!.Cap).Distinct().ToArray();
            StrokeCap = allHaveStroke && caps.Length == 1 ? caps[0] : null;

            var joins = styles.Where(s => s.Stroke is not null).Select(s => s.Stroke!.Join).Distinct().ToArray();
            StrokeJoin = allHaveStroke && joins.Length == 1 ? joins[0] : null;

            var miters = styles.Where(s => s.Stroke is not null).Select(s => (decimal)s.Stroke!.MiterLimit).Distinct().ToArray();
            StrokeMiterLimit = allHaveStroke && miters.Length == 1 ? miters[0] : null;

            var phases = styles.Where(s => s.Stroke is not null).Select(s => (decimal)s.Stroke!.DashPhase).Distinct().ToArray();
            StrokeDashPhase = allHaveStroke && phases.Length == 1 ? phases[0] : null;

            var dashStrings = styles.Select(s => FormatDashArray(s.Stroke?.DashArray)).Distinct().ToArray();
            if (dashStrings.Length == 1)
            {
                if (!TryParseDashArray(CustomDashArray, out var currentParsed) || !DashEquals(currentParsed, styles[0].Stroke?.DashArray))
                {
                    CustomDashArray = dashStrings[0];
                }
            }
            else
            {
                CustomDashArray = "";
            }

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
            SyncShapeDetails();
            SyncTransformFields();
            SyncEffectsInspector();
            SyncPaintEditors();
            OnPropertyChanged(nameof(IsSolidFill));
            OnPropertyChanged(nameof(IsGradientFill));
            OnPropertyChanged(nameof(IsTextureFill));
            OnPropertyChanged(nameof(HasParametricPolygon));
            OnPropertyChanged(nameof(IsRectangleTool));
            OnPropertyChanged(nameof(StyleTarget));
            OnPropertyChanged(nameof(StyleTargetDisplay));
        }
        finally { _syncingEditors = false; }
    }

    private void EditStyle(Action<ShapeStyle> edit, string description)
    {
        if (_syncingEditors || IsInteracting || IsBusy) return;
        edit(DefaultCreationStyle);
        if (_paintGesture is { } gesture)
        {
            foreach (var item in gesture.Shapes) edit(item.Shape.Style);
            NotifyDocumentState();
            SyncSelectionEditors();
            RequestRedraw();
            return;
        }
        var selected = GetEditableNodes().Select(e => e.Node).OfType<ShapeNode>().Where(n => SelectedNodes.Contains(n))
            .Where(n =>
            {
                var after = n.Style.Clone();
                edit(after);
                return !StylesEqual(n.Style, after);
            }).ToArray();
        if (selected.Length > 0) ExecuteCommand(new StyleEditCommand(selected, edit, description));
        else { SyncSelectionEditors(); RequestRedraw(); }
    }

    private TextureFill CoerceTextureFill(ShapeFill? fill)
    {
        if (fill is TextureFill tex) return tex;
        var pri = fill is SolidColorFill solid ? solid.Color : (SKColor.TryParse(TexturePrimaryColor, out var p) ? p : new SKColor(56, 189, 248));
        var sec = SKColor.TryParse(TextureSecondaryColor, out var c) ? c : new SKColor(15, 23, 42);
        return new TextureFill(
            TexturePattern ?? TexturePatternKind.Checkerboard,
            pri,
            sec,
            (float)Math.Clamp(TextureScale ?? 16m, 4m, 128m),
            (float)(TextureAngle ?? 0m));
    }

    private StrokeStyle EnsureStroke(StrokeStyle? stroke)
    {
        if (stroke is not null) return stroke;
        var color = SKColor.TryParse(StrokeColor, out var parsed) ? parsed : new SKColor(226, 232, 240);
        var width = (float)Math.Clamp(StrokeWidth ?? 2m, 0.5m, 1000m);
        return new StrokeStyle(
            color,
            width,
            StrokeCap ?? SKStrokeCap.Round,
            StrokeJoin ?? SKStrokeJoin.Round,
            (float)(StrokeMiterLimit ?? 4m));
    }

    partial void OnFillKindChanged(string? value)
    {
        OnPropertyChanged(nameof(IsSolidFill));
        OnPropertyChanged(nameof(IsGradientFill));
        OnPropertyChanged(nameof(IsTextureFill));
        if (_syncingEditors || value is null) return;
        switch (value)
        {
            case "なし":
                EditStyle(s => s.Fill = null, "Remove fill");
                break;
            case "単色":
            {
                var fallback = SKColor.TryParse(FillColor, out var parsed) ? parsed : new SKColor(56, 141, 255);
                EditStyle(s =>
                {
                    if (s.Fill is SolidColorFill) return;
                    var color = s.Fill is TextureFill tex ? tex.PrimaryColor : fallback;
                    s.Fill = new SolidColorFill(color);
                }, "Fill kind: solid");
                break;
            }
            case "テクスチャ":
                EditStyle(s => s.Fill = CoerceTextureFill(s.Fill), "Fill kind: texture");
                break;
            case "グラデーション":
                EditStyle(s =>
                {
                    if (s.Fill is LinearGradientFill) return;
                    var color = s.Fill is SolidColorFill solid ? solid.Color : s.Fill is TextureFill texture ? texture.PrimaryColor : SKColors.Black;
                    s.Fill = new LinearGradientFill(new[] { new GradientStop(0, color), new GradientStop(1, color.WithAlpha(0)) }, new SKPoint(0, 0), new SKPoint(1, 0));
                }, "Fill kind: gradient");
                break;
        }
    }

    partial void OnFillColorChanged(string value)
    {
        OnPropertyChanged(nameof(FillColorDisplay));
        if (_syncingEditors || string.IsNullOrWhiteSpace(value) || value is "texture" or "gradient") return;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) EditStyle(s => s.Fill = null, "Remove fill");
        else if (SKColor.TryParse(value, out var color)) EditStyle(s => s.Fill = new SolidColorFill(color), "Fill color");
    }

    partial void OnFillOpacityChanged(decimal? value)
    {
        if (_syncingEditors || value is not (>= 0m and <= 100m)) return;
        var alpha = (byte)Math.Clamp(Math.Round((double)value.Value / 100.0 * 255.0), 0, 255);
        var fallback = SKColor.TryParse(FillColor, out var parsed) ? parsed : new SKColor(56, 141, 255);
        EditStyle(s =>
        {
            s.Fill = s.Fill switch
            {
                SolidColorFill solid => new SolidColorFill(solid.Color.WithAlpha(alpha)),
                TextureFill tex => tex with { PrimaryColor = tex.PrimaryColor.WithAlpha(alpha) },
                LinearGradientFill gradient => gradient with { Opacity = (float)(value.Value / 100) },
                _ => new SolidColorFill(fallback.WithAlpha(alpha))
            };
        }, "Fill opacity");
    }

    partial void OnTexturePatternChanged(TexturePatternKind? value)
    {
        // Hidden texture controls can publish transient selections during DataContext attachment.
        // Only an explicit paint-type change may convert another fill into a texture.
        if (_syncingEditors || !IsTextureFill || value is null) return;
        EditStyle(s => s.Fill = CoerceTextureFill(s.Fill) with { PatternKind = value.Value }, "Texture pattern");
    }

    partial void OnTexturePrimaryColorChanged(string value)
    {
        if (_syncingEditors || !IsTextureFill || string.IsNullOrWhiteSpace(value) || !SKColor.TryParse(value, out var color)) return;
        EditStyle(s => s.Fill = CoerceTextureFill(s.Fill) with { PrimaryColor = color }, "Texture primary color");
    }

    partial void OnTextureSecondaryColorChanged(string value)
    {
        if (_syncingEditors || !IsTextureFill || string.IsNullOrWhiteSpace(value) || !SKColor.TryParse(value, out var color)) return;
        EditStyle(s => s.Fill = CoerceTextureFill(s.Fill) with { SecondaryColor = color }, "Texture secondary color");
    }

    partial void OnTextureScaleChanged(decimal? value)
    {
        if (_syncingEditors || !IsTextureFill || value is not (>= 4m and <= 128m)) return;
        var scale = (float)value.Value;
        EditStyle(s => s.Fill = CoerceTextureFill(s.Fill) with { Scale = scale }, "Texture scale");
    }

    partial void OnTextureAngleChanged(decimal? value)
    {
        if (_syncingEditors || !IsTextureFill || value is not (>= -360m and <= 360m)) return;
        var angle = (float)value.Value;
        EditStyle(s => s.Fill = CoerceTextureFill(s.Fill) with { AngleDegrees = angle }, "Texture angle");
    }

    partial void OnStrokeColorChanged(string value)
    {
        if (_syncingEditors || string.IsNullOrWhiteSpace(value)) return;
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase)) EditStyle(s => s.Stroke = null, "Remove stroke");
        else if (SKColor.TryParse(value, out var color)) EditStyle(s => s.Stroke = EnsureStroke(s.Stroke) with { Color = color, Paint = null }, "Stroke color");
    }

    partial void OnStrokeWidthChanged(decimal? value)
    {
        if (value is >= 0 and <= 1000) EditStyle(s => s.Stroke = EnsureStroke(s.Stroke) with { Width = (float)value.Value }, "Stroke width");
    }

    partial void OnStrokeOpacityChanged(decimal? value)
    {
        if (_syncingEditors || value is not (>= 0m and <= 100m)) return;
        var alpha = (byte)Math.Clamp(Math.Round((double)value.Value / 100.0 * 255.0), 0, 255);
        EditStyle(s =>
        {
            var st = EnsureStroke(s.Stroke);
            s.Stroke = st.Paint is LinearGradientFill gradient
                ? st with { Paint = gradient with { Opacity = (float)(value.Value / 100) } }
                : st with { Color = st.Color.WithAlpha(alpha) };
        }, "Stroke opacity");
    }

    partial void OnStrokeCapChanged(SKStrokeCap? value)
    {
        if (_syncingEditors || value is null) return;
        EditStyle(s => s.Stroke = EnsureStroke(s.Stroke) with { Cap = value.Value }, "Stroke cap");
    }

    partial void OnStrokeJoinChanged(SKStrokeJoin? value)
    {
        if (_syncingEditors || value is null) return;
        EditStyle(s => s.Stroke = EnsureStroke(s.Stroke) with { Join = value.Value }, "Stroke join");
    }

    partial void OnStrokeMiterLimitChanged(decimal? value)
    {
        if (_syncingEditors || value is not (>= 1m and <= 100m)) return;
        EditStyle(s => s.Stroke = EnsureStroke(s.Stroke) with { MiterLimit = (float)value.Value }, "Stroke miter limit");
    }

    partial void OnStrokeDashChanged(string? value)
    {
        if (_syncingEditors || value is null) return;
        if (value == "カスタム")
        {
            var custom = TryParseDashArray(CustomDashArray, out var parsed) && parsed is not null ? parsed : [8f, 4f];
            EditStyle(s => s.Stroke = EnsureStroke(s.Stroke) with { DashArray = custom }, "Stroke dash");
            return;
        }
        float[]? dash = value switch { "破線" => [8, 4], "点線" => [1, 4], "一点鎖線" => [8, 4, 1, 4], _ => null };
        EditStyle(s => s.Stroke = EnsureStroke(s.Stroke) with { DashArray = dash }, "Stroke dash");
    }

    partial void OnCustomDashArrayChanged(string value)
    {
        if (_syncingEditors) return;
        if (TryParseDashArray(value, out var dash))
        {
            EditStyle(s => s.Stroke = EnsureStroke(s.Stroke) with { DashArray = dash }, "Custom stroke dash");
        }
    }

    [RelayCommand]
    private void ApplyCustomDash()
    {
        if (TryParseDashArray(CustomDashArray, out var dash))
        {
            EditStyle(s => s.Stroke = EnsureStroke(s.Stroke) with { DashArray = dash }, "Custom stroke dash");
        }
    }

    partial void OnStrokeDashPhaseChanged(decimal? value)
    {
        if (_syncingEditors || value is not (>= -1000m and <= 1000m)) return;
        EditStyle(s => s.Stroke = EnsureStroke(s.Stroke) with { DashPhase = (float)value.Value }, "Stroke dash phase");
    }

    private static string DashName(float[]? dash) => dash is null or { Length: 0 } ? "実線" : dash.SequenceEqual(new float[] { 8, 4 }) ? "破線"
        : dash.SequenceEqual(new float[] { 1, 4 }) ? "点線" : dash.SequenceEqual(new float[] { 8, 4, 1, 4 }) ? "一点鎖線" : "カスタム";

    private static string FormatDashArray(float[]? dash) =>
        dash is { Length: > 0 } ? string.Join(", ", dash.Select(v => v.ToString("G9", CultureInfo.InvariantCulture))) : "";

    private static bool DashEquals(float[]? a, float[]? b)
    {
        if (a is null or { Length: 0 } && b is null or { Length: 0 }) return true;
        if (a is null || b is null) return false;
        return a.SequenceEqual(b);
    }

    private static bool TryParseDashArray(string? raw, out float[]? dash)
    {
        dash = null;
        if (string.IsNullOrWhiteSpace(raw) || raw.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tokens = raw.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries);
        var values = new List<float>(tokens.Length);
        foreach (var token in tokens)
        {
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) || !float.IsFinite(v) || v < 0f)
            {
                return false;
            }
            values.Add(v);
        }

        if (values.Count == 0 || values.All(v => v <= 0f))
        {
            dash = null;
            return true;
        }

        if (values.Count % 2 == 1)
        {
            values.AddRange(values.ToArray());
        }

        dash = values.ToArray();
        return true;
    }

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
    partial void OnCornerRadiusChanged(decimal value) => EditCorners(false, value);
    partial void OnCornerRadiusYChanged(decimal value) => EditCorners(true, value);
    private void EditCorners(bool yAxis, decimal value)
    {
        if (_syncingEditors || IsInteracting) return;
        if (SelectedNodes.Count != 1 || SelectedNodes[0] is not RectangleShapeNode rect)
        {
            if (LinkCorners)
            { _syncingEditors = true; CornerRadius = CornerRadiusY = value; _syncingEditors = false; }
            return;
        }
        var before = (rect.CornerRadiusX, rect.CornerRadiusY);
        var x = LinkCorners || !yAxis ? Math.Max(0, (float)value) : before.CornerRadiusX;
        var y = LinkCorners || yAxis ? Math.Max(0, (float)value) : before.CornerRadiusY;
        ExecuteCommand(new DelegateDocumentCommand("Rectangle corners",
            () => { rect.CornerRadiusX = x; rect.CornerRadiusY = y; },
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
