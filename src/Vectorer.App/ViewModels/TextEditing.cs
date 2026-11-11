using CommunityToolkit.Mvvm.ComponentModel;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Core.Commands;
using Vectorer.Core.Geometry;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    [ObservableProperty] private string _fontFamily = "sans-serif";
    [ObservableProperty] private decimal _fontSize = 32;
    [ObservableProperty] private bool _textBold;
    [ObservableProperty] private bool _textItalic;
    [ObservableProperty] private SKTextAlign _textAlignment;
    [ObservableProperty] private decimal _letterSpacing;
    public IReadOnlyList<SKTextAlign> TextAlignments { get; } = Enum.GetValues<SKTextAlign>();
    public event Action<TextShapeNode, SKPoint>? TextEditRequested;
    public event Action? FinishTextEditingRequested;
    public bool IsTextEditing => _editingText is not null;
    public void FinishTextEditing() => FinishTextEditingRequested?.Invoke();
    private TextShapeNode? _editingText;
    private ContainerNode? _textContainer;
    private bool _newText;

    public void BeginTextEdit(SKPoint point)
    {
        var hit = GetEditableNodes().Reverse().FirstOrDefault(e => e.Node is TextShapeNode && CanvasGeometry.HitTest(e.Node, e.Parent, point, (float)(4 / Zoom)));
        if (hit.Node is TextShapeNode existing)
        {
            _editingText = existing; _newText = false;
            SelectNodes([existing]);
            TextEditRequested?.Invoke(existing, SKMatrix.Concat(hit.Parent, existing.Transform).MapPoint(existing.Position));
            return;
        }
        if (GetDrawingTarget() is not { } target || !target.World.TryInvert(out var inverse)) return;
        _textContainer = target.Container;
        point = SnapPoint(point);
        _editingText = new TextShapeNode { Position = inverse.MapPoint(point), Style = DefaultCreationStyle.Clone() };
        ApplyTextSettings(_editingText); _newText = true;
        TextEditRequested?.Invoke(_editingText, point);
    }

    public void CommitText(string? text)
    {
        if (_editingText is not { } node) return;
        _editingText = null;
        if (text is null) return;
        if (_newText)
        {
            if (string.IsNullOrWhiteSpace(text) || _textContainer is null) return;
            node.Text = text;
            ExecuteCommand(new AddNodeCommand(_textContainer, node, "Create text"));
            SelectNodes([node]);
        }
        else if (node.Text != text)
        {
            var before = node.Text;
            ExecuteCommand(new DelegateDocumentCommand("Edit text", () => node.Text = text, () => node.Text = before));
        }
    }
    private void ApplyTextSettings(TextShapeNode node)
    {
        node.FontFamily = FontFamily; node.FontSize = (float)FontSize;
        node.IsBold = TextBold; node.IsItalic = TextItalic;
        node.TextAlign = TextAlignment; node.LetterSpacing = (float)LetterSpacing;
    }
    private void EditTextSettings()
    {
        if (_syncingEditors || IsInteracting || SelectedNodes.Count != 1 || SelectedNodes[0] is not TextShapeNode text) return;
        var before = (text.FontFamily, text.FontSize, text.IsBold, text.IsItalic, text.TextAlign, text.LetterSpacing);
        ApplyTextSettings(text);
        var after = (text.FontFamily, text.FontSize, text.IsBold, text.IsItalic, text.TextAlign, text.LetterSpacing);
        void Apply((string Family, float Size, bool Bold, bool Italic, SKTextAlign Align, float Spacing) value)
        { text.FontFamily = value.Family; text.FontSize = value.Size; text.IsBold = value.Bold; text.IsItalic = value.Italic; text.TextAlign = value.Align; text.LetterSpacing = value.Spacing; }
        ExecuteCommand(new DelegateDocumentCommand("Text properties", () => Apply(after), () => Apply(before)));
    }
    partial void OnFontFamilyChanged(string value) => EditTextSettings();
    partial void OnFontSizeChanged(decimal value) => EditTextSettings();
    partial void OnTextBoldChanged(bool value) => EditTextSettings();
    partial void OnTextItalicChanged(bool value) => EditTextSettings();
    partial void OnTextAlignmentChanged(SKTextAlign value) => EditTextSettings();
    partial void OnLetterSpacingChanged(decimal value) => EditTextSettings();
}
