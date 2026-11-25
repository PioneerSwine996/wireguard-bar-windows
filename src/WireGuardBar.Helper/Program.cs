using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using WireGuardBar.Helper;

// wgbar-helper: the only part of WireGuard Bar that runs elevated (as LocalSystem).
//   wgbar-helper.exe install [--user ACCOUNT]   register/upgrade the service (elevated; run by the installer
//                                               and the app's setup screen)
//   wgbar-helper.exe uninstall                  stop our tunnels and remove the service (elevated)
//   wgbar-helper.exe --user S-1-5-21-...        what the service runs: only that account may use the pipe
// `--console` runs the service in the foreground for debugging (from an elevated prompt).

if (args is ["install", ..]) return ServiceInstaller.Install(args);
if (args is ["uninstall"]) return ServiceInstaller.Uninstall();

var userIndex = Array.IndexOf(args, "--user");
if (userIndex < 0 || userIndex + 1 >= args.Length)
{
    Console.Error.WriteLine("usage: wgbar-helper --user <SID> [--console]");
    return 2;
}
var user = new SecurityIdentifier(args[userIndex + 1]);
var dataDir = Path.Combine(AppContext.BaseDirectory, "Data");

if (args.Contains("--console"))
{
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
    await new PipeServer(user, new Commands(dataDir)).RunAsync(cts.Token);
    return 0;
}

ServiceBase.Run(new HelperService(user, dataDir));
return 0;

sealed class HelperService : ServiceBase
{
    private readonly SecurityIdentifier user;
    private readonly string dataDir;

    public HelperService(SecurityIdentifier user, string dataDir)
    {
        this.user = user;
        this.dataDir = dataDir;
        ServiceName = "wgbar-helper";
    }

    private readonly CancellationTokenSource stop = new();
    private Task? running;

    protected override void OnStart(string[] args)
    {
        running = Task.Run(async () =>
        {
            try { await new PipeServer(user, new Commands(dataDir)).RunAsync(stop.Token); }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                EventLog.WriteEntry(ServiceName, $"wgbar-helper stopped: {e}", EventLogEntryType.Error);
                // exit as a crash so the service's failure actions restart it
                // (a clean Stop() would report success and leave it stopped)
                Environment.Exit(1);
            }
        });
    }

    protected override void OnStop()
    {
        stop.Cancel();
        running?.Wait(TimeSpan.FromSeconds(10));
    }
}
