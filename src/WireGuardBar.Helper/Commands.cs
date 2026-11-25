using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using WireGuardBar.Core;

namespace WireGuardBar.Helper;

class HelperException(string message) : Exception(message);

/// The helper's whole command set, mirroring the macOS wgbar-helper:
/// version | status | up NAME [ALLOWEDIPS] | down NAME | addresses NAME | read NAME | write NAME | import NAME.
/// Configs live in Data\Configurations under the install folder (SYSTEM and Administrators only);
/// per-connection copies with AllowedIPs overridden go to Data\Run.
sealed class Commands
{
    private const string Usage = "usage: version | status | addresses NAME | import NAME | up NAME [ALLOWEDIPS] | down NAME | read NAME | write NAME";
    public static readonly string WireGuardDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WireGuard");

    private readonly string confDir, runDir;
    private readonly SemaphoreSlim changes = new(1, 1);   // one up/down/write/import at a time

    public Commands(string dataDir)
    {
        confDir = Path.Combine(dataDir, "Configurations");
        runDir = Path.Combine(dataDir, "Run");
        Secure(dataDir);
        Secure(confDir);
        Secure(runDir);
    }

    public async Task<HelperResponse> HandleAsync(HelperRequest r, CancellationToken ct)
    {
        try
        {
            var output = r.Command switch
            {
                "version" => Protocol.HelperVersion.ToString(),
                "status" => await StatusAsync(ct),
                "addresses" => ConfigText.AddressLines(File.ReadAllText(Conf(r.Name))),
                "read" => File.ReadAllText(Conf(r.Name)),
                "up" => await Locked(() => UpAsync(r.Name, r.Argument, ct)),
                "down" => await Locked(() => DownAsync(r.Name, ct)),
                "write" => await Locked(() => Task.FromResult(Write(Conf(r.Name), r.Input, overwrite: true))),
                "import" => await Locked(() => Task.FromResult(Write(NewConf(r.Name), r.Input, overwrite: false))),
                _ => throw new HelperException(Usage),
            };
            return new HelperResponse(true, output);
        }
        catch (HelperException e) { return new HelperResponse(false, Error: e.Message); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new HelperResponse(false, Error: e.Message);
        }
    }

    private async Task<string> Locked(Func<Task<string>> action)
    {
        await changes.WaitAsync();
        try { return await action(); }
        finally { changes.Release(); }
    }

    private async Task<string> StatusAsync(CancellationToken ct)
    {
        var configs = Directory.EnumerateFiles(confDir, "*.conf")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(TunnelNames.IsValid)
            .Select(n => $"config\t{n}\n");
        var dump = await RunAsync("wg.exe", ["show", "all", "dump"], ct);
        return string.Concat(configs) + ConfigText.HideKeys(dump);
    }

    private async Task<string> UpAsync(string? name, string? allowedIps, CancellationToken ct)
    {
        var path = Conf(name);
        if (allowedIps is not null)
        {
            if (!ConfigText.IsValidAllowedIps(allowedIps)) throw new HelperException($"invalid AllowedIPs: {allowedIps}");
            var copy = Path.Combine(runDir, name + ".conf");   // same file name: it names the tunnel
            File.WriteAllText(copy, ConfigText.WithAllowedIps(File.ReadAllText(path), allowedIps));
            path = copy;
        }
        return await RunAsync("wireguard.exe", ["/installtunnelservice", path], ct);
    }

    private async Task<string> DownAsync(string? name, CancellationToken ct)
    {
        Conf(name);
        var output = await RunAsync("wireguard.exe", ["/uninstalltunnelservice", name!], ct);
        File.Delete(Path.Combine(runDir, name + ".conf"));
        return output;
    }

    /// Existing tunnels only.
    private string Conf(string? name)
    {
        var path = NewPath(name);
        if (!File.Exists(path)) throw new HelperException($"No tunnel named {name}.");
        return path;
    }

    /// New tunnels only; never overwrites.
    private string NewConf(string? name)
    {
        var path = NewPath(name);
        if (File.Exists(path)) throw new HelperException($"A tunnel named {name} already exists.");
        return path;
    }

    private string NewPath(string? name)
    {
        if (!TunnelNames.IsValid(name))
            throw new HelperException($"\"{name}\" can't be used as a tunnel name: WireGuard needs 1-{TunnelNames.MaxLength} letters, digits or _=+.- (no spaces or brackets).");
        return Path.Combine(confDir, name + ".conf");
    }

    private string Write(string path, string? text, bool overwrite)
    {
        if (text is null || !ConfigText.LooksLikeConfig(text)) throw new HelperException("That isn't a WireGuard configuration (no [Interface] section).");
        if (text.Length > ConfigText.MaxConfigBytes) throw new HelperException("That configuration is too large.");
        if (ConfigText.HasScripts(text))
            throw new HelperException("PreUp, PostUp, PreDown and PostDown aren't allowed: they would run as SYSTEM. Remove those lines.");
        var tmp = Path.Combine(confDir, $".wgbar-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(tmp, text);   // inherits the folder's SYSTEM/Administrators-only ACL
            File.Move(tmp, path, overwrite);
        }
        catch (IOException) when (!overwrite && File.Exists(path))
        {
            throw new HelperException($"A tunnel named {Path.GetFileNameWithoutExtension(path)} already exists.");
        }
        finally { File.Delete(tmp); }
        return "";
    }

    /// Runs a WireGuard for Windows tool by full path (never via PATH or a shell).
    private static async Task<string> RunAsync(string exe, string[] args, CancellationToken ct)
    {
        var path = Path.Combine(WireGuardDir, exe);
        if (!File.Exists(path)) throw new HelperException("WireGuard for Windows isn't installed. Get it from https://www.wireguard.com/install/");
        var psi = new ProcessStartInfo(path) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync(ct);
        var stderr = process.StandardError.ReadToEndAsync(ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(); throw new HelperException($"{exe} didn't finish within a minute."); }
        if (process.ExitCode != 0)
        {
            var text = ((await stderr).Trim() is { Length: > 0 } e ? e : (await stdout).Trim());
            throw new HelperException(text.Length > 0 ? text : $"{exe} exited with status {process.ExitCode}");
        }
        return await stdout;
    }

    /// SYSTEM and Administrators only, not inherited from Program Files (which lets Users read).
    /// Refuses junctions/symlinks so the folder can't be redirected.
    private static void Secure(string dir)
    {
        var info = Directory.CreateDirectory(dir);
        if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new InvalidOperationException($"{dir} is a link; refusing to use it.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
        {
            security.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(sid, null), FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        }
        info.SetAccessControl(security);
    }
}
