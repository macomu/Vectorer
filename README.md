# Vectorer

**Vectorer** は、.NET 10 と Avalonia UI、SkiaSharp で構築された軽量かつ高度な拡張性を持つ SVG エディタです。

## 主な特徴・目標機能

- **SVG ファイルの読み書き**: 標準 SVG 要素の入出力に加え、`vectorer:*` 拡張名前空間によりレイヤー合成やカスタムシェーダー設定などのエディタ固有情報も非破壊で保持します。
- **柔軟な PNG エクスポート**:
  - ドキュメント全体のサイズ指定出力
  - 選択オブジェクトのみの切り出し出力
  - レイヤー単位での一括（バッチ）出力
- **レイヤーとグループ管理**:
  - オーバーレイ (Overlay)、加算 (Add/Plus)、減算 (Subtract/Difference)、乗算 (Multiply)、スクリーン (Screen) などの基本的なブレンドモードによるレイヤー合成
- **多彩な図形描画と図形論理演算 (Boolean Operations)**:
  - 凸多角形、凹多角形、円・楕円、円弧 (Arc)、パスなどの描画
  - 図形同士の結合 (Union)、差分 (Difference)、交差 (Intersection)、排他的論理和 (Xor)
- **スタイル・エフェクト**:
  - アウトライン（ストローク）描画（線幅・破線・端点のカスタマイズ）
  - テクスチャ塗りつぶし（パターンや画像による塗り）
  - フィルタ効果（強度を指定可能なガウスぼかし等）
- **プラグイン・シェーダー拡張システム**:
  - **SkiaSharp シェーダー (`SKRuntimeEffect` / SkSL)**: オブジェクト単位で実行時にカスタムシェーダーを適用可能
  - **DLL プラグイン読み込み**: 外部 DLL を動的ロードし、メニューやツールバーに項目を追加してプラグイン側から SVG ドキュメントツリーを直接操作可能

## 技術スタック

- **ランタイム / 言語**: .NET 10 (`net10.0`) / C#
- **UI フレームワーク**: Avalonia UI (`11.3.0`) + `CommunityToolkit.Mvvm`
- **2D グラフィックス / 幾何演算 / シェーダー**: `SkiaSharp` (`2.88.9`)
- **ライセンス**: [MIT License](LICENSE)

## プロジェクト構成

```text
Vectorer/
├── .agents/                        # AI エージェント (Antigravity 等) 向けルール設定
├── docs/                           # アーキテクチャ・プラグイン仕様・SVG拡張仕様・ロードマップ
├── src/
│   ├── Vectorer.Abstractions/      # プラグイン SDK・共通ドメインモデル・インターフェース
│   ├── Vectorer.Core/              # ドキュメント操作・図形論理演算・Undo/Redo 管理
│   ├── Vectorer.Svg/               # SVG リーダー / ライター (System.Xml.Linq ベース)
│   ├── Vectorer.Rendering/         # SkiaSharp レンダリングエンジン・シェーダー・PNG エクスポーター
│   ├── Vectorer.Plugins/           # AssemblyLoadContext による DLL プラグイン動的ローダー
│   └── Vectorer.App/               # Avalonia デスクトップアプリケーション本体
├── plugins/
│   └── Vectorer.SamplePlugin/      # 公式サンプルプラグイン (メニュー追加・カスタムシェーダー提供)
└── tests/                          # 単体・結合テスト (xUnit)
```

## ビルドとテスト

```bash
# ソリューション全体のビルド
dotnet build

# テストの実行
dotnet test

# アプリケーションの起動
dotnet run --project src/Vectorer.App

# リリースパッケージの作成 (.NET 10 ファイルベースアプリ)
# デフォルトで win-x64 (.zip), win-arm64 (.zip), osx-arm64 (.tar.gz) を artifacts/packages/ に出力
dotnet run build.cs

# 特定の RID のみパッケージ化する場合
dotnet run build.cs -- --rid win-x64
dotnet run build.cs -- --rid osx-arm64 --skip-tests
```

## 開発者および AI エージェント向けガイド

このリポジトリは人間の開発者だけでなく、**Codex** や **Antigravity** などの AI コーディングエージェントとの協業を前提に設計されています。
作業を開始する前に必ず以下のドキュメントを参照してください。

- [AGENTS.md](AGENTS.md) — AI エージェントおよび開発者向けの最優先ルール・コマンド・責務境界
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — シーングラフ、レンダリングパイプライン、座標系・図形演算の設計
- [docs/PLUGIN_SYSTEM.md](docs/PLUGIN_SYSTEM.md) — DLL プラグインおよび SkiaSharp シェーダー拡張の仕様
- [docs/SVG_SPEC_SUPPORT.md](docs/SVG_SPEC_SUPPORT.md) — SVG 入出力マッピングと `vectorer:*` 拡張属性仕様
- [docs/ROADMAP.md](docs/ROADMAP.md) — 開発フェーズと機能チェックリスト
