# AGENTS.md — Vectorer AI Agent & Developer Guide

このファイルは、**Codex**、**Antigravity**、およびその他の AI コーディングエージェントや人間の開発者が **Vectorer** プロジェクトで作業する際の最優先ガイドラインです。

---

## 1. プロジェクトの目的

**Vectorer** は、.NET 10 と Avalonia UI、SkiaSharp を用いた**軽量・高速・高拡張な SVG エディタ**です。
以下の機能群をクリーンなアーキテクチャと高い応答性で実現します。

1. **SVG ファイルの読み書き**（標準 SVG 互換性を維持しつつ、`vectorer:*` 拡張属性で独自機能も保存）
2. **PNG エクスポート**（サイズ指定保存：ドキュメント全体 / 選択要素のみ / レイヤー単位のバッチ出力）
3. **レイヤーとグループ**（Overlay、加算、減算、乗算、スクリーン等のブレンドモードによる合成）
4. **図形描画と論理演算**（凸多角形・凹多角形・円・円弧等の描画、および Union / Difference / Intersection / Xor の図形演算）
5. **スタイル・フィルタ**（アウトライン描画、テクスチャ・パターン塗りつぶし、強度調整可能なぼかし等のフィルタ）
6. **プラグイン & シェーダー拡張**
   - `SkiaSharp` のシェーダー (`SKRuntimeEffect` / SkSL) をオブジェクト単位で実行時適用
   - 外部 DLL を読み込み、メニューやツールバーに項目を追加し、DLL 側から SVG ドキュメントを操作可能にする

---

## 2. 厳守事項（ハードルール）

### A. ライセンス制約（最重要）
- 本プロジェクトは **MIT ライセンス** で配布されます。
- 追加・使用するすべての NuGet パッケージや外部コードは、**MIT ライセンスでの配布と互換性があるパーミッシブライセンス（MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, ISC 等）のみ**に限定します。
- **GPL / LGPL / AGPL / SSPL / 商用制限のあるパッケージの追加は厳禁**です。
- 新しいパッケージを追加する場合は、必ず `Directory.Packages.props` にライセンスコメントを添えて定義し、`THIRD_PARTY_NOTICES.md` を更新してください。

### B. プロジェクト間の依存関係ルール
各プロジェクトの責務と参照方向を厳格に守ってください。循環参照や UI 依存の漏洩は禁止です。

```text
Vectorer.App (Avalonia UI / MVVM)
  ├──> Vectorer.Core
  ├──> Vectorer.Svg
  ├──> Vectorer.Rendering
  ├──> Vectorer.Plugins
  └──> Vectorer.Abstractions

Vectorer.Plugins (DLL 動的ロード)
  ├──> Vectorer.Core
  └──> Vectorer.Abstractions

Vectorer.Rendering (SkiaSharp 描画・フィルタ・Shader・PNG出力)
  ├──> Vectorer.Core
  └──> Vectorer.Abstractions

Vectorer.Svg (SVG パース・シリアライズ)
  ├──> Vectorer.Core
  └──> Vectorer.Abstractions

Vectorer.Core (ドキュメント操作・Boolean演算・Undo/Redo)
  └──> Vectorer.Abstractions

Vectorer.Abstractions (プラグイン SDK・共通モデル・SkiaSharp 参照のみ)
  └──> SkiaSharp (外部 NuGet のみ、Avalonia への参照は厳禁)
```

- **`Vectorer.Abstractions` や `Vectorer.Core` から `Avalonia.*` を参照してはなりません。**
- サードパーティ製プラグイン（例: `plugins/Vectorer.SamplePlugin`）は **`Vectorer.Abstractions` のみ**を参照して開発できるように保ってください。

### C. リソース管理とパフォーマンス
- `SkiaSharp` のネイティブリソース（`SKPath`, `SKPaint`, `SKSurface`, `SKImage`, `SKShader`, `SKRuntimeEffect`, `SKImageFilter` など）を扱う際は、リークを防ぐため必ず `using` ステートメントまたは明示的な `Dispose()` を行ってください。
- エディタの軽快さを損なわないよう、重い I/O やバッチ PNG エクスポート処理は非同期 (`async/await`) またはバックグラウンドで実行できるように設計してください。

---

## 3. よく使うコマンド

ビルド用SDKは `global.json` が指定する .NET 11 です。アプリとテストの実行には .NET 10 ランタイムが必要です。

作業の前後や変更後には、必ず以下のコマンドでビルドとテストが通ることを確認してください。

```bash
# ソリューション全体のビルド
dotnet build Vectorer.slnx

# 全単体テストの実行
dotnet test Vectorer.slnx

# ※ AI エージェント (Codex / Antigravity) のサンドボックス環境で
# MSBuild ワーカーノードや VSTest の TCP ソケット通信が制限される場合は以下を使用:
dotnet build Vectorer.slnx -m:1 --disable-build-servers --source "$HOME/.nuget/packages"
dotnet tests/Vectorer.Core.Tests/bin/Debug/net10.0/Vectorer.Core.Tests.dll
dotnet tests/Vectorer.Svg.Tests/bin/Debug/net10.0/Vectorer.Svg.Tests.dll
dotnet tests/Vectorer.Rendering.Tests/bin/Debug/net10.0/Vectorer.Rendering.Tests.dll

# アプリケーションの実行
dotnet run --project src/Vectorer.App/Vectorer.App.csproj

# 配布パッケージ作成 (win-x64 / win-arm64 / osx-arm64)
dotnet run build.cs
```

---

## 4. コーディング規約とワークフロー

1. **ターゲットフレームワーク**: `.NET 10` (`net10.0`)、C# 最新言語機能（File-scoped namespace、Nullable reference types 有効）を使用します。
2. **MVVM パターン**: `Vectorer.App` では `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`) を使用し、View (`.axaml`) にビジネスロジックを書かないでください。
3. **Undo / Redo の考慮**: ドキュメント構造や図形プロパティを編集する操作は、`Vectorer.Abstractions.Commands.IDocumentCommand` / `IUndoRedoService` を通じて履歴管理可能にすることを推奨します。
4. **テストの追加**: `Vectorer.Core`、`Vectorer.Svg`、`Vectorer.Rendering` に新しいロジック（図形演算、SVG 変換、レンダリング・PNG 出力など）を追加した場合は、対応する `tests/*.Tests` プロジェクトに xUnit テストを追加してください。

---

## 5. ドキュメント索引

詳細な設計仕様については、`docs/` 配下のドキュメントを参照・更新してください。

- [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md): シーングラフ構造、ブレンドモード、フィルタ、テクスチャ塗り、Boolean 演算、PNG エクスポーターの設計
- [`docs/UI_DESIGN.md`](docs/UI_DESIGN.md): 画面レイアウト、メニューバー、左側描画ツールボックス、動的コンテキストツールバー、右側インスペクタ＆レイヤーツリー、各種ダイアログの GUI 設計仕様
- [`docs/PLUGIN_SYSTEM.md`](docs/PLUGIN_SYSTEM.md): 外部 DLL プラグインのインターフェース (`IVectorerPlugin`)、メニュー・ツールバー拡張、Skia シェーダー適用の仕組み
- [`docs/SVG_SPEC_SUPPORT.md`](docs/SVG_SPEC_SUPPORT.md): 標準 SVG 要素と `xmlns:vectorer="https://vectorer.dev/ns/1.0"` 拡張属性のマッピング仕様
- [`docs/ROADMAP.md`](docs/ROADMAP.md): 実装フェーズとタスク進捗管理
