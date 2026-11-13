# SVG Specification Support & Extension Namespace (`SVG_SPEC_SUPPORT.en.md`)

**Languages:** [日本語](SVG_SPEC_SUPPORT.md) | English

Vectorer reads and writes standard SVG 1.1 / SVG 2.0 files while preserving editor-specific metadata (layers, custom blend modes, parametric shapes, node kinds, SkSL shaders, etc.) through a dedicated XML namespace.

---

## 1. XML Namespace Definition

Root `<svg>` elements written by Vectorer declare the `vectorer` namespace:

```xml
<svg xmlns="http://www.w3.org/2000/svg"
     xmlns:vectorer="https://vectorer.dev/ns/1.0"
     width="800" height="600" viewBox="0 0 800 600">
```

Browsers and other vector editors (Inkscape, Illustrator, etc.) ignore `vectorer:*` attributes and render the standard SVG elements normally.

---

## 2. Element & Model Mapping

| Vectorer Model (`SceneNode`) | SVG Element | Extension Attributes / Notes |
| :--- | :--- | :--- |
| `LayerNode` | `<g>` | `vectorer:layer="true"`, `vectorer:name="..."`, `vectorer:blend-mode="Overlay"`, `vectorer:locked="false"` |
| `GroupNode` | `<g>` | Standard group element |
| `RectangleShapeNode` | `<rect>` | `x`, `y`, `width`, `height`, `rx`, `ry` |
| `EllipseShapeNode` | `<ellipse>` / `<circle>` | `cx`, `cy`, `rx`, `ry` |
| `PolygonShapeNode` | `<polygon>` / `<polyline>` | Standard `points="x1,y1 x2,y2 ..."` plus `vectorer:poly-kind="regular\|star\|freeform"`, `vectorer:cx`, `vectorer:cy`, `vectorer:points-count`, `vectorer:outer-r`, `vectorer:inner-r`, `vectorer:start-angle` for parametric regeneration (transitions to `freeform` on vertex edits) |
| `ArcShapeNode` | `<path>` | SVG `A` (Elliptical Arc) command + `vectorer:shape-type="arc"` and angle attributes |
| `PathShapeNode` | `<path>` | `d="M ... Z"` |
| `TextShapeNode` | `<text>` | `x`, `y`, `font-family`, `font-size`, `font-weight`, `font-style`, `text-anchor`, `letter-spacing` (plus `<tspan>` lines) |

---

## 3. Styles, Filters & Shaders

### 3.1 Layer Blend Modes
- Outputs standard CSS `style="mix-blend-mode: overlay;"` alongside `vectorer:blend-mode="..."` so Vectorer-specific modes such as `Add` and `Subtract` round-trip accurately.

### 3.2 Filters (Gaussian Blur)
- Writes `<filter id="filter_xxx"><feGaussianBlur stdDeviation="sigmaX sigmaY" /></filter>` inside `<defs>` and references it via `filter="url(#filter_xxx)"`, plus `vectorer:blur-sigma` for simplified recovery.

### 3.3 Texture Fills
- Stores `vectorer:texture-kind`, `vectorer:texture-scale`, `vectorer:texture-primary-color`, and `vectorer:texture-secondary-color`, while emitting a fallback `fill` color in standard attributes.

### 3.4 SkiaSharp Shaders (`SKRuntimeEffect`)
- Persists `vectorer:shader-id`, `vectorer:shader-uniforms`, and inline custom SkSL definitions via `<defs><vectorer:shader id="..." sksl="..." /></defs>`.

---

## 4. Compatibility & Round-Trip Details

- Affine transforms on shapes, layers, and groups are serialized as `transform="matrix(a b c d e f)"`; parsing supports `matrix`, `translate`, `scale`, `rotate`, `skewX`, and `skewY`.
- Path anchor node types are stored in `vectorer:node-kinds` (`Corner` / `Smooth` / `Symmetric`) alongside standard `d` and `fill-rule`.
- Stroke caps, joins, miter limit, dash array, dash offset, and separate `fill-opacity` / `stroke-opacity` use standard SVG attributes; 8-digit hex colors are parsed in RGBA order.
- `viewBox` and `preserveAspectRatio` (`meet` / `slice` / `none`), inherited presentation attributes, inline `style`, named colors, `rgb()`/`rgba()`, and `currentColor` are supported. Arbitrary external CSS stylesheets and embedded web fonts are outside the current scope.
