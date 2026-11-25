using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using WireGuardBar.Core;

namespace WireGuardBar;

/// A dotted globe showing where traffic goes: This PC → the local network → the VPN server
/// (route all traffic), or forking at the local network into VPN and plain internet (split).
/// Packets: green = sending, blue = receiving through the VPN, gray = traffic outside it.
/// DataContext is the TunnelItem. Redraws at most 30 fps, and not at all while the window is
/// hidden or minimized.
public sealed class TunnelGlobe : FrameworkElement
{
    private static readonly long Epoch = Stopwatch.GetTimestamp();
    private readonly DispatcherTimer timer = new(DispatcherPriority.Render) { Interval = TimeSpan.FromSeconds(1.0 / 30) };
    private Window? window;
    private INotifyPropertyChanged? watched;

    public TunnelGlobe()
    {
        timer.Tick += (_, _) => InvalidateVisual();
        Loaded += (_, _) =>
        {
            window = Window.GetWindow(this);
            if (window is not null) window.StateChanged += (_, _) => UpdateTimer();
            UpdateTimer();
        };
        Unloaded += (_, _) => timer.Stop();
        IsVisibleChanged += (_, _) => UpdateTimer();
        DataContextChanged += (_, _) =>
        {
            // still redraw on state changes while the animation is paused
            if (watched is not null) watched.PropertyChanged -= Redraw;
            watched = DataContext as INotifyPropertyChanged;
            if (watched is not null) watched.PropertyChanged += Redraw;
            InvalidateVisual();
        };
    }

    private void Redraw(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private void UpdateTimer() =>
        timer.IsEnabled = IsVisible && window?.WindowState != WindowState.Minimized && SystemParameters.ClientAreaAnimation;

    protected override void OnRender(DrawingContext dc)
    {
        if (DataContext is not TunnelItem tunnel) return;
        var t = timer.IsEnabled ? Stopwatch.GetElapsedTime(Epoch).TotalSeconds : 0;
        new GlobeScene(t, tunnel.IsConnected, tunnel.IsUp, tunnel.RouteAll, tunnel.ServerLabel, App.Store.NetworkLabel,
                VisualTreeHelper.GetDpi(this).PixelsPerDip)
            .Draw(dc, RenderSize);
    }
}

public readonly record struct GlobeScene(double T, bool Live, bool Up, bool RouteAll, string ServerLabel, string NetworkLabel, double PixelsPerDip)
{
    public static readonly Color Send = Color.FromRgb(0x40, 0xF2, 0x8C);
    public static readonly Color Receive = Color.FromRgb(0x4D, 0x99, 0xFF);
    private static readonly Color Direct = Color.FromRgb(0xB8, 0xB8, 0xB8);

    // ponytail: decorative positions, not real geography (that needs an IP-geolocation lookup).
    private static readonly Vec3 Pc = Unit(2, -46), Network = Unit(34, -12), Server = Unit(26, 38), Internet = Unit(-28, 22);
    private static readonly Vec3[] Lattice = Enumerable.Range(0, 900).Select(i => OrbWeb.FibDir(i, 900)).ToArray();
    private static readonly Vec3[] VpnRoute = [Pc, Network, Server], DirectRoute = [Pc, Network, Internet];
    private const double PacketPeriod = 1.8;   // seconds per trip
    private static readonly Typeface Label = new(new FontFamily("Segoe UI Variable Text, Segoe UI"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    public void Draw(DrawingContext dc, Size size)
    {
        var r = Math.Min(size.Width, size.Height) * 0.36;
        var c = new Point(size.Width / 2, size.Height / 2);
        // gentle sway that keeps every node on the near side
        var project = OrbWeb.MakeProjector(0.3 * Math.Sin(T * 0.18), 0.3, c.X, c.Y, r);

        DrawSphere(dc, c, r, project);

        // links: PC → network → server always; split mode adds network → internet
        Stroke(dc, VpnRoute, project, Color.FromArgb(Alpha(Up ? 0.9 : 0.25), 255, 255, 255), 1.5, Up ? null : [3, 4]);
        if (!RouteAll)
            Stroke(dc, [Network, Internet], project, WithAlpha(Direct, Up ? 0.55 : 0.2), 1.2, [2, 5]);

        if (Live)
        {
            var trip = T / PacketPeriod;
            var sending = (long)Math.Floor(trip) % 2 == 0;
            DrawPacket(dc, VpnRoute, trip, sending, sending ? Send : Receive, project);
            if (!RouteAll)
            {
                // traffic that skips the VPN, out of step with the VPN packet
                var other = trip + 0.5;
                DrawPacket(dc, DirectRoute, other, (long)Math.Floor(other) % 2 == 0, Direct, project);
            }
        }

        DrawNode(dc, Pc, "This PC", dim: false, above: false, project);
        // the network node's links leave from below it, so its label goes on top
        DrawNode(dc, Network, NetworkLabel, dim: false, above: true, project);
        DrawNode(dc, Server, ServerLabel, dim: false, above: false, project);
        if (!RouteAll) DrawNode(dc, Internet, "Internet", dim: true, above: false, project);
    }

    private static void DrawSphere(DrawingContext dc, Point c, double r, Func<Vec3, Vec3> project)
    {
        var glow = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb(15, 255, 255, 255), 0.8 / 1.15), new GradientStop(Colors.Transparent, 1) },
        };
        glow.Freeze();
        dc.DrawEllipse(glow, null, c, r * 1.15, r * 1.15);

        // dots bucketed by depth, one geometry + one fill per bucket (far → near)
        const int buckets = 8;
        var layers = new StreamGeometry[buckets];
        var contexts = new StreamGeometryContext[buckets];
        for (var i = 0; i < buckets; i++) contexts[i] = (layers[i] = new StreamGeometry()).Open();
        foreach (var d in Lattice)
        {
            var p = project(d);
            var depth = (p.Z + 1) / 2;
            var dr = 0.5 + 1.1 * depth;
            AddCircle(contexts[Math.Min(buckets - 1, (int)(depth * buckets))], p.X, p.Y, dr);
        }
        for (var i = 0; i < buckets; i++)
        {
            contexts[i].Close();
            layers[i].Freeze();
            var depth = (i + 0.5) / buckets;
            dc.DrawGeometry(Solid(WithAlpha(Color.FromRgb(217, 217, 217), 0.05 + 0.4 * depth * depth)), null, layers[i]);
        }
    }

