# Vectorer

**Languages:** [日本語](README.md) | English

**Vectorer** is a lightweight, highly extensible, cross-platform SVG editor built with .NET 10, Avalonia UI, and SkiaSharp.

## Key Features

- **SVG Reading and Writing**:
  - Reads and writes supported SVG elements, including basic shapes, paths, text, and linear gradients. Editor metadata is preserved in the Vectorer namespace. If Vectorer detects unsupported content, saving requires a separate file to protect the original.
  - Includes document presets on creation, document settings editing, recent files history, and unsaved-changes protection.
- **Interactive Drawing & Editing Tools**:
  - Tools for rectangles (independent/linked corner radii), ellipses, arcs (pie, chord, open), regular polygons, stars, freehand drawing, Bézier pen, and text.
  - Bounding-box gizmos for move, 8-direction resize, rotation, flip, alignment, distribution, grouping/ungrouping, group isolation editing, and snapping to grid, vertices, and bounding boxes.
  - Path (node) editing tool supporting anchor add/delete, corner/smooth/symmetric handles, path join/split/simplify/Bézier refit, plus `Object to Path` and `Stroke to Path` conversions.
- **Boolean Operations**:
  - Undoable `Union`, `Difference`, `Intersection`, `Xor`, and `ReverseDifference` operations on selected shapes.
- **Layer Hierarchy, Inspectors & Effects**:
  - **Layers & Objects Tree**: Visibility/lock toggles, inline renaming, drag-and-drop reordering and reparenting across containers, and a custom-angle layer rotation dialog.
  - **Fill & Stroke Inspector**: None / Solid / Texture fills (pattern kind, colors, scale, angle) and detailed stroke settings (line cap, join, miter limit, custom dash array & offset).
  - **Effects & Shaders Inspector**: Blend modes, linked or independent Gaussian blur, and plugin-provided shaders with editable uniforms. Inline SkSL in existing SVGs is preserved on save, and shapes render with their regular fills.
- **Flexible PNG Export**:
  - `PngExportDialog` supports exporting the full document, selected elements only, or per-layer batch export with target dimensions, scale presets, aspect-ratio lock, and transparency preview.
- **Bilingual UI (English / 日本語)**:
  - Follows the OS language (`System`) by default and allows instant runtime switching between `English` and `日本語` from `View -> Language`.
- **Plugin & Shader Extensibility**:
  - **Dynamic DLL Plugins**: Loads external plugin DLLs from `./plugins/` via `AssemblyLoadContext`, allowing plugins to register menu items, toolbar actions, and custom shaders that interact with the SVG document tree.
  - **Built-in Sample Plugin**: Includes `Vectorer.SamplePlugin` as a reference implementation.

## Download & Run

Pre-built binaries for each release tag are available on the GitHub **Releases** page (`Vectorer-win-x64.zip`, `Vectorer-win-arm64.zip`, `Vectorer-osx-arm64.tar.gz`, and `SHA256SUMS.txt`).

- **Windows (`win-x64` / `win-arm64`)**: Extract the `.zip` archive and run `Vectorer.App.exe`.
- **macOS (`osx-arm64`)**: Extract the `.tar.gz` archive and run `./Vectorer.App` inside the extracted folder (currently distributed as an unsigned standalone binary rather than a notarized `.app` bundle, so macOS Gatekeeper may require explicitly allowing execution on first launch).

## Tech Stack & License

- **Runtime / Language**: .NET 10 (`net10.0`) / C#
- **UI Framework**: Avalonia UI (`11.3.0`) + `CommunityToolkit.Mvvm`
- **2D Graphics / Geometry / Shaders**: `SkiaSharp` (`2.88.9`)
- **License**: [MIT License](LICENSE) (see [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for third-party dependency licenses)

## Project Structure

```text
Vectorer/
├── .agents/                        # Rules for AI coding agents
├── .github/workflows/build.yml     # CI build/test and tag-triggered GitHub Release workflow
├── docs/                           # Architecture, UI design, plugin spec, SVG spec, roadmap
├── plugins/
│   └── Vectorer.SamplePlugin/      # Official sample plugin (menus, toolbar actions, SkSL shader)
├── src/
│   ├── Vectorer.Abstractions/      # Plugin SDK, shared domain models, and interfaces
│   ├── Vectorer.Core/              # Document services, boolean geometry ops, Undo/Redo
│   ├── Vectorer.Svg/               # SVG reader / writer (System.Xml.Linq based)
│   ├── Vectorer.Rendering/         # SkiaSharp rendering engine, shaders, and PNG exporter
│   ├── Vectorer.Plugins/           # AssemblyLoadContext-based dynamic DLL plugin loader
│   └── Vectorer.App/               # Avalonia desktop application (with en/ja localization)
├── tests/                          # Unit and integration tests (xUnit)
├── build.cs                        # Cross-platform build & packaging script
└── Vectorer.slnx                   # .NET solution file
```

## Build, Test & Packaging

Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`10.0.403` or later per [global.json](global.json))

