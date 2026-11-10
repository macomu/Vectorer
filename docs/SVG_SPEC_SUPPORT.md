# SVG 仕様サポートと拡張名前空間 (`SVG_SPEC_SUPPORT.md`)

Vectorer は標準的な SVG 1.1 / SVG 2.0 ファイルを読み書きできると同時に、Vectorer 固有の高度な編集情報（レイヤー情報、特殊ブレンド、カスタム SkiaSharp シェーダー等）を失わずに保存するため、専用の XML 名前空間を使用します。

---

## 1. XML 名前空間定義

Vectorer が保存する SVG のルート `<svg>` 要素には以下の名前空間が付与されます。

```xml
<svg xmlns="http://www.w3.org/2000/svg"
     xmlns:vectorer="https://vectorer.dev/ns/1.0"
     width="800" height="600" viewBox="0 0 800 600">
```

標準のブラウザや他ツール（Inkscape, Illustrator 等）で開いた場合でも、`vectorer:*` 属性は無視されるため標準の SVG として問題なく表示できます。

---

## 2. 要素とモデルのマッピング

| Vectorer モデル (`SceneNode`) | SVG 要素 | Vectorer 拡張属性 / 備考 |
| :--- | :--- | :--- |
| `LayerNode` | `<g>` | `vectorer:layer="true"`, `vectorer:name="..."`, `vectorer:blend-mode="Overlay"`, `vectorer:locked="false"` |
| `GroupNode` | `<g>` | 通常のグループ要素 |
| `RectangleShapeNode` | `<rect>` | `x`, `y`, `width`, `height`, `rx`, `ry` |
| `EllipseShapeNode` | `<ellipse>` / `<circle>` | `cx`, `cy`, `rx`, `ry` |
| `PolygonShapeNode` | `<polygon>` | `points="x1,y1 x2,y2 ..."` |
| `ArcShapeNode` | `<path>` | SVG `A` (Elliptical Arc) コマンド + `vectorer:shape-type="arc"` および角度メタ属性 |
| `PathShapeNode` | `<path>` | `d="M ... Z"` |

---

## 3. スタイル・フィルタ・シェーダーの保存仕様

### 3.1 レイヤーブレンドモード
- 標準 CSS `style="mix-blend-mode: overlay;"` を出力すると同時に、Vectorer 固有の加算 (`Add`) や減算 (`Subtract`) も正確に復元できるよう `vectorer:blend-mode="Add"` を併記します。

### 3.2 フィルタ（ぼかし等）
- `<defs>` 内に `<filter id="filter_xxx"><feGaussianBlur stdDeviation="sigmaX sigmaY" /></filter>` を出力し、対象要素から `filter="url(#filter_xxx)"` で参照します。また、簡易復元のために `vectorer:blur-sigma="4.5"` 属性もサポートします。

### 3.3 テクスチャ塗りつぶし
- `vectorer:texture-kind`、`vectorer:texture-scale`、`vectorer:texture-primary-color`、`vectorer:texture-secondary-color` などを保持し、フォールバック用の `fill` カラーも標準属性に出力します。

### 3.4 SkiaSharp シェーダー (`SKRuntimeEffect`)
- オブジェクトに適用されたシェーダーは、`vectorer:shader-id`、`vectorer:shader-uniforms`（JSON または key=value 形式）、およびカスタム SkSL がある場合は `<defs><vectorer:shader id="..." sksl="..." /></defs>` として保存します。