    private static void AddCircle(StreamGeometryContext ctx, double x, double y, double r)
    {
        ctx.BeginFigure(new Point(x + r, y), isFilled: true, isClosed: true);
        ctx.ArcTo(new Point(x - r, y), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
        ctx.ArcTo(new Point(x + r, y), new Size(r, r), 0, false, SweepDirection.Clockwise, false, false);
    }

    private static void Stroke(DrawingContext dc, Vec3[] route, Func<Vec3, Vec3> project, Color color, double width, double[]? dash)
    {
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            var points = Enumerable.Range(0, 97).Select(i => project(PointOn(route, i / 96.0))).Select(p => new Point(p.X, p.Y)).ToList();
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, false);
        }
        geometry.Freeze();
        var pen = new Pen(Solid(color), width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, DashCap = PenLineCap.Round };
        // WPF dash lengths are in multiples of the pen width
        if (dash is not null) pen.DashStyle = new DashStyle(dash.Select(d => d / width), 0);
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private void DrawNode(DrawingContext dc, Vec3 point, string label, bool dim, bool above, Func<Vec3, Vec3> project)
    {
        var p = project(point);
        var at = new Point(p.X, p.Y);
        var strength = (Up ? 1 : 0.4) * (dim ? 0.6 : 1);
        dc.DrawEllipse(Solid(Color.FromArgb(Alpha(0.15 * strength), 255, 255, 255)), null, at, 9, 9);
        dc.DrawEllipse(Solid(Color.FromArgb(Alpha(strength), 255, 255, 255)), null, at, 4, 4);

        var lines = label.Split('\n').Length;
        var text = new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Label, 11,
            Solid(Color.FromArgb(Alpha(0.75 * Math.Max(strength, 0.6)), 255, 255, 255)), PixelsPerDip)
        { TextAlignment = TextAlignment.Center };
        var y = at.Y + (above ? -1 : 1) * (12 + 7 * lines);
        // with Center alignment and no max width, x is the centre of the text block
        dc.DrawText(text, new Point(at.X, y - text.Height / 2));
    }

    /// One packet per trip along `route`; a fading tail, dimming as it travels.
    private static void DrawPacket(DrawingContext dc, Vec3[] route, double trip, bool outbound, Color color, Func<Vec3, Vec3> project)
    {
        var phase = trip - Math.Floor(trip);
        var eased = phase * phase * (3 - 2 * phase);
        var head = outbound ? eased : 1 - eased;
        var fade = 1 - 0.75 * phase;

        const int tail = 16;
        for (var k = tail; k >= 0; k--)
        {
            var f = head + (outbound ? -1 : 1) * k * 0.012;
            if (f is < 0 or > 1) continue;
            var p = project(PointOn(route, f));
            var at = new Point(p.X, p.Y);
            var falloff = 1 - (double)k / (tail + 1);
            var rad = 1.2 + 2.6 * falloff;
            dc.DrawEllipse(Solid(WithAlpha(color, fade * falloff * falloff)), null, at, rad, rad);
            if (k == 0)
            {
                var glow = new RadialGradientBrush(WithAlpha(color, 0.6 * fade), WithAlpha(color, 0));
                glow.Freeze();
                dc.DrawEllipse(glow, null, at, 10, 10);
            }
        }
    }

    /// A point `f` (0…1) of the way along a multi-hop route, each hop an arc lifted off the surface.
    private static Vec3 PointOn(Vec3[] route, double f)
    {
        var hops = route.Length - 1;
        var i = Math.Min((int)(f * hops), route.Length - 2);
        return Arc(route[i], route[i + 1], f * hops - i);
    }

    /// Slerp from a to b, raised up to 15% above the surface mid-way.
    private static Vec3 Arc(Vec3 a, Vec3 b, double f)
    {
        var omega = Math.Acos(Math.Min(1, a.X * b.X + a.Y * b.Y + a.Z * b.Z));
        double wa = Math.Sin((1 - f) * omega) / Math.Sin(omega), wb = Math.Sin(f * omega) / Math.Sin(omega);
        var lift = 1 + 0.15 * Math.Sin(Math.PI * f);
        return new Vec3((wa * a.X + wb * b.X) * lift, (wa * a.Y + wb * b.Y) * lift, (wa * a.Z + wb * b.Z) * lift);
    }

    private static Vec3 Unit(double lat, double lon)
    {
        double la = lat * Math.PI / 180, lo = lon * Math.PI / 180;
        return new Vec3(Math.Cos(la) * Math.Sin(lo), Math.Sin(la), Math.Cos(la) * Math.Cos(lo));
    }

    private static byte Alpha(double a) => (byte)Math.Round(255 * Math.Clamp(a, 0, 1));
    private static Color WithAlpha(Color c, double a) => Color.FromArgb(Alpha(a), c.R, c.G, c.B);

    private static SolidColorBrush Solid(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
