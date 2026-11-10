using System.Reflection;
using System.Runtime.Loader;
using Vectorer.Abstractions.Plugins;

namespace Vectorer.Plugins;

/// <summary>
/// Isolated <see cref="AssemblyLoadContext"/> for loading external plugin DLLs while sharing
/// <see cref="Vectorer.Abstractions"/> and <see cref="SkiaSharp"/> with the host process.
/// </summary>
internal sealed class PluginLoadContext(string pluginMainAssemblyPath) : AssemblyLoadContext(isCollectible: true)
{
    private readonly AssemblyDependencyResolver _resolver = new(pluginMainAssemblyPath);

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (assemblyName.Name is "Vectorer.Abstractions" or "SkiaSharp")
        {
            return null; // Fall back to the default host AssemblyLoadContext
        }

        var assemblyPath = _resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is not null ? LoadFromAssemblyPath(assemblyPath) : null;
    }
}

/// <summary>
/// Discovers, loads, and manages external <see cref="IVectorerPlugin"/> assemblies and their contributions.
/// </summary>
public sealed class PluginHostService : IPluginContext
{
    private readonly List<IVectorerPlugin> _loadedPlugins = [];
    private readonly List<IMenuContribution> _menuContributions = [];
    private readonly List<IToolbarContribution> _toolbarContributions = [];
    private readonly List<IShaderEffectProvider> _shaderProviders = [];

    public IReadOnlyList<IVectorerPlugin> LoadedPlugins => _loadedPlugins;
    public IReadOnlyList<IMenuContribution> MenuContributions => _menuContributions;
    public IReadOnlyList<IToolbarContribution> ToolbarContributions => _toolbarContributions;
    public IReadOnlyList<IShaderEffectProvider> ShaderProviders => _shaderProviders;

    public void RegisterPlugin(IVectorerPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (_loadedPlugins.Any(p => string.Equals(p.Metadata.Id, plugin.Metadata.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        plugin.Initialize(this);
        _loadedPlugins.Add(plugin);
    }

    public int LoadFromAssemblyFile(string assemblyFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyFilePath);
        var fullPath = Path.GetFullPath(assemblyFilePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("Plugin assembly file was not found.", fullPath);
        }

        var loadContext = new PluginLoadContext(fullPath);
        var assembly = loadContext.LoadFromAssemblyPath(fullPath);
        return LoadFromAssembly(assembly);
    }

    public int LoadFromAssembly(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var count = 0;

        foreach (var type in assembly.GetExportedTypes())
        {
            if (type is { IsAbstract: false, IsInterface: false }
                && typeof(IVectorerPlugin).IsAssignableFrom(type)
                && type.GetConstructor(Type.EmptyTypes) is not null)
            {
                if (Activator.CreateInstance(type) is IVectorerPlugin plugin)
                {
                    RegisterPlugin(plugin);
                    count++;
                }
            }
        }

        return count;
    }

    public int LoadPluginsFromDirectory(string directoryPath)
    {
        if (!Directory.Exists(directoryPath))
        {
            return 0;
        }

        var totalLoaded = 0;
        foreach (var dllPath in Directory.EnumerateFiles(directoryPath, "*.dll", SearchOption.TopDirectoryOnly))
        {
            try
            {
                totalLoaded += LoadFromAssemblyFile(dllPath);
            }
            catch
            {
                // Skip non-plugin or incompatible native DLLs in the folder
            }
        }

        return totalLoaded;
    }

    public void RegisterMenuContribution(IMenuContribution menuContribution)
    {
        ArgumentNullException.ThrowIfNull(menuContribution);
        _menuContributions.Add(menuContribution);
    }

    public void RegisterToolbarContribution(IToolbarContribution toolbarContribution)
    {
        ArgumentNullException.ThrowIfNull(toolbarContribution);
        _toolbarContributions.Add(toolbarContribution);
    }

    public void RegisterShaderProvider(IShaderEffectProvider shaderProvider)
    {
        ArgumentNullException.ThrowIfNull(shaderProvider);
        _shaderProviders.Add(shaderProvider);
    }
}
