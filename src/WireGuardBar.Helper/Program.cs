using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using WireGuardBar.Helper;

// wgbar-helper: the only part of WireGuard Bar that runs elevated (as LocalSystem).
// Installed as the "wgbar-helper" service by install.ps1 with the one account allowed to talk to it:
//   wgbar-helper.exe --user S-1-5-21-...
// `--console` runs it in the foreground for debugging (from an elevated prompt).

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

sealed class HelperService(SecurityIdentifier user, string dataDir) : ServiceBase
{
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
                EventLog.WriteEntry("Application", $"wgbar-helper stopped: {e}", EventLogEntryType.Error);
                Stop();
            }
        });
    }

    protected override void OnStop()
    {
        stop.Cancel();
        running?.Wait(TimeSpan.FromSeconds(10));
    }
}
