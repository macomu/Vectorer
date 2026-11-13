# Vectorer

**Vectorer** は、.NET 10 と Avalonia UI、SkiaSharp で構築された軽量かつ高度な拡張性を持つクロスプラットフォーム対応の SVG エディタです。

## 主な機能

- **SVG ファイルの読み書き**:
  - 標準 SVG 1.1 / 2.0 要素の入出力に加え、`vectorer:*` 拡張名前空間（`https://vectorer.dev/ns/1.0`）によりレイヤー合成・パラメトリック図形・ノード種別・カスタムシェーダー等の編集情報を非破壊で保持します。
  - ドキュメントプリセット付き新規作成、ドキュメント設定変更、最近使ったファイル履歴、未保存変更の保護に対応しています。
- **インタラクティブな描画・編集ツール**:
  - 矩形（独立/連動角丸）、楕円、円弧（扇形・弦・開弧）、正多角形、星形、フリーハンド、ベジェ曲線ペン、テキストツールを搭載。
  - バウンディングボックスによる移動・8方向拡縮・回転・反転、整列・等間隔分布、グループ化/解除、グループ内編集、グリッド/頂点/バウンディングボックスへのスナップに対応しています。
  - パス編集（ノード）ツールによるアンカー追加・削除、コーナー/スムーズ/対称ハンドル編集、結合・切断・単純化・ベジェ再フィット、および `Object to Path` / `Stroke to Path` 変換を備えています。
- **図形論理演算 (Boolean Operations)**:
  - 選択図形に対する結合 (`Union`)、差分 (`Difference`)、交差 (`Intersection`)、排他的論理和 (`Xor`)、逆差分 (`ReverseDifference`) を Undo/Redo 対応で実行できます。
- **レイヤー階層・インスペクタ・エフェクト**:
  - **レイヤー＆オブジェクト階層ツリー**: 表示・ロック切替、インライン名前変更、ドラッグ＆ドロップでの順序変更や別コンテナ移動、角度指定レイヤー回転ダイアログに対応。
  - **塗り＆ストローク詳細パネル**: 塗りなし / 単色 / テクスチャ（パターン種別・色・スケール・角度）と、線端・角形状・マイター制限・カスタム破線配列/位相を編集可能。
  - **エフェクト＆シェーダーパネル**: ブレンドモード（`Normal`, `Multiply`, `Screen`, `Overlay`, `Add`, `Subtract`, `Difference`, `Darken`, `Lighten`）、縦横連動対応ガウスぼかし、`SkiaSharp` シェーダー (`SKRuntimeEffect` / SkSL) の適用・Uniform 編集・ライブ SkSL エディタを搭載。
- **柔軟な PNG エクスポート**:
  - `PngExportDialog` により、ドキュメント全体 / 選択要素のみ / レイヤー単位バッチ出力の 3 モードで、サイズ指定・倍率プリセット・縦横比ロック・透過プレビュー付き PNG エクスポートが可能です。
- **日本語 / English 多言語 UI**:
  - OS の言語に追従する `システム / System` に加え、メニュー（表示 → 言語 / Language）から `日本語` / `English` をリアルタイムに切り替え可能です。
- **プラグイン・シェーダー拡張システム**:
  - **DLL プラグイン動的読み込み**: `AssemblyLoadContext` により `./plugins/` 配下の外部 DLL をロードし、メニューやツールバーに独自コマンドを追加して SVG ドキュメントツリーを操作できます。
  - **公式サンプルプラグイン**: `Vectorer.SamplePlugin` を同梱しています。

## ダウンロードと実行

各リリースバージョンのビルド済みバイナリは、GitHub の **Releases** ページからダウンロードできます（`win-x64` / `win-arm64` 向け `.zip`、`osx-arm64` 向け `.tar.gz`、および `SHA256SUMS.txt` を配布）。

- **Windows (`win-x64` / `win-arm64`)**: `.zip` を展開し、`Vectorer.App.exe` を実行してください。
- **macOS (`osx-arm64`)**: `.tar.gz` を展開し、ディレクトリ内の `./Vectorer.App` を実行してください（`.app` バンドル化およびコード署名・公証には未対応のため、初回起動時は macOS のセキュリティ設定で実行を許可する必要がある場合があります）。

## 技術スタックとライセンス

- **ランタイム / 言語**: .NET 10 (`net10.0`) / C#
- **UI フレームワーク**: Avalonia UI (`11.3.0`) + `CommunityToolkit.Mvvm`
- **2D グラフィックス / 幾何演算 / シェーダー**: `SkiaSharp` (`2.88.9`)
- **ライセンス**: [MIT License](LICENSE)（サードパーティ依存のライセンス一覧は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照）

