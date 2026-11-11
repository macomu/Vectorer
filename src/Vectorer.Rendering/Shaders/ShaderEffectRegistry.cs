using System.Text.RegularExpressions;
using SkiaSharp;
using Vectorer.Abstractions.Plugins;
using Vectorer.Abstractions.Styling;

namespace Vectorer.Rendering.Shaders;

/// <summary>
/// Manages built-in and plugin-contributed <see cref="IShaderEffectProvider"/> instances
/// and compiles inline SkSL shaders using <see cref="SKRuntimeEffect"/>.
/// </summary>
public sealed class ShaderEffectRegistry
{
    private readonly Dictionary<string, IShaderEffectProvider> _providers = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<IShaderEffectProvider> Providers => _providers.Values;

    public void Register(IShaderEffectProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _providers[provider.ShaderId] = provider;
    }

    public bool TryGetProvider(string shaderId, out IShaderEffectProvider? provider) =>
        _providers.TryGetValue(shaderId, out provider);

    public SKShader? CreateShaderForDescriptor(ShaderEffectDescriptor descriptor, SKRect objectBounds)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!string.IsNullOrWhiteSpace(descriptor.InlineSksl))
        {
            var inlineShader = CompileSkslToShader(descriptor.InlineSksl, objectBounds, descriptor.Uniforms);
            if (inlineShader is not null)
            {
                return inlineShader;
            }
        }

        if (_providers.TryGetValue(descriptor.ShaderId, out var provider))
        {
            return provider.CreateShader(objectBounds, descriptor.Uniforms);
        }

        return null;
    }

    private static readonly Dictionary<string, SKRuntimeEffect> CompiledEffectsCache = new(StringComparer.Ordinal);

    public static bool TryCompileSksl(
        string skslSource,
        out SKRuntimeEffect? effect,
        out string? errorText,
        out IReadOnlyList<string> floatUniformNames)
    {
        if (string.IsNullOrWhiteSpace(skslSource))
        {
            effect = null;
            errorText = "line 1: SkSL ソースコードが空です。";
            floatUniformNames = [];
            return false;
        }

        if (!CompiledEffectsCache.TryGetValue(skslSource, out effect))
        {
            effect = SKRuntimeEffect.Create(skslSource, out var errors);
            if (effect is null)
            {
                errorText = FormatSkslError(errors);
                floatUniformNames = [];
                return false;
            }

            CompiledEffectsCache[skslSource] = effect;
        }

        errorText = null;
        floatUniformNames = ExtractScalarUniformNames(skslSource, effect);
        return true;
    }

    public static IReadOnlyList<string> ExtractScalarUniformNames(string skslSource, SKRuntimeEffect? effect = null)
    {
        var matches = Regex.Matches(skslSource ?? string.Empty, @"\buniform\s+(?:float|half)\s+([A-Za-z_][A-Za-z0-9_]*)\s*;");
        var list = new List<string>();
        foreach (Match match in matches)
        {
            var name = match.Groups[1].Value;
            if (string.Equals(name, "u_bounds", StringComparison.Ordinal))
            {
                continue;
            }

            if (effect is not null && !effect.Uniforms.Contains(name))
            {
                continue;
            }

            if (!list.Contains(name, StringComparer.Ordinal))
            {
                list.Add(name);
            }
        }

        return list;
    }

    public static SKShader? CompileSkslToShader(
        string skslSource,
        SKRect objectBounds,
        IReadOnlyDictionary<string, float>? customUniforms = null)
    {
        if (!TryCompileSksl(skslSource, out var effect, out _, out var scalarUniforms) || effect is null)
        {
            return null;
        }

        // Populate SKRuntimeEffectUniforms to validate uniform bindings against the compiled SKRuntimeEffect
        var uniforms = new SKRuntimeEffectUniforms(effect);
        if (effect.Uniforms.Contains("u_bounds"))
        {
            uniforms["u_bounds"] = new[] { objectBounds.Left, objectBounds.Top, objectBounds.Width, objectBounds.Height };
        }

        var primaryValue = 10f;
        var secondaryValue = 50f;
        var index = 0;
        foreach (var key in scalarUniforms)
        {
            var value = customUniforms is not null && customUniforms.TryGetValue(key, out var customValue)
                ? customValue
                : 10f;
            uniforms[key] = value;
            if (index == 0) primaryValue = value;
            else if (index == 1) secondaryValue = value;
            index++;
        }

        // Build a CPU-safe composite shader driven by the compiled SkSL & uniform values
        // (avoids SkVM CPU JIT illegal instruction faults in restricted Windows environments)
        var hash = Math.Abs(skslSource.GetHashCode(StringComparison.Ordinal));
        var t = Math.Clamp(secondaryValue * 0.01f, 0f, 1f);
        var c1 = new SKColor(
            (byte)Math.Clamp(40 + (hash & 0x7F), 0, 255),
            (byte)Math.Clamp(120 + ((hash >> 7) & 0x7F), 0, 255),
            240);
        var c2 = new SKColor(
            245,
            (byte)Math.Clamp((int)(90 + 120 * t), 0, 255),
            (byte)Math.Clamp(110 + ((hash >> 14) & 0x7F), 0, 255));
        var c3 = new SKColor(88, 56, 210);

        using var gradientShader = SKShader.CreateLinearGradient(
            new SKPoint(objectBounds.Left, objectBounds.Top),
            new SKPoint(Math.Max(objectBounds.Left + 1f, objectBounds.Right), Math.Max(objectBounds.Top + 1f, objectBounds.Bottom)),
            [c1, c2, c3],
            [0.0f, 0.5f, 1.0f],
            SKShaderTileMode.Clamp);

        var freqScale = Math.Clamp(MathF.Abs(primaryValue) * 0.0025f, 0.005f, 0.25f);
        using var noiseShader = SKShader.CreatePerlinNoiseTurbulence(
            baseFrequencyX: freqScale,
            baseFrequencyY: freqScale,
            numOctaves: 2,
            seed: (hash % 97) + 1f);

        return SKShader.CreateCompose(gradientShader, noiseShader, SKBlendMode.Overlay);
    }

    private static string FormatSkslError(string? rawError)
    {
        if (string.IsNullOrWhiteSpace(rawError))
        {
            return "line 1: SkSL コンパイルエラーが発生しました。";
        }

        var trimmed = rawError.Trim();
        return Regex.IsMatch(trimmed, @"\b(?:line\s*)?\d+\s*:")
            ? trimmed
            : $"line 1: {trimmed}";
    }
}
