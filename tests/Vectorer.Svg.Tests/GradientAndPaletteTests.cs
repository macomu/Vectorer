using System.Xml.Linq;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Xunit;

namespace Vectorer.Svg.Tests;

public sealed class GradientAndPaletteTests
{
    private readonly SvgSerializer _serializer = new();

    [Fact]
    public void RoundTrip_PreservesIndependentFillAndStrokeGradientsAndTheirOpacity()
    {
        var document = SvgDocument.CreateDefault();
        var fill = new LinearGradientFill(
            [new(0, SKColors.Red), new(0.35f, new SKColor(1, 2, 3, 128)), new(1, SKColors.Transparent)],
            new SKPoint(0.1f, 0.2f), new SKPoint(0.9f, 0.8f),
            GradientTransform: SKMatrix.CreateRotationDegrees(30), SpreadMethod: GradientSpreadMethod.Reflect, Opacity: 0.6f);
        var stroke = fill with { UseObjectBoundingBox = false, Start = new SKPoint(10, 20), End = new SKPoint(80, 90), Opacity = 0.25f };
        document.Layers[0].Children.Add(new RectangleShapeNode
        {
            Style = new ShapeStyle { Fill = fill, Stroke = new StrokeStyle(SKColors.Transparent, 7, Paint: stroke) }
        });

        var svg = _serializer.SerializeToString(document);
        var xml = XDocument.Parse(svg);
        Assert.Equal(2, xml.Descendants(SvgSerializer.SvgNs + "linearGradient").Count());
        var rectangle = xml.Descendants(SvgSerializer.SvgNs + "rect").Single();
        Assert.StartsWith("url(#", rectangle.Attribute("fill")!.Value);
        Assert.StartsWith("url(#", rectangle.Attribute("stroke")!.Value);
        var restored = Assert.IsType<RectangleShapeNode>(_serializer.DeserializeFromString(svg).Layers[0].Children[0]);
        AssertGradient(fill, Assert.IsType<LinearGradientFill>(restored.Style.Fill));
        AssertGradient(stroke, Assert.IsType<LinearGradientFill>(restored.Style.Stroke!.Paint));
        Assert.Equal(7, restored.Style.Stroke.Width);
    }

    [Fact]
    public void RoundTrip_PreservesInvisibleGradientStrokeForFurtherEditing()
    {
        var document = SvgDocument.CreateDefault();
        var gradient = new LinearGradientFill([new(0, SKColors.Red), new(1, SKColors.Blue)], new(0, 0), new(1, 0), Opacity: 0);
        document.Layers[0].Children.Add(new RectangleShapeNode { Style = new ShapeStyle { Stroke = new StrokeStyle(SKColors.Black, 0, Paint: gradient) } });
        var restored = Assert.IsType<RectangleShapeNode>(_serializer.DeserializeFromString(_serializer.SerializeToString(document)).Layers[0].Children[0]);
        Assert.Equal(0, restored.Style.Stroke!.Width);
        Assert.Equal(0, Assert.IsType<LinearGradientFill>(restored.Style.Stroke.Paint).Opacity);
    }

    [Fact]
    public void Import_UsesSvgDefaultsAndStopStylesWithClampedNondecreasingOffsets()
    {
        var document = _serializer.DeserializeFromString("""
            <svg xmlns="http://www.w3.org/2000/svg">
              <defs><linearGradient id="g" color="red">
                <stop offset="-10%" stop-color="currentColor"/>
                <stop offset="60%" style="stop-color:rgb(0, 128, 255);stop-opacity:50%"/>
                <stop offset="40%" stop-color="blue"/>
                <stop offset="120%" stop-color="white"/>
              </linearGradient></defs>
              <g fill="url(#g)" fill-opacity="80%"><rect width="100" height="30"/></g>
            </svg>
            """);
        var rectangle = Assert.IsType<RectangleShapeNode>(Assert.IsType<GroupNode>(document.Layers[0].Children[0]).Children[0]);
        var gradient = Assert.IsType<LinearGradientFill>(rectangle.Style.Fill);
        Assert.True(gradient.UseObjectBoundingBox);
        Assert.Equal(new SKPoint(0, 0), gradient.Start);
        Assert.Equal(new SKPoint(1, 0), gradient.End);
        Assert.Equal(0.8f, gradient.Opacity);
        Assert.Equal(new[] { 0f, 0.6f, 0.6f, 1f }, gradient.Stops.Select(s => s.Offset));
        Assert.Equal(SKColors.Red, gradient.Stops[0].Color);
        Assert.Equal(new SKColor(0, 128, 255, 128), gradient.Stops[1].Color);
    }

