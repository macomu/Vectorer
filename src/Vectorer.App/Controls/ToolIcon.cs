using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace Vectorer.App.Controls;

/// <summary>Draws the project's path-only SVG icons as resolution-independent geometry.</summary>
public sealed class ToolIcon : Control
{
    public static readonly StyledProperty<string> IconProperty = AvaloniaProperty.Register<ToolIcon, string>(nameof(Icon), "select");
    public string Icon { get => GetValue(IconProperty); set => SetValue(IconProperty, value); }
    private static readonly Dictionary<string, Geometry> Cache = [];
    static ToolIcon() => AffectsRender<ToolIcon>(IconProperty);
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (!Cache.TryGetValue(Icon, out var geometry))
        {
            using var stream = AssetLoader.Open(new Uri($"avares://Vectorer.App/Assets/Icons/{Icon}.svg"));
            var svg = XDocument.Load(stream);
            var path = svg.Root!.Elements().Single();
            geometry = Geometry.Parse(path.Attribute("d")!.Value);
            Cache.Add(Icon, geometry);
        }
        var size = Math.Min(Bounds.Width, Bounds.Height);
        using (context.PushTransform(Matrix.CreateScale(size / 24, size / 24)))
            context.DrawGeometry(null, new Pen(Brushes.LightBlue, 1.6, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }
}
