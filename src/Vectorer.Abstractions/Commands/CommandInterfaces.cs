namespace Vectorer.Abstractions.Commands;

/// <summary>
/// Represents a reversible action on a document for Undo/Redo support.
/// </summary>
public interface IDocumentCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}

/// <summary>
/// Manages the Undo/Redo history stack for document modifications.
/// </summary>
public interface IUndoRedoService
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    IReadOnlyList<IDocumentCommand> UndoStack { get; }
    IReadOnlyList<IDocumentCommand> RedoStack { get; }

    event EventHandler? HistoryChanged;

    void ExecuteCommand(IDocumentCommand command);
    void Undo();
    void Redo();
    void Clear();
}
