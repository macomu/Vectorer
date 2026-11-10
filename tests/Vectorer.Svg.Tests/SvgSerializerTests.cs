using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Xunit;

namespace Vectorer.Svg.Tests;

public sealed class SvgSerializerTests
{
    [Fact]
    public void SerializeAndDeserialize_PreservesLayersBlendModesFiltersTexturesAndShapes()
    {
        var doc = new SvgDocument
        {
            Title = "Roundtrip Test",
            Width = 640,
            Height = 480
        };

        var layer = new LayerNode
        {
            Id = "layer_main",
            Name = "Overlay Layer",
            BlendMode = LayerBlendMode.Overlay,
            Filter = new FilterSettings(BlurSigmaX: 4.5f, BlurSigmaY: 4.5f)
        };

        var polygon = PolygonShapeNode.CreateRegularConvex(new SKPoint(200, 200), 60f, 5);
        polygon.Id = "poly_1";
        polygon.Style.Fill = new TextureFill(
            TexturePatternKind.Checkerboard,
            new SKColor(255, 100, 50),
            new SKColor(20, 40, 80),
            Scale: 24f);
        polygon.ShaderEffect = new ShaderEffectDescriptor("sample.sunset_wave");

        var arc = new ArcShapeNode
        {
            Id = "arc_1",
            Center = new SKPoint(350, 220),
            RadiusX = 75f,
            RadiusY = 75f,
            StartAngleDegrees = 15f,
            SweepAngleDegrees = 210f,
            CloseMode = ArcCloseMode.PieSlice
        };

        layer.Children.Add(polygon);
        layer.Children.Add(arc);
        doc.Layers.Add(layer);

        var serializer = new SvgSerializer();
        var svgXml = serializer.SerializeToString(doc);

        Assert.Contains("xmlns:vectorer=\"https://vectorer.dev/ns/1.0\"", svgXml);
        Assert.Contains("Overlay", svgXml);

        var restored = serializer.DeserializeFromString(svgXml);

        Assert.Equal("Roundtrip Test", restored.Title);
        Assert.Equal(640f, restored.Width);
        Assert.Equal(480f, restored.Height);
        Assert.Single(restored.Layers);

        var restoredLayer = restored.Layers[0];
        Assert.Equal("Overlay Layer", restoredLayer.Name);
        Assert.Equal(LayerBlendMode.Overlay, restoredLayer.BlendMode);
        Assert.NotNull(restoredLayer.Filter);
        Assert.Equal(4.5f, restoredLayer.Filter.BlurSigmaX);

        var restoredPoly = Assert.IsType<PolygonShapeNode>(restoredLayer.FindNodeById("poly_1"));
        Assert.Equal(5, restoredPoly.Points.Count);
        var restoredTexture = Assert.IsType<TextureFill>(restoredPoly.Style.Fill);
        Assert.Equal(TexturePatternKind.Checkerboard, restoredTexture.PatternKind);
        Assert.Equal(24f, restoredTexture.Scale);
        Assert.Equal("sample.sunset_wave", restoredPoly.ShaderEffect?.ShaderId);

        var restoredArc = Assert.IsType<ArcShapeNode>(restoredLayer.FindNodeById("arc_1"));
        Assert.Equal(210f, restoredArc.SweepAngleDegrees);
        Assert.Equal(ArcCloseMode.PieSlice, restoredArc.CloseMode);
    }
}
