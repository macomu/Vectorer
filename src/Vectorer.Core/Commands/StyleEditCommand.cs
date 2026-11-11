using Vectorer.Abstractions.Commands;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;

namespace Vectorer.Core.Commands;

/// <summary>Snapshots each shape separately so a single-attribute edit preserves mixed attributes.</summary>
public sealed class StyleEditCommand : IDocumentCommand
{
    private readonly (ShapeNode Shape, ShapeStyle Before, ShapeStyle After)[] _changes;
    public string Description { get; }

    public StyleEditCommand(IEnumerable<ShapeNode> shapes, Action<ShapeStyle> edit, string description)
    {
        Description = description;
        _changes = shapes.Select(shape =>
        {
            var after = shape.Style.Clone();
            edit(after);
            return (shape, shape.Style.Clone(), after);
        }).ToArray();
    }

    public void Execute() { foreach (var (shape, _, after) in _changes) shape.Style = after.Clone(); }
    public void Undo() { foreach (var (shape, before, _) in _changes) shape.Style = before.Clone(); }
}
