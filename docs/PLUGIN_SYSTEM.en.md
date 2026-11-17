# Plugin & Shader Extension System (`PLUGIN_SYSTEM.en.md`)

**Languages:** [日本語](PLUGIN_SYSTEM.md) | English

Vectorer loads external .NET DLL plugins that add menus, toolbars, and per-object SkiaSharp shaders.

---

## 1. Plugin Development Overview

1. Create a `.NET 10` (`net10.0`) class library project referencing `Vectorer.Abstractions`.
2. Because `Vectorer.Abstractions` references `SkiaSharp`, plugins can directly use `SKPath`, `SKCanvas`, `SKShader`, `SKRuntimeEffect`, etc.
3. Expose a public class implementing `IVectorerPlugin` (and optionally `IUiCultureAwarePlugin` for live language switching).

---

## 2. Core Interfaces (`Vectorer.Abstractions.Plugins`)

### 2.1 `IVectorerPlugin`
Entry point for a plugin DLL:

```csharp
public interface IVectorerPlugin
{
    PluginMetadata Metadata { get; }
    void Initialize(IPluginContext context);
}
```

### 2.2 `IPluginContext`
Registration context passed by the Vectorer host during initialization:

- `RegisterMenuContribution(IMenuContribution item)`: Registers a command under the Extensions menu.
- `RegisterToolbarContribution(IToolbarContribution item)`: Adds a one-shot action button to the toolbar without switching the active drawing tool.
- `RegisterShaderProvider(IShaderEffectProvider provider)`: Registers a custom `SkiaSharp` shader (`SKRuntimeEffect`) selectable on shapes and layers.

### 2.3 `IEditorWorkspace`
Workspace interface passed to plugin actions when executed, enabling safe Undo/Redo-aware manipulation of the active SVG document:

- `SvgDocument CurrentDocument { get; }`
- `IReadOnlyList<SceneNode> SelectedNodes { get; }`
- `void ExecuteCommand(IDocumentCommand command)`
- `void RequestRedraw()`

---

## 3. Per-Object SkiaSharp Shaders (`IShaderEffectProvider`)

Any `ShapeNode` or `LayerNode` can attach a `ShaderEffectDescriptor`:

```csharp
public interface IShaderEffectProvider
{
    string ShaderId { get; }
    string DisplayName { get; }
    string SkslSource { get; }
    IReadOnlyDictionary<string, float> DefaultUniforms { get; }
    SKShader? CreateShader(SKRect bounds, IReadOnlyDictionary<string, float> uniforms);
}
```

- The renderer uses the shader returned by the matching `IShaderEffectProvider`. v0.1.0 preserves inline SkSL from existing SVGs and renders those objects with their regular fills.

---

## 4. Dynamic DLL Loading (`Vectorer.Plugins`)

- `PluginLoader` scans a target directory (e.g., `./plugins/`) for `*.dll` files.
- Each plugin is loaded in an isolated `AssemblyLoadContext` (`PluginLoadContext`), while `Vectorer.Abstractions` and `SkiaSharp` are shared with the host context to prevent type identity mismatches.
- See `plugins/Vectorer.SamplePlugin` for the official reference plugin implementation.
