# SVG Specification Support & Extension Namespace (`SVG_SPEC_SUPPORT.en.md`)

**Languages:** [日本語](SVG_SPEC_SUPPORT.md) | English

Vectorer reads and writes a subset of SVG and preserves editor metadata through a dedicated XML namespace.

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
- Writes supported blend modes using CSS `mix-blend-mode` alongside `vectorer:blend-mode`. Add and Subtract use the Vectorer extension.

### 3.2 Filters (Gaussian Blur)
- Writes a referenced `<filter><feGaussianBlur stdDeviation="sigmaX sigmaY"/></filter>` inside `<defs>` alongside `vectorer:blur-x` and `vectorer:blur-y`. A single Gaussian blur is supported on import.

### 3.3 Texture Fills
- Stores `vectorer:texture-kind`, `vectorer:texture-scale`, `vectorer:texture-primary-color`, and `vectorer:texture-secondary-color`, while emitting a fallback `fill` color in standard attributes.

### 3.4 Plugin Shaders
- Persists `vectorer:shader-id` and `vectorer:shader-uniforms` in `key=value` format. Existing `vectorer:shader-sksl` source is retained, and v0.1.0 renders those shapes with their regular fills.

---

## 4. Compatibility & Round-Trip Details

- Root `opacity` is applied after compositing the document. If unsupported elements or styles are detected, saving requires a separate file.

- Affine transforms on shapes, layers, and groups are serialized as `transform="matrix(a b c d e f)"`; parsing supports `matrix`, `translate`, `scale`, `rotate`, `skewX`, and `skewY`.
- Path anchor node types are stored in `vectorer:node-kinds` (`Corner` / `Smooth` / `Symmetric`) alongside standard `d` and `fill-rule`.
- Stroke caps, joins, miter limit, dash array, dash offset, and separate `fill-opacity` / `stroke-opacity` use standard SVG attributes; 8-digit hex colors are parsed in RGBA order.
- `viewBox` and `preserveAspectRatio` (`meet` / `slice` / `none`), inherited presentation attributes, inline `style`, named colors, `rgb()`/`rgba()`, and `currentColor` are supported. Arbitrary external CSS stylesheets and embedded web fonts are outside the current scope.

## Colors, Linear Gradients, and Palettes (2026-11-14)

- Linear gradient fills and strokes use standard `<defs><linearGradient id="..."><stop .../></linearGradient></defs>` definitions referenced by `fill="url(#...)"` / `stroke="url(#...)"`. Radial gradients are outside the current implementation.
- Each stop has an `offset`, RGB `stop-color`, and alpha `stop-opacity`. Overall gradient opacity is stored separately in `fill-opacity` / `stroke-opacity`; importing keeps stop alpha independent of overall opacity. `stop-color` and `stop-opacity` do not automatically inherit from their parents.
- Both `gradientUnits` modes (`objectBoundingBox`, the default, and `userSpaceOnUse`), `gradientTransform`, and all three `spreadMethod` values (`pad`, `repeat`, `reflect`) round-trip. Percentage coordinates resolve against the bounding box or the `viewBox` width/height. The `viewBox` origin is not added to percentage coordinates.
- Import supports same-document `href="#id"` / `xlink:href="#id"` templates, inline stop styles, and `currentColor`. External references are not fetched. Unresolved references use an explicit fallback color, or no paint; circular references terminate safely.
- Stop positions are clamped to 0–1 and made nondecreasing according to SVG ordering rules. Multiple stops at the same position retain their order, allowing abrupt color changes.

Document color presets are stored as private metadata:

```xml
<metadata>
  <vectorer:palette>
    <vectorer:color value="#40112233" name="Translucent color" />
    <vectorer:color value="#FF0000" />
  </vectorer:palette>
</metadata>
```

Element order determines preset order. `name` is optional. `value` uses `#RRGGBB` when opaque and the existing extension convention `#AARRGGBB` when alpha is present (distinct from standard SVG's RGBA order for eight-digit colors). An absent palette uses the default presets; an explicit empty `<vectorer:palette />` stays empty. Applying a preset copies its color without linking shape styles to the palette entry. Other editors may discard private metadata when saving the file again.
