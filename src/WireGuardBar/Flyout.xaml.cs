using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace WireGuardBar;

/// The black, rounded panel that opens from the tray icon, placed next to it.
public partial class Flyout : Window
{
    private long hiddenAt;

    public Flyout()
    {
        InitializeComponent();
        DataContext = App.Store;
        Deactivated += (_, _) => HidePanel();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) HidePanel(); };
    }

    public void Toggle()
    {
        if (IsVisible) { HidePanel(); return; }
        // the tray click that just deactivated (and so closed) the panel shouldn't reopen it
        if (Environment.TickCount64 - hiddenAt < 300) return;
        Opacity = 0;
        Show();
        UpdateLayout();
        Place();
        Opacity = 1;
        Activate();
        _ = App.Store.RefreshAsync();
    }

    private void HidePanel()
    {
        if (!IsVisible) return;
        Hide();
        hiddenAt = Environment.TickCount64;
    }

    /// Next to the cursor (which is on the tray icon), inside the work area, on whichever edge
    /// the taskbar is.
    private void Place()
    {
        var cursor = Forms.Cursor.Position;
        var area = Forms.Screen.FromPoint(cursor).WorkingArea;   // physical pixels
        var dpi = VisualTreeHelper.GetDpi(this);
        double w = ActualWidth * dpi.DpiScaleX, h = ActualHeight * dpi.DpiScaleY, m = 12 * dpi.DpiScaleX;

        var x = cursor.X < area.Left ? area.Left + m
              : cursor.X > area.Right ? area.Right - w - m
              : Math.Clamp(cursor.X - w / 2, area.Left + m, area.Right - w - m);
        var y = cursor.Y < area.Top ? area.Top + m
              : Math.Clamp(cursor.Y - h - m, area.Top + m, area.Bottom - h - m);
        // ponytail: converts with this window's DPI; on mixed-DPI setups the first open can land slightly off
        Left = x / dpi.DpiScaleX;
        Top = y / dpi.DpiScaleY;
    }

    private void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is TunnelItem tunnel) _ = App.Store.SetEnabledAsync(tunnel, !tunnel.IsUp);
    }

    private void Manage_Click(object sender, RoutedEventArgs e)
    {
        HidePanel();
        App.ShowManage();
    }

    private void Quit_Click(object sender, RoutedEventArgs e) => App.Quit();

    private void Error_Click(object sender, MouseButtonEventArgs e) => App.Store.Error = null;
}