    [Fact]
    public void Import_ResolvesHrefTemplatesAndUserSpacePercentages()
    {
        var document = _serializer.DeserializeFromString("""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 200 100">
              <defs>
                <linearGradient id="base" x1="25%" y1="10%" x2="75%" y2="90%" spreadMethod="repeat">
                  <stop stop-color="red"/><stop offset="1" stop-color="blue"/>
                </linearGradient>
                <linearGradient id="middle" xlink:href="#base" gradientUnits="userSpaceOnUse"/>
                <linearGradient id="g" href="#middle" gradientTransform="translate(5 6)"/>
              </defs>
              <rect fill="url('#g')" stroke="url(#g)" stroke-opacity=".5"/>
            </svg>
            """);
        var rectangle = Assert.IsType<RectangleShapeNode>(document.Layers[0].Children[0]);
        var gradient = Assert.IsType<LinearGradientFill>(rectangle.Style.Fill);
        Assert.False(gradient.UseObjectBoundingBox);
        Assert.Equal(new SKPoint(50, 10), gradient.Start);
        Assert.Equal(new SKPoint(150, 90), gradient.End);
        Assert.Equal(GradientSpreadMethod.Repeat, gradient.SpreadMethod);
        Assert.Equal(new SKPoint(5, 6), gradient.GradientTransform!.Value.MapPoint(new SKPoint(0, 0)));
        Assert.Equal(2, gradient.Stops.Count);
        Assert.Equal(0.5f, Assert.IsType<LinearGradientFill>(rectangle.Style.Stroke!.Paint).Opacity);
    }

    [Fact]
    public void Import_MissingOrCyclicPaintReferencesUseFallbackWithoutRecursing()
    {
        var document = _serializer.DeserializeFromString("""
            <svg xmlns="http://www.w3.org/2000/svg">
              <defs><linearGradient id="a" href="#b"/><linearGradient id="b" href="#a"/></defs>
              <rect fill="url(#missing) red" stroke="url(#a) none"/>
              <rect fill="url(#a)" stroke="url(https://example.invalid/gradient.svg#g)"/>
            </svg>
            """);
        var first = Assert.IsType<RectangleShapeNode>(document.Layers[0].Children[0]);
        Assert.Equal(new SolidColorFill(SKColors.Red), first.Style.Fill);
        Assert.Null(first.Style.Stroke);
        var second = Assert.IsType<RectangleShapeNode>(document.Layers[0].Children[1]);
        Assert.Null(second.Style.Fill);
        Assert.Null(second.Style.Stroke);
    }

    [Fact]
    public void Import_UserSpacePercentagesUseViewBoxSizeWithoutAddingItsOrigin()
    {
        var document = _serializer.DeserializeFromString("""
            <svg xmlns="http://www.w3.org/2000/svg" width="400" height="200" viewBox="50 30 200 100">
              <defs><linearGradient id="g" gradientUnits="userSpaceOnUse" x1="25%" y1="10%" x2="75%" y2="90%"
                gradientTransform="translate(5 6) scale(2 3)"><stop/><stop offset="1" stop-color="white"/></linearGradient></defs>
              <rect fill="url(#g)" transform="translate(10 20)"/>
            </svg>
            """);
        var rectangle = Assert.IsType<RectangleShapeNode>(document.Layers[0].Children[0]);
        var gradient = Assert.IsType<LinearGradientFill>(rectangle.Style.Fill);
        Assert.Equal(new SKPoint(50, 10), gradient.Start);
        Assert.Equal(new SKPoint(150, 90), gradient.End);
        // SVG transform lists post-multiply: scale the coordinates, then translate them.
        var mapped = gradient.GradientTransform!.Value.MapPoint(gradient.Start);
        Assert.Equal(new SKPoint(105, 36), mapped);
        Assert.Equal(new SKPoint(115, 56), rectangle.Transform.MapPoint(mapped));
        var restored = Assert.IsType<RectangleShapeNode>(_serializer.DeserializeFromString(_serializer.SerializeToString(document)).Layers[0].Children[0]);
        AssertGradient(gradient, Assert.IsType<LinearGradientFill>(restored.Style.Fill));
    }

