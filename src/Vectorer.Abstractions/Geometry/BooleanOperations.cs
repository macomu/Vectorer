using Vectorer.Abstractions.Documents;

namespace Vectorer.Abstractions.Geometry;

/// <summary>
/// Boolean geometry operations between shapes.
/// </summary>
public enum BooleanOperationType
{
    Union,
    Difference,
    Intersection,
    Xor,
    ReverseDifference
}

/// <summary>
/// Service for performing constructive solid geometry (boolean operations) on 2D vector shapes.
/// </summary>
public interface IPathBooleanService
{
    /// <summary>
    /// Combines two shapes using the specified boolean operation and returns a new <see cref="PathShapeNode"/>.
    /// </summary>
    PathShapeNode? Combine(ShapeNode primary, ShapeNode secondary, BooleanOperationType operation);

    /// <summary>
    /// Combines multiple shapes sequentially using the specified boolean operation.
    /// </summary>
    PathShapeNode? CombineMany(IReadOnlyList<ShapeNode> shapes, BooleanOperationType operation);
}
