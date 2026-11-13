using SkiaSharp;

namespace Vectorer.Abstractions.Styling;

public static class GradientGeometry
{
    /// <summary>Maps gradient coordinates into the shape's local coordinate system.</summary>
    public static SKMatrix GetLocalMatrix(LinearGradientFill gradient, SKRect geometryBounds)
    {
        var transform = gradient.GradientTransform ?? SKMatrix.CreateIdentity();
        if (!gradient.UseObjectBoundingBox) return transform;
        var boundsMatrix = SKMatrix.Concat(
            SKMatrix.CreateTranslation(geometryBounds.Left, geometryBounds.Top),
            SKMatrix.CreateScale(geometryBounds.Width, geometryBounds.Height));
        return SKMatrix.Concat(boundsMatrix, transform);
    }

    /// <summary>Preserves a gradient's appearance when an operation changes the geometry bounds.</summary>
    public static ShapeFill ToUserSpace(ShapeFill paint, SKRect originalBounds) => paint is LinearGradientFill gradient
        ? gradient with { UseObjectBoundingBox = false, GradientTransform = GetLocalMatrix(gradient, originalBounds) }
        : paint;
}
