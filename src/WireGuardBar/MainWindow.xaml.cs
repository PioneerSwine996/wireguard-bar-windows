using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Win32;

namespace WireGuardBar;

/// Sidebar + detail on one dark background. The sidebar collapses to a narrow rail of numbered
/// tunnel badges with status lights instead of disappearing.
public partial class MainWindow : Window
{
    private string? editing;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = App.Store;
        Icon = TrayIcon.WindowIcon();
        SourceInitialized += (_, _) => DarkTitleBar();
        PreviewKeyDown += Editor_KeyDown;
        Closed += (_, _) => EditorText.Clear();
    }

    private static TunnelStore Store => App.Store;

    /// Dark caption in the window colour and no window border (Windows 11; Windows 10 gets the dark caption only).
    private void DarkTitleBar()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int on = 1, color = 0x00090909 /* COLORREF of #090909 */, none = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(hwnd, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref on, sizeof(int));
        DwmSetWindowAttribute(hwnd, 35 /* DWMWA_CAPTION_COLOR */, ref color, sizeof(int));
        DwmSetWindowAttribute(hwnd, 34 /* DWMWA_BORDER_COLOR: none */, ref none, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e) => Store.Compact = !Store.Compact;

    private void Rail_Click(object sender, RoutedEventArgs e) => Store.Selected = ((FrameworkElement)sender).DataContext as TunnelItem;

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "WireGuard configuration (*.conf)|*.conf|All files|*.*", Title = "Import Tunnel" };
        if (dialog.ShowDialog(this) != true) return;
        try { await Store.ImportAsync(dialog.FileName); }
        catch (Exception ex) when (ex is HelperFailure or System.IO.IOException or UnauthorizedAccessException) { Store.Error = ex.Message; }
    }

    private void Error_Click(object sender, MouseButtonEventArgs e) => Store.Error = null;

    // On-Demand

    private void AddNetwork_Click(object sender, RoutedEventArgs e) => AddTypedNetwork();

    private void NewNetwork_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) AddTypedNetwork();
    }

    private void AddTypedNetwork()
    {
        if (Store.Selected is not { } tunnel) return;
        Store.AddOnDemand(tunnel.Name, NewNetwork.Text);
        NewNetwork.Clear();
    }

    private void AddCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (Store.Selected is { } tunnel && Store.Ssid is { } ssid) Store.AddOnDemand(tunnel.Name, ssid);
    }

    private void RemoveNetwork_Click(object sender, RoutedEventArgs e)
    {
        if (Store.Selected is { } tunnel && ((FrameworkElement)sender).DataContext is string network) Store.RemoveOnDemand(tunnel.Name, network);
    }

    // Edit Configuration: Windows Hello first, then the editor inside this window.
    // The config text (with its private key) only lives while the editor is open.

    private async void EditConfig_Click(object sender, RoutedEventArgs e)
    {
        if (Store.Selected is not { } tunnel) return;
        try
        {
            if (!await Auth.VerifyAsync(this, $"Show the {tunnel.Name} configuration and its private key")) return;
            Activate();
            EditorText.Text = await Store.ReadConfigAsync(tunnel.Name);
        }
        catch (HelperFailure ex) { Store.Error = ex.Message; return; }
        editing = tunnel.Name;
        EditorTitle.Text = tunnel.Name + ".conf";
        EditorError.Text = "";
        Editor.Visibility = Visibility.Visible;
        EditorText.Focus();
        EditorText.CaretIndex = 0;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (editing is null) return;
        SaveButton.IsEnabled = false;
        try
        {
            await Store.WriteConfigAsync(editing, EditorText.Text);
            CloseEditor();
        }
        catch (HelperFailure ex) { EditorError.Text = ex.Message; }
        finally { SaveButton.IsEnabled = true; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => CloseEditor();

    private void CloseEditor()
    {
        Editor.Visibility = Visibility.Collapsed;
        EditorText.Clear();
        editing = null;
    }

    private void Editor_KeyDown(object sender, KeyEventArgs e)
    {
        if (Editor.Visibility != Visibility.Visible) return;
        if (e.Key == Key.Escape) { CloseEditor(); e.Handled = true; }
        else if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control) { Save_Click(this, e); e.Handled = true; }
    }
}
