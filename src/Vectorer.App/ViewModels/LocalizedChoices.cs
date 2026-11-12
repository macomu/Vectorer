using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Styling;
using Vectorer.App.Localization;

namespace Vectorer.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly Dictionary<string, IReadOnlyList<LocalizedChoice>> _localizedChoices = [];

    private IReadOnlyList<LocalizedChoice> Choices<T>(string id, IEnumerable<T> values, Func<T, string> key) where T : notnull
    {
        if (!_localizedChoices.TryGetValue(id, out var choices))
        {
            choices = values.Select(v => new LocalizedChoice(v, key(v), L10n)).ToArray();
            _localizedChoices.Add(id, choices);
        }
        return choices;
    }

    public IReadOnlyList<LocalizedChoice> FillChoices => Choices(nameof(FillChoices), FillKinds, v => v switch
    {
        "なし" => nameof(Strings.Ui_None), "単色" => nameof(Strings.Choice_Solid), _ => nameof(Strings.Choice_Texture)
    });
    public IReadOnlyList<LocalizedChoice> DashChoices => Choices(nameof(DashChoices), DashOptions, v => v switch
    {
        "実線" => nameof(Strings.Choice_SolidLine), "破線" => nameof(Strings.Choice_Dashed), "点線" => nameof(Strings.Choice_Dotted),
        "一点鎖線" => nameof(Strings.Choice_DashDot), _ => nameof(Strings.Ui_Custom)
    });
    public IReadOnlyList<LocalizedChoice> EllipseChoices => Choices(nameof(EllipseChoices), EllipseModes, v => v switch
    {
        "楕円" => nameof(Strings.Tool_Ellipse_Title), "扇形" => nameof(Strings.Choice_PieSlice),
        "弦" => nameof(Strings.Choice_Chord), _ => nameof(Strings.Choice_Open)
    });
    public IReadOnlyList<LocalizedChoice> BlendChoices => Choices(nameof(BlendChoices), BlendModes, v => "Choice_Blend_" + v);
    public IReadOnlyList<LocalizedChoice> TextureChoices => Choices(nameof(TextureChoices), TexturePatterns, v => "Choice_Texture_" + v);
    public IReadOnlyList<LocalizedChoice> CapChoices => Choices(nameof(CapChoices), StrokeCaps, v => "Choice_Cap_" + v);
    public IReadOnlyList<LocalizedChoice> JoinChoices => Choices(nameof(JoinChoices), StrokeJoins, v => "Choice_Join_" + v);
    public IReadOnlyList<LocalizedChoice> ArcChoices => Choices(nameof(ArcChoices), ArcModes, v => "Choice_" + v);
    public IReadOnlyList<LocalizedChoice> AlignmentChoices => Choices(nameof(AlignmentChoices), TextAlignments, v => "Choice_Align_" + v);
}
