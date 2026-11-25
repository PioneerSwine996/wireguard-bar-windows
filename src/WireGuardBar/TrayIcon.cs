using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace WireGuardBar;

public enum ShieldKind { Off, Locked, Split }

/// Tray icon. Off: shield with a slash. On: shield with a closed lock, or an open lock while the
/// active tunnel isn't routing all traffic (split tunnel). White on dark taskbars, black on light.
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon icon = new() { Text = "WireGuard", Visible = true };
    private readonly TunnelStore store;
    private readonly Dictionary<(ShieldKind, bool), Icon> cache = [];

    public TrayIcon(TunnelStore store, Action click)
    {
        this.store = store;
        icon.MouseUp += (_, _) => click();
        store.Changed += Update;
        SystemEvents.UserPreferenceChanged += ThemeChanged;
        Update();
    }

    private void ThemeChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General) Update();   // light/dark variants are both cached
    }

    private void Update()
    {
        var active = store.Active;
        var kind = active is null ? ShieldKind.Off : store.RouteAllFor(active.Name) == false ? ShieldKind.Split : ShieldKind.Locked;
        var light = LightTaskbar();
        if (!cache.TryGetValue((kind, light), out var image))
            cache[(kind, light)] = image = ToIcon(Draw(kind, light ? Color.Black : Color.White, Forms.SystemInformation.SmallIconSize.Width));
        icon.Icon = image;
        icon.Text = active is null ? "WireGuard" : kind == ShieldKind.Split ? $"WireGuard: {active.Name} (split)" : $"WireGuard: {active.Name}";
    }

    private static bool LightTaskbar() =>
        Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is 1;

    /// The Manage window's icon: a blue locked shield, readable on any taskbar.
    public static BitmapSource WindowIcon()
    {
        using var bitmap = Draw(ShieldKind.Locked, Color.FromArgb(0x0A, 0x84, 0xFF), 64);
        var handle = bitmap.GetHicon();   // an HICON keeps the alpha channel; GetHbitmap would drop it
        try { return Imaging.CreateBitmapSourceFromHIcon(handle, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
        finally { DestroyIcon(handle); }
    }

    /// Drawn on a 16-unit grid and scaled, so it stays sharp at any tray size.
    public static Bitmap Draw(ShieldKind kind, Color color, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.ScaleTransform(size / 16f, size / 16f);

        using var shield = new GraphicsPath();
        shield.AddLine(8f, 1f, 14f, 3.3f);
        shield.AddLine(14f, 3.3f, 14f, 8f);
        shield.AddBezier(14f, 8f, 14f, 11.6f, 11.4f, 14f, 8f, 15.2f);
        shield.AddBezier(8f, 15.2f, 4.6f, 14f, 2f, 11.6f, 2f, 8f);
        shield.AddLine(2f, 8f, 2f, 3.3f);
        shield.CloseFigure();

        if (kind == ShieldKind.Off)
        {
            using var pen = new Pen(color, 1.3f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawPath(pen, shield);
            g.DrawLine(pen, 2.2f, 1.6f, 13.8f, 14.6f);
            return bitmap;
        }

        using (var fill = new SolidBrush(color)) g.FillPath(fill, shield);
        // cut the lock out of the filled shield
        g.CompositingMode = CompositingMode.SourceCopy;
        using var clear = new SolidBrush(Color.Transparent);
        using var shackle = new Pen(Color.Transparent, 1.25f);
        g.FillRectangle(clear, 5.3f, 7.6f, 5.4f, 4.2f);
        if (kind == ShieldKind.Locked)
        {
            g.DrawArc(shackle, 6.2f, 4.6f, 3.6f, 3.6f, 180, 180);
            g.DrawLine(shackle, 6.2f, 6.4f, 6.2f, 7.8f);
            g.DrawLine(shackle, 9.8f, 6.4f, 9.8f, 7.8f);
        }
        else
        {
            g.DrawArc(shackle, 6.2f, 3.4f, 3.6f, 3.6f, 180, 180);
            g.DrawLine(shackle, 6.2f, 5.2f, 6.2f, 5.9f);   // open: left leg lifted clear of the body
            g.DrawLine(shackle, 9.8f, 5.2f, 9.8f, 7.8f);
        }
        return bitmap;
    }

    private static Icon ToIcon(Bitmap bitmap)
    {
        using (bitmap)
        {
            var handle = bitmap.GetHicon();
            try { using var temp = Icon.FromHandle(handle); return (Icon)temp.Clone(); }
            finally { DestroyIcon(handle); }
        }
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= ThemeChanged;
        icon.Visible = false;
        icon.Dispose();
        foreach (var i in cache.Values) i.Dispose();
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);
}
