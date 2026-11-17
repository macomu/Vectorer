# Contributing to Vectorer

**Languages:** [日本語](CONTRIBUTING.md) | English

Thank you for your interest in contributing to Vectorer! Bug reports, feature proposals, and pull requests are all welcome.
Please review [README.en.md](README.en.md) for an overview of the project, [LICENSE](LICENSE) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for licensing rules, and [CODE_OF_CONDUCT.en.md](CODE_OF_CONDUCT.en.md) for community expectations.

---

## 1. Development Environment, Build & Tests

- **Required SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`10.0.403` or later per [global.json](global.json))

```bash
# Build the entire solution
dotnet build Vectorer.slnx

# Run all unit and integration tests
dotnet test Vectorer.slnx

# Launch the desktop application
dotnet run --project src/Vectorer.App

# Build a local release archive (optional)
dotnet run build.cs -- --rid win-x64
```

---

## 2. Architecture & Project Dependency Boundaries

To keep responsibilities cleanly separated, strictly follow the project reference direction below (see [docs/ARCHITECTURE.en.md](docs/ARCHITECTURE.en.md) for details):

```text
Vectorer.App (Avalonia UI / MVVM)
  ├──> Vectorer.Core
  ├──> Vectorer.Svg
  ├──> Vectorer.Rendering
  ├──> Vectorer.Plugins
  └──> Vectorer.Abstractions

Vectorer.Plugins (Dynamic DLL loading)
  ├──> Vectorer.Core
  └──> Vectorer.Abstractions

Vectorer.Rendering (SkiaSharp rendering, filters, shaders, PNG export)
  ├──> Vectorer.Core
  └──> Vectorer.Abstractions

Vectorer.Svg (SVG parser / serializer)
  ├──> Vectorer.Core
  └──> Vectorer.Abstractions

Vectorer.Core (Document operations, Boolean ops, Undo/Redo)
  └──> Vectorer.Abstractions

Vectorer.Abstractions (Plugin SDK, domain models, SkiaSharp only)
  └──> SkiaSharp
```

- **Never reference `Avalonia.*` from `Vectorer.Abstractions` or `Vectorer.Core`.**
- Keep external plugins dependent **only on `Vectorer.Abstractions`**, as demonstrated by [plugins/Vectorer.SamplePlugin](plugins/Vectorer.SamplePlugin/Vectorer.SamplePlugin.csproj).

---

## 3. Coding & Licensing Rules

### A. MIT License Compatibility (Strict)
- Vectorer is distributed under the **MIT License**.
- Any new NuGet package or third-party code must use a **permissive license compatible with MIT distribution** (`MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `ISC`, etc.).
- Packages licensed under `GPL`, `LGPL`, `AGPL`, `SSPL`, or restrictive commercial terms are strictly prohibited.
- Whenever adding or updating packages in [Directory.Packages.props](Directory.Packages.props), add a license comment next to the entry and update [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

### B. Coding Conventions & Design Guidelines
- **Language & Runtime**: `.NET 10` (`net10.0`), file-scoped namespaces, and nullable reference types enabled.
- **MVVM Pattern**: Use `CommunityToolkit.Mvvm` in `Vectorer.App`, and keep business logic out of `.axaml` views.
- **Undo / Redo**: Document and shape edits should be tracked via `IDocumentCommand` / `IUndoRedoService`.
- **SkiaSharp Resource Management**: Always dispose transient `SKPath`, `SKPaint`, `SKSurface`, `SKImage`, `SKShader`, `SKRuntimeEffect`, and `SKImageFilter` instances via `using` or explicit `Dispose()`.
- **Async I/O**: Keep heavy file I/O and batch PNG export operations asynchronous (`async/await`) so the UI thread remains responsive.

### C. Tests & Localization
- Add xUnit tests in the corresponding `tests/*.Tests` project whenever adding or modifying logic in `Vectorer.Core`, `Vectorer.Svg`, `Vectorer.Rendering`, or `Vectorer.App`.
- Whenever adding or modifying UI strings, update **both** [Strings.resx](src/Vectorer.App/Localization/Strings.resx) (English) and [Strings.ja.resx](src/Vectorer.App/Localization/Strings.ja.resx) (Japanese) and verify `LocalizationTests` passes.

---

## 4. Issues & Pull Requests

- **Bug Reports & Feature Requests**: Use the GitHub Issue templates in `.github/ISSUE_TEMPLATE/`. For security vulnerabilities, do **not** open a public issue — follow [SECURITY.en.md](SECURITY.en.md) instead.
- **Pull Requests**:
  1. Fork the repository and create a branch from `main`.
  2. Implement your changes and tests, and verify `dotnet build Vectorer.slnx` and `dotnet test Vectorer.slnx` pass with zero warnings and zero errors.
  3. Fill out the Pull Request template and submit your PR on GitHub.
