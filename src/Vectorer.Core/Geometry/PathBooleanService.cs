using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Geometry;

namespace Vectorer.Core.Geometry;

/// <summary>
/// Performs boolean geometry operations (Union, Difference, Intersection, Xor) on shapes using SkiaSharp's <see cref="SKPath.Op"/>.
/// </summary>
public sealed class PathBooleanService : IPathBooleanService
{
    public PathShapeNode? Combine(ShapeNode primary, ShapeNode secondary, BooleanOperationType operation)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(secondary);

        using var pathA = primary.BuildTransformedPath();
        using var pathB = secondary.BuildTransformedPath();
        using var resultPath = pathA.Op(pathB, ToSkiaPathOp(operation));

        if (resultPath is null || resultPath.IsEmpty)
        {
            return null;
        }

        return new PathShapeNode(resultPath.ToSvgPathData())
        {
            Name = $"{primary.Name} {operation} {secondary.Name}",
            Style = primary.Style.Clone(),
            Opacity = primary.Opacity,
            BlendMode = primary.BlendMode,
            Filter = primary.Filter,
            ShaderEffect = primary.ShaderEffect
        };
    }

    public PathShapeNode? CombineMany(IReadOnlyList<ShapeNode> shapes, BooleanOperationType operation)
    {
        ArgumentNullException.ThrowIfNull(shapes);
        if (shapes.Count == 0)
        {
            return null;
        }

        if (shapes.Count == 1)
        {
            using var singlePath = shapes[0].BuildTransformedPath();
            return new PathShapeNode(singlePath.ToSvgPathData())
            {
                Name = shapes[0].Name,
                Style = shapes[0].Style.Clone()
            };
        }

        var current = shapes[0].BuildTransformedPath();
        try
        {
            var skOp = ToSkiaPathOp(operation);
            for (var i = 1; i < shapes.Count; i++)
            {
                using var next = shapes[i].BuildTransformedPath();
                var combined = current.Op(next, skOp);
                current.Dispose();
                if (combined is null)
                {
                    return null;
                }

                current = combined;
            }

            if (current.IsEmpty)
            {
                return null;
            }

            return new PathShapeNode(current.ToSvgPathData())
            {
                Name = $"{shapes[0].Name} ({operation})",
                Style = shapes[0].Style.Clone(),
                Opacity = shapes[0].Opacity,
                BlendMode = shapes[0].BlendMode,
                Filter = shapes[0].Filter,
                ShaderEffect = shapes[0].ShaderEffect
            };
        }
        finally
        {
            current.Dispose();
        }
    }

    private static SKPathOp ToSkiaPathOp(BooleanOperationType operation) => operation switch
    {
        BooleanOperationType.Union => SKPathOp.Union,
        BooleanOperationType.Difference => SKPathOp.Difference,
        BooleanOperationType.Intersection => SKPathOp.Intersect,
        BooleanOperationType.Xor => SKPathOp.Xor,
        BooleanOperationType.ReverseDifference => SKPathOp.ReverseDifference,
        _ => SKPathOp.Union
    };
}
