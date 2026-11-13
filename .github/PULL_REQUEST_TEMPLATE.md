## 変更概要 / Summary

<!-- この Pull Request で何を変更・修正したかを記載してください -->

## 関連 Issue / Related Issues

<!-- Closes #123 など -->

## 確認チェックリスト / Checklist

- [ ] `dotnet build Vectorer.slnx` が警告・エラー 0 で通ること
- [ ] `dotnet test Vectorer.slnx`（および関連テスト）がすべて通ること
- [ ] 新規 NuGet パッケージを追加していない、または MIT 互換ライセンスであることを確認し `Directory.Packages.props` と `THIRD_PARTY_NOTICES.md` を更新したこと
- [ ] `Vectorer.Abstractions` / `Vectorer.Core` に `Avalonia.*` への依存を追加していないこと
- [ ] UI 文言を変更した場合、`Strings.resx`（英語）と `Strings.ja.resx`（日本語）の両方を更新したこと
- [ ] 必要に応じてドキュメント（`README.md` / `docs/*`）を更新したこと
