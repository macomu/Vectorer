using SkiaSharp;
using Vectorer.Abstractions.Documents;

namespace Vectorer.Core.Geometry;

public static class PathEditingService
{
    /// <summary>Joins the nearest open endpoints, in the paths' shared parent coordinate system.</summary>
    public static PathShapeNode? Join(PathShapeNode first, PathShapeNode second)
    {
        if (!first.Transform.TryInvert(out var inverse)) return null;
        using var firstPath = first.BuildTransformedPath();
        using var secondPath = second.BuildTransformedPath();
        var a = EditablePath.Parse(firstPath.ToSvgPathData());
        var b = EditablePath.Parse(secondPath.ToSvgPathData());
        static void RestoreKinds(EditablePath model, string kinds)
        {
            var values = kinds.Split(','); var i = 0;
            foreach (var anchor in model.Contours.SelectMany(c => c.Anchors))
            { if (i < values.Length && Enum.TryParse<PathNodeKind>(values[i], out var kind)) anchor.Kind = kind; i++; }
        }
        RestoreKinds(a, first.NodeKinds); RestoreKinds(b, second.NodeKinds);
        var candidates = from ac in a.Contours.Where(c => !c.IsClosed && c.Anchors.Count > 0)
                         from bc in b.Contours.Where(c => !c.IsClosed && c.Anchors.Count > 0)
                         from ai in new[] { 0, ac.Anchors.Count - 1 }
                         from bi in new[] { 0, bc.Anchors.Count - 1 }
                         select (A: ac, B: bc, Ai: ai, Bi: bi, Distance: PathAnchor.Length(ac.Anchors[ai].Position - bc.Anchors[bi].Position));
        var chosen = candidates.OrderBy(c => c.Distance).FirstOrDefault();
        if (chosen.A is null) return null;
        if (chosen.Ai == 0) Reverse(chosen.A);
        if (chosen.Bi != 0) Reverse(chosen.B);
        // The bridge is a straight segment; outer control handles retain their original geometry.
        chosen.A.Anchors[^1].Outgoing = null;
        chosen.B.Anchors[0].Incoming = null;
        if (chosen.Distance < 0.0001f)
        {
            chosen.A.Anchors[^1].Outgoing = chosen.B.Anchors[0].Outgoing;
            chosen.A.Anchors.AddRange(chosen.B.Anchors.Skip(1));
        }
        else chosen.A.Anchors.AddRange(chosen.B.Anchors);
        b.Contours.Remove(chosen.B); a.Contours.AddRange(b.Contours);
        a.FillType = first.FillType;
        var result = (PathShapeNode)SceneNodeCloner.Clone(first);
        result.Transform = first.Transform;
        using var merged = a.BuildPath(); merged.Transform(inverse);
        result.SvgPathData = merged.ToSvgPathData();
        result.NodeKinds = string.Join(",", a.Contours.SelectMany(c => c.Anchors).Select(n => n.Kind));
        return result;
    }

    public static void Reverse(PathContour contour)
    {
        contour.Anchors.Reverse();
        foreach (var anchor in contour.Anchors) (anchor.Incoming, anchor.Outgoing) = (anchor.Outgoing, anchor.Incoming);
    }
}
