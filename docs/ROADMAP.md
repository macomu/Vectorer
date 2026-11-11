# Vectorer 開発ロードマップ (`ROADMAP.md`)

AI エージェントや開発者が現在の実装状況と今後の拡張タスクを把握するためのロードマップです。

---

## Phase 1: コア基盤とアーキテクチャ構築 (完了・初期構築済み)
- [x] リポジトリ構成、`.NET 10` (`net10.0`) ソリューション、Central Package Management (`Directory.Packages.props`)
- [x] MIT ライセンスポリシーおよび AI エージェント向け規約 (`AGENTS.md`, `.agents/rules/`)
- [x] ドメインモデル・プラグイン SDK (`Vectorer.Abstractions`)
  - シーングラフ (`SvgDocument`, `LayerNode`, `GroupNode`, `ShapeNode` 各種)
  - スタイル (`SolidColorFill`, `TextureFill`, `StrokeStyle`, `FilterSettings`, `LayerBlendMode`)
  - プラグイン・シェーダー拡張インターフェース (`IVectorerPlugin`, `IShaderEffectProvider`)
- [x] コアサービス (`Vectorer.Core`)
  - 図形論理演算サービス (`PathBooleanService` — Union, Difference, Intersection, Xor)
  - Undo / Redo コマンド履歴管理 (`UndoRedoService`)
- [x] SVG 読み書きエンジン基盤 (`Vectorer.Svg`)
  - 標準 SVG + `vectorer:*` 名前空間対応の Serializer / Parser
- [x] SkiaSharp レンダリングエンジン & PNG エクスポーター (`Vectorer.Rendering`)
  - ブレンドモード合成（Overlay, Add, Subtract 等）、ぼかしフィルタ、テクスチャ塗り、アウトライン、`SKRuntimeEffect` シェーダー描画
  - PNG エクスポート（ドキュメント全体、選択オブジェクトのみ、レイヤー単位バッチ出力）
- [x] 動的プラグインローダー (`Vectorer.Plugins`) と公式サンプルプラグイン (`Vectorer.SamplePlugin`)
- [x] Avalonia デスクトップアプリ骨格 (`Vectorer.App`) と単体テストスイート (`tests/*`)

---

## Phase 2: インタラクティブキャンバスとツール拡充 (次期フェーズ — 仕様は [`docs/UI_DESIGN.md`](UI_DESIGN.md) 参照)
- [ ] メニューバー・左描画ツールボックス・上部動的コンテキストツールバーの UI レイアウト構築
- [ ] キャンバス上でのマウスドラッグによるリアルタイム図形作成ツール（四角形・角丸、円・円弧、正多角形・星形、フリーハンド、ベジェ曲線、テキスト）
- [ ] 選択ツールとバウンディングボックスによる移動・回転・拡縮ギズモ (Transform Handles)
- [ ] パス編集（ノード）ツール（頂点選択・移動、ダブルクリックでの頂点追加、削除、コーナー/スムーズ/対称ハンドルの傾き編集、パスのシンプル化・結合・切断）
- [ ] オブジェクトのパス変換 (`Object to Path`) およびストロークのパス変換 (`Stroke to Path`)
- [ ] ズーム・パン（視点移動）とグリッド・スナップ機能

---

## Phase 3: UI パネルの充実と高度なエフェクト編集
- [ ] レイヤー＆オブジェクト階層ツリーパネル（表示/ロック切替、ドラッグ＆ドロップ並び替え、ブレンドモード切替、90°/任意角度でのレイヤー回転）
- [ ] 右上段インスペクタタブ①：塗り＆ストローク詳細パネル（単色、テクスチャ塗り詳細、線端・角形状、破線設定）
- [ ] 右上段インスペクタタブ②：エフェクト＆シェーダーパネル（ガウスぼかしスライダー、SkSL シェーダーライブエディタと Uniform スライダーの動的生成）
- [ ] PNG エクスポート設定ダイアログ（全体 / 選択要素のみ / レイヤー別バッチ出力の3モード切替、解像度・スケール・透過・プレビュー）
