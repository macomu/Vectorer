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

        if (_providers.TryGetValue(descriptor.ShaderId, out var provider))
        {
            return provider.CreateShader(objectBounds, descriptor.Uniforms);
        }

        if (!string.IsNullOrWhiteSpace(descriptor.InlineSksl))
        {
            return CompileSkslToShader(descriptor.InlineSksl, objectBounds, descriptor.Uniforms);
        }

        return null;
    }

    private static readonly Dictionary<string, SKRuntimeEffect> CompiledEffectsCache = new(StringComparer.Ordinal);

    public static SKShader? CompileSkslToShader(
        string skslSource,
        SKRect objectBounds,
        IReadOnlyDictionary<string, float>? customUniforms = null)
    {
        if (!CompiledEffectsCache.TryGetValue(skslSource, out var effect))
        {
            effect = SKRuntimeEffect.Create(skslSource, out _);
            if (effect is null)
            {
                return null;
            }

            CompiledEffectsCache[skslSource] = effect;
        }

        var uniforms = new SKRuntimeEffectUniforms(effect);
        if (effect.Uniforms.Contains("u_bounds"))
        {
            uniforms["u_bounds"] = new[] { objectBounds.Left, objectBounds.Top, objectBounds.Width, objectBounds.Height };
        }

        if (customUniforms is not null)
        {
            foreach (var (key, value) in customUniforms)
            {
                if (effect.Uniforms.Contains(key))
                {
                    uniforms[key] = value;
                }
            }
        }

        return effect.ToShader(isOpaque: false, uniforms);
    }
}
