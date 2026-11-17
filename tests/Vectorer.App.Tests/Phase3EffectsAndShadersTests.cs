using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.App.ViewModels;
using Vectorer.SamplePlugin;
using Vectorer.Svg;
using Xunit;

namespace Vectorer.App.Tests;

public sealed class Phase3EffectsAndShadersTests
{
    private static MainWindowViewModel CreateWorkspace()
    {
        var vm = new MainWindowViewModel();
        vm.CurrentDocument.Layers.Clear();
        var layer = new LayerNode { Name = "Layer 1" };
        vm.CurrentDocument.Layers.Add(layer);
        vm.ActiveLayer = layer;
        vm.SelectNodes([]);
        return vm;
    }

    [Fact]
    public void CompositingAndBlur_LinkedAndIndependentSigmaEditWithUndoRedo()
    {
        var vm = CreateWorkspace();
        var shape = new RectangleShapeNode { Rect = new SKRect(20, 20, 120, 120) };
        vm.ActiveLayer!.Children.Add(shape);
        vm.SelectNodes([shape]);

        Assert.Equal(100m, vm.EffectOpacity);
        Assert.Equal(LayerBlendMode.Normal, vm.EffectBlendMode);
        Assert.False(vm.IsBlurEnabled);

        vm.EffectOpacity = 75m;
        vm.EffectBlendMode = LayerBlendMode.Multiply;
        Assert.Equal(0.75f, shape.Opacity, 3);
        Assert.Equal(LayerBlendMode.Multiply, shape.BlendMode);

        // Linked blur sigma (X = Y)
        vm.LinkBlurSigma = true;
        vm.BlurSigmaX = 8.5m;
        Assert.True(vm.IsBlurEnabled);
        Assert.Equal(8.5m, vm.BlurSigmaY);
        Assert.Equal(new FilterSettings(8.5f, 8.5f), shape.Filter);

        // Independent blur sigma (X != Y)
        vm.LinkBlurSigma = false;
        vm.BlurSigmaY = 14m;
        Assert.Equal(new FilterSettings(8.5f, 14f), shape.Filter);

        vm.UndoCommand.Execute(null);
        Assert.Equal(new FilterSettings(8.5f, 8.5f), shape.Filter);

        vm.UndoCommand.Execute(null);
        Assert.Null(shape.Filter);
    }

    [Fact]
    public void ShaderSelection_ExposesPluginUniformsAndPreservesInactiveInlineShader()
    {
        var vm = CreateWorkspace();
        var shape = new RectangleShapeNode { Rect = new SKRect(10, 10, 110, 110) };
        vm.ActiveLayer!.Children.Add(shape);
        vm.SelectNodes([shape]);

        Assert.Equal(MainWindowViewModel.NoShaderId, vm.SelectedShaderId);
        Assert.False(vm.HasActiveShader);

        // Select registered plugin shader
        vm.SelectedShaderId = SampleVectorerPlugin.SunsetWaveShaderId;
        Assert.True(vm.HasActiveShader);
        Assert.NotNull(shape.ShaderEffect);
        Assert.Equal(SampleVectorerPlugin.SunsetWaveShaderId, shape.ShaderEffect!.ShaderId);

        var freqUniform = Assert.Single(vm.ShaderUniforms);
        Assert.Equal("u_frequency", freqUniform.Name);
        Assert.Equal(10m, freqUniform.Value);

        // Edit uniform via dynamic slider item
        freqUniform.Value = 24.5m;
        Assert.Equal(24.5f, shape.ShaderEffect.Uniforms!["u_frequency"]);

        var inline = new ShaderEffectDescriptor(MainWindowViewModel.CustomShaderId,
            "half4 main(float2 p) { return half4(1, 0, 0, 1); }");
        shape.ShaderEffect = inline;
        vm.SelectNodes([]);
        vm.SelectNodes([shape]);

        Assert.True(vm.HasInactiveInlineShader);
        Assert.False(vm.HasActiveShader);
        Assert.Empty(vm.ShaderUniforms);
        Assert.DoesNotContain(vm.ShaderOptions, option => option.Id == MainWindowViewModel.CustomShaderId);
        Assert.Same(inline, shape.ShaderEffect);
    }

    [Fact]
    public void SvgRoundTrip_PreservesShaderUniformsAndInlineSksl()
    {
        var doc = new SvgDocument { Width = 400, Height = 300 };
        var layer = new LayerNode { Name = "Shader Layer" };
        var rect = new RectangleShapeNode
        {
            Id = "shader_rect",
            Rect = new SKRect(20, 30, 180, 150),
            ShaderEffect = new ShaderEffectDescriptor(
                MainWindowViewModel.CustomShaderId,
                "uniform float u_alpha; half4 main(float2 p) { return half4(1, 0, 0, u_alpha); }",
                new Dictionary<string, float>
                {
                    ["u_alpha"] = 0.75f,
                    ["u_frequency"] = 18.25f
                })
        };
        layer.Children.Add(rect);
        doc.Layers.Add(layer);

        var serializer = new SvgSerializer();
        var xml = serializer.SerializeToString(doc);
        Assert.Contains("vectorer:shader-uniforms=", xml);

        var restoredDoc = serializer.DeserializeFromString(xml);
        var restoredRect = Assert.IsType<RectangleShapeNode>(restoredDoc.FindNodeById("shader_rect"));
        Assert.NotNull(restoredRect.ShaderEffect);
        Assert.Equal(MainWindowViewModel.CustomShaderId, restoredRect.ShaderEffect!.ShaderId);
        Assert.Contains("u_alpha", restoredRect.ShaderEffect.InlineSksl);
        Assert.NotNull(restoredRect.ShaderEffect.Uniforms);
        Assert.Equal(0.75f, restoredRect.ShaderEffect.Uniforms!["u_alpha"]);
        Assert.Equal(18.25f, restoredRect.ShaderEffect.Uniforms["u_frequency"]);
    }
}
