using SkiaSharp;
using Vectorer.Abstractions.Plugins;
using Vectorer.Abstractions.Styling;

namespace Vectorer.Rendering.Shaders;

/// <summary>
/// Manages built-in and plugin-contributed <see cref="IShaderEffectProvider"/> instances.
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
            return null;

        if (_providers.TryGetValue(descriptor.ShaderId, out var provider))
        {
            return provider.CreateShader(objectBounds, descriptor.Uniforms);
        }

        return null;
    }
}
