# Changelog

**Languages:** 日本語 | [English](CHANGELOG.en.md)

本プロジェクトの主な変更履歴を [Keep a Changelog](https://keepachangelog.com/ja/1.1.0/) 形式で記録します。
バージョニングは [Semantic Versioning](https://semver.org/) に準拠します。

## [0.1.0] - 2026-11-13

### Added
- **.NET 10 / Avalonia 11.3.0 / SkiaSharp 2.88.9** ベースの軽量 SVG エディタ初回公開版
- **SVG 入出力**: 標準 SVG 1.1 / 2.0 および `vectorer:*` 拡張属性（`https://vectorer.dev/ns/1.0`）の読み書き
- **描画・編集ツール**: 選択/変形ギズモ、矩形・角丸、楕円、円弧、正多角形・星形、フリーハンド、ベジェペン、パス（ノード）編集、テキスト編集、`Object to Path` / `Stroke to Path`、グリッド・頂点・バウンディングボックススナップ
- **図形論理演算**: `Union` / `Difference` / `Intersection` / `Xor` / `ReverseDifference`（Undo / Redo 対応）
- **レイヤー＆インスペクタ**: 階層ツリー（D&D 移動・並び替え・表示/ロック・名前変更・角度指定レイヤー回転）、塗り＆ストローク詳細パネル、エフェクト＆シェーダーパネル（ブレンドモード・ガウスぼかし・`SKRuntimeEffect` / ライブ SkSL エディタ）
- **PNG エクスポート**: ドキュメント全体 / 選択要素のみ / レイヤー単位バッチ出力対応 `PngExportDialog`
- **多言語 UI**: 日本語 / English のリアルタイム切り替えと設定永続化
- **プラグイン拡張**: `IVectorerPlugin` / `IUiCultureAwarePlugin` による外部 DLL ロードと `Vectorer.SamplePlugin` 同梱
- **リリース自動化**: `build.cs` および `.github/workflows/build.yml` による `win-x64` / `win-arm64` / `osx-arm64` パッケージ生成とタグ駆動 GitHub Release 公開
