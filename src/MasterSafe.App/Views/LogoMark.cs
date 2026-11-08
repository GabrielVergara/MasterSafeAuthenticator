// File:    LogoMark.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MasterSafe.App.Views;

/// <summary>
/// The MasterSafe logo drawn as vectors, so it can be animated: a key ring with an amber arc and a teal keyhole.
/// <see cref="Progress"/> runs the intro from an empty ring (0) to the finished logo (1). The amber arc sweeps clockwise
/// from 12 o'clock to 70 % of the ring, then the keyhole fades in. At 1 it is exactly the static logo.
/// </summary>
public sealed class LogoMark : Control
{
    /// <summary>How far the intro has played, from 0 to 1. The default shows the finished logo.</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<LogoMark, double>(nameof(Progress), 1.0, coerce: (_, v) => double.IsNaN(v) ? 1.0 : Math.Clamp(v, 0, 1));

    // The colours and sizes of the logo, in its own 128 by 128 grid.
    private static readonly IBrush Tile = new SolidColorBrush(Color.Parse("#12263A"));
    private static readonly IPen Track = new Pen(new SolidColorBrush(Color.Parse("#2B4560")), 8);
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#F2B33D"));
    private static readonly IBrush Teal = new SolidColorBrush(Color.Parse("#1FA38A"));
    private const double Ring = 40;
    private const double ArcShare = 0.70;

    static LogoMark() => AffectsRender<LogoMark>(ProgressProperty);

    /// <inheritdoc cref="ProgressProperty"/>
    public double Progress
    {
        get => GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    /// <summary>The share of the ring the amber arc covers at a point of the intro: 0 at the start, 0.7 at the end.</summary>
    public static double ArcLength(double progress) => ArcShare * EaseInOut(Math.Clamp(progress, 0, 1));

    /// <summary>How visible the keyhole is: nothing in the first half, then it fades and grows in.</summary>
    public static double KeyholeShown(double progress) => EaseOut(Math.Clamp((progress - 0.5) / 0.5, 0, 1));

    private static double EaseInOut(double t) => t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;

    private static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0)
        {
            return;
        }

        var scale = side / 128;
        var offset = new Point((Bounds.Width - side) / 2, (Bounds.Height - side) / 2);
        using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(offset.X, offset.Y)))
        {
            context.DrawRectangle(Tile, null, new Rect(0, 0, 128, 128), 28, 28);
            var centre = new Point(64, 64);
            context.DrawEllipse(null, Track, centre, Ring, Ring);

            DrawArc(context, centre, ArcLength(Progress), Math.Clamp(Progress / 0.06, 0, 1));
            DrawKeyhole(context, KeyholeShown(Progress));
        }
    }

    private static void DrawArc(DrawingContext context, Point centre, double share, double opacity)
    {
        if (share <= 0.001 || opacity <= 0)
        {
            return;
        }

        var end = -Math.PI / 2 + share * 2 * Math.PI;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(centre.X, centre.Y - Ring), false);
            g.ArcTo(new Point(centre.X + Ring * Math.Cos(end), centre.Y + Ring * Math.Sin(end)), new Size(Ring, Ring), 0, share > 0.5, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        using (context.PushOpacity(opacity))
        {
            context.DrawGeometry(null, new Pen(Amber, 8, lineCap: PenLineCap.Round), geometry);
        }
    }

    private static void DrawKeyhole(DrawingContext context, double shown)
    {
        if (shown <= 0)
        {
            return;
        }

        // It grows from 80 % to full size around the centre of the logo while it fades in.
        var size = 0.8 + 0.2 * shown;
        using (context.PushOpacity(shown))
        using (context.PushTransform(Matrix.CreateTranslation(-64, -64) * Matrix.CreateScale(size, size) * Matrix.CreateTranslation(64, 64)))
        {
            context.DrawEllipse(Teal, null, new Point(64, 58), 10, 10);
            var stem = new StreamGeometry();
            using (var g = stem.Open())
            {
                g.BeginFigure(new Point(58, 64), true);
                g.LineTo(new Point(70, 64));
                g.LineTo(new Point(73, 84));
                g.LineTo(new Point(55, 84));
                g.EndFigure(true);
            }

            context.DrawGeometry(Teal, null, stem);
        }
    }
}
