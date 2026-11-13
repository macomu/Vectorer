# Vectorer コントリビューションガイド

Vectorer へのバグ報告・機能提案・Pull Request を歓迎します。
プロジェクトの全体像は [README.md](README.md)、ライセンス方針は [LICENSE](LICENSE) および [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)、コミュニティの行動規範は [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) をご確認ください。

---

## 1. 開発環境とビルド・テスト

- **必須 SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)（[global.json](global.json) で `10.0.100` 以上を指定）

```bash
# ソリューション全体のビルド
dotnet build Vectorer.slnx

# 全単体・結合テストの実行
dotnet test Vectorer.slnx

# アプリケーションの起動
dotnet run --project src/Vectorer.App

# リリースパッケージのローカル生成 (任意)
dotnet run build.cs -- --rid win-x64
```

---

## 2. アーキテクチャとプロジェクト依存境界

各プロジェクトの責務分離を保つため、以下の参照方向を厳守してください（詳細は [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) を参照）。

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

Vectorer.Rendering (SkiaSharp 描画・フィルタ・シェーダー・PNG出力)
  ├──> Vectorer.Core
  └──> Vectorer.Abstractions

Vectorer.Svg (SVG パース・シリアライズ)
  ├──> Vectorer.Core
  └──> Vectorer.Abstractions

Vectorer.Core (ドキュメント操作・Boolean 演算・Undo/Redo)
  └──> Vectorer.Abstractions

Vectorer.Abstractions (プラグイン SDK・共通モデル・SkiaSharp のみ)
  └──> SkiaSharp
```

- `Vectorer.Abstractions` および `Vectorer.Core` から `Avalonia.*` を参照しないでください。
- プラグイン開発のサンプル（[plugins/Vectorer.SamplePlugin](plugins/Vectorer.SamplePlugin/Vectorer.SamplePlugin.csproj)）が示すとおり、外部プラグインは `Vectorer.Abstractions` のみに依存する構成を維持してください。

---

## 3. 開発ルール・厳守事項

### A. MIT ライセンス互換性（最重要）
- Vectorer は **MIT License** で配布されています。
- 新しい NuGet パッケージや外部コードを追加する場合は、**MIT 互換のパーミッシブライセンス（MIT / Apache-2.0 / BSD-2-Clause / BSD-3-Clause / ISC 等）のみ** を使用してください。
- `GPL` / `LGPL` / `AGPL` / `SSPL` や商用・再配布制限のあるパッケージは追加禁止です。
- パッケージを追加・更新する際は、[Directory.Packages.props](Directory.Packages.props) にライセンスコメントを添えて定義し、[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) の依存一覧表を更新してください。

### B. コーディング規約・設計方針
- **言語・フレームワーク**: `.NET 10` (`net10.0`)、File-scoped namespace、Nullable reference types 有効。
- **MVVM パターン**: `Vectorer.App` では `CommunityToolkit.Mvvm` を使用し、View (`.axaml`) 側へビジネスロジックを書かないでください。
- **Undo / Redo**: ドキュメント構造や図形プロパティを変更する操作は、`IDocumentCommand` / `IUndoRedoService` を通じて履歴管理できるようにしてください。
- **SkiaSharp リソース管理**: `SKPath`、`SKPaint`、`SKSurface`、`SKImage`、`SKShader`、`SKRuntimeEffect`、`SKImageFilter` 等のネイティブリソースは `using` または確実な `Dispose()` を行ってください。
- **非同期 I/O**: ファイル読み書きやバッチ PNG エクスポート等の重い処理は `async/await` で UI スレッドをブロックしないようにしてください。

### C. テストと多言語対応
- `Vectorer.Core` / `Vectorer.Svg` / `Vectorer.Rendering` / `Vectorer.App` にロジックを追加・変更した場合は、対応する `tests/*.Tests` プロジェクトに xUnit テストを追加してください。
- UI 文字列を追加・変更する場合は、[Strings.resx](src/Vectorer.App/Localization/Strings.resx)（英語）と [Strings.ja.resx](src/Vectorer.App/Localization/Strings.ja.resx)（日本語）の両方を更新し、`LocalizationTests` が通ることを確認してください（設計は [docs/LOCALIZATION.md](docs/LOCALIZATION.md) 参照）。

---

## 4. Issue / Pull Request の流れ

- **バグ報告・機能提案**: GitHub の Issues から該当テンプレートを選んで起票してください。セキュリティ上の脆弱性については公開 Issue ではなく [SECURITY.md](SECURITY.md) の手順に従ってください。
- **Pull Request**:
  1. リポジトリをフォークし、`main` から作業ブランチを作成します。
  2. 変更とテストを実装し、`dotnet build Vectorer.slnx` および `dotnet test Vectorer.slnx` が警告・エラー 0 で通ることを確認します。
  3. Pull Request テンプレートに沿って変更概要・確認項目を記入し、GitHub 上で提出してください。
