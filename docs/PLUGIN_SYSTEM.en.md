# Plugin & Shader Extension System (`PLUGIN_SYSTEM.en.md`)

**Languages:** [日本語](PLUGIN_SYSTEM.md) | English

Vectorer supports loading external .NET DLL plugins that extend menus and toolbars, as well as attaching per-object `SkiaSharp` runtime shaders (`SKRuntimeEffect` / SkSL).

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

- When a node has a `ShaderEffectDescriptor`, `Vectorer.Rendering` resolves the matching `IShaderEffectProvider` or compiles inline SkSL via `SKRuntimeEffect` to render the fill.

---

## 4. Dynamic DLL Loading (`Vectorer.Plugins`)

- `PluginLoader` scans a target directory (e.g., `./plugins/`) for `*.dll` files.
- Each plugin is loaded in an isolated `AssemblyLoadContext` (`PluginLoadContext`), while `Vectorer.Abstractions` and `SkiaSharp` are shared with the host context to prevent type identity mismatches.
- See `plugins/Vectorer.SamplePlugin` for the official reference plugin implementation.
