using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using Vectorer.Abstractions.Documents;
using Vectorer.Abstractions.Services;

namespace Vectorer.App.Controls;

/// <summary>
/// Custom Avalonia control that renders a <see cref="SvgDocument"/> using <see cref="IDocumentRenderer"/>.
/// </summary>
public sealed class VectorCanvasControl : Control
{
    public static readonly StyledProperty<SvgDocument?> DocumentProperty =
        AvaloniaProperty.Register<VectorCanvasControl, SvgDocument?>(nameof(Document));

    public static readonly StyledProperty<IDocumentRenderer?> RendererProperty =
        AvaloniaProperty.Register<VectorCanvasControl, IDocumentRenderer?>(nameof(Renderer));

    public static readonly StyledProperty<int> RenderVersionProperty =
        AvaloniaProperty.Register<VectorCanvasControl, int>(nameof(RenderVersion));

    static VectorCanvasControl()
    {
        AffectsRender<VectorCanvasControl>(DocumentProperty, RendererProperty, RenderVersionProperty);
    }

    public SvgDocument? Document
    {
        get => GetValue(DocumentProperty);
        set => SetValue(DocumentProperty, value);
    }

    public IDocumentRenderer? Renderer
    {
        get => GetValue(RendererProperty);
        set => SetValue(RendererProperty, value);
    }

    public int RenderVersion
    {
        get => GetValue(RenderVersionProperty);
        set => SetValue(RenderVersionProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var doc = Document;
        var renderer = Renderer;
        if (doc is null || renderer is null || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            return;
        }

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(doc.Width));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(doc.Height));

        using var writeableBitmap = new WriteableBitmap(
            new PixelSize(pixelWidth, pixelHeight),
            new Vector(96, 96),
            PixelFormat.Rgba8888,
            AlphaFormat.Premul);

        using (var fb = writeableBitmap.Lock())
        {
            var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info, fb.Address, fb.RowBytes);
            if (surface is not null)
            {
                surface.Canvas.Clear(new SKColor(28, 32, 38));
                renderer.RenderDocument(doc, surface.Canvas);
            }
        }

        var destRect = new Rect(0, 0, doc.Width, doc.Height);
        context.DrawImage(writeableBitmap, new Rect(0, 0, pixelWidth, pixelHeight), destRect);
    }
}
