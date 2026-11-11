using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Xunit;

namespace Vectorer.Svg.Tests;

public sealed class ExternalSvgCompatibilityTests
{
    [Fact]
    public void Import_InheritsPresentationAndInlineStyleAndParsesWhitespacePoints()
    {
        var document = new SvgSerializer().DeserializeFromString("""
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="10 20 100 100" width="200" height="200">
              <g fill="red" stroke="blue" stroke-width="3"><polygon points="10 20 30 20 30 40"/><rect width="20" height="30" style="fill:#00ff00;stroke-width:5"/></g>
            </svg>
            """);
        var group = Assert.IsType<GroupNode>(document.Layers[0].Children[0]);
        var polygon = Assert.IsType<PolygonShapeNode>(group.Children[0]);
        Assert.Equal(3, polygon.Points.Count); Assert.Equal(new SolidColorFill(SKColors.Red), polygon.Style.Fill);
        Assert.Equal(SKColors.Blue, polygon.Style.Stroke!.Color); Assert.Equal(3, polygon.Style.Stroke.Width);
        var rectangle = Assert.IsType<RectangleShapeNode>(group.Children[1]);
        Assert.Equal(new SolidColorFill(SKColors.Lime), rectangle.Style.Fill); Assert.Equal(5, rectangle.Style.Stroke!.Width);
        Assert.Equal(new SKPoint(0, 0), document.GetViewportTransform().MapPoint(new SKPoint(10, 20)));
        var restored = new SvgSerializer().DeserializeFromString(new SvgSerializer().SerializeToString(document));
        Assert.Equal(document.ViewBox, restored.ViewBox);
    }
    [Fact]
    public void ViewBox_HandlesMeetSliceAndNonUniformScale()
    {
        var document = SvgDocument.CreateDefault(200, 100); document.ViewBox = new SKRect(10, 20, 110, 120);
        Assert.Equal(new SKPoint(50, 0), document.GetViewportTransform().MapPoint(new SKPoint(10, 20)));
        document.PreserveAspectRatio = "none"; Assert.Equal(new SKPoint(200, 100), document.GetViewportTransform().MapPoint(new SKPoint(110, 120)));
        document.PreserveAspectRatio = "xMinYMin slice"; Assert.Equal(new SKPoint(200, 200), document.GetViewportTransform().MapPoint(new SKPoint(110, 120)));
    }
    [Fact]
    public void Import_UsesSvgDefaultBlackCurrentColorAndInheritedFonts()
    {
        var doc = new SvgSerializer().DeserializeFromString("<svg xmlns='http://www.w3.org/2000/svg' color='#123456' font-family='Arial' font-size='22'><rect width='20' height='20'/><g fill='currentColor'><text x='10' y='30'>SVG 日本語</text></g></svg>");
        Assert.Equal(new SolidColorFill(SKColors.Black), Assert.IsType<RectangleShapeNode>(doc.Layers[0].Children[0]).Style.Fill);
        var text = Assert.IsType<TextShapeNode>(Assert.IsType<GroupNode>(doc.Layers[0].Children[1]).Children[0]);
        Assert.Equal("Arial", text.FontFamily); Assert.Equal(22, text.FontSize);
        Assert.Equal(new SolidColorFill(new SKColor(0x12, 0x34, 0x56)), text.Style.Fill);
    }
    [Fact]
    public void Import_RejectsNonSvgXml()
    { Assert.Throws<System.Xml.XmlException>(() => new SvgSerializer().DeserializeFromString("<not-svg/>")); }
}
