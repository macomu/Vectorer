using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Services;
using Vectorer.Abstractions.Styling;

namespace Vectorer.Svg;

/// <summary>
/// Serializes and deserializes <see cref="SvgDocument"/> to/from standard SVG XML with <c>vectorer:*</c> extension metadata.
/// </summary>
public sealed class SvgSerializer : ISvgSerializer
{
    public static readonly XNamespace SvgNs = "http://www.w3.org/2000/svg";
    public static readonly XNamespace VectorerNs = "https://vectorer.dev/ns/1.0";

    public string SerializeToString(SvgDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var xDoc = BuildXDocument(document);
        using var sw = new Utf8StringWriter();
        xDoc.Save(sw, SaveOptions.None);
        return sw.ToString();
    }

    public void SaveToStream(SvgDocument document, Stream outputStream)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(outputStream);
        var xDoc = BuildXDocument(document);
        xDoc.Save(outputStream);
    }

    public SvgDocument DeserializeFromString(string svgContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(svgContent);
        var xDoc = XDocument.Parse(svgContent);
        return ParseXDocument(xDoc);
    }

    public SvgDocument LoadFromStream(Stream inputStream)
    {
        ArgumentNullException.ThrowIfNull(inputStream);
        var xDoc = XDocument.Load(inputStream);
        return ParseXDocument(xDoc);
    }

    private static XDocument BuildXDocument(SvgDocument document)
    {
        var root = new XElement(
            SvgNs + "svg",
            new XAttribute("xmlns", SvgNs.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "vectorer", VectorerNs.NamespaceName),
            new XAttribute("width", FormatFloat(document.Width)),
            new XAttribute("height", FormatFloat(document.Height)),
            new XAttribute("viewBox", $"{FormatFloat(document.ViewBox.Left)} {FormatFloat(document.ViewBox.Top)} {FormatFloat(document.ViewBox.Width)} {FormatFloat(document.ViewBox.Height)}"),
            new XAttribute(VectorerNs + "title", document.Title));

        if (document.BackgroundColor.Alpha > 0)
        {
            root.Add(new XAttribute(VectorerNs + "background", FormatColor(document.BackgroundColor)));
        }

        foreach (var layer in document.Layers)
        {
            root.Add(SerializeNode(layer));
        }

        return new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
    }

    private static XElement SerializeNode(SceneNode node)
    {
        XElement element = node switch
        {
            LayerNode layer => SerializeLayer(layer),
            GroupNode group => SerializeGroup(group),
            RectangleShapeNode rect => SerializeRectangle(rect),
            EllipseShapeNode ellipse => SerializeEllipse(ellipse),
            PolygonShapeNode polygon => SerializePolygon(polygon),
            ArcShapeNode arc => SerializeArc(arc),
            PathShapeNode path => SerializePath(path),
            _ => new XElement(SvgNs + "g")
        };

        element.SetAttributeValue("id", node.Id);
        element.SetAttributeValue(VectorerNs + "name", node.Name);

        if (!node.IsVisible)
        {
            element.SetAttributeValue("display", "none");
        }

        if (node.IsLocked)
        {
            element.SetAttributeValue(VectorerNs + "locked", "true");
        }

        if (Math.Abs(node.Opacity - 1.0f) > 0.001f)
        {
            element.SetAttributeValue("opacity", FormatFloat(node.Opacity));
        }

        if (node.BlendMode != LayerBlendMode.Normal)
        {
            element.SetAttributeValue(VectorerNs + "blend-mode", node.BlendMode.ToString());
        }

        if (node.Filter is { HasActiveFilter: true } filter)
        {
            element.SetAttributeValue(VectorerNs + "blur-x", FormatFloat(filter.BlurSigmaX));
            element.SetAttributeValue(VectorerNs + "blur-y", FormatFloat(filter.BlurSigmaY));
        }

        if (node.ShaderEffect is not null)
        {
            element.SetAttributeValue(VectorerNs + "shader-id", node.ShaderEffect.ShaderId);
            if (!string.IsNullOrWhiteSpace(node.ShaderEffect.InlineSksl))
            {
                element.SetAttributeValue(VectorerNs + "shader-sksl", node.ShaderEffect.InlineSksl);
            }
        }

        if (node is ShapeNode shapeNode)
        {
            WriteShapeStyleAttributes(element, shapeNode.Style);
        }

        return element;
    }

    private static XElement SerializeLayer(LayerNode layer)
    {
        var g = new XElement(
            SvgNs + "g",
            new XAttribute(VectorerNs + "layer", "true"));

        foreach (var child in layer.Children)
        {
            g.Add(SerializeNode(child));
        }

        return g;
    }

    private static XElement SerializeGroup(GroupNode group)
    {
        var g = new XElement(SvgNs + "g");
        foreach (var child in group.Children)
        {
            g.Add(SerializeNode(child));
        }

        return g;
    }

    private static XElement SerializeRectangle(RectangleShapeNode rect)
    {
        var el = new XElement(
            SvgNs + "rect",
            new XAttribute("x", FormatFloat(rect.Rect.Left)),
            new XAttribute("y", FormatFloat(rect.Rect.Top)),
            new XAttribute("width", FormatFloat(rect.Rect.Width)),
            new XAttribute("height", FormatFloat(rect.Rect.Height)));

        if (rect.CornerRadiusX > 0f)
        {
            el.Add(new XAttribute("rx", FormatFloat(rect.CornerRadiusX)));
        }

        if (rect.CornerRadiusY > 0f)
        {
            el.Add(new XAttribute("ry", FormatFloat(rect.CornerRadiusY)));
        }

        return el;
    }

    private static XElement SerializeEllipse(EllipseShapeNode ellipse)
    {
        return new XElement(
            SvgNs + "ellipse",
            new XAttribute("cx", FormatFloat(ellipse.Center.X)),
            new XAttribute("cy", FormatFloat(ellipse.Center.Y)),
            new XAttribute("rx", FormatFloat(ellipse.RadiusX)),
            new XAttribute("ry", FormatFloat(ellipse.RadiusY)));
    }

    private static XElement SerializePolygon(PolygonShapeNode polygon)
    {
        var pointsStr = string.Join(" ", polygon.Points.Select(p => $"{FormatFloat(p.X)},{FormatFloat(p.Y)}"));
        var tagName = polygon.IsClosed ? "polygon" : "polyline";
        return new XElement(
            SvgNs + tagName,
            new XAttribute("points", pointsStr));
    }

    private static XElement SerializeArc(ArcShapeNode arc)
    {
        using var path = arc.BuildLocalPath();
        return new XElement(
            SvgNs + "path",
            new XAttribute("d", path.ToSvgPathData()),
            new XAttribute(VectorerNs + "shape-type", "arc"),
            new XAttribute(VectorerNs + "cx", FormatFloat(arc.Center.X)),
            new XAttribute(VectorerNs + "cy", FormatFloat(arc.Center.Y)),
            new XAttribute(VectorerNs + "rx", FormatFloat(arc.RadiusX)),
            new XAttribute(VectorerNs + "ry", FormatFloat(arc.RadiusY)),
            new XAttribute(VectorerNs + "start-angle", FormatFloat(arc.StartAngleDegrees)),
            new XAttribute(VectorerNs + "sweep-angle", FormatFloat(arc.SweepAngleDegrees)),
            new XAttribute(VectorerNs + "close-mode", arc.CloseMode.ToString()));
    }

    private static XElement SerializePath(PathShapeNode path)
    {
        return new XElement(
            SvgNs + "path",
            new XAttribute("d", path.SvgPathData));
    }

    private static void WriteShapeStyleAttributes(XElement element, ShapeStyle style)
    {
        switch (style.Fill)
        {
            case SolidColorFill solid:
                element.SetAttributeValue("fill", FormatColor(solid.Color));
                break;
            case TextureFill texture:
                element.SetAttributeValue("fill", FormatColor(texture.PrimaryColor));
                element.SetAttributeValue(VectorerNs + "texture-kind", texture.PatternKind.ToString());
                element.SetAttributeValue(VectorerNs + "texture-primary", FormatColor(texture.PrimaryColor));
                element.SetAttributeValue(VectorerNs + "texture-secondary", FormatColor(texture.SecondaryColor));
                element.SetAttributeValue(VectorerNs + "texture-scale", FormatFloat(texture.Scale));
                element.SetAttributeValue(VectorerNs + "texture-angle", FormatFloat(texture.AngleDegrees));
                break;
            default:
                element.SetAttributeValue("fill", "none");
                break;
        }

        if (style.Stroke is { IsVisible: true } stroke)
        {
            element.SetAttributeValue("stroke", FormatColor(stroke.Color));
            element.SetAttributeValue("stroke-width", FormatFloat(stroke.Width));
        }
        else
        {
            element.SetAttributeValue("stroke", "none");
        }
    }

    private static SvgDocument ParseXDocument(XDocument xDoc)
    {
        var root = xDoc.Root ?? throw new XmlException("Invalid SVG document: missing root element.");
        var width = ParseFloat(root.Attribute("width")?.Value, 800f);
        var height = ParseFloat(root.Attribute("height")?.Value, 600f);

        var doc = new SvgDocument
        {
            Title = root.Attribute(VectorerNs + "title")?.Value ?? "Untitled",
            Width = width,
            Height = height,
            ViewBox = new SKRect(0, 0, width, height)
        };

        var bgAttr = root.Attribute(VectorerNs + "background")?.Value;
        if (!string.IsNullOrWhiteSpace(bgAttr) && SKColor.TryParse(bgAttr, out var bgColor))
        {
            doc.BackgroundColor = bgColor;
        }

        LayerNode? fallbackLayer = null;

        foreach (var childEl in root.Elements())
        {
            var localName = childEl.Name.LocalName;
            if (localName == "g" && childEl.Attribute(VectorerNs + "layer")?.Value == "true")
            {
                var layer = new LayerNode();
                ApplyCommonAttributes(childEl, layer);
                foreach (var grandChild in childEl.Elements())
                {
                    var parsedChild = ParseElement(grandChild);
                    if (parsedChild is not null)
                    {
                        layer.Children.Add(parsedChild);
                    }
                }

                doc.Layers.Add(layer);
            }
            else
            {
                var parsedNode = ParseElement(childEl);
                if (parsedNode is not null)
                {
                    fallbackLayer ??= new LayerNode { Name = "Layer 1" };
                    fallbackLayer.Children.Add(parsedNode);
                }
            }
        }

        if (fallbackLayer is not null)
        {
            doc.Layers.Insert(0, fallbackLayer);
        }

        if (doc.Layers.Count == 0)
        {
            doc.Layers.Add(new LayerNode { Name = "Layer 1" });
        }

        return doc;
    }

    private static SceneNode? ParseElement(XElement el)
    {
        var localName = el.Name.LocalName;
        SceneNode? node = localName switch
        {
            "g" => ParseGroup(el),
            "rect" => ParseRectangle(el),
            "ellipse" => ParseEllipse(el),
            "circle" => ParseCircle(el),
            "polygon" => ParsePolygon(el, isClosed: true),
            "polyline" => ParsePolygon(el, isClosed: false),
            "path" => ParsePathOrArc(el),
            _ => null
        };

        if (node is not null)
        {
            ApplyCommonAttributes(el, node);
            if (node is ShapeNode shapeNode)
            {
                ReadShapeStyleAttributes(el, shapeNode.Style);
            }
        }

        return node;
    }

    private static GroupNode ParseGroup(XElement el)
    {
        var group = new GroupNode();
        foreach (var child in el.Elements())
        {
            var parsed = ParseElement(child);
            if (parsed is not null)
            {
                group.Children.Add(parsed);
            }
        }

        return group;
    }

    private static RectangleShapeNode ParseRectangle(XElement el)
    {
        var x = ParseFloat(el.Attribute("x")?.Value, 0f);
        var y = ParseFloat(el.Attribute("y")?.Value, 0f);
        var w = ParseFloat(el.Attribute("width")?.Value, 100f);
        var h = ParseFloat(el.Attribute("height")?.Value, 100f);
        return new RectangleShapeNode
        {
            Rect = new SKRect(x, y, x + w, y + h),
            CornerRadiusX = ParseFloat(el.Attribute("rx")?.Value, 0f),
            CornerRadiusY = ParseFloat(el.Attribute("ry")?.Value, 0f)
        };
    }

    private static EllipseShapeNode ParseEllipse(XElement el)
    {
        return new EllipseShapeNode
        {
            Center = new SKPoint(
                ParseFloat(el.Attribute("cx")?.Value, 0f),
                ParseFloat(el.Attribute("cy")?.Value, 0f)),
            RadiusX = ParseFloat(el.Attribute("rx")?.Value, 50f),
            RadiusY = ParseFloat(el.Attribute("ry")?.Value, 50f)
        };
    }

    private static EllipseShapeNode ParseCircle(XElement el)
    {
        var r = ParseFloat(el.Attribute("r")?.Value, 50f);
        return new EllipseShapeNode
        {
            Center = new SKPoint(
                ParseFloat(el.Attribute("cx")?.Value, 0f),
                ParseFloat(el.Attribute("cy")?.Value, 0f)),
            RadiusX = r,
            RadiusY = r
        };
    }

    private static PolygonShapeNode ParsePolygon(XElement el, bool isClosed)
    {
        var poly = new PolygonShapeNode { IsClosed = isClosed };
        var rawPoints = el.Attribute("points")?.Value;
        if (!string.IsNullOrWhiteSpace(rawPoints))
        {
            var pairs = rawPoints.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var coords = pair.Split(',');
                if (coords.Length == 2)
                {
                    poly.Points.Add(new SKPoint(ParseFloat(coords[0], 0f), ParseFloat(coords[1], 0f)));
                }
            }
        }

        return poly;
    }

    private static ShapeNode ParsePathOrArc(XElement el)
    {
        if (el.Attribute(VectorerNs + "shape-type")?.Value == "arc")
        {
            var closeModeStr = el.Attribute(VectorerNs + "close-mode")?.Value;
            _ = Enum.TryParse<ArcCloseMode>(closeModeStr, ignoreCase: true, out var closeMode);
            return new ArcShapeNode
            {
                Center = new SKPoint(
                    ParseFloat(el.Attribute(VectorerNs + "cx")?.Value, 0f),
                    ParseFloat(el.Attribute(VectorerNs + "cy")?.Value, 0f)),
                RadiusX = ParseFloat(el.Attribute(VectorerNs + "rx")?.Value, 50f),
                RadiusY = ParseFloat(el.Attribute(VectorerNs + "ry")?.Value, 50f),
                StartAngleDegrees = ParseFloat(el.Attribute(VectorerNs + "start-angle")?.Value, 0f),
                SweepAngleDegrees = ParseFloat(el.Attribute(VectorerNs + "sweep-angle")?.Value, 180f),
                CloseMode = closeMode
            };
        }

        return new PathShapeNode(el.Attribute("d")?.Value ?? string.Empty);
    }

    private static void ApplyCommonAttributes(XElement el, SceneNode node)
    {
        var idAttr = el.Attribute("id")?.Value;
        if (!string.IsNullOrWhiteSpace(idAttr))
        {
            node.Id = idAttr;
        }

        var nameAttr = el.Attribute(VectorerNs + "name")?.Value;
        if (!string.IsNullOrWhiteSpace(nameAttr))
        {
            node.Name = nameAttr;
        }

        node.IsVisible = !string.Equals(el.Attribute("display")?.Value, "none", StringComparison.OrdinalIgnoreCase);
        node.IsLocked = string.Equals(el.Attribute(VectorerNs + "locked")?.Value, "true", StringComparison.OrdinalIgnoreCase);
        node.Opacity = ParseFloat(el.Attribute("opacity")?.Value, 1.0f);

        var blendStr = el.Attribute(VectorerNs + "blend-mode")?.Value;
        if (!string.IsNullOrWhiteSpace(blendStr) && Enum.TryParse<LayerBlendMode>(blendStr, ignoreCase: true, out var blendMode))
        {
            node.BlendMode = blendMode;
        }

        var blurX = ParseFloat(el.Attribute(VectorerNs + "blur-x")?.Value, 0f);
        var blurY = ParseFloat(el.Attribute(VectorerNs + "blur-y")?.Value, 0f);
        if (blurX > 0f || blurY > 0f)
        {
            node.Filter = new FilterSettings(blurX, blurY);
        }

        var shaderId = el.Attribute(VectorerNs + "shader-id")?.Value;
        if (!string.IsNullOrWhiteSpace(shaderId))
        {
            var inlineSksl = el.Attribute(VectorerNs + "shader-sksl")?.Value;
            node.ShaderEffect = new ShaderEffectDescriptor(shaderId, inlineSksl);
        }
    }

    private static void ReadShapeStyleAttributes(XElement el, ShapeStyle style)
    {
        var textureKindStr = el.Attribute(VectorerNs + "texture-kind")?.Value;
        if (!string.IsNullOrWhiteSpace(textureKindStr) && Enum.TryParse<TexturePatternKind>(textureKindStr, ignoreCase: true, out var patternKind))
        {
            var primary = ParseColor(el.Attribute(VectorerNs + "texture-primary")?.Value, SKColors.DodgerBlue);
            var secondary = ParseColor(el.Attribute(VectorerNs + "texture-secondary")?.Value, SKColors.White);
            var scale = ParseFloat(el.Attribute(VectorerNs + "texture-scale")?.Value, 16f);
            var angle = ParseFloat(el.Attribute(VectorerNs + "texture-angle")?.Value, 0f);
            style.Fill = new TextureFill(patternKind, primary, secondary, scale, angle);
        }
        else
        {
            var fillAttr = el.Attribute("fill")?.Value;
            if (string.Equals(fillAttr, "none", StringComparison.OrdinalIgnoreCase))
            {
                style.Fill = null;
            }
            else if (!string.IsNullOrWhiteSpace(fillAttr))
            {
                style.Fill = new SolidColorFill(ParseColor(fillAttr, new SKColor(66, 133, 244)));
            }
        }

        var strokeAttr = el.Attribute("stroke")?.Value;
        if (string.Equals(strokeAttr, "none", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(strokeAttr))
        {
            style.Stroke = null;
        }
        else
        {
            var strokeColor = ParseColor(strokeAttr, SKColors.Black);
            var strokeWidth = ParseFloat(el.Attribute("stroke-width")?.Value, 1f);
            style.Stroke = new StrokeStyle(strokeColor, strokeWidth);
        }
    }

    private static string FormatFloat(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static float ParseFloat(string? raw, float defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        var cleaned = raw.Replace("px", "", StringComparison.OrdinalIgnoreCase).Trim();
        return float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : defaultValue;
    }

    private static string FormatColor(SKColor color) =>
        color.Alpha == 255
            ? $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}"
            : $"#{color.Alpha:X2}{color.Red:X2}{color.Green:X2}{color.Blue:X2}";

    private static SKColor ParseColor(string? raw, SKColor fallback) =>
        !string.IsNullOrWhiteSpace(raw) && SKColor.TryParse(raw, out var color) ? color : fallback;

    private sealed class Utf8StringWriter : StringWriter
    {
        public override Encoding Encoding => Encoding.UTF8;
    }
}
