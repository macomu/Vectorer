using System.Globalization;
using System.Xml.Linq;
using System.Text.RegularExpressions;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;

namespace Vectorer.Svg;

public sealed partial class SvgSerializer
{
    private static readonly HashSet<string> SceneElements =
        ["g", "rect", "ellipse", "circle", "polygon", "polyline", "path", "text"];

    private static readonly HashSet<string> VectorerAttributes =
    [
        "title", "background", "name", "locked", "blend-mode", "blur-x", "blur-y", "shader-id",
        "shader-sksl", "shader-uniforms", "layer", "poly-kind", "cx", "cy", "points-count",
        "outer-r", "inner-r", "start-angle", "shape-type", "rx", "ry", "sweep-angle", "close-mode",
        "node-kinds", "texture-kind", "texture-primary", "texture-secondary", "texture-scale", "texture-angle"
    ];

    private static bool HasSvgNamespace(XElement element) =>
        element.Name.Namespace == SvgNs || element.Name.Namespace == XNamespace.None;

    private static bool IsSupportedStyleProperty(string name) =>
        InheritedProperties.Contains(name) ||
        name is "opacity" or "display" or "stop-color" or "stop-opacity" or "mix-blend-mode" or "filter";

    private static bool HasUnsupportedContent(XDocument document)
    {
        if (document.Root is not { } root) return false;
        if (document.Nodes().OfType<XProcessingInstruction>().Any()) return true;

        var elements = root.DescendantsAndSelf().ToArray();
        var definitions = elements.Where(element => element.Attribute("id") is not null)
            .ToLookup(element => element.Attribute("id")!.Value, StringComparer.Ordinal);
        return elements.Any(element => !IsSupportedElement(element, root) ||
            element.Attributes().Any(attribute => !IsSupportedAttribute(element, attribute, definitions)));
    }

    private static bool IsSupportedElement(XElement element, XElement root)
    {
        if (element.Name == VectorerNs + "palette")
            return element.Parent?.Name.LocalName == "metadata";
        if (element.Name == VectorerNs + "color")
            return element.Parent?.Name == VectorerNs + "palette";
        if (!HasSvgNamespace(element)) return false;

        var parent = element.Parent;
        var parentName = parent?.Name.LocalName;
        var name = element.Name.LocalName;
        return name switch
        {
            "svg" => element == root,
            "metadata" or "defs" => parent == root,
            "linearGradient" or "filter" => parentName == "defs",
            "stop" => parentName == "linearGradient",
            "feGaussianBlur" => parentName == "filter" && ReadBlurDefinition(parent!) is not null,
            "tspan" => parentName == "text" && IsSupportedTextLine(element, parent!),
            _ => SceneElements.Contains(name) && (parent == root || parentName == "g")
        };
    }

    private static bool IsSupportedTextLine(XElement line, XElement text)
    {
        if (text.Nodes().OfType<XText>().Any(node => !string.IsNullOrWhiteSpace(node.Value)))
            return false;

        var index = line.ElementsBeforeSelf().Count();
        var expectedX = ParseFloat(text.Attribute("x")?.Value, 0f);
        var expectedY = ParseFloat(text.Attribute("y")?.Value, 0f) +
            index * ParseFloat(text.Attribute("font-size")?.Value, 16f) * 1.2f;
        return MatchesCoordinate("x", expectedX) && MatchesCoordinate("y", expectedY);

        bool MatchesCoordinate(string name, float expected) =>
            line.Attribute(name) is { } attribute
                ? Math.Abs(ParseFloat(attribute.Value, float.NaN) - expected) < 0.001f
                : index == 0;
    }

