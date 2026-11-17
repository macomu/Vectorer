using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Xunit;

namespace Vectorer.Svg.Tests;

public sealed class ExternalSvgCompatibilityTests
{
    [Theory]
    [InlineData("<rect width='10' height='10' clip-path='url(#clip)'/>")]
    [InlineData("<rect width='10' height='10' style='mask: url(#m)'/>")]
    [InlineData("<image href='figure.png'/>")]
    [InlineData("<svg><rect width='10' height='10'/></svg>")]
    public void Load_MarksUnsupportedContentAsImport(string contents)
    {
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
            $"<svg xmlns='http://www.w3.org/2000/svg'>{contents}</svg>"));
        new SvgSerializer().LoadFromStream(input, out var requiresSaveAs);
        Assert.True(requiresSaveAs);
    }

    [Fact]
    public void LoadAndSave_PreservesRootOpacityAndSupportedStandardEffects()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" style="opacity: 50%">
              <defs><filter id="blur"><feGaussianBlur stdDeviation="2 4"/></filter></defs>
              <rect width="10" height="10" filter="url(#blur)" style="mix-blend-mode: multiply"/>
            </svg>
            """;
        var serializer = new SvgSerializer();
        using var input = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(svg));
        var document = serializer.LoadFromStream(input, out var requiresSaveAs);
        Assert.False(requiresSaveAs);
        Assert.Equal(0.5f, document.Opacity);
        var rectangle = Assert.Single(document.Layers[0].Children);
        Assert.Equal(Vectorer.Abstractions.Styling.LayerBlendMode.Multiply, rectangle.BlendMode);
        Assert.Equal(new Vectorer.Abstractions.Styling.FilterSettings(2f, 4f), rectangle.Filter);

        var output = serializer.SerializeToString(document);
        Assert.Contains("opacity=\"0.5\"", output);
        Assert.Contains("mix-blend-mode: multiply", output);
        Assert.Contains("feGaussianBlur stdDeviation=\"2 4\"", output);
        using var roundTrip = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(output));
        serializer.LoadFromStream(roundTrip, out requiresSaveAs);
        Assert.False(requiresSaveAs);
    }
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
