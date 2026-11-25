using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using WireGuardBar.Core;

namespace WireGuardBar;

/// Animated dotted constellation shown while a tunnel is switching on or off (see Core/OrbWeb.cs).
/// Light dots for dark backgrounds; animates only while visible and when Windows animations are on.
public sealed class ConnectingOrb : FrameworkElement
{
    private static readonly long Epoch = Stopwatch.GetTimestamp();
    private bool hooked;

    public ConnectingOrb()
    {
        Width = Height = OrbWeb.Size;
        IsVisibleChanged += (_, _) => Hook(IsVisible && SystemParameters.ClientAreaAnimation);
        Unloaded += (_, _) => Hook(false);
    }

    private void Hook(bool on)
    {
        if (on == hooked) return;
        hooked = on;
        if (on) CompositionTarget.Rendering += Tick;
        else CompositionTarget.Rendering -= Tick;
    }

    private void Tick(object? sender, EventArgs e) => InvalidateVisual();

    protected override void OnRender(DrawingContext dc)
    {
        var t = hooked ? Stopwatch.GetElapsedTime(Epoch).TotalSeconds * OrbWeb.Speed : 0;
        var frame = OrbWeb.Frame(OrbWeb.Size, t);
        foreach (var l in frame.Lines)
            dc.DrawLine(new Pen(Ink(l.White, l.A), l.W), new Point(l.X1, l.Y1), new Point(l.X2, l.Y2));
        foreach (var d in frame.Dots)
            dc.DrawEllipse(Ink(d.White, d.A), null, new Point(d.X, d.Y), d.R, d.R);
    }

    /// Dark substrate: ink is mirrored (1 - white) so near dots read bright.
    private static Brush Ink(double white, double alpha)
    {
        var v = (byte)Math.Round(255 * (1 - Math.Clamp(white, 0, 1)));
        var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * Math.Clamp(alpha, 0, 1)), v, v, v));
        brush.Freeze();
        return brush;
    }
}
