# Changelog

**Languages:** [日本語](CHANGELOG.md) | English

All notable changes to this project will be documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/).

## [0.1.0] - 2026-11-13

### Added
- Initial public release of the lightweight SVG editor built with **.NET 10 / Avalonia 11.3.0 / SkiaSharp 2.88.9**
- **SVG I/O**: Standard SVG 1.1 / 2.0 parsing and serialization with non-destructive `vectorer:*` extension attributes (`https://vectorer.dev/ns/1.0`)
- **Drawing & Editing Tools**: Selection/transform gizmos, rectangles with independent/linked corner radii, ellipses, arcs, regular polygons and stars, freehand, Bézier pen, path (node) editing, text editing, `Object to Path`, `Stroke to Path`, and grid/vertex/bounds snapping
- **Boolean Path Operations**: `Union`, `Difference`, `Intersection`, `Xor`, and `ReverseDifference` with Undo/Redo support
- **Layers & Inspectors**: Hierarchical scene tree (drag-and-drop reordering/reparenting, visibility/lock toggles, inline renaming, custom-angle layer rotation dialog), Fill & Stroke inspector, and Effects & Shaders inspector (blend modes, Gaussian blur, `SKRuntimeEffect` / live SkSL editor)
- **PNG Export**: `PngExportDialog` supporting full document, selection-only, and per-layer batch export
- **Bilingual Localization**: Instant runtime switching between English and Japanese with persisted settings
- **Plugin System**: Dynamic DLL loading via `IVectorerPlugin` / `IUiCultureAwarePlugin` and built-in `Vectorer.SamplePlugin`
- **Release Automation**: `build.cs` and `.github/workflows/build.yml` for cross-platform packaging (`win-x64`, `win-arm64`, `osx-arm64`) and tag-triggered GitHub Releases
