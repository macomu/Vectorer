# Vectorer Development Roadmap (`ROADMAP.en.md`)

**Languages:** [日本語](ROADMAP.md) | English

Overview of implemented phases and feature milestones in Vectorer.

---

## Phase 1: Core Foundation & Architecture (Completed)
- [x] Repository layout, `.NET 10` (`net10.0`) solution, Central Package Management (`Directory.Packages.props`)
- [x] MIT license policy and architecture rules
- [x] Domain models & Plugin SDK (`Vectorer.Abstractions`: scene graph, styles, `IVectorerPlugin`, `IShaderEffectProvider`)
- [x] Core services (`Vectorer.Core`: `PathBooleanService` for Union/Difference/Intersection/Xor, `UndoRedoService`)
- [x] SVG reader/writer (`Vectorer.Svg`: standard SVG + `vectorer:*` extension attributes)
- [x] SkiaSharp rendering engine & PNG exporter (`Vectorer.Rendering`: blend modes, blur filters, texture fills, strokes, `SKRuntimeEffect` shaders, full/selection/per-layer batch PNG export)
- [x] Dynamic DLL plugin loader (`Vectorer.Plugins`) and sample plugin (`Vectorer.SamplePlugin`)
- [x] Avalonia desktop app skeleton (`Vectorer.App`) and xUnit test suites (`tests/*`)

---

## Phase 2: Interactive Canvas & Editing Tools (Completed)
- [x] Menu bar, left exclusive tool palette, dynamic context toolbar, coordinates/selection/zoom status bar
- [x] 11 custom miter-clean SVG tool icons (no external icon package)
- [x] Interactive creation & editing for rectangles (linked/independent `Rx`/`Ry`), ellipses, arcs (pie/chord/open), regular polygons, stars, freehand, Bézier pen, and multiline text
- [x] Selection gizmos: click/Shift/marquee select, move, 8-handle resize, rotation (Shift 15° snap), flip, align & distribute, grouping/isolation editing
- [x] Node editing tool: anchor select/move/add/delete, Corner/Smooth/Symmetric handles, line/curve toggle, join/split/open/close/simplify/Bézier refit
- [x] `Object to Path` and `Stroke to Path` conversions
- [x] Zoom (10%–3200%), pan, grid overlay, snapping (grid, vertices, bounding boxes)
- [x] Boolean operations (`Union`, `Difference`, `Intersection`, `Xor`, `ReverseDifference`) with Undo/Redo
- [x] Async SVG open/save/save-as, recent files, document presets, unsaved-changes protection, external SVG compatibility (`viewBox`, inherited styles, CSS colors)

---

## Phase 3: Inspectors, Scene Tree & Advanced Export (Completed)
- [x] **3.1 Fill & Stroke Detailed Inspector**: Right-docked split pane, None/Solid/Texture fill editing, Stroke cap/join/miter/opacity/custom dash array & offset, mixed-selection attribute preservation
- [x] **3.2 Effects & Shaders Inspector**: Compositing blend modes, linked/independent Gaussian blur (`BlurSigmaX`/`BlurSigmaY`), SkSL shader picker, dynamic uniform sliders, collapsible live SkSL editor with line-numbered diagnostics, `vectorer:shader-uniforms` SVG round-trip
- [x] **3.3 Hierarchical Layers & Objects Tree + Layer Rotation**: Unified layer/group/shape tree, blend/`[fx]` badges, bidirectional selection sync, visibility/lock toggles, inline rename, drag-and-drop reordering & container reparenting preserving world transforms, custom-angle layer rotation dialog with live preview
- [x] **3.4 PNG Export Dialog (`PngExportDialog`)**: Exact selection render bounds (transforms, stroke widths/caps/joins, blur), Full / Selection / Per-layer batch modes, scale presets, aspect-ratio lock, and transparent checkerboard preview

---

## Post-Phase 3 Enhancements (Completed)
- [x] Runtime bilingual UI localization (English / 日本語 / System) with live language switching and persisted settings
- [x] Searchable installed-font picker (`FontPicker`)
- [x] Cross-platform build & packaging script (`build.cs`) and tag-triggered GitHub Releases workflow (`.github/workflows/build.yml`)
- [x] Assembly version (`0.1.0`) metadata and localized Help → About dialog

## Color Editing (2026-11-14)

- [x] Shared RGBA/HSVA editor with component color ramps, triangular handles, numeric/HEX input and transparency previews
- [x] Linear gradients for fill and stroke, arbitrary color stops, add/move/edit/remove, endpoints and overall opacity
- [x] Document palettes with independent scrolling, add/reorder/remove and SVG extension metadata persistence
- [x] Live preview and one undo operation per drag; regression coverage for SVG round trips, rendering, PNG export, mixed selection and document palettes
- [ ] Radial gradients and on-canvas gradient endpoint handles
