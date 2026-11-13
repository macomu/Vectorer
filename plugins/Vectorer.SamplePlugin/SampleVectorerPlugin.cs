using System.Globalization;
using SkiaSharp;
using Vectorer.Abstractions.Commands;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Plugins;
using Vectorer.Abstractions.Styling;

namespace Vectorer.SamplePlugin;

/// <summary>
/// Reference implementation of an external Vectorer plugin DLL.
/// Demonstrates adding menu items, toolbar actions, manipulating the SVG scene graph,
/// and registering a custom SkiaSharp SkSL shader effect.
/// </summary>
public sealed class SampleVectorerPlugin : IVectorerPlugin, IUiCultureAwarePlugin
{
    public const string SunsetWaveShaderId = "sample.sunset_wave";
    private CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    private bool IsJapanese => string.Equals(_uiCulture.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase);

    public PluginMetadata Metadata { get; } = new(
        Id: "vectorer.sample.plugin",
        Name: "Vectorer Sample Extensions",
        Version: "1.0.0",
        Author: "Vectorer Team",
        Description: "Adds a Sunset Wave Skia shader and quick star-burst generator tools.");

    public void OnUiCultureChanged(CultureInfo culture)
    {
        _uiCulture = culture;
    }

    public void Initialize(IPluginContext context)
    {
        var shaderProvider = new SunsetWaveShaderProvider(() => IsJapanese);
        context.RegisterShaderProvider(shaderProvider);
        context.RegisterMenuContribution(new ApplySunsetShaderMenuContribution(() => IsJapanese));
        context.RegisterToolbarContribution(new AddStarburstToolbarContribution(() => IsJapanese));
    }
}

/// <summary>
/// Custom SkiaSharp runtime shader (SkSL) that renders a smooth procedural gradient wave per-object.
/// </summary>
public sealed class SunsetWaveShaderProvider(Func<bool>? isJapanese = null) : IShaderEffectProvider
{
    public string ShaderId => SampleVectorerPlugin.SunsetWaveShaderId;
    public string DisplayName => isJapanese?.Invoke() == true ? "サンセットウェーブ (SkSL)" : "Sunset Wave (SkSL)";

    public string SkslSource => """
        uniform float4 u_bounds;
        uniform float u_frequency;

        half4 main(float2 coord) {
            return half4(0.98, 0.38, 0.35, 1.0);
        }
        """;

    public IReadOnlyDictionary<string, float> DefaultUniforms { get; } = new Dictionary<string, float>
    {
        ["u_frequency"] = 10.0f
    };

    public SKShader? CreateShader(SKRect objectBounds, IReadOnlyDictionary<string, float>? customUniforms = null)
    {
        var frequency = DefaultUniforms["u_frequency"];
        if (customUniforms is not null && customUniforms.TryGetValue("u_frequency", out var customFreq))
        {
            frequency = customFreq;
        }

        using var gradientShader = SKShader.CreateLinearGradient(
            new SKPoint(objectBounds.Left, objectBounds.Top),
            new SKPoint(objectBounds.Right, objectBounds.Bottom),
            [new SKColor(250, 97, 89), new SKColor(245, 158, 11), new SKColor(64, 46, 166)],
            [0.0f, 0.5f, 1.0f],
            SKShaderTileMode.Clamp);

        var freqScale = Math.Clamp(frequency * 0.002f, 0.005f, 0.2f);
        using var noiseShader = SKShader.CreatePerlinNoiseTurbulence(
            baseFrequencyX: freqScale,
            baseFrequencyY: freqScale,
            numOctaves: 2,
            seed: 42f);

        return SKShader.CreateCompose(gradientShader, noiseShader, SKBlendMode.Overlay);
    }
}

internal sealed class ApplySunsetShaderMenuContribution(Func<bool>? isJapanese = null) : IMenuContribution
{
    public string Id => "sample.menu.apply_sunset_shader";
    public string MenuPath => "Extensions/Shaders";
    public string Title => isJapanese?.Invoke() == true ? "選択にサンセットウェーブ シェーダーを適用" : "Apply Sunset Wave Shader to Selection";
    public string? ShortcutGesture => "Ctrl+Shift+W";

    public void Execute(IEditorWorkspace workspace)
    {
        var targets = workspace.SelectedNodes.OfType<ShapeNode>().ToList();
        if (targets.Count == 0 && workspace.ActiveLayer is not null)
        {
            targets = workspace.ActiveLayer.Children.OfType<ShapeNode>().ToList();
        }

        if (targets.Count == 0)
        {
            return;
        }

        var previousShaders = targets.ToDictionary(n => n, n => n.ShaderEffect);
        var descriptor = new ShaderEffectDescriptor(SampleVectorerPlugin.SunsetWaveShaderId);

        workspace.ExecuteCommand(new PluginActionCommand(
            "Apply Sunset Wave Shader",
            execute: () =>
            {
                foreach (var shape in targets)
                {
                    shape.ShaderEffect = descriptor;
                }
                workspace.RequestRedraw();
            },
            undo: () =>
            {
                foreach (var (shape, prev) in previousShaders)
                {
                    shape.ShaderEffect = prev;
                }
                workspace.RequestRedraw();
            }));
    }
}

internal sealed class AddStarburstToolbarContribution(Func<bool>? isJapanese = null) : IToolbarContribution
{
    public string Id => "sample.toolbar.add_starburst";
    public string Title => isJapanese?.Invoke() == true ? "スターバースト (プラグイン)" : "Starburst (Plugin)";
    public string? Tooltip => isJapanese?.Invoke() == true ? "サンセットウェーブ SkSL シェーダー付きの8角星形を追加" : "Adds a concave 8-point starburst with the Sunset Wave SkSL shader";
    public string? IconPathData => "M 12 2 L 15 9 L 22 12 L 15 15 L 12 22 L 9 15 L 2 12 L 9 9 Z";

    public void Execute(IEditorWorkspace workspace)
    {
        var targetLayer = workspace.ActiveLayer ?? workspace.CurrentDocument.Layers.FirstOrDefault();
        if (targetLayer is null)
        {
            return;
        }

        var star = PolygonShapeNode.CreateConcaveStar(
            center: new SKPoint(workspace.CurrentDocument.Width * 0.5f, workspace.CurrentDocument.Height * 0.5f),
            outerRadius: 110f,
            innerRadius: 48f,
            pointsCount: 8);

        star.Name = isJapanese?.Invoke() == true ? "プラグイン スターバースト" : "Plugin Starburst";
        star.ShaderEffect = new ShaderEffectDescriptor(SampleVectorerPlugin.SunsetWaveShaderId);
        star.Style.Stroke = new StrokeStyle(new SKColor(255, 240, 200), 2.5f);

        workspace.ExecuteCommand(new PluginActionCommand(
            "Add Plugin Starburst",
            execute: () =>
            {
                targetLayer.Children.Add(star);
                workspace.SelectNodes([star]);
                workspace.RequestRedraw();
            },
            undo: () =>
            {
                targetLayer.Children.Remove(star);
                workspace.RequestRedraw();
            }));
    }
}

internal sealed class PluginActionCommand(string description, Action execute, Action undo) : IDocumentCommand
{
    public string Description { get; } = description;
    public void Execute() => execute();
    public void Undo() => undo();
}
