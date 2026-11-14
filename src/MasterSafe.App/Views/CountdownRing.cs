// File:    CountdownRing.cs
// Project: MasterSafe
// Author:  Gabriel Vergara Ezcurdia
// License: See LICENSE

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MasterSafe.App.Views;

/// <summary>
/// The time left for a code, drawn like the logo's ring: a full ring that empties clockwise from 12 o'clock and turns to the
/// warning colour near the end. The number of seconds is a separate text, so screen readers read it.
/// </summary>
public sealed class CountdownRing : Control
{
    /// <summary>The share of the period left, from 1 (new code) to 0.</summary>
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<CountdownRing, double>(nameof(Progress), 1.0, coerce: (_, v) => double.IsNaN(v) ? 0 : Math.Clamp(v, 0, 1));

    /// <summary>True in the last seconds, when the ring uses <see cref="EndingBrush"/>.</summary>
    public static readonly StyledProperty<bool> IsEndingProperty = AvaloniaProperty.Register<CountdownRing, bool>(nameof(IsEnding));

    /// <summary>The colour of the time left.</summary>
    public static readonly StyledProperty<IBrush?> RingBrushProperty = AvaloniaProperty.Register<CountdownRing, IBrush?>(nameof(RingBrush));

    /// <summary>The colour of the time left near the end.</summary>
    public static readonly StyledProperty<IBrush?> EndingBrushProperty = AvaloniaProperty.Register<CountdownRing, IBrush?>(nameof(EndingBrush));

    /// <summary>The colour of the empty part of the ring.</summary>
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<CountdownRing, IBrush?>(nameof(TrackBrush));

    static CountdownRing() => AffectsRender<CountdownRing>(ProgressProperty, IsEndingProperty, RingBrushProperty, EndingBrushProperty, TrackBrushProperty);

    /// <inheritdoc cref="ProgressProperty"/>
    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }

    /// <inheritdoc cref="IsEndingProperty"/>
    public bool IsEnding { get => GetValue(IsEndingProperty); set => SetValue(IsEndingProperty, value); }

    /// <inheritdoc cref="RingBrushProperty"/>
    public IBrush? RingBrush { get => GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }

    /// <inheritdoc cref="EndingBrushProperty"/>
    public IBrush? EndingBrush { get => GetValue(EndingBrushProperty); set => SetValue(EndingBrushProperty, value); }

    /// <inheritdoc cref="TrackBrushProperty"/>
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        var side = Math.Min(Bounds.Width, Bounds.Height);
        if (side <= 0)
        {
            return;
        }

        var thickness = Math.Max(2, side / 9);
        var radius = (side - thickness) / 2;
        var centre = new Point(Bounds.Width / 2, Bounds.Height / 2);
        if (TrackBrush is { } track)
        {
            context.DrawEllipse(null, new Pen(track, thickness), centre, radius, radius);
        }

        var share = Progress;
        var brush = IsEnding ? EndingBrush ?? RingBrush : RingBrush;
        if (brush is null || share <= 0.001)
        {
            return;
        }

        if (share >= 0.999)
        {
            context.DrawEllipse(null, new Pen(brush, thickness), centre, radius, radius);
            return;
        }

        // The time left ends at 12 o'clock; the used part opens clockwise from there, as on a clock.
        var start = -Math.PI / 2 + (1 - share) * 2 * Math.PI;
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(new Point(centre.X + radius * Math.Cos(start), centre.Y + radius * Math.Sin(start)), false);
            g.ArcTo(new Point(centre.X, centre.Y - radius), new Size(radius, radius), 0, share > 0.5, SweepDirection.Clockwise);
            g.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(brush, thickness, lineCap: PenLineCap.Round), geometry);
    }
}
