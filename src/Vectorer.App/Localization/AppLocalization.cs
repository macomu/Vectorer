using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Vectorer.App.Localization;

public sealed class AppLocalization : ObservableObject
{
    public const string SystemLanguage = "system";
    public const string JapaneseLanguage = "ja";
    public const string EnglishLanguage = "en";

    private static readonly CultureInfo JapaneseCulture = CultureInfo.GetCultureInfo("ja");
    private static readonly CultureInfo EnglishCulture = CultureInfo.GetCultureInfo("en");

    public CultureInfo OsUiCulture { get; }
    public CultureInfo FormattingCulture { get; }
    public CultureInfo UiCulture { get; private set; }
    public string RequestedLanguage { get; private set; }
    public long Revision { get; private set; }

    public bool IsSystemLanguage => RequestedLanguage == SystemLanguage;
    public bool IsJapaneseLanguage => RequestedLanguage == JapaneseLanguage;
    public bool IsEnglishLanguage => RequestedLanguage == EnglishLanguage;

    public AppLocalization(CultureInfo? osUiCulture = null, CultureInfo? formattingCulture = null, string? initialLanguage = null)
    {
        OsUiCulture = osUiCulture ?? CultureInfo.CurrentUICulture;
        FormattingCulture = formattingCulture ?? CultureInfo.CurrentCulture;
        RequestedLanguage = NormalizeLanguage(initialLanguage);
        UiCulture = ResolveCulture(RequestedLanguage, OsUiCulture);
    }

    public static string NormalizeLanguage(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        JapaneseLanguage => JapaneseLanguage,
        EnglishLanguage => EnglishLanguage,
        _ => SystemLanguage
    };

    public static CultureInfo ResolveCulture(string? requestedLanguage, CultureInfo osUiCulture)
    {
        return NormalizeLanguage(requestedLanguage) switch
        {
            JapaneseLanguage => JapaneseCulture,
            EnglishLanguage => EnglishCulture,
            _ => HasJapaneseParent(osUiCulture) ? JapaneseCulture : EnglishCulture
        };
    }