    [Fact]
    public void Import_OverallPaintOpacityIsInheritedButStopAlphaIsIndependent()
    {
        var document = _serializer.DeserializeFromString("""
            <svg xmlns="http://www.w3.org/2000/svg" fill-opacity=".25">
              <defs><linearGradient id="g" fill-opacity=".1" stop-color="red" stop-opacity=".1">
                <stop/><stop offset=".5" stop-color="rgba(255,0,0,.5)" stop-opacity=".5"/>
                <stop offset=".5" stop-color="blue"/><stop offset="1" stop-color="white"/>
              </linearGradient></defs>
              <metadata><some-private-data><rect fill="red"/></some-private-data></metadata>
              <rect fill="url(#g)"/>
            </svg>
            """);
        Assert.Single(document.Layers[0].Children);
        var rectangle = Assert.IsType<RectangleShapeNode>(document.Layers[0].Children[0]);
        var gradient = Assert.IsType<LinearGradientFill>(rectangle.Style.Fill);
        Assert.Equal(.25f, gradient.Opacity);
        // stop-color and stop-opacity do not inherit; the absent stop values use black/1.
        Assert.Equal(SKColors.Black, gradient.Stops[0].Color);
        Assert.Equal(64, gradient.Stops[1].Color.Alpha);
        Assert.Equal(.5f, gradient.Stops[1].Offset);
        Assert.Equal(.5f, gradient.Stops[2].Offset);
        Assert.Equal(SKColors.Blue, gradient.Stops[2].Color);
        var restored = Assert.IsType<RectangleShapeNode>(_serializer.DeserializeFromString(_serializer.SerializeToString(document)).Layers[0].Children[0]);
        AssertGradient(gradient, Assert.IsType<LinearGradientFill>(restored.Style.Fill));
    }

    [Fact]
    public void Serialize_GradientIdsDoNotCollideWithSceneNodeIds()
    {
        var document = SvgDocument.CreateDefault();
        document.Layers[0].Children.Add(new RectangleShapeNode
        {
            Id = "vectorer_gradient_1",
            Style = new ShapeStyle { Fill = new LinearGradientFill([new(0, SKColors.Red), new(1, SKColors.Blue)], new(0, 0), new(1, 0)) }
        });
        var xml = XDocument.Parse(_serializer.SerializeToString(document));
        var ids = xml.Descendants().Attributes("id").Select(a => a.Value).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Fact]
    public void Palette_RoundTripPreservesOrderNamesAndAlphaWithoutLinkingPaint()
    {
        var document = SvgDocument.CreateDefault();
        document.ColorPresets.Clear();
        document.ColorPresets.Add(new ColorPreset(new SKColor(1, 2, 3, 64), "日本語 & <色>"));
        document.ColorPresets.Add(new ColorPreset(SKColors.Red));
        document.Layers[0].Children.Add(new RectangleShapeNode { Style = new ShapeStyle { Fill = new SolidColorFill(SKColors.Blue) } });
        var svg = _serializer.SerializeToString(document);
        Assert.Contains("vectorer:palette", svg);
        var restored = _serializer.DeserializeFromString(svg);
        Assert.Equal(document.ColorPresets, restored.ColorPresets);
        Assert.Equal(new SolidColorFill(SKColors.Blue), Assert.IsType<RectangleShapeNode>(restored.Layers[0].Children[0]).Style.Fill);
    }

    [Fact]
    public void Palette_AbsentUsesDefaultsAndExplicitEmptyStaysEmpty()
    {
        var document = _serializer.DeserializeFromString("<svg xmlns='http://www.w3.org/2000/svg'/>");
        Assert.NotEmpty(document.ColorPresets);
        document.ColorPresets.Clear();
        Assert.Empty(_serializer.DeserializeFromString(_serializer.SerializeToString(document)).ColorPresets);
        var malformed = _serializer.DeserializeFromString("""
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:vectorer="https://vectorer.dev/ns/1.0">
              <metadata><vectorer:palette><vectorer:color value="invalid"/><vectorer:color value="#123456"/></vectorer:palette></metadata>
            </svg>
            """);
        Assert.Single(malformed.ColorPresets);
        Assert.Equal(new SKColor(0x12, 0x34, 0x56), malformed.ColorPresets[0].Color);
    }

    private static void AssertGradient(LinearGradientFill expected, LinearGradientFill actual)
    {
        Assert.Equal(expected.Stops, actual.Stops);
        Assert.Equal(expected.Start, actual.Start);
        Assert.Equal(expected.End, actual.End);
        Assert.Equal(expected.UseObjectBoundingBox, actual.UseObjectBoundingBox);
        Assert.Equal(expected.GradientTransform, actual.GradientTransform);
        Assert.Equal(expected.SpreadMethod, actual.SpreadMethod);
        Assert.Equal(expected.Opacity, actual.Opacity);
    }
}
