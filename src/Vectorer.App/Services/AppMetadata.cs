using System.Reflection;
using System.Runtime.InteropServices;

namespace Vectorer.App.Services;

/// <summary>
/// Application metadata read from compiled assembly attributes for display in UI and diagnostics.
/// </summary>
public sealed record AppMetadata(
    string ProductName,
    string Version,
    string InformationalVersion,
    string Description,
    string Copyright,
    string License,
    string RuntimeSummary)
{
    public static AppMetadata Current { get; } = FromAssembly(typeof(AppMetadata).Assembly);

    public static AppMetadata FromAssembly(Assembly assembly)
    {
        var product = assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var cleanVersion = NormalizeDisplayVersion(informational)
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.1.0";
        var description = assembly.GetCustomAttribute<AssemblyDescriptionAttribute>()?.Description;
        var copyright = assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright;

        return new AppMetadata(
            ProductName: string.IsNullOrWhiteSpace(product) ? "Vectorer" : product,
            Version: cleanVersion,
            InformationalVersion: string.IsNullOrWhiteSpace(informational) ? cleanVersion : informational,
            Description: string.IsNullOrWhiteSpace(description)
                ? "Lightweight, extensible SVG editor built with .NET 10, Avalonia UI, and SkiaSharp."
                : description,
            Copyright: string.IsNullOrWhiteSpace(copyright)
                ? "Copyright (c) 2026 Vectorer Contributors"
                : copyright,
            License: "MIT License",
            RuntimeSummary: $"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.OSArchitecture})");
    }

    private static string? NormalizeDisplayVersion(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return null;
        }

        var plusIndex = informationalVersion.IndexOf('+');
        var trimmed = plusIndex >= 0 ? informationalVersion[..plusIndex] : informationalVersion;
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
