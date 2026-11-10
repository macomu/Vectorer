# Third-Party Notices

Vectorer is licensed under the **MIT License**. All third-party dependencies used in this project are required to use permissive open-source licenses compatible with MIT distribution.

## Dependencies

| Package | Version | License | Project URL |
| :--- | :--- | :--- | :--- |
| **Avalonia** (`Avalonia.*`) | 11.3.0 | MIT | https://github.com/AvaloniaUI/Avalonia |
| **SkiaSharp** | 2.88.9 | MIT | https://github.com/mono/SkiaSharp |
| **CommunityToolkit.Mvvm** | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| **xUnit** (`xunit.*`) | 2.9.3 / 3.0.2 | Apache-2.0 | https://github.com/xunit/xunit |
| **Microsoft.NET.Test.Sdk** | 17.13.0 | MIT | https://github.com/microsoft/vstest |
| **coverlet.collector** | 6.0.4 | MIT | https://github.com/coverlet-coverage/coverlet |

## Policy for Adding New Dependencies

1. **Permissive Licenses Only**: Only packages with `MIT`, `Apache-2.0`, `BSD-2-Clause`, `BSD-3-Clause`, or `ISC` licenses are permitted.
2. **Prohibited Licenses**: `GPL`, `LGPL`, `AGPL`, `SSPL`, or proprietary/commercial-restricted packages are strictly forbidden.
3. **Update Required**: When adding a new package to `Directory.Packages.props`, always verify its license and add an entry to this table.
