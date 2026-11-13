using Vectorer.Abstractions.Documents;
using Vectorer.App.Localization;
using Vectorer.Core.Geometry;

namespace Vectorer.App.Services;

/// <summary>
/// Produces initial document, layer, and shape names in the active UI language at creation time.
/// Generated names are stored as document data and are never retroactively re-translated.
/// </summary>
public sealed class EditorNaming(AppLocalization localization)
{
    public string UntitledDocument() => localization.Get(nameof(Strings.DefaultName_Untitled));

    public string Layer(int number) =>
        string.Format(localization.FormattingCulture, localization.Get(nameof(Strings.DefaultName_Layer)), number);

    public string CopyOf(string originalName) =>
        string.Format(localization.FormattingCulture, localization.Get(nameof(Strings.DefaultName_CopyOf)), originalName);

    public string RectangleName() => localization.Get(nameof(Strings.DefaultName_Rectangle));
    public string EllipseName() => localization.Get(nameof(Strings.DefaultName_Ellipse));
    public string CircleName() => localization.Get(nameof(Strings.DefaultName_Circle));
    public string ArcName() => localization.Get(nameof(Strings.DefaultName_Arc));
    public string PieArcName() => localization.Get(nameof(Strings.DefaultName_PieArc));
    public string FreehandName() => localization.Get(nameof(Strings.DefaultName_Freehand));
    public string PathName() => localization.Get(nameof(Strings.DefaultName_Path));
    public string TextName() => localization.Get(nameof(Strings.DefaultName_Text));

    public string PolygonName(int sides) =>
        string.Format(localization.FormattingCulture, localization.Get(nameof(Strings.DefaultName_Polygon)), sides);

    public string StarName(int points) =>
        string.Format(localization.FormattingCulture, localization.Get(nameof(Strings.DefaultName_Star)), points);

    public string ShapeName(DrawingTool tool, ShapeNode shape) => tool switch
    {
        DrawingTool.Rectangle => RectangleName(),
        DrawingTool.Ellipse => EllipseName(),
        DrawingTool.Arc => shape is ArcShapeNode { CloseMode: ArcCloseMode.PieSlice } ? PieArcName() : ArcName(),
        DrawingTool.Polygon when shape is PolygonShapeNode poly => PolygonName(Math.Max(3, poly.PointCount)),
        DrawingTool.Star when shape is PolygonShapeNode poly => StarName(Math.Max(3, poly.PointCount)),
        DrawingTool.Freehand => FreehandName(),
        DrawingTool.Bezier => PathName(),
        DrawingTool.Text => TextName(),
        _ => shape switch
        {
            RectangleShapeNode => RectangleName(),
            EllipseShapeNode => EllipseName(),
            ArcShapeNode arc => arc.CloseMode == ArcCloseMode.PieSlice ? PieArcName() : ArcName(),
            PolygonShapeNode poly when poly.PolygonKind == PolygonKind.ConcaveStar => StarName(Math.Max(3, poly.PointCount)),
            PolygonShapeNode poly => PolygonName(Math.Max(3, poly.PointCount)),
            TextShapeNode => TextName(),
            _ => PathName()
        }
    };

    public SvgDocument CreateDefaultDocument(float width = 860, float height = 540)
    {
        var document = SvgDocument.CreateDefault(width, height);
        document.Title = UntitledDocument();
        if (document.Layers.Count > 0)
        {
            document.Layers[0].Name = Layer(1);
        }
        return document;
    }
}
