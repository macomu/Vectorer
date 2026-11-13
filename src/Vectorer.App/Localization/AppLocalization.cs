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
    public string Ui_Apply => Get(nameof(Strings.Ui_Apply));
    public string Ui_Rotate180 => Get(nameof(Strings.Ui_Rotate180));
    public string Ui_MiterLimit => Get(nameof(Strings.Ui_MiterLimit));
    public string Ui_LinkRadii => Get(nameof(Strings.Ui_LinkRadii));
    public string Ui_CompileSksl => Get(nameof(Strings.Ui_CompileSksl));
    public string Ui_Sksl => Get(nameof(Strings.Ui_Sksl));
    public string Ui_ApplySksl => Get(nameof(Strings.Ui_ApplySksl));
    public string Ui_Uniforms => Get(nameof(Strings.Ui_Uniforms));
    public string Ui_Parent => Get(nameof(Strings.Ui_Parent));
    public string Ui_BlurDialog => Get(nameof(Strings.Ui_BlurDialog));
    public string Ui_EnableBlur => Get(nameof(Strings.Ui_EnableBlur));
    public string Ui_GaussianBlur => Get(nameof(Strings.Ui_GaussianBlur));
    public string Ui_Grid => Get(nameof(Strings.Ui_Grid));
    public string Ui_SnapHint => Get(nameof(Strings.Ui_SnapHint));
    public string Ui_Corner => Get(nameof(Strings.Ui_Corner));
    public string Ui_Secondary => Get(nameof(Strings.Ui_Secondary));
    public string Ui_Shader => Get(nameof(Strings.Ui_Shader));
    public string Ui_Simplify => Get(nameof(Strings.Ui_Simplify));
    public string Ui_NodeX => Get(nameof(Strings.Ui_NodeX));
    public string Ui_TextureScale => Get(nameof(Strings.Ui_TextureScale));
    public string Ui_StrokeDetails => Get(nameof(Strings.Ui_StrokeDetails));
    public string Ui_Snap => Get(nameof(Strings.Ui_Snap));
    public string Ui_Smoothing => Get(nameof(Strings.Ui_Smoothing));
    public string Ui_Smooth => Get(nameof(Strings.Ui_Smooth));
    public string Ui_TreeHint => Get(nameof(Strings.Ui_TreeHint));
    public string Ui_Pattern => Get(nameof(Strings.Ui_Pattern));
    public string Ui_ClearFilters => Get(nameof(Strings.Ui_ClearFilters));
    public string Ui_Extensions => Get(nameof(Strings.Ui_Extensions));
    public string Ui_PluginCommands => Get(nameof(Strings.Ui_PluginCommands));
    public string Ui_Primary => Get(nameof(Strings.Ui_Primary));
    public string Ui_LiveSksl => Get(nameof(Strings.Ui_LiveSksl));
    public string Ui_ToggleLayerBlur => Get(nameof(Strings.Ui_ToggleLayerBlur));
    public string Ui_RotateLayer => Get(nameof(Strings.Ui_RotateLayer));
    public string Ui_DuplicateLayer => Get(nameof(Strings.Ui_DuplicateLayer));
    public string Ui_LayerTransforms => Get(nameof(Strings.Ui_LayerTransforms));
    public string Ui_LayerAngle => Get(nameof(Strings.Ui_LayerAngle));
    public string Ui_Layers => Get(nameof(Strings.Ui_Layers));
    public string Ui_ToggleLock => Get(nameof(Strings.Ui_ToggleLock));
    public string Ui_Lock => Get(nameof(Strings.Ui_Lock));
    public string Ui_FlipVertical => Get(nameof(Strings.Ui_FlipVertical));
    public string Ui_MergeLayer => Get(nameof(Strings.Ui_MergeLayer));
    public string Ui_MergeLayerHint => Get(nameof(Strings.Ui_MergeLayerHint));
    public string Ui_OpacityShort => Get(nameof(Strings.Ui_OpacityShort));
    public string Ui_Opacity => Get(nameof(Strings.Ui_Opacity));
    public string Ui_CenterX => Get(nameof(Strings.Ui_CenterX));
    public string Ui_AddMidpoint => Get(nameof(Strings.Ui_AddMidpoint));
    public string Ui_DashExample => Get(nameof(Strings.Ui_DashExample));
    public string Ui_SkewX => Get(nameof(Strings.Ui_SkewX));
    public string Ui_SkewY => Get(nameof(Strings.Ui_SkewY));
    public string Ui_InnerRatio => Get(nameof(Strings.Ui_InnerRatio));
    public string Ui_StarRatio => Get(nameof(Strings.Ui_StarRatio));
    public string Ui_Effects => Get(nameof(Strings.Ui_Effects));
    public string Ui_Tolerance => Get(nameof(Strings.Ui_Tolerance));
    public string Ui_SolidHex => Get(nameof(Strings.Ui_SolidHex));
    public string Ui_RotateCCW => Get(nameof(Strings.Ui_RotateCCW));
    public string Ui_BlendShort => Get(nameof(Strings.Ui_BlendShort));
    public string Ui_BlendMode => Get(nameof(Strings.Ui_BlendMode));
    public string Ui_Compositing => Get(nameof(Strings.Ui_Compositing));
    public string Ui_Rotate => Get(nameof(Strings.Ui_Rotate));
    public string Ui_BlurY => Get(nameof(Strings.Ui_BlurY));
    public string Ui_Fill => Get(nameof(Strings.Ui_Fill));
    public string Ui_FillHex => Get(nameof(Strings.Ui_FillHex));
    public string Ui_FillDetails => Get(nameof(Strings.Ui_FillDetails));
    public string Ui_NoFill => Get(nameof(Strings.Ui_NoFill));
    public string Ui_FillOpacity => Get(nameof(Strings.Ui_FillOpacity));
    public string Ui_FillKind => Get(nameof(Strings.Ui_FillKind));
    public string Ui_FillColor => Get(nameof(Strings.Ui_FillColor));
    public string Ui_FillStroke => Get(nameof(Strings.Ui_FillStroke));
    public string Ui_Transform => Get(nameof(Strings.Ui_Transform));
    public string Ui_ApplyTransform => Get(nameof(Strings.Ui_ApplyTransform));
    public string Ui_OuterRadius => Get(nameof(Strings.Ui_OuterRadius));
    public string Ui_PolygonParams => Get(nameof(Strings.Ui_PolygonParams));
    public string Ui_Bold => Get(nameof(Strings.Ui_Bold));
    public string Ui_LetterSpacing => Get(nameof(Strings.Ui_LetterSpacing));
    public string Ui_Symmetric => Get(nameof(Strings.Ui_Symmetric));
    public string Ui_Sweep => Get(nameof(Strings.Ui_Sweep));
    public string Ui_FlipHorizontal => Get(nameof(Strings.Ui_FlipHorizontal));
    public string Ui_Width => Get(nameof(Strings.Ui_Width));
    public string Ui_Shape => Get(nameof(Strings.Ui_Shape));
    public string Ui_ApplyShape => Get(nameof(Strings.Ui_ApplyShape));
    public string Ui_CloseAfterDraw => Get(nameof(Strings.Ui_CloseAfterDraw));
    public string Ui_Italic => Get(nameof(Strings.Ui_Italic));
    public string Ui_NewLayerHint => Get(nameof(Strings.Ui_NewLayerHint));
    public string Ui_RotateCW => Get(nameof(Strings.Ui_RotateCW));
    public string Ui_Curve => Get(nameof(Strings.Ui_Curve));
    public string Ui_BlurX => Get(nameof(Strings.Ui_BlurX));
    public string Ui_LinkBlur => Get(nameof(Strings.Ui_LinkBlur));
    public string Ui_Mixed => Get(nameof(Strings.Ui_Mixed));
    public string Ui_MixedHex => Get(nameof(Strings.Ui_MixedHex));
    public string Ui_MixedNone => Get(nameof(Strings.Ui_MixedNone));
    public string Ui_MixedCustom => Get(nameof(Strings.Ui_MixedCustom));
    public string Ui_DeleteNode => Get(nameof(Strings.Ui_DeleteNode));
    public string Ui_Polyline => Get(nameof(Strings.Ui_Polyline));
    public string Ui_Straight => Get(nameof(Strings.Ui_Straight));
    public string Ui_DashPhase => Get(nameof(Strings.Ui_DashPhase));
    public string Ui_DashKind => Get(nameof(Strings.Ui_DashKind));
    public string Ui_DashArray => Get(nameof(Strings.Ui_DashArray));
    public string Ui_JoinEndpoints => Get(nameof(Strings.Ui_JoinEndpoints));
    public string Ui_Merge => Get(nameof(Strings.Ui_Merge));
    public string Ui_Stroke => Get(nameof(Strings.Ui_Stroke));
    public string Ui_NoStroke => Get(nameof(Strings.Ui_NoStroke));
    public string Ui_StrokeOpacity => Get(nameof(Strings.Ui_StrokeOpacity));
    public string Ui_StrokeWidth => Get(nameof(Strings.Ui_StrokeWidth));
    public string Ui_StrokeWidthHint => Get(nameof(Strings.Ui_StrokeWidthHint));
    public string Ui_LineStyle => Get(nameof(Strings.Ui_LineStyle));
    public string Ui_Cap => Get(nameof(Strings.Ui_Cap));
    public string Ui_StrokeColor => Get(nameof(Strings.Ui_StrokeColor));
    public string Ui_LockAspect => Get(nameof(Strings.Ui_LockAspect));
    public string Ui_Color => Get(nameof(Strings.Ui_Color));
    public string Ui_ToggleVisibility => Get(nameof(Strings.Ui_ToggleVisibility));
    public string Ui_Visibility => Get(nameof(Strings.Ui_Visibility));
    public string Ui_Duplicate => Get(nameof(Strings.Ui_Duplicate));
    public string Ui_Join => Get(nameof(Strings.Ui_Join));
    public string Ui_RadiusX => Get(nameof(Strings.Ui_RadiusX));
    public string Ui_RadiusY => Get(nameof(Strings.Ui_RadiusY));
    public string Ui_ResetCorners => Get(nameof(Strings.Ui_ResetCorners));
    public string Ui_Angle => Get(nameof(Strings.Ui_Angle));
    public string Ui_RotateCustom => Get(nameof(Strings.Ui_RotateCustom));
    public string Ui_Sides => Get(nameof(Strings.Ui_Sides));
    public string Ui_ExtraRotation => Get(nameof(Strings.Ui_ExtraRotation));
    public string Ui_CutNodes => Get(nameof(Strings.Ui_CutNodes));
    public string Ui_DeleteTarget => Get(nameof(Strings.Ui_DeleteTarget));
    public string Ui_CloseFinish => Get(nameof(Strings.Ui_CloseFinish));
    public string Ui_FinishOpen => Get(nameof(Strings.Ui_FinishOpen));
    public string Ui_ToggleClosed => Get(nameof(Strings.Ui_ToggleClosed));
    public string Ui_StartAngle => Get(nameof(Strings.Ui_StartAngle));
    public string Ui_VertexCount => Get(nameof(Strings.Ui_VertexCount));
    public string Ui_Height => Get(nameof(Strings.Ui_Height));
    public string Ui_ExportPng => Get(nameof(Strings.Ui_ExportPng));
    public string Ui_SavePng => Get(nameof(Strings.Ui_SavePng));
    public string Ui_SaveSvg => Get(nameof(Strings.Ui_SaveSvg));
    public string Ui_OpenSvg => Get(nameof(Strings.Ui_OpenSvg));
    public string Ui_Export => Get(nameof(Strings.Ui_Export));
    public string Ui_Cancel => Get(nameof(Strings.Ui_Cancel));
    public string Ui_Title => Get(nameof(Strings.Ui_Title));
    public string Ui_WholeDocument => Get(nameof(Strings.Ui_WholeDocument));
    public string Ui_DocumentSettings => Get(nameof(Strings.Ui_DocumentSettings));
    public string Ui_FilePrefix => Get(nameof(Strings.Ui_FilePrefix));
    public string Ui_FileError => Get(nameof(Strings.Ui_FileError));
    public string Ui_Preview => Get(nameof(Strings.Ui_Preview));
    public string Ui_LivePreview => Get(nameof(Strings.Ui_LivePreview));
    public string Ui_LayerRotation => Get(nameof(Strings.Ui_LayerRotation));
    public string Ui_ExportFolder => Get(nameof(Strings.Ui_ExportFolder));
    public string Ui_BatchLayers => Get(nameof(Strings.Ui_BatchLayers));
    public string Ui_Create => Get(nameof(Strings.Ui_Create));
    public string Ui_Save => Get(nameof(Strings.Ui_Save));
    public string Ui_Discard => Get(nameof(Strings.Ui_Discard));
    public string Ui_Scale => Get(nameof(Strings.Ui_Scale));
    public string Ui_ExportMode => Get(nameof(Strings.Ui_ExportMode));
    public string Ui_ExportSettings => Get(nameof(Strings.Ui_ExportSettings));
    public string Ui_ApplyRotation => Get(nameof(Strings.Ui_ApplyRotation));
    public string Ui_RotationCenter => Get(nameof(Strings.Ui_RotationCenter));
    public string Ui_RotationRange => Get(nameof(Strings.Ui_RotationRange));
    public string Ui_BaseSize => Get(nameof(Strings.Ui_BaseSize));
    public string Ui_WidthPx => Get(nameof(Strings.Ui_WidthPx));
    public string Ui_NewDocument => Get(nameof(Strings.Ui_NewDocument));
    public string Ui_Unsaved => Get(nameof(Strings.Ui_Unsaved));
    public string Ui_SaveQuestion => Get(nameof(Strings.Ui_SaveQuestion));
    public string Ui_SwapDimensions => Get(nameof(Strings.Ui_SwapDimensions));
    public string Ui_Transparent => Get(nameof(Strings.Ui_Transparent));
    public string Ui_TransparentBackground => Get(nameof(Strings.Ui_TransparentBackground));
    public string Ui_SelectionOnly => Get(nameof(Strings.Ui_SelectionOnly));
    public string Ui_Close => Get(nameof(Strings.Ui_Close));
    public string Ui_VisibleLayersOnly => Get(nameof(Strings.Ui_VisibleLayersOnly));
    public string Ui_HeightPx => Get(nameof(Strings.Ui_HeightPx));
    public string Ui_AspectLocked => Get(nameof(Strings.Ui_AspectLocked));
    public string Ui_None => Get(nameof(Strings.Ui_None));
    public string Ui_Custom => Get(nameof(Strings.Ui_Custom));
    public string Ui_CustomSksl => Get(nameof(Strings.Ui_CustomSksl));
    public string Ui_CanvasCenter => Get(nameof(Strings.Ui_CanvasCenter));
    public string Ui_LayerCenter => Get(nameof(Strings.Ui_LayerCenter));
    public string Ui_PresetPortrait => Get(nameof(Strings.Ui_PresetPortrait));
    public string Ui_PresetLandscape => Get(nameof(Strings.Ui_PresetLandscape));
    public string Ui_SelectedShapes => Get(nameof(Strings.Ui_SelectedShapes));
    public string Ui_NextShape => Get(nameof(Strings.Ui_NextShape));
    public string Ui_Document => Get(nameof(Strings.Ui_Document));
    public string Ui_NoSelectionExport => Get(nameof(Strings.Ui_NoSelectionExport));
    public string Ui_NoLayersExport => Get(nameof(Strings.Ui_NoLayersExport));
    public string Ui_InvalidExport => Get(nameof(Strings.Ui_InvalidExport));
    public string Ui_NoTarget => Get(nameof(Strings.Ui_NoTarget));
    public string Status_CannotDraw => Get(nameof(Strings.Status_CannotDraw));
    public string Status_SkslApplied => Get(nameof(Strings.Status_SkslApplied));
    public string Status_SkslFailed => Get(nameof(Strings.Status_SkslFailed));
    public string Status_AddedHexagon => Get(nameof(Strings.Status_AddedHexagon));
    public string Status_AddedStar => Get(nameof(Strings.Status_AddedStar));
    public string Status_AddedCircle => Get(nameof(Strings.Status_AddedCircle));
    public string Status_AddedArc => Get(nameof(Strings.Status_AddedArc));
    public string Status_Undo => Get(nameof(Strings.Status_Undo));
    public string Status_Redo => Get(nameof(Strings.Status_Redo));
    public string Status_NeedShapes => Get(nameof(Strings.Status_NeedShapes));
    public string Status_BlendChanged => Get(nameof(Strings.Status_BlendChanged));
    public string Status_BooleanEmpty => Get(nameof(Strings.Status_BooleanEmpty));
    public string Status_BooleanApplied => Get(nameof(Strings.Status_BooleanApplied));
    public string Status_CopyFailed => Get(nameof(Strings.Status_CopyFailed));
    public string Status_PasteFailed => Get(nameof(Strings.Status_PasteFailed));
    public string Status_NeedPaths => Get(nameof(Strings.Status_NeedPaths));
    public string Status_NoEndpoints => Get(nameof(Strings.Status_NoEndpoints));
    public string Status_FontFailed => Get(nameof(Strings.Status_FontFailed));
    public string Status_RecentFailed => Get(nameof(Strings.Status_RecentFailed));
    public string Status_BlurApplied => Get(nameof(Strings.Status_BlurApplied));
    public string Status_BlurCleared => Get(nameof(Strings.Status_BlurCleared));
    public string Status_PluginExecuted => Get(nameof(Strings.Status_PluginExecuted));
    public string Ui_EditingTarget => Get(nameof(Strings.Ui_EditingTarget));
    public string Ui_SelectedElements => Get(nameof(Strings.Ui_SelectedElements));
    public string Ui_TargetLayer => Get(nameof(Strings.Ui_TargetLayer));
    public string Ui_CustomScale => Get(nameof(Strings.Ui_CustomScale));
    public string Ui_OutputSize => Get(nameof(Strings.Ui_OutputSize));
    public string Ui_FontSearch => Get(nameof(Strings.Ui_FontSearch));
    public string Ui_FontName => Get(nameof(Strings.Ui_FontName));
    public string Ui_FontSelect => Get(nameof(Strings.Ui_FontSelect));
    public string Ui_MoreSettings => Get(nameof(Strings.Ui_MoreSettings));
    public string Status_LanguageChangePending => Get(nameof(Strings.Status_LanguageChangePending));
    public string Menu_Help => Get(nameof(Strings.Menu_Help));
    public string Menu_Help_About => Get(nameof(Strings.Menu_Help_About));
    public string Ui_AboutTitle => Get(nameof(Strings.Ui_AboutTitle));
    public string Ui_AboutDescription => Get(nameof(Strings.Ui_AboutDescription));
    public string Ui_VersionLabel => Get(nameof(Strings.Ui_VersionLabel));
    public string Ui_LicenseLabel => Get(nameof(Strings.Ui_LicenseLabel));
    public string Ui_RuntimeLabel => Get(nameof(Strings.Ui_RuntimeLabel));
    public string Paint_None => Get(nameof(Strings.Paint_None));
    public string Paint_Solid => Get(nameof(Strings.Paint_Solid));
    public string Paint_LinearGradient => Get(nameof(Strings.Paint_LinearGradient));
    public string Paint_Texture => Get(nameof(Strings.Paint_Texture));
    public string Paint_Type => Get(nameof(Strings.Paint_Type));
    public string Paint_Mixed => Get(nameof(Strings.Paint_Mixed));
    public string Paint_GradientHelp => Get(nameof(Strings.Paint_GradientHelp));
    public string Paint_Position => Get(nameof(Strings.Paint_Position));
    public string Paint_RemoveStop => Get(nameof(Strings.Paint_RemoveStop));
    public string Paint_Start => Get(nameof(Strings.Paint_Start));
    public string Paint_End => Get(nameof(Strings.Paint_End));
    public string Paint_ObjectCoordinates => Get(nameof(Strings.Paint_ObjectCoordinates));
    public string Paint_Opacity => Get(nameof(Strings.Paint_Opacity));
    public string Paint_Presets => Get(nameof(Strings.Paint_Presets));
    public string Paint_EditPresets => Get(nameof(Strings.Paint_EditPresets));
    public string Paint_MoveEarlier => Get(nameof(Strings.Paint_MoveEarlier));
    public string Paint_MoveLater => Get(nameof(Strings.Paint_MoveLater));
    public string Paint_RemovePreset => Get(nameof(Strings.Paint_RemovePreset));
    public string Paint_AddPreset => Get(nameof(Strings.Paint_AddPreset));
    public string Paint_UserCoordinates => Get(nameof(Strings.Paint_UserCoordinates));
}
