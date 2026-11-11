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
| `PolygonShapeNode` | `<polygon>` / `<polyline>` | 標準 `points="x1,y1 x2,y2 ..."` に加え、正多角形・星形の場合は再編集用に `vectorer:poly-kind="regular\|star\|freeform"`, `vectorer:cx`, `vectorer:cy`, `vectorer:points-count`, `vectorer:outer-r`, `vectorer:inner-r`, `vectorer:start-angle` を併記（個別頂点編集時は `freeform` に移行） |
| `ArcShapeNode` | `<path>` | SVG `A` (Elliptical Arc) コマンド + `vectorer:shape-type="arc"` および角度メタ属性 |
| `PathShapeNode` | `<path>` | `d="M ... Z"` |
| `TextShapeNode` | `<text>` | `x`, `y`, `font-family`, `font-size`, `font-weight`, `font-style`, `text-anchor`, `letter-spacing` |

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


## Phase 2 編集データの保存（2026-11-11）

- 基本図形・レイヤー・グループのアフィン変形は標準 `transform="matrix(a b c d e f)"` として保持します。読み込みでは matrix / translate / scale / rotate / skewX / skewY に対応します。
- 正多角形・星形は標準の `points` に加えて `vectorer:poly-kind="regular|star"`、`vectorer:cx/cy`、`vectorer:points-count`、`vectorer:outer-r`、`vectorer:inner-r`、`vectorer:start-angle` を保存します。Freeform化した図形は標準頂点列を使用します。
- パスの `vectorer:node-kinds` はアンカー順の Corner / Smooth / Symmetric のカンマ区切りです。幾何形状は標準 `d`、塗り規則は標準 `fill-rule` に保持します。
- 線端・線結合・マイター・破線・位相は標準の stroke-linecap / stroke-linejoin / stroke-miterlimit / stroke-dasharray / stroke-dashoffset を使用します。単色の透明度は fill-opacity / stroke-opacity へ分離し、標準SVGの8桁色はRGBA順で読み込みます。
- テキストは標準 `<text>` と行ごとの `<tspan>` として保存します。フォント名・サイズ・太字・斜体・字間・配置を保持します。表示とパス変換はローカルフォントのグリフ輪郭を共有します。
- 編集用モデルへ変換するSVGの円弧は分割近似です。複雑な文字組み、任意のCSS継承、フォント埋込みはこの実装範囲に含めません。

## Phase 2 仕上げでの互換性補完

- `viewBox` の原点・サイズと `preserveAspectRatio`（meet / slice / none）を読み書きし、レンダラーとキャンバス操作で同じビューポート変換を使用します。
- 塗り・線・塗り規則・不透明度・フォント設定の親からの継承とインラインstyleに対応します。SVG既定の塗りは黒です。
- 色名、rgb()/rgba()、currentColor、空白区切りpolygon/polyline座標に対応します。任意の外部CSS、tspanごとの複雑な組版、フォント埋込みは対象外です。
- パス単純化は適応分割と三次ベジェ再フィットにより端点・鋭い角・複数輪郭を保持します。独立したPathの結合では最も近い開いた端点を使い、スタイルは背面側から継承します。
