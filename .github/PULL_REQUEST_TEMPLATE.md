## 変更概要 / Summary

<!-- この Pull Request で何を変更・修正したかを記載してください / Describe the changes made in this Pull Request. -->

## 関連 Issue / Related Issues

<!-- Closes #123 など / e.g., Closes #123 -->

## 確認チェックリスト / Checklist

- [ ] `dotnet build Vectorer.slnx` が警告・エラー 0 で通ること / Solution builds with 0 warnings and 0 errors
- [ ] `dotnet test Vectorer.slnx`（および関連テスト）がすべて通ること / All tests pass
- [ ] 新規 NuGet パッケージを追加していない、または MIT 互換ライセンスであることを確認し `Directory.Packages.props` と `THIRD_PARTY_NOTICES.md` を更新したこと / No non-permissive NuGet packages added (`Directory.Packages.props` and `THIRD_PARTY_NOTICES.md` updated if needed)
- [ ] `Vectorer.Abstractions` / `Vectorer.Core` に `Avalonia.*` への依存を追加していないこと / No `Avalonia.*` references added to `Vectorer.Abstractions` or `Vectorer.Core`
- [ ] UI 文言を変更した場合、`Strings.resx`（英語）と `Strings.ja.resx`（日本語）の両方を更新したこと / Both `Strings.resx` and `Strings.ja.resx` updated if UI strings changed
- [ ] 必要に応じてドキュメント（`README*.md` / `docs/*`）を更新したこと / Documentation updated if needed
