using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
            new XAttribute("preserveAspectRatio", document.PreserveAspectRatio),
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
            TextShapeNode text => SerializeText(text),
            _ => new XElement(SvgNs + "g")
        };

        if (!node.Transform.IsIdentity)
        {
            var m = node.Transform;
            element.SetAttributeValue("transform", $"matrix({FormatFloat(m.ScaleX)} {FormatFloat(m.SkewY)} {FormatFloat(m.SkewX)} {FormatFloat(m.ScaleY)} {FormatFloat(m.TransX)} {FormatFloat(m.TransY)})");
        }
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
        var element = new XElement(SvgNs + tagName, new XAttribute("points", pointsStr));
        if (polygon.PolygonKind != PolygonKind.Freeform)
        {
            element.SetAttributeValue(VectorerNs + "poly-kind", polygon.PolygonKind == PolygonKind.ConcaveStar ? "star" : "regular");
            element.SetAttributeValue(VectorerNs + "cx", FormatFloat(polygon.Center.X));
            element.SetAttributeValue(VectorerNs + "cy", FormatFloat(polygon.Center.Y));
            element.SetAttributeValue(VectorerNs + "points-count", polygon.PointCount);
            element.SetAttributeValue(VectorerNs + "outer-r", FormatFloat(polygon.OuterRadius));
            element.SetAttributeValue(VectorerNs + "inner-r", FormatFloat(polygon.InnerRadius));
            element.SetAttributeValue(VectorerNs + "start-angle", FormatFloat(polygon.StartAngleDegrees));
        }
        return element;
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

    private static XElement SerializeText(TextShapeNode text)
    {
        var element = new XElement(SvgNs + "text", new XAttribute("x", FormatFloat(text.Position.X)), new XAttribute("y", FormatFloat(text.Position.Y)),
            new XAttribute("font-family", text.FontFamily), new XAttribute("font-size", FormatFloat(text.FontSize)),
            new XAttribute("font-weight", text.IsBold ? "bold" : "normal"), new XAttribute("font-style", text.IsItalic ? "italic" : "normal"),
            new XAttribute("text-anchor", text.TextAlign == SKTextAlign.Center ? "middle" : text.TextAlign == SKTextAlign.Right ? "end" : "start"),
            new XAttribute("letter-spacing", FormatFloat(text.LetterSpacing)), new XAttribute(XNamespace.Xml + "space", "preserve"));
        var lines = text.Text.Replace("\r", "").Split('\n');
        for (var i = 0; i < lines.Length; i++)
            element.Add(new XElement(SvgNs + "tspan", new XAttribute("x", FormatFloat(text.Position.X)),
                new XAttribute("y", FormatFloat(text.Position.Y + i * text.FontSize * 1.2f)), lines[i]));
        return element;
    }

    private static TextShapeNode ParseText(XElement el) => new()
    {
        Text = el.Elements(SvgNs + "tspan").Any() ? string.Join("\n", el.Elements(SvgNs + "tspan").Select(e => e.Value)) : el.Value,
        Position = new SKPoint(ParseFloat(el.Attribute("x")?.Value, 0), ParseFloat(el.Attribute("y")?.Value, 0)),
        FontFamily = el.Attribute("font-family")?.Value ?? "sans-serif", FontSize = ParseFloat(el.Attribute("font-size")?.Value, 16),
        IsBold = el.Attribute("font-weight")?.Value is "bold" or "700", IsItalic = el.Attribute("font-style")?.Value == "italic",
        TextAlign = el.Attribute("text-anchor")?.Value switch { "middle" => SKTextAlign.Center, "end" => SKTextAlign.Right, _ => SKTextAlign.Left },
        LetterSpacing = ParseFloat(el.Attribute("letter-spacing")?.Value, 0)
    };

    private static XElement SerializePath(PathShapeNode path)
    {
        return new XElement(
            SvgNs + "path",
            new XAttribute("d", path.SvgPathData),
            new XAttribute("fill-rule", path.FillType == SKPathFillType.EvenOdd ? "evenodd" : "nonzero"),
            new XAttribute(VectorerNs + "node-kinds", path.NodeKinds));
    }

    private static void WriteShapeStyleAttributes(XElement element, ShapeStyle style)
    {
        switch (style.Fill)
        {
            case SolidColorFill solid:
                element.SetAttributeValue("fill", Rgb(solid.Color));
                if (solid.Color.Alpha < 255) element.SetAttributeValue("fill-opacity", FormatFloat(solid.Color.Alpha / 255f));
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
            element.SetAttributeValue("stroke", Rgb(stroke.Color));
            if (stroke.Color.Alpha < 255) element.SetAttributeValue("stroke-opacity", FormatFloat(stroke.Color.Alpha / 255f));
            element.SetAttributeValue("stroke-width", FormatFloat(stroke.Width));
            element.SetAttributeValue("stroke-linecap", stroke.Cap.ToString().ToLowerInvariant());
            element.SetAttributeValue("stroke-linejoin", stroke.Join.ToString().ToLowerInvariant());
            element.SetAttributeValue("stroke-miterlimit", FormatFloat(stroke.MiterLimit));
            if (stroke.DashArray is { Length: > 0 })
                element.SetAttributeValue("stroke-dasharray", string.Join(" ", stroke.DashArray.Select(FormatFloat)));
            element.SetAttributeValue("stroke-dashoffset", FormatFloat(stroke.DashPhase));
        }
        else
        {
            element.SetAttributeValue("stroke", "none");
        }
    }

    private static SvgDocument ParseXDocument(XDocument xDoc)
    {
        var root = xDoc.Root ?? throw new XmlException("Invalid SVG document: missing root element.");
        if (root.Name.LocalName != "svg" || root.Name.Namespace != SvgNs && root.Name.Namespace != XNamespace.None)
            throw new XmlException("The selected file is not an SVG document.");
        ExpandInheritedStyles(root, new Dictionary<string, string> { ["fill"] = "#000000", ["stroke"] = "none" });
        var width = ParseFloat(root.Attribute("width")?.Value, 800f);
        var height = ParseFloat(root.Attribute("height")?.Value, 600f);

        var doc = new SvgDocument
        {
            Title = root.Attribute(VectorerNs + "title")?.Value ?? "Untitled",
            Width = width,
            Height = height,
            ViewBox = new SKRect(0, 0, width, height)
        };

        var viewBox = Numbers(root.Attribute("viewBox")?.Value);
        if (viewBox.Length == 4 && viewBox[2] > 0 && viewBox[3] > 0)
            doc.ViewBox = SKRect.Create(viewBox[0], viewBox[1], viewBox[2], viewBox[3]);
        doc.PreserveAspectRatio = root.Attribute("preserveAspectRatio")?.Value ?? "xMidYMid meet";
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
            "text" => ParseText(el),
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
        var coordinates = Numbers(el.Attribute("points")?.Value);
        for (var i = 0; i + 1 < coordinates.Length; i += 2) poly.Points.Add(new SKPoint(coordinates[i], coordinates[i + 1]));

        var kind = el.Attribute(VectorerNs + "poly-kind")?.Value;
        if (kind is "star" or "regular")
        {
            poly.PolygonKind = kind == "star" ? PolygonKind.ConcaveStar : PolygonKind.RegularConvex;
            poly.Center = new SKPoint(ParseFloat(el.Attribute(VectorerNs + "cx")?.Value, 0), ParseFloat(el.Attribute(VectorerNs + "cy")?.Value, 0));
            poly.PointCount = Math.Clamp((int)ParseFloat(el.Attribute(VectorerNs + "points-count")?.Value, 5), 3, 64);
            poly.OuterRadius = Math.Max(0, ParseFloat(el.Attribute(VectorerNs + "outer-r")?.Value, 100));
            poly.InnerRadius = Math.Max(0, ParseFloat(el.Attribute(VectorerNs + "inner-r")?.Value, 45));
            poly.StartAngleDegrees = ParseFloat(el.Attribute(VectorerNs + "start-angle")?.Value, -90);
            poly.RegeneratePoints();
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

        return new PathShapeNode(el.Attribute("d")?.Value ?? string.Empty)
        { NodeKinds = el.Attribute(VectorerNs + "node-kinds")?.Value ?? "", FillType = el.Attribute("fill-rule")?.Value == "evenodd" ? SKPathFillType.EvenOdd : SKPathFillType.Winding };
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
        node.Transform = ParseTransform(el.Attribute("transform")?.Value);

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
                var color = ParseSvgColor(fillAttr, new SKColor(66, 133, 244));
                style.Fill = new SolidColorFill(color.WithAlpha((byte)Math.Clamp(color.Alpha * ParseFloat(el.Attribute("fill-opacity")?.Value, 1), 0, 255)));
            }
        }

        var strokeAttr = el.Attribute("stroke")?.Value;
        if (string.Equals(strokeAttr, "none", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(strokeAttr))
        {
            style.Stroke = null;
        }
        else
        {
            var strokeColor = ParseSvgColor(strokeAttr, SKColors.Black);
            strokeColor = strokeColor.WithAlpha((byte)Math.Clamp(strokeColor.Alpha * ParseFloat(el.Attribute("stroke-opacity")?.Value, 1), 0, 255));
            var strokeWidth = ParseFloat(el.Attribute("stroke-width")?.Value, 1f);
            _ = Enum.TryParse<SKStrokeCap>(el.Attribute("stroke-linecap")?.Value, true, out var cap);
            _ = Enum.TryParse<SKStrokeJoin>(el.Attribute("stroke-linejoin")?.Value, true, out var join);
            var dash = Numbers(el.Attribute("stroke-dasharray")?.Value).Where(n => n >= 0).ToArray();
            if (dash.Length % 2 == 1) dash = dash.Concat(dash).ToArray();
            style.Stroke = new StrokeStyle(strokeColor, strokeWidth, cap, join,
                ParseFloat(el.Attribute("stroke-miterlimit")?.Value, 4), dash.Length > 0 && dash.Any(n => n > 0) ? dash : null,
                ParseFloat(el.Attribute("stroke-dashoffset")?.Value, 0));
        }
    }

    private static readonly HashSet<string> InheritedProperties = ["fill", "stroke", "fill-rule", "fill-opacity", "stroke-opacity", "stroke-width", "stroke-linecap", "stroke-linejoin", "stroke-miterlimit", "stroke-dasharray", "stroke-dashoffset", "color", "font-family", "font-size", "font-weight", "font-style", "text-anchor", "letter-spacing"];
    private static void ExpandInheritedStyles(XElement element, Dictionary<string, string> inherited)
    {
        foreach (var declaration in (element.Attribute("style")?.Value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = declaration.IndexOf(':'); if (colon < 0) continue;
            var name = declaration[..colon].Trim();
            if (InheritedProperties.Contains(name) || name is "opacity" or "display") element.SetAttributeValue(name, declaration[(colon + 1)..].Trim());
        }
        var current = new Dictionary<string, string>(inherited);
        foreach (var property in InheritedProperties)
        {
            var value = element.Attribute(property)?.Value;
            if (value is null or "inherit") { if (inherited.TryGetValue(property, out var parent)) element.SetAttributeValue(property, parent); }
            else current[property] = value;
        }
        foreach (var property in new[] { "fill", "stroke" })
            if (element.Attribute(property)?.Value == "currentColor") element.SetAttributeValue(property, current.GetValueOrDefault("color", "#000000"));
        foreach (var child in element.Elements()) ExpandInheritedStyles(child, current);
    }

    private static float[] Numbers(string? raw) => Regex.Matches(raw ?? "", @"[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?")
        .Select(m => float.Parse(m.Value, CultureInfo.InvariantCulture)).ToArray();

    private static SKMatrix ParseTransform(string? raw)
    {
        var result = SKMatrix.CreateIdentity();
        foreach (Match match in Regex.Matches(raw ?? "", @"([a-zA-Z]+)\s*\(([^)]*)\)"))
        {
            var v = Numbers(match.Groups[2].Value);
            var next = match.Groups[1].Value switch
            {
                "matrix" when v.Length == 6 => new SKMatrix(v[0], v[2], v[4], v[1], v[3], v[5], 0, 0, 1),
                "translate" when v.Length >= 1 => SKMatrix.CreateTranslation(v[0], v.Length > 1 ? v[1] : 0),
                "scale" when v.Length >= 1 => SKMatrix.CreateScale(v[0], v.Length > 1 ? v[1] : v[0]),
                "rotate" when v.Length == 3 => SKMatrix.CreateRotationDegrees(v[0], v[1], v[2]),
                "rotate" when v.Length == 1 => SKMatrix.CreateRotationDegrees(v[0]),
                "skewX" when v.Length == 1 => SKMatrix.CreateSkew(MathF.Tan(v[0] * MathF.PI / 180), 0),
                "skewY" when v.Length == 1 => SKMatrix.CreateSkew(0, MathF.Tan(v[0] * MathF.PI / 180)),
                _ => SKMatrix.CreateIdentity()
            };
            result = SKMatrix.Concat(result, next);
        }
        return result;
    }

    private static string FormatFloat(float value) => value.ToString("G9", CultureInfo.InvariantCulture);

    private static float ParseFloat(string? raw, float defaultValue)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return defaultValue;
        }

        var cleaned = raw.Replace("px", "", StringComparison.OrdinalIgnoreCase).Trim();
        return float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? (float.IsFinite(parsed) ? parsed : defaultValue)
            : defaultValue;
    }

    private static string Rgb(SKColor color) => $"#{color.Red:X2}{color.Green:X2}{color.Blue:X2}";
    private static SKColor ParseSvgColor(string? raw, SKColor fallback)
    {
        raw = raw?.Trim();
        if (raw is not null && NamedColors.TryGetValue(raw, out var named)) return named;
        if (raw is not null && Regex.Match(raw, @"^rgba?\((.*)\)$", RegexOptions.IgnoreCase) is { Success: true } rgb)
        {
            var components = Regex.Split(rgb.Groups[1].Value.Trim(), @"[,\s/]+");
            if (components.Length is 3 or 4)
            {
                static byte Component(string text, bool alpha)
                {
                    var percent = text.EndsWith('%');
                    var value = float.TryParse(text.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && float.IsFinite(parsed) ? parsed : 0;
                    value *= percent ? 2.55f : alpha ? 255 : 1;
                    return (byte)Math.Clamp(MathF.Round(value), 0, 255);
                }
                return new SKColor(Component(components[0], false), Component(components[1], false), Component(components[2], false), components.Length == 4 ? Component(components[3], true) : (byte)255);
            }
        }
        if (raw is { Length: 9 } && raw[0] == '#' && uint.TryParse(raw.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgba))
            return new SKColor((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), (byte)rgba);
        return ParseColor(raw, fallback);
    }
    private static readonly Dictionary<string, SKColor> NamedColors = typeof(SKColors).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
        .Where(f => f.FieldType == typeof(SKColor)).ToDictionary(f => f.Name, f => (SKColor)f.GetValue(null)!, StringComparer.OrdinalIgnoreCase);

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
