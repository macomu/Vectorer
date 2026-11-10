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

## Phase 2: インタラクティブキャンバスとツール拡充 (次期フェーズ)
- [ ] キャンバス上でのマウスドラッグによるリアルタイム図形作成ツール（多角形頂点打ち、円弧の角度ハンドル操作）
- [ ] バウンディングボックスによる移動・回転・拡縮ギズモ (Transform Handles)
- [ ] ベジェ曲線・ノード（アンカーポイント）編集ツール
- [ ] ズーム・パン（視点移動）とグリッド・スナップ機能

---

## Phase 3: UI パネルの充実と高度なエフェクト編集
- [ ] レイヤーパネルでのドラッグ＆ドロップ並び替え、ブレンドモード切り替え UI
- [ ] プロパティインスペクタ（線幅、破線、テクスチャ詳細設定、ぼかし強度スライダー）
- [ ] SkSL シェーダーライブエディタパネル（コード編集と Uniform スライダーの動的生成）
- [ ] PNG エクスポート設定ダイアログ（解像度・スケール・対象レイヤー選択プレビュー）