```bash
# Build the entire solution
dotnet build Vectorer.slnx

# Run all unit and integration tests
dotnet test Vectorer.slnx

# Launch the desktop application
dotnet run --project src/Vectorer.App

# Create release packages (.NET 10 file-based app)
# Outputs win-x64 (.zip), win-arm64 (.zip), osx-arm64 (.tar.gz) to artifacts/packages/ by default
dotnet run build.cs

# Package a specific RID and/or version
dotnet run build.cs -- --rid win-x64 --version 0.1.0
dotnet run build.cs -- --rid osx-arm64 --skip-tests
```

## Release Flow (GitHub Actions)

[`.github/workflows/build.yml`](.github/workflows/build.yml) automates CI and releases:

- **CI Build & Package Verification**: Runs tests, builds, and packages `win-x64`, `win-arm64`, and `osx-arm64` on `push` to `main`, `pull_request` targeting `main`, and `workflow_dispatch`.
- **Release candidates**: Pushing a version tag runs the NuGet audit and standard tests for each target. Passing archives are attached to a draft GitHub Release with `SHA256SUMS.txt`. Tags containing a hyphen are marked as prereleases.

```bash
git tag v0.1.0
git push origin v0.1.0
```

Extract the draft archives on their target platforms. Verify their checksums, startup, editing and Undo, SVG save and reopen, PNG export, and both UI languages. Check `licenses/DEPENDENCIES.txt` and the bundled notices, then set the actual release date in the changelog and publish the tested draft assets.

## Documentation & Contributing

If you would like to contribute or explore the internals, start with [CONTRIBUTING.en.md](CONTRIBUTING.en.md) and the design documents below:

- [CONTRIBUTING.en.md](CONTRIBUTING.en.md) ([日本語](CONTRIBUTING.md)) — Contributor guide, dependency boundaries, and license policy
- [CODE_OF_CONDUCT.en.md](CODE_OF_CONDUCT.en.md) ([日本語](CODE_OF_CONDUCT.md)) — Community Code of Conduct
- [SECURITY.en.md](SECURITY.en.md) ([日本語](SECURITY.md)) — Security policy & GitHub Private vulnerability reporting
- [CHANGELOG.en.md](CHANGELOG.en.md) ([日本語](CHANGELOG.md)) — Release changelog
- [docs/ARCHITECTURE.en.md](docs/ARCHITECTURE.en.md) ([日本語](docs/ARCHITECTURE.md)) — Scene graph, rendering pipeline, and boolean geometry design
- [docs/PLUGIN_SYSTEM.en.md](docs/PLUGIN_SYSTEM.en.md) ([日本語](docs/PLUGIN_SYSTEM.md)) — External DLL plugin SDK and SkiaSharp shader extensions
- [docs/SVG_SPEC_SUPPORT.en.md](docs/SVG_SPEC_SUPPORT.en.md) ([日本語](docs/SVG_SPEC_SUPPORT.md)) — SVG mapping and `vectorer:*` extension namespace specification
- [docs/ROADMAP.en.md](docs/ROADMAP.en.md) ([日本語](docs/ROADMAP.md)) — Implementation phases and feature checklist
- [docs/UI_DESIGN.md](docs/UI_DESIGN.md) *(Japanese)* — Detailed UI layout, toolbox, inspector, and dialog specs
- [docs/LOCALIZATION.md](docs/LOCALIZATION.md) *(Japanese)* — English/Japanese localization architecture and runtime switching

*(Instructions specifically for AI coding agents are maintained in [AGENTS.md](AGENTS.md) and `.agents/rules/`.)*
