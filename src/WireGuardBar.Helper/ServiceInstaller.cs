using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.ServiceProcess;

namespace WireGuardBar.Helper;

/// `install` / `uninstall`, run elevated by the setup .exe, its uninstaller, and the app's setup screen.
static class ServiceInstaller
{
    private const string Service = "wgbar-helper";

    public static int Install(string[] args)
    {
        try
        {
            var at = Array.IndexOf(args, "--user");
            var user = at > 0 && at + 1 < args.Length ? Sid(args[at + 1]) : SessionUser();
            var binPath = $"\"{Environment.ProcessPath}\" --user {user.Value}";

            if (Exists())
            {
                Stop();
                Sc("config", Service, "binPath=", binPath, "start=", "auto");
            }
            else
            {
                Sc("create", Service, "binPath=", binPath, "start=", "auto", "DisplayName=", "WireGuard Bar helper");
            }
            Sc("description", Service, "Starts and stops WireGuard tunnels for WireGuard Bar.");
            Sc("failure", Service, "reset=", "86400", "actions=", "restart/5000/restart/5000");

            using var controller = new ServiceController(Service);
            controller.Start();
            controller.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
            Console.WriteLine($"wgbar-helper installed for {user.Translate(typeof(NTAccount))}.");
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"wgbar-helper install failed: {e.Message}");
            return 1;
        }
    }

    public static int Uninstall()
    {
        try
        {
            // Stop tunnels started from our configs, then remove the service. Configs stay on disk.
            var wireguard = Path.Combine(Commands.WireGuardDir, "wireguard.exe");
            var configs = Path.Combine(AppContext.BaseDirectory, "Data", "Configurations");
            if (File.Exists(wireguard) && Directory.Exists(configs))
            {
                var running = ServiceController.GetServices().Select(s => s.ServiceName).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var name in Directory.EnumerateFiles(configs, "*.conf").Select(Path.GetFileNameWithoutExtension))
                {
                    if (running.Contains($"WireGuardTunnel${name}")) Run(wireguard, "/uninstalltunnelservice", name!);
                }
            }
            if (Exists())
            {
                Stop();
                Sc("delete", Service);
            }
            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"wgbar-helper uninstall failed: {e.Message}");
            return 1;
        }
    }

    private static bool Exists() => ServiceController.GetServices().Any(s => s.ServiceName == Service);

    private static void Stop()
    {
        using var controller = new ServiceController(Service);
        if (controller.Status == ServiceControllerStatus.Stopped) return;
        controller.Stop();
        controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
    }

    private static SecurityIdentifier Sid(string account) =>
        account.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)
            ? new SecurityIdentifier(account)
            : (SecurityIdentifier)new NTAccount(account).Translate(typeof(SecurityIdentifier));

    /// The account signed in to this Windows session, which is the person who will use the app even
    /// when the elevation prompt was answered with a separate administrator account.
    private static SecurityIdentifier SessionUser()
    {
        var session = Process.GetCurrentProcess().SessionId;
        var user = SessionString(session, WtsUserName);
        var domain = SessionString(session, WtsDomainName);
        if (string.IsNullOrEmpty(user)) throw new InvalidOperationException("No user is signed in to this session; pass --user ACCOUNT.");
        return Sid(string.IsNullOrEmpty(domain) ? user : $@"{domain}\{user}");
    }

    private static void Sc(params string[] args) => Run(Path.Combine(Environment.SystemDirectory, "sc.exe"), args);

    private static void Run(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"{Path.GetFileName(exe)} {string.Join(' ', args)}: {output.Trim()}");
    }

    private const int WtsUserName = 5, WtsDomainName = 7;

    private static string? SessionString(int session, int infoClass)
    {
        if (!WTSQuerySessionInformation(IntPtr.Zero, session, infoClass, out var buffer, out _)) return null;
        try { return Marshal.PtrToStringUni(buffer); }
        finally { WTSFreeMemory(buffer); }
    }

    [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WTSQuerySessionInformation(IntPtr server, int sessionId, int infoClass, out IntPtr buffer, out int bytes);

    [DllImport("wtsapi32.dll")]
    private static extern void WTSFreeMemory(IntPtr memory);
}