    private static bool IsSupportedAttribute(XElement element, XAttribute attribute, ILookup<string, XElement> definitions)
    {
        if (attribute.IsNamespaceDeclaration) return true;
        var name = attribute.Name.LocalName;
        var tag = element.Name.LocalName;
        if (attribute.Name.Namespace == VectorerNs) return VectorerAttributes.Contains(name);
        if (attribute.Name == XNamespace.Xml + "space") return tag == "text";
        if (attribute.Name.Namespace != XNamespace.None &&
            attribute.Name != XName.Get("href", "http://www.w3.org/1999/xlink")) return false;

        if (name == "style")
        {
            return attribute.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .All(part =>
                {
                    var colon = part.IndexOf(':');
                    return colon > 0 && IsSupportedPresentation(tag, part[..colon].Trim(), part[(colon + 1)..].Trim(), definitions);
                });
        }
        if (IsSupportedStyleProperty(name))
            return IsSupportedPresentation(tag, name, attribute.Value, definitions);

        return tag switch
        {
            "svg" => name is "width" or "height" or "viewBox" or "preserveAspectRatio" or "version",
            "color" => name is "name" or "value",
            "linearGradient" => name is "id" or "x1" or "x2" or "y1" or "y2" or "gradientUnits" or "gradientTransform" or "spreadMethod"
                || name == "href" && attribute.Value.StartsWith('#') &&
                    definitions[attribute.Value[1..]].Any(definition => definition.Name.LocalName == "linearGradient"),
            "stop" => name == "offset",
            "filter" => name == "id" && ReadBlurDefinition(element) is not null,
            "feGaussianBlur" => name == "stdDeviation",
            "tspan" => name is "x" or "y",
            _ => name is "id" or "transform" || tag switch
            {
                "rect" => name is "x" or "y" or "width" or "height" or "rx" or "ry",
                "ellipse" => name is "cx" or "cy" or "rx" or "ry",
                "circle" => name is "cx" or "cy" or "r",
                "polygon" or "polyline" => name == "points",
                "path" => name == "d",
                "text" => name is "x" or "y",
                _ => false
            }
        };
    }

    private static bool IsSupportedPresentation(string tag, string name, string value, ILookup<string, XElement> definitions)
    {
        if (value.Contains("!important", StringComparison.OrdinalIgnoreCase)) return false;
        if (name is "stop-color" or "stop-opacity") return tag == "stop";
        if (name is "opacity" or "display") return SceneElements.Contains(tag) || tag == "svg" && name == "opacity";
        if (name == "mix-blend-mode") return SceneElements.Contains(tag) && ReadStandardBlend(value) is not null;
        if (name == "filter")
            return SceneElements.Contains(tag) && (value == "none" || GetLocalReference(value) is { } filterId &&
                definitions[filterId].Any(definition => ReadBlurDefinition(definition) is not null));
        if (name is "fill" or "stroke" && value.TrimStart().StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            return GetLocalReference(value, allowFallback: true) is { } gradientId &&
                definitions[gradientId].Any(definition => definition.Name.LocalName == "linearGradient");
        return InheritedProperties.Contains(name) && tag != "tspan";
    }

    private static string? GetLocalReference(string? value, bool allowFallback = false)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var match = Regex.Match(value.Trim(), """^url\(\s*['"]?#([^)'"\s]+)['"]?\s*\)""", RegexOptions.IgnoreCase);
        return match.Success && (allowFallback || match.Length == value.Trim().Length) ? match.Groups[1].Value : null;
    }

    private static FilterSettings? ReadBlurDefinition(XElement element)
    {
        if (!HasSvgNamespace(element) || element.Name.LocalName != "filter" ||
            element.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && attribute.Name != "id"))
            return null;
        var primitives = element.Elements().ToArray();
        if (primitives.Length != 1) return null;
        var blur = primitives[0];
        if (!HasSvgNamespace(blur) || blur.Name.LocalName != "feGaussianBlur" || blur.HasElements ||
            blur.Attributes().Any(attribute => !attribute.IsNamespaceDeclaration && attribute.Name != "stdDeviation"))
            return null;

        var parts = (blur.Attribute("stdDeviation")?.Value ?? "").Split([' ', '\t', '\r', '\n', ','],
            StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2 || !TryReadSigma(parts[0], out var x)) return null;
        var y = x;
        if (parts.Length == 2 && !TryReadSigma(parts[1], out y)) return null;
        return new FilterSettings(x, y);

        static bool TryReadSigma(string text, out float sigma) =>
            float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out sigma) &&
            float.IsFinite(sigma) && sigma >= 0f;
    }

    private static LayerBlendMode? ReadStandardBlend(string? value) => value switch
    {
        "normal" => LayerBlendMode.Normal,
        "multiply" => LayerBlendMode.Multiply,
        "screen" => LayerBlendMode.Screen,
        "overlay" => LayerBlendMode.Overlay,
        "difference" => LayerBlendMode.Difference,
        "darken" => LayerBlendMode.Darken,
        "lighten" => LayerBlendMode.Lighten,
        _ => null
    };

    private static void ApplyStandardEffects(XElement element, SceneNode node, ReadContext context)
    {
        if (element.Attribute(VectorerNs + "blend-mode") is null &&
            ReadStandardBlend(element.Attribute("mix-blend-mode")?.Value) is { } mode)
            node.BlendMode = mode;

        node.Filter ??= context.ReadBlur(element.Attribute("filter")?.Value);
    }
}