    private static bool HasJapaneseParent(CultureInfo? culture)
    {
        var current = culture;
        while (current is not null && !current.Equals(CultureInfo.InvariantCulture))
        {
            if (string.Equals(current.TwoLetterISOLanguageName, "ja", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            current = current.Parent;
        }
        return false;
    }

    public string Get(string key) =>
        Strings.ResourceManager.GetString(key, UiCulture)
        ?? Strings.ResourceManager.GetString(key, EnglishCulture)
        ?? key;

    public string Format(LocalizedMessage message)
    {
        if (message.IsLiteral)
        {
            return message.LiteralText!;
        }

        var template = Get(message.Key!);
        return message.Arguments.Length == 0
            ? template
            : string.Format(FormattingCulture, template, message.Arguments);
    }

    /// <summary>
    /// Updates the active UI culture and notifies all bound properties. Must be called on the UI thread.
    /// </summary>
    public bool ApplyCulture(string requestedLanguage, CultureInfo culture)
    {
        var normalized = NormalizeLanguage(requestedLanguage);
        var languageChanged = !string.Equals(RequestedLanguage, normalized, StringComparison.Ordinal);
        var cultureChanged = !string.Equals(UiCulture.Name, culture.Name, StringComparison.OrdinalIgnoreCase);

        if (!languageChanged && !cultureChanged)
        {
            return false;
        }

        RequestedLanguage = normalized;
        UiCulture = culture;
        Revision++;

        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (cultureChanged)
        {
            OnPropertyChanged(string.Empty);
        }
        else
        {
            OnPropertyChanged(nameof(RequestedLanguage));
            OnPropertyChanged(nameof(IsSystemLanguage));
            OnPropertyChanged(nameof(IsJapaneseLanguage));
            OnPropertyChanged(nameof(IsEnglishLanguage));
        }

        return cultureChanged;
    }

    // Typed UI properties (compile-time checked against Strings.resx)
    public string Menu_File => Get(nameof(Strings.Menu_File));
    public string Menu_File_New => Get(nameof(Strings.Menu_File_New));
    public string Menu_File_Open => Get(nameof(Strings.Menu_File_Open));
    public string Menu_File_Recent => Get(nameof(Strings.Menu_File_Recent));
    public string Menu_File_Recent_Empty => Get(nameof(Strings.Menu_File_Recent_Empty));
    public string Menu_File_Recent_Clear => Get(nameof(Strings.Menu_File_Recent_Clear));
    public string Menu_File_Save => Get(nameof(Strings.Menu_File_Save));
    public string Menu_File_SaveAs => Get(nameof(Strings.Menu_File_SaveAs));
    public string Menu_File_ExportPng => Get(nameof(Strings.Menu_File_ExportPng));
    public string Menu_File_DocumentSettings => Get(nameof(Strings.Menu_File_DocumentSettings));
    public string Menu_File_Exit => Get(nameof(Strings.Menu_File_Exit));
    public string Menu_View => Get(nameof(Strings.Menu_View));
    public string Menu_View_Language => Get(nameof(Strings.Menu_View_Language));
    public string Menu_View_Language_System => Get(nameof(Strings.Menu_View_Language_System));
    public string Menu_Edit => Get(nameof(Strings.Menu_Edit));
    public string Menu_Edit_Undo => Get(nameof(Strings.Menu_Edit_Undo));
    public string Menu_Edit_Redo => Get(nameof(Strings.Menu_Edit_Redo));
    public string Menu_Edit_Cut => Get(nameof(Strings.Menu_Edit_Cut));
    public string Menu_Edit_Copy => Get(nameof(Strings.Menu_Edit_Copy));
    public string Menu_Edit_Paste => Get(nameof(Strings.Menu_Edit_Paste));
    public string Menu_Edit_Duplicate => Get(nameof(Strings.Menu_Edit_Duplicate));
    public string Menu_Edit_Delete => Get(nameof(Strings.Menu_Edit_Delete));
    public string Menu_Edit_SelectAll => Get(nameof(Strings.Menu_Edit_SelectAll));
    public string Menu_View_ZoomIn => Get(nameof(Strings.Menu_View_ZoomIn));
    public string Menu_View_ZoomOut => Get(nameof(Strings.Menu_View_ZoomOut));
    public string Menu_View_Zoom100 => Get(nameof(Strings.Menu_View_Zoom100));
    public string Menu_View_FitCanvas => Get(nameof(Strings.Menu_View_FitCanvas));
    public string Menu_View_ToggleGrid => Get(nameof(Strings.Menu_View_ToggleGrid));
    public string Menu_View_ToggleSnap => Get(nameof(Strings.Menu_View_ToggleSnap));
    public string Menu_Object => Get(nameof(Strings.Menu_Object));
    public string Menu_Object_Group => Get(nameof(Strings.Menu_Object_Group));
    public string Menu_Object_Ungroup => Get(nameof(Strings.Menu_Object_Ungroup));
    public string Menu_Object_EnterGroup => Get(nameof(Strings.Menu_Object_EnterGroup));
    public string Menu_Object_ExitGroup => Get(nameof(Strings.Menu_Object_ExitGroup));
    public string Menu_Object_FlipHorizontal => Get(nameof(Strings.Menu_Object_FlipHorizontal));
    public string Menu_Object_FlipVertical => Get(nameof(Strings.Menu_Object_FlipVertical));
    public string Menu_Object_Order => Get(nameof(Strings.Menu_Object_Order));
    public string Menu_Object_Order_Front => Get(nameof(Strings.Menu_Object_Order_Front));
    public string Menu_Object_Order_Forward => Get(nameof(Strings.Menu_Object_Order_Forward));
    public string Menu_Object_Order_Backward => Get(nameof(Strings.Menu_Object_Order_Backward));
    public string Menu_Object_Order_Back => Get(nameof(Strings.Menu_Object_Order_Back));
    public string Menu_Object_Align => Get(nameof(Strings.Menu_Object_Align));
    public string Menu_Object_Align_Left => Get(nameof(Strings.Menu_Object_Align_Left));
    public string Menu_Object_Align_CenterX => Get(nameof(Strings.Menu_Object_Align_CenterX));
    public string Menu_Object_Align_Right => Get(nameof(Strings.Menu_Object_Align_Right));
    public string Menu_Object_Align_Top => Get(nameof(Strings.Menu_Object_Align_Top));
    public string Menu_Object_Align_CenterY => Get(nameof(Strings.Menu_Object_Align_CenterY));
    public string Menu_Object_Align_Bottom => Get(nameof(Strings.Menu_Object_Align_Bottom));
    public string Menu_Object_Align_DistributeX => Get(nameof(Strings.Menu_Object_Align_DistributeX));
    public string Menu_Object_Align_DistributeY => Get(nameof(Strings.Menu_Object_Align_DistributeY));
    public string Menu_Path => Get(nameof(Strings.Menu_Path));
    public string Menu_Path_ObjectToPath => Get(nameof(Strings.Menu_Path_ObjectToPath));
    public string Menu_Path_StrokeToPath => Get(nameof(Strings.Menu_Path_StrokeToPath));
    public string Menu_Path_Union => Get(nameof(Strings.Menu_Path_Union));
    public string Menu_Path_Difference => Get(nameof(Strings.Menu_Path_Difference));
    public string Menu_Path_Intersection => Get(nameof(Strings.Menu_Path_Intersection));
    public string Menu_Path_Xor => Get(nameof(Strings.Menu_Path_Xor));
    public string Menu_Path_ReverseDifference => Get(nameof(Strings.Menu_Path_ReverseDifference));
    public string Menu_Path_ToggleClosed => Get(nameof(Strings.Menu_Path_ToggleClosed));
    public string Menu_Path_Join => Get(nameof(Strings.Menu_Path_Join));
    public string Menu_Path_Simplify => Get(nameof(Strings.Menu_Path_Simplify));
    public string Menu_Layer => Get(nameof(Strings.Menu_Layer));
    public string Menu_Layer_New => Get(nameof(Strings.Menu_Layer_New));
    public string Menu_Layer_Duplicate => Get(nameof(Strings.Menu_Layer_Duplicate));
    public string Menu_Layer_Delete => Get(nameof(Strings.Menu_Layer_Delete));
    public string Menu_Layer_RotateFlip => Get(nameof(Strings.Menu_Layer_RotateFlip));
    public string Menu_Layer_Rotate90CW => Get(nameof(Strings.Menu_Layer_Rotate90CW));
    public string Menu_Layer_Rotate90CCW => Get(nameof(Strings.Menu_Layer_Rotate90CCW));
    public string Menu_Layer_Rotate180 => Get(nameof(Strings.Menu_Layer_Rotate180));
    public string Menu_Layer_RotateCustom => Get(nameof(Strings.Menu_Layer_RotateCustom));
    public string Menu_Layer_BlendMode => Get(nameof(Strings.Menu_Layer_BlendMode));
    public string Menu_Layer_Order => Get(nameof(Strings.Menu_Layer_Order));
    public string Common_Cancel => Get(nameof(Strings.Common_Cancel));
    public string Common_Ok => Get(nameof(Strings.Common_Ok));
}
