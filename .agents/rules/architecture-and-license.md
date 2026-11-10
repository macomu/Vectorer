---
description: Core architecture, dependency boundaries, SkiaSharp resource safety, and MIT license compliance rules for Vectorer.
---

# Vectorer Architecture & License Rules

1. **MIT License Compliance**:
   - Vectorer is distributed under the MIT License.
   - Only add dependencies with permissive licenses (`MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, `ISC`).
   - Never introduce `GPL`, `LGPL`, `AGPL`, or commercial-restricted libraries.
   - Centralize all package versions in `Directory.Packages.props` with a comment stating the package license, and document them in `THIRD_PARTY_NOTICES.md`.

2. **Strict Project Dependency Boundaries**:
   - `Vectorer.Abstractions`: Contains domain models, plugin interfaces, and `SkiaSharp` references. Must NEVER reference `Avalonia.*`.
   - `Vectorer.Core`: Implements document manipulation, Undo/Redo history, and boolean path geometry (`SKPath.Op`). Depends only on `Vectorer.Abstractions`.
   - `Vectorer.Svg`: Implements SVG reading/writing (`System.Xml.Linq`). Depends on `Vectorer.Abstractions` and `Vectorer.Core`.
   - `Vectorer.Rendering`: Implements the SkiaSharp rendering pipeline (layers, blend modes, blur/filters, texture fills, outlines, runtime SkSL shaders) and PNG exporting (full document, selection, and layer batch).
   - `Vectorer.Plugins`: Implements dynamic DLL loading via `AssemblyLoadContext`.
   - `Vectorer.App`: Avalonia UI desktop application using `CommunityToolkit.Mvvm`.

3. **SkiaSharp Native Resource Management**:
   - Always dispose transient `SKSurface`, `SKImage`, `SKBitmap`, `SKPath`, `SKPaint`, `SKShader`, `SKRuntimeEffect`, and `SKImageFilter` instances via `using` declarations.

4. **Verification**:
   - Always verify compilation and unit tests after making code changes.
   - In standard environments: `dotnet build Vectorer.slnx` and `dotnet test Vectorer.slnx`.
   - In sandboxed environments (where multi-node pipes or VSTest loopback sockets are blocked):
     - Build: `dotnet build Vectorer.slnx -m:1 --disable-build-servers --no-restore`
     - Test: run `dotnet tests/Vectorer.Core.Tests/bin/Debug/net10.0/Vectorer.Core.Tests.dll`, `dotnet tests/Vectorer.Svg.Tests/bin/Debug/net10.0/Vectorer.Svg.Tests.dll`, and `dotnet tests/Vectorer.Rendering.Tests/bin/Debug/net10.0/Vectorer.Rendering.Tests.dll`.
