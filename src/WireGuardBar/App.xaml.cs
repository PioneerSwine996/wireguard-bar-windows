using System.Windows;

namespace WireGuardBar;

/// Tray app: one instance per user. Launching it again (e.g. from the Start menu) opens Manage in
/// the running instance. `--background` (used at sign-in) starts with only the tray icon.
public partial class App : Application
{
    public static TunnelStore Store { get; private set; } = null!;

    private Mutex? single;
    private EventWaitHandle? showSignal;
    private TrayIcon? tray;
    private Flyout? flyout;
    private MainWindow? manage;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        single = new Mutex(true, @"Local\WireGuardBar", out var first);
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WireGuardBar.Manage");
        if (!first)
        {
            showSignal.Set();
            Shutdown();
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Dispatcher.BeginInvoke(ShowManage), null, -1, executeOnlyOnce: false);

        Store = new TunnelStore();
        flyout = new Flyout();
        tray = new TrayIcon(Store, flyout.Toggle);
        if (!e.Args.Contains("--background")) ShowManage();
    }

    public static void ShowManage()
    {
        var app = (App)Current;
        if (app.manage is null)
        {
            app.manage = new MainWindow();
            app.manage.Closed += (_, _) => app.manage = null;
        }
        app.manage.Show();
        if (app.manage.WindowState == WindowState.Minimized) app.manage.WindowState = WindowState.Normal;
        app.manage.Activate();
    }

    public static void Quit()
    {
        var app = (App)Current;
        app.tray?.Dispose();
        app.Shutdown();
    }
}
