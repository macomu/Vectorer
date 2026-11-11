using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.Core.Commands;
using Vectorer.Rendering.Shaders;

namespace Vectorer.App.ViewModels;

public sealed record ShaderOptionItem(string Id, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed partial class ShaderUniformItem : ObservableObject
{
    private readonly Action<string, float> _onValueChanged;
    private bool _suppressCallback;

    public string Name { get; }

    [ObservableProperty] private decimal _value;
    [ObservableProperty] private decimal _minimum;
    [ObservableProperty] private decimal _maximum;
    [ObservableProperty] private decimal _increment;

    public ShaderUniformItem(string name, float value, Action<string, float> onValueChanged)
    {
        Name = name;
        _onValueChanged = onValueChanged;
        _suppressCallback = true;
        var span = Math.Max(20f, MathF.Abs(value) * 3f);
        Minimum = (decimal)Math.Min(0f, value - span);
        Maximum = (decimal)Math.Max(100f, value + span);
        Increment = 0.5m;
        Value = (decimal)value;
        _suppressCallback = false;
    }

    public void SetValueSilently(float value)
    {
        _suppressCallback = true;
        if ((decimal)value < Minimum) Minimum = (decimal)value;
        if ((decimal)value > Maximum) Maximum = (decimal)value;
        Value = (decimal)value;
        _suppressCallback = false;
    }

    partial void OnValueChanged(decimal value)
    {
        if (!_suppressCallback)
        {
            _onValueChanged(Name, (float)value);
        }
    }
}

public sealed partial class MainWindowViewModel
{
    public const string NoShaderId = "none";
    public const string CustomShaderId = "custom";

    private const string DefaultCustomSksl = """
        uniform float4 u_bounds;
        uniform float u_frequency;
        uniform float u_mix;

        half4 main(float2 coord) {
            float2 uv = (coord - u_bounds.xy) / max(u_bounds.zw, float2(1.0, 1.0));
            float wave = 0.5 + 0.5 * sin(uv.x * max(u_frequency, 1.0) + uv.y * 4.0);
            float t = clamp(u_mix * 0.01, 0.0, 1.0);
            return half4(mix(float3(0.15, 0.65, 0.95), float3(0.95, 0.35, 0.65), wave * (0.5 + 0.5 * t)), 1.0);
        }
        """;

    private bool _syncingEffects;

    [ObservableProperty] private int _selectedInspectorTab;
    [ObservableProperty] private decimal? _effectOpacity = 100m;
    [ObservableProperty] private LayerBlendMode? _effectBlendMode = LayerBlendMode.Normal;
    [ObservableProperty] private bool _isBlurEnabled;
    [ObservableProperty] private decimal? _blurSigmaX = 0m;
    [ObservableProperty] private decimal? _blurSigmaY = 0m;
    [ObservableProperty] private bool _linkBlurSigma = true;
    [ObservableProperty] private ShaderOptionItem? _selectedShaderOption;
    [ObservableProperty] private string _skslSourceCode = DefaultCustomSksl;
    [ObservableProperty] private string? _skslCompileError;

    public ObservableCollection<ShaderOptionItem> ShaderOptions { get; } = [];
    public ObservableCollection<ShaderUniformItem> ShaderUniforms { get; } = [];

    public string EffectTargetLabel => SelectedNodes.Count > 0
        ? $"選択要素 ({SelectedNodes.Count} 個)"
        : ActiveLayer is not null ? $"レイヤー: {ActiveLayer.Name}" : "対象なし";

    public bool HasActiveShader => SelectedShaderOption is { Id: not NoShaderId };
    public bool HasSkslError => !string.IsNullOrWhiteSpace(SkslCompileError);

    public string? SelectedShaderId
    {
        get => SelectedShaderOption?.Id;
        set
        {
            if (value is null)
            {
                SelectedShaderOption = null;
                return;
            }
            var match = ShaderOptions.FirstOrDefault(o => string.Equals(o.Id, value, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                SelectedShaderOption = match;
            }
        }
    }

    private void RebuildShaderOptions()
    {
        ShaderOptions.Clear();
        ShaderOptions.Add(new ShaderOptionItem(NoShaderId, "なし"));
        foreach (var provider in _shaderRegistry.Providers)
        {
            ShaderOptions.Add(new ShaderOptionItem(provider.ShaderId, provider.DisplayName));
        }
        ShaderOptions.Add(new ShaderOptionItem(CustomShaderId, "カスタム SkSL"));
    }

    public SceneNode[] GetEffectTargets()
    {
        var selected = GetEditableNodes().Select(e => e.Node).Where(SelectedNodes.Contains).ToArray();
        if (selected.Length > 0)
        {
            return selected;
        }

        if (ActiveLayer is { IsLocked: false } layer && CurrentDocument.Layers.Contains(layer))
        {
            return [layer];
        }

        return [];
    }

    private void SyncEffectsInspector()
    {
        if (_syncingEffects) return;
        _syncingEffects = true;
        try
        {
            if (ShaderOptions.Count != _shaderRegistry.Providers.Count + 2)
            {
                RebuildShaderOptions();
            }

            var targets = GetEffectTargets();
            if (targets.Length == 0)
            {
                EffectOpacity = 100m;
                EffectBlendMode = LayerBlendMode.Normal;
                IsBlurEnabled = false;
                BlurSigmaX = 0m;
                BlurSigmaY = 0m;
                SelectedShaderOption = ShaderOptions.FirstOrDefault();
                ShaderUniforms.Clear();
                SkslCompileError = null;
            }
            else
            {
                var opacities = targets.Select(t => Math.Round((decimal)t.Opacity * 100m, 1)).Distinct().ToArray();
                EffectOpacity = opacities.Length == 1 ? opacities[0] : null;

                var blends = targets.Select(t => t.BlendMode).Distinct().ToArray();
                EffectBlendMode = blends.Length == 1 ? blends[0] : null;

                var anyBlur = targets.Any(t => t.Filter is { HasActiveFilter: true });
                IsBlurEnabled = anyBlur;

                var blurXs = targets.Select(t => Math.Round((decimal)(t.Filter?.BlurSigmaX ?? 0f), 2)).Distinct().ToArray();
                var blurYs = targets.Select(t => Math.Round((decimal)(t.Filter?.BlurSigmaY ?? 0f), 2)).Distinct().ToArray();
                BlurSigmaX = blurXs.Length == 1 ? blurXs[0] : null;
                BlurSigmaY = blurYs.Length == 1 ? blurYs[0] : null;

                var shaderIds = targets.Select(t => t.ShaderEffect?.ShaderId ?? NoShaderId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (shaderIds.Length == 1)
                {
                    var id = shaderIds[0];
                    var option = ShaderOptions.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase))
                        ?? ShaderOptions.FirstOrDefault(o => o.Id == CustomShaderId);
                    SelectedShaderOption = option;

                    var firstEffect = targets[0].ShaderEffect;
                    if (firstEffect is not null)
                    {
                        if (!string.IsNullOrWhiteSpace(firstEffect.InlineSksl))
                        {
                            SkslSourceCode = firstEffect.InlineSksl;
                        }
                        else if (_shaderRegistry.TryGetProvider(firstEffect.ShaderId, out var provider) && provider is not null)
                        {
                            SkslSourceCode = provider.SkslSource;
                        }
                        SyncUniformItems(firstEffect);
                    }
                    else
                    {
                        ShaderUniforms.Clear();
                    }
                }
                else
                {
                    SelectedShaderOption = null;
                    ShaderUniforms.Clear();
                }
                SkslCompileError = null;
            }

            OnPropertyChanged(nameof(EffectTargetLabel));
            OnPropertyChanged(nameof(HasActiveShader));
            OnPropertyChanged(nameof(HasSkslError));
            OnPropertyChanged(nameof(SelectedShaderId));
        }
        finally
        {
            _syncingEffects = false;
        }
    }

    private void SyncUniformItems(ShaderEffectDescriptor descriptor)
    {
        var uniformValues = ResolveUniformValues(descriptor);
        if (ShaderUniforms.Select(u => u.Name).SequenceEqual(uniformValues.Keys, StringComparer.Ordinal))
        {
            foreach (var item in ShaderUniforms)
            {
                item.SetValueSilently(uniformValues[item.Name]);
            }
            return;
        }

        ShaderUniforms.Clear();
        foreach (var (name, val) in uniformValues)
        {
            ShaderUniforms.Add(new ShaderUniformItem(name, val, SetShaderUniform));
        }
    }

    private Dictionary<string, float> ResolveUniformValues(ShaderEffectDescriptor descriptor)
    {
        var result = new Dictionary<string, float>(StringComparer.Ordinal);
        if (_shaderRegistry.TryGetProvider(descriptor.ShaderId, out var provider) && provider is not null)
        {
            foreach (var (key, value) in provider.DefaultUniforms)
            {
                result[key] = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(descriptor.InlineSksl))
        {
            foreach (var name in ShaderEffectRegistry.ExtractScalarUniformNames(descriptor.InlineSksl))
            {
                if (!result.ContainsKey(name))
                {
                    result[name] = 10f;
                }
            }
        }

        if (descriptor.Uniforms is not null)
        {
            foreach (var (key, value) in descriptor.Uniforms)
            {
                result[key] = value;
            }
        }

        return result;
    }

    partial void OnEffectOpacityChanged(decimal? value)
    {
        if (_syncingEffects || IsInteracting || value is not (>= 0m and <= 100m)) return;
        var targets = GetEffectTargets();
        if (targets.Length == 0) return;
        var opacity = Math.Clamp((float)(value.Value / 100m), 0f, 1f);
        var snapshots = targets.Select(t => (Node: t, Before: t.Opacity)).ToArray();
        if (snapshots.All(s => Math.Abs(s.Before - opacity) < 0.0005f)) return;
        ExecuteCommand(new DelegateDocumentCommand(
            "Node opacity",
            () => { foreach (var s in snapshots) s.Node.Opacity = opacity; },
            () => { foreach (var s in snapshots) s.Node.Opacity = s.Before; }));
    }

    partial void OnEffectBlendModeChanged(LayerBlendMode? value)
    {
        if (_syncingEffects || IsInteracting || value is null) return;
        var targets = GetEffectTargets();
        if (targets.Length == 0) return;
        var mode = value.Value;
        var snapshots = targets.Select(t => (Node: t, Before: t.BlendMode)).ToArray();
        if (snapshots.All(s => s.Before == mode)) return;
        ExecuteCommand(new DelegateDocumentCommand(
            "Blend mode",
            () => { foreach (var s in snapshots) s.Node.BlendMode = mode; },
            () => { foreach (var s in snapshots) s.Node.BlendMode = s.Before; }));
    }

    partial void OnIsBlurEnabledChanged(bool value)
    {
        if (_syncingEffects || IsInteracting) return;
        var targets = GetEffectTargets();
        if (targets.Length == 0) return;

        if (!value)
        {
            var snapshots = targets.Select(t => (Node: t, Before: t.Filter)).ToArray();
            if (snapshots.All(s => s.Before is null)) return;
            ExecuteCommand(new DelegateDocumentCommand(
                "Disable Gaussian blur",
                () => { foreach (var s in snapshots) s.Node.Filter = null; },
                () => { foreach (var s in snapshots) s.Node.Filter = s.Before; }));
        }
        else
        {
            var sx = BlurSigmaX is > 0m ? (float)BlurSigmaX.Value : 6f;
            var sy = LinkBlurSigma ? sx : (BlurSigmaY is > 0m ? (float)BlurSigmaY.Value : 6f);
            var after = new FilterSettings(sx, sy);
            var snapshots = targets.Select(t => (Node: t, Before: t.Filter)).ToArray();
            ExecuteCommand(new DelegateDocumentCommand(
                "Enable Gaussian blur",
                () => { foreach (var s in snapshots) s.Node.Filter = after; },
                () => { foreach (var s in snapshots) s.Node.Filter = s.Before; }));
        }
    }

    partial void OnBlurSigmaXChanged(decimal? value) => ApplyBlurSigmaEdit(isY: false, value);
    partial void OnBlurSigmaYChanged(decimal? value) => ApplyBlurSigmaEdit(isY: true, value);

    private void ApplyBlurSigmaEdit(bool isY, decimal? value)
    {
        if (_syncingEffects || IsInteracting || value is not (>= 0m and <= 100m)) return;
        var targets = GetEffectTargets();
        if (targets.Length == 0) return;

        var sigma = (float)value.Value;
        var snapshots = targets.Select(t =>
        {
            var before = t.Filter;
            var curX = before?.BlurSigmaX ?? (float)(BlurSigmaX ?? 0m);
            var curY = before?.BlurSigmaY ?? (float)(BlurSigmaY ?? 0m);
            var nx = LinkBlurSigma || !isY ? sigma : curX;
            var ny = LinkBlurSigma || isY ? sigma : curY;
            var after = nx <= 0f && ny <= 0f ? null : new FilterSettings(nx, ny);
            return (Node: t, Before: before, After: after);
        }).ToArray();

        if (snapshots.All(s => Equals(s.Before, s.After))) return;
        ExecuteCommand(new DelegateDocumentCommand(
            "Gaussian blur sigma",
            () => { foreach (var s in snapshots) s.Node.Filter = s.After; },
            () => { foreach (var s in snapshots) s.Node.Filter = s.Before; }));
    }

    partial void OnSelectedShaderOptionChanged(ShaderOptionItem? value)
    {
        OnPropertyChanged(nameof(HasActiveShader));
        OnPropertyChanged(nameof(SelectedShaderId));
        if (_syncingEffects || IsInteracting || value is null) return;

        var targets = GetEffectTargets();
        if (targets.Length == 0) return;

        SkslCompileError = null;
        OnPropertyChanged(nameof(HasSkslError));

        if (value.Id == NoShaderId)
        {
            var snapshots = targets.Select(t => (Node: t, Before: t.ShaderEffect)).ToArray();
            if (snapshots.All(s => s.Before is null)) return;
            ExecuteCommand(new DelegateDocumentCommand(
                "Clear shader",
                () => { foreach (var s in snapshots) s.Node.ShaderEffect = null; },
                () => { foreach (var s in snapshots) s.Node.ShaderEffect = s.Before; }));
            return;
        }

        if (_shaderRegistry.TryGetProvider(value.Id, out var provider) && provider is not null)
        {
            var defaultUniforms = provider.DefaultUniforms.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            var after = new ShaderEffectDescriptor(provider.ShaderId, null, defaultUniforms);
            var snapshots = targets.Select(t => (Node: t, Before: t.ShaderEffect)).ToArray();
            ExecuteCommand(new DelegateDocumentCommand(
                $"Apply shader {provider.DisplayName}",
                () => { foreach (var s in snapshots) s.Node.ShaderEffect = CloneDescriptor(after); },
                () => { foreach (var s in snapshots) s.Node.ShaderEffect = s.Before; }));
            return;
        }

        if (value.Id == CustomShaderId)
        {
            var sksl = string.IsNullOrWhiteSpace(SkslSourceCode) ? DefaultCustomSksl : SkslSourceCode;
            if (!ShaderEffectRegistry.TryCompileSksl(sksl, out _, out _, out var scalarNames))
            {
                sksl = DefaultCustomSksl;
                ShaderEffectRegistry.TryCompileSksl(sksl, out _, out _, out scalarNames);
            }
            var uniforms = scalarNames.ToDictionary(n => n, _ => 10f, StringComparer.Ordinal);
            var after = new ShaderEffectDescriptor(CustomShaderId, sksl, uniforms);
            var snapshots = targets.Select(t => (Node: t, Before: t.ShaderEffect)).ToArray();
            ExecuteCommand(new DelegateDocumentCommand(
                "Apply custom SkSL shader",
                () => { foreach (var s in snapshots) s.Node.ShaderEffect = CloneDescriptor(after); },
                () => { foreach (var s in snapshots) s.Node.ShaderEffect = s.Before; }));
        }
    }

    public void SetShaderUniform(string name, float value)
    {
        if (_syncingEffects || IsInteracting || string.IsNullOrWhiteSpace(name)) return;
        var targets = GetEffectTargets().Where(t => t.ShaderEffect is not null).ToArray();
        if (targets.Length == 0) return;

        var snapshots = targets.Select(t =>
        {
            var before = t.ShaderEffect!;
            var dict = ResolveUniformValues(before);
            dict[name] = value;
            var after = before with { Uniforms = dict };
            return (Node: t, Before: before, After: after);
        }).ToArray();

        ExecuteCommand(new DelegateDocumentCommand(
            $"Shader uniform {name}",
            () => { foreach (var s in snapshots) s.Node.ShaderEffect = CloneDescriptor(s.After); },
            () => { foreach (var s in snapshots) s.Node.ShaderEffect = CloneDescriptor(s.Before); }));
    }

    [RelayCommand]
    private void CompileAndApplySksl()
    {
        if (IsInteracting) return;
        if (!ShaderEffectRegistry.TryCompileSksl(SkslSourceCode, out _, out var error, out var scalarNames))
        {
            SkslCompileError = error;
            OnPropertyChanged(nameof(HasSkslError));
            StatusMessage = $"SkSL compile error: {error}";
            return;
        }

        SkslCompileError = null;
        OnPropertyChanged(nameof(HasSkslError));

        var targets = GetEffectTargets();
        if (targets.Length == 0) return;

        var shaderId = SelectedShaderOption is { Id: not NoShaderId } opt ? opt.Id : CustomShaderId;
        var snapshots = targets.Select(t =>
        {
            var before = t.ShaderEffect;
            var dict = new Dictionary<string, float>(StringComparer.Ordinal);
            if (before is not null)
            {
                foreach (var (k, v) in ResolveUniformValues(before))
                {
                    if (scalarNames.Contains(k, StringComparer.Ordinal))
                    {
                        dict[k] = v;
                    }
                }
            }
            foreach (var uName in scalarNames)
            {
                if (!dict.ContainsKey(uName))
                {
                    dict[uName] = 10f;
                }
            }
            var after = new ShaderEffectDescriptor(shaderId, SkslSourceCode, dict);
            return (Node: t, Before: before, After: after);
        }).ToArray();

        ExecuteCommand(new DelegateDocumentCommand(
            "Compile SkSL shader",
            () => { foreach (var s in snapshots) s.Node.ShaderEffect = CloneDescriptor(s.After); },
            () => { foreach (var s in snapshots) s.Node.ShaderEffect = s.Before is null ? null : CloneDescriptor(s.Before); }));
        StatusMessage = "Compiled and applied SkSL shader.";
    }

    [RelayCommand]
    private void OpenBlurInspector()
    {
        SelectedInspectorTab = 1;
        if (!IsBlurEnabled)
        {
            IsBlurEnabled = true;
        }
    }

    [RelayCommand]
    private void OpenShaderInspector()
    {
        SelectedInspectorTab = 1;
        if (!HasActiveShader && ShaderOptions.Count > 1)
        {
            SelectedShaderOption = ShaderOptions[1];
        }
    }

    private static ShaderEffectDescriptor CloneDescriptor(ShaderEffectDescriptor descriptor) =>
        descriptor with
        {
            Uniforms = descriptor.Uniforms?.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal)
        };
}
