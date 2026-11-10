using Vectorer.Abstractions.Commands;
using Vectorer.Abstractions.Documents;

namespace Vectorer.Core.Commands;

/// <summary>
/// Default implementation of <see cref="IUndoRedoService"/>.
/// </summary>
public sealed class UndoRedoService : IUndoRedoService
{
    private readonly List<IDocumentCommand> _undoStack = [];
    private readonly List<IDocumentCommand> _redoStack = [];

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public IReadOnlyList<IDocumentCommand> UndoStack => _undoStack;
    public IReadOnlyList<IDocumentCommand> RedoStack => _redoStack;

    public event EventHandler? HistoryChanged;

    public void ExecuteCommand(IDocumentCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        command.Execute();
        _undoStack.Add(command);
        _redoStack.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        var lastIndex = _undoStack.Count - 1;
        var command = _undoStack[lastIndex];
        _undoStack.RemoveAt(lastIndex);
        command.Undo();
        _redoStack.Add(command);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        var lastIndex = _redoStack.Count - 1;
        var command = _redoStack[lastIndex];
        _redoStack.RemoveAt(lastIndex);
        command.Execute();
        _undoStack.Add(command);
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        HistoryChanged?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Command that adds a <see cref="SceneNode"/> to a <see cref="ContainerNode"/>.
/// </summary>
public sealed class AddNodeCommand(ContainerNode container, SceneNode node, string? description = null) : IDocumentCommand
{
    public string Description { get; } = description ?? $"Add {node.Name}";

    public void Execute()
    {
        if (!container.Children.Contains(node))
        {
            container.Children.Add(node);
        }
    }

    public void Undo()
    {
        container.Children.Remove(node);
    }
}

/// <summary>
/// Command that removes a <see cref="SceneNode"/> from a <see cref="ContainerNode"/>.
/// </summary>
public sealed class RemoveNodeCommand(ContainerNode container, SceneNode node, string? description = null) : IDocumentCommand
{
    private int _removedIndex = -1;

    public string Description { get; } = description ?? $"Remove {node.Name}";

    public void Execute()
    {
        _removedIndex = container.Children.IndexOf(node);
        if (_removedIndex >= 0)
        {
            container.Children.RemoveAt(_removedIndex);
        }
    }

    public void Undo()
    {
        if (_removedIndex >= 0 && _removedIndex <= container.Children.Count)
        {
            container.Children.Insert(_removedIndex, node);
        }
        else if (!container.Children.Contains(node))
        {
            container.Children.Add(node);
        }
    }
}

/// <summary>
/// Lightweight delegate-based document command for plugins and custom operations.
/// </summary>
public sealed class DelegateDocumentCommand(string description, Action execute, Action undo) : IDocumentCommand
{
    public string Description { get; } = description;
    public void Execute() => execute();
    public void Undo() => undo();
}
