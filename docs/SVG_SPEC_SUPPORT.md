# SVG 仕様サポートと拡張名前空間 (`SVG_SPEC_SUPPORT.md`)

**Languages:** 日本語 | [English](SVG_SPEC_SUPPORT.en.md)

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
- 対応する標準のブレンドモードは CSS の `mix-blend-mode` と `vectorer:blend-mode` を併記します。加算・減算は Vectorer 拡張属性に保持します。

### 3.2 フィルタ（ぼかし等）
- `<defs>` に `<filter><feGaussianBlur stdDeviation="sigmaX sigmaY"/></filter>` を出力します。対象要素には `filter="url(#id)"` と `vectorer:blur-x` / `vectorer:blur-y` を併記します。

### 3.3 テクスチャ塗りつぶし
- `vectorer:texture-kind`、`vectorer:texture-scale`、`vectorer:texture-primary-color`、`vectorer:texture-secondary-color` などを保持し、フォールバック用の `fill` カラーも標準属性に出力します。

### 3.4 プラグインシェーダー
- `vectorer:shader-id` と `vectorer:shader-uniforms`（`key=value` 形式）を保存します。既存の `vectorer:shader-sksl` は保持し、描画には図形の通常の塗りを使います。


## Phase 2 編集データの保存（2026-11-11）

- 基本図形・レイヤー・グループのアフィン変形は標準 `transform="matrix(a b c d e f)"` として保持します。読み込みでは matrix / translate / scale / rotate / skewX / skewY に対応します。
- 正多角形・星形は標準の `points` に加えて `vectorer:poly-kind="regular|star"`、`vectorer:cx/cy`、`vectorer:points-count`、`vectorer:outer-r`、`vectorer:inner-r`、`vectorer:start-angle` を保存します。Freeform化した図形は標準頂点列を使用します。
- パスの `vectorer:node-kinds` はアンカー順の Corner / Smooth / Symmetric のカンマ区切りです。幾何形状は標準 `d`、塗り規則は標準 `fill-rule` に保持します。
- 線端・線結合・マイター・破線・位相は標準の stroke-linecap / stroke-linejoin / stroke-miterlimit / stroke-dasharray / stroke-dashoffset を使用します。単色の透明度は fill-opacity / stroke-opacity へ分離し、標準SVGの8桁色はRGBA順で読み込みます。
- テキストは標準 `<text>` と行ごとの `<tspan>` として保存します。フォント名・サイズ・太字・斜体・字間・配置を保持します。表示とパス変換はローカルフォントのグリフ輪郭を共有します。
- 編集用モデルへ変換するSVGの円弧は分割近似です。複雑な文字組み、任意のCSS継承、フォント埋込みはこの実装範囲に含めません。

## Phase 2 仕上げでの互換性補完

- ルートの `opacity` はドキュメント全体を合成した後に適用します。未対応の要素やスタイルを検出した場合は別名保存になります。

- `viewBox` の原点・サイズと `preserveAspectRatio`（meet / slice / none）を読み書きし、レンダラーとキャンバス操作で同じビューポート変換を使用します。
- 塗り・線・塗り規則・不透明度・フォント設定の親からの継承とインラインstyleに対応します。SVG既定の塗りは黒です。
- 色名、rgb()/rgba()、currentColor、空白区切りpolygon/polyline座標に対応します。任意の外部CSS、tspanごとの複雑な組版、フォント埋込みは対象外です。
- パス単純化は適応分割と三次ベジェ再フィットにより端点・鋭い角・複数輪郭を保持します。独立したPathの結合では最も近い開いた端点を使い、スタイルは背面側から継承します。

## 色・線形グラデーション・パレット（2026-11-14）

- フィルとストロークの線形グラデーションは、標準の `<defs><linearGradient id="..."><stop .../></linearGradient></defs>` と `fill="url(#...)"` / `stroke="url(#...)"` で保存します。放射状グラデーションは現在の実装対象外です。
- 各制御点は `offset`、RGBの `stop-color`、Alphaの `stop-opacity` で表現し、グラデーション全体の不透明度は `fill-opacity` / `stroke-opacity` に分離します。読み込みでは制御点のAlphaと全体の不透明度を独立して保持します。`stop-color` と `stop-opacity` は親から自動継承しません。
- `gradientUnits` の `objectBoundingBox`（既定）/ `userSpaceOnUse`、`gradientTransform`、`spreadMethod` の `pad` / `repeat` / `reflect` を読み書きします。割合座標はバウンディングボックスの比率、または `viewBox` の幅・高さに対する割合として解決します。`viewBox` の原点は割合座標へ加算しません。
- 同一文書内の `href="#id"` / `xlink:href="#id"` によるテンプレート参照、制御点のインラインスタイル・`currentColor` に対応します。外部参照は取得せず、解決できない参照は指定された代替色、または塗りなしとして扱います。循環参照は停止します。
- 制御点の位置は0〜1に制限し、SVGの順序規則に従って直前より小さい位置を補正します。同一位置の複数制御点は順序を保って保存し、急な色の切り替えを表現できます。

文書の色プリセットは、次の独自メタデータとして保存します。

```xml
<metadata>
  <vectorer:palette>
    <vectorer:color value="#40112233" name="半透明色" />
    <vectorer:color value="#FF0000" />
  </vectorer:palette>
</metadata>
```

`vectorer:color` の並びが表示順です。`name` は任意で、`value` は不透明なら `#RRGGBB`、Alphaつきなら拡張属性で従来使用している `#AARRGGBB` 順です（標準SVGの8桁色のRGBA順とは異なります）。パレットがなければ初期プリセットを用い、明示的な空の `<vectorer:palette />` は空のまま復元します。プリセットの適用は色のコピーであり、図形のスタイルとの参照リンクは作りません。他のエディタでの再保存では、この独自メタデータが削除される場合があります。