## プロジェクト構成

```text
Vectorer/
├── .agents/                        # AI エージェント向けルール設定
├── .github/workflows/build.yml     # CI ビルド・テストおよびタグリリース用ワークフロー
├── docs/                           # アーキテクチャ・UI設計・プラグイン仕様・SVG仕様・ロードマップ
├── plugins/
│   └── Vectorer.SamplePlugin/      # 公式サンプルプラグイン (メニュー追加・カスタムシェーダー提供)
├── src/
│   ├── Vectorer.Abstractions/      # プラグイン SDK・共通ドメインモデル・インターフェース
│   ├── Vectorer.Core/              # ドキュメント操作・図形論理演算・Undo/Redo 管理
│   ├── Vectorer.Svg/               # SVG リーダー / ライター (System.Xml.Linq ベース)
│   ├── Vectorer.Rendering/         # SkiaSharp レンダリングエンジン・シェーダー・PNG エクスポーター
│   ├── Vectorer.Plugins/           # AssemblyLoadContext による DLL プラグイン動的ローダー
│   └── Vectorer.App/               # Avalonia デスクトップアプリケーション本体 (日英ローカライズ含む)
├── tests/                          # 単体・結合テスト (xUnit)
├── build.cs                        # クロスプラットフォーム向けビルド＆パッケージ化スクリプト
└── Vectorer.slnx                   # .NET ソリューションファイル
```

## ビルド・テスト・パッケージ化

前提: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`global.json` で `10.0.100` 以上を指定)

```bash
# ソリューション全体のビルド
dotnet build Vectorer.slnx

# 全単体テストの実行
dotnet test Vectorer.slnx

# アプリケーションの起動
dotnet run --project src/Vectorer.App

# リリースパッケージの作成 (.NET 10 ファイルベースアプリ)
# デフォルトで win-x64 (.zip), win-arm64 (.zip), osx-arm64 (.tar.gz) を artifacts/packages/ に出力
dotnet run build.cs

# 特定の RID やバージョンを指定してパッケージ化する場合
dotnet run build.cs -- --rid win-x64 --version 0.1.0
dotnet run build.cs -- --rid osx-arm64 --skip-tests
```

## リリースフロー (GitHub Actions)

[`.github/workflows/build.yml`](.github/workflows/build.yml) により以下が自動実行されます。

- **CI ビルド＆パッケージ検証**: `main` ブランチへの `push`、`main` 向け `pull_request`、および手動実行 (`workflow_dispatch`) で `win-x64` / `win-arm64` / `osx-arm64` のテスト・ビルド・パッケージ生成を実行します。
- **GitHub Releases 自動公開**: `v*` 形式のタグ（例: `v0.1.0`）をプッシュすると、各プラットフォームのアーカイブをタグ名由来のバージョン付きでビルド・テストし、`SHA256SUMS.txt` とともに GitHub Release へ自動公開します（`v0.1.0-rc.1` のようにハイフンを含むタグは自動で Pre-release として公開されます）。

```bash
git tag v0.1.0
git push origin v0.1.0
```

## 開発・コントリビューション向けドキュメント

開発やコントリビューションに参加される方は、まず [CONTRIBUTING.md](CONTRIBUTING.md) および関連する設計ドキュメントをご参照ください。

- [CONTRIBUTING.md](CONTRIBUTING.md) — 開発手順・コントリビューションガイド・依存境界・ライセンスルール
- [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) — コミュニティ行動規範
- [SECURITY.md](SECURITY.md) — セキュリティポリシー・脆弱性報告方法（GitHub Security Advisories）
- [CHANGELOG.md](CHANGELOG.md) — リリース変更履歴
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — シーングラフ、レンダリングパイプライン、座標系・図形演算の設計
- [docs/UI_DESIGN.md](docs/UI_DESIGN.md) — 画面レイアウト、ツールボックス、インスペクタ、ダイアログの GUI 設計仕様
- [docs/PLUGIN_SYSTEM.md](docs/PLUGIN_SYSTEM.md) — DLL プラグインおよび SkiaSharp シェーダー拡張の仕様
- [docs/SVG_SPEC_SUPPORT.md](docs/SVG_SPEC_SUPPORT.md) — SVG 入出力マッピングと `vectorer:*` 拡張属性仕様
- [docs/LOCALIZATION.md](docs/LOCALIZATION.md) — 日本語・英語対応とリアルタイム言語切替の設計
- [docs/ROADMAP.md](docs/ROADMAP.md) — 開発フェーズと機能チェックリスト

（※ AI コーディングエージェント向けの作業規約は [AGENTS.md](AGENTS.md) および `.agents/rules/` に定義されています。）
