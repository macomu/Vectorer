# Security Policy

**Languages:** [日本語](SECURITY.md) | English

## Supported Versions

Security updates are currently supported for the following versions:

| Version | Supported |
| :--- | :--- |
| `0.1.x` | Yes |

---

## Reporting a Vulnerability

If you discover a security vulnerability, **please do not report it in a public GitHub Issue or Pull Request**. Instead, report it privately using GitHub **Private vulnerability reporting**:

1. Open the **Security** tab of this repository.
2. Select **Advisories** (or **Report a vulnerability**).
3. Provide affected versions, impact details, and reproduction steps.

All communication will be handled privately within the GitHub security advisory thread.

---

## Security Notes for Plugins, Shaders & External Files

- **External DLL Plugins (`./plugins/*.dll`)**: Vectorer dynamically loads plugin DLLs using `AssemblyLoadContext`. Because plugins execute arbitrary code with the same privileges as the host process, only install plugins from trusted sources.
- **External SVG files**. Please report parser or renderer crashes privately using the steps above. v0.1.0 preserves inline SkSL when saving the file.
