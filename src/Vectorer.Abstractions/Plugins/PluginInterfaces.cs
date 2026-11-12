using SkiaSharp;
using Vectorer.Abstractions.Commands;
using Vectorer.Abstractions.Documents;

namespace Vectorer.Abstractions.Plugins;

/// <summary>
/// Metadata describing an external Vectorer plugin DLL.
/// </summary>
public sealed record PluginMetadata(
    string Id,
    string Name,
    string Version,
    string Author,
    string Description);

/// <summary>
/// Entry point implemented by external plugin assemblies.
/// </summary>
public interface IVectorerPlugin
{
    PluginMetadata Metadata { get; }
    void Initialize(IPluginContext context);
}

/// <summary>
/// Context provided to plugins during initialization to register menus, toolbar buttons, and custom Skia shaders.
/// </summary>
public interface IPluginContext
{
    void RegisterMenuContribution(IMenuContribution menuContribution);
    void RegisterToolbarContribution(IToolbarContribution toolbarContribution);
    void RegisterShaderProvider(IShaderEffectProvider shaderProvider);
}

/// <summary>
/// Represents the active editor state passed to plugin callbacks so plugins can inspect or manipulate the SVG document.
/// </summary>
public interface IEditorWorkspace
{
    SvgDocument CurrentDocument { get; }
    LayerNode? ActiveLayer { get; }
    IReadOnlyList<SceneNode> SelectedNodes { get; }

    void ExecuteCommand(IDocumentCommand command);
    void SelectNodes(IEnumerable<SceneNode> nodes);
    void RequestRedraw();
}

/// <summary>
/// Defines a menu item added by a plugin.
/// </summary>
public interface IMenuContribution
{
    string Id { get; }
    string MenuPath { get; }
    string Title { get; }
    string? ShortcutGesture { get; }
    void Execute(IEditorWorkspace workspace);
}

/// <summary>
/// Defines a toolbar button/action added by a plugin.
/// </summary>
public interface IToolbarContribution
{
    string Id { get; }
    string Title { get; }
    string? Tooltip { get; }
    string? IconPathData { get; }
    void Execute(IEditorWorkspace workspace);
}

/// <summary>
/// Provides a runtime SkiaSharp shader (SkSL) that can be applied per-object.
/// </summary>
public interface IShaderEffectProvider
{
    string ShaderId { get; }
    string DisplayName { get; }
    string SkslSource { get; }
    IReadOnlyDictionary<string, float> DefaultUniforms { get; }

    /// <summary>
    /// Compiles and creates a <see cref="SKShader"/> for a target object's bounding box.
    /// </summary>
    SKShader? CreateShader(SKRect objectBounds, IReadOnlyDictionary<string, float>? customUniforms = null);
}

/// <summary>
/// Optional contract for plugins that support runtime UI language switching.
/// </summary>
public interface IUiCultureAwarePlugin
{
    void OnUiCultureChanged(System.Globalization.CultureInfo culture);
}
