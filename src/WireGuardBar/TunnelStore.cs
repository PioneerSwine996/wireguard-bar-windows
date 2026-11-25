using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using WireGuardBar.Core;

namespace WireGuardBar;

/// Everything the tray flyout and Manage window show. Lives on the UI thread; helper calls are async.
public sealed class TunnelStore : Observable
{
    /// Shortest time the loading orb stays up for a connect/disconnect, so it reads as feedback.
    private static readonly TimeSpan MinimumBusy = TimeSpan.FromSeconds(1.5);

    private readonly Settings settings = Settings.Load();
    private readonly DispatcherTimer timer;
    private Task? refreshing;
    private string? error, conflict, ssid, localIp;
    private TunnelItem? selected;

    private bool ssidRead;          // false = re-check on-demand against the current network
    private string? lastSsid;
    private string? autoConnected;  // tunnel that on-demand turned on

    public ObservableCollection<TunnelItem> Tunnels { get; } = [];
    public string? Error { get => error; set => Set(ref error, value); }
    /// A tunnel running that isn't ours, e.g. from the official WireGuard app.
    public string? Conflict { get => conflict; private set => Set(ref conflict, value); }
    public string? ConflictText => Conflict is { } c ? $"The WireGuard app has \"{c}\" connected. Turn it off there." : null;
    public string? Ssid { get => ssid; private set => Set(ref ssid, value); }
    public string NetworkLabel => string.Join("\n", new[] { ssid ?? "Local network", localIp }.OfType<string>());
    public TunnelItem? Active => Tunnels.FirstOrDefault(t => t.IsUp);

    public TunnelItem? Selected
    {
        get => selected;
        set
        {
            if (selected is not null) selected.IsSelected = false;
            Set(ref selected, value);
            if (value is not null) value.IsSelected = true;
        }
    }

    public bool Compact
    {
        get => settings.SidebarCompact;
        set { settings.SidebarCompact = value; settings.Save(); Raise(); }
    }

    /// After every refresh and every change that affects the tray icon.
    public event Action? Changed;

    public TunnelStore()
    {
        // ponytail: fixed 3 s poll (one pipe call each); pause while no UI is visible if it ever matters.
        timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += async (_, _) => { await RefreshAsync(); await ApplyOnDemandAsync(); };
        timer.Start();
        _ = RefreshAsync();
    }

    /// Joins a refresh already in flight instead of starting a second one.
    public Task RefreshAsync() => refreshing ??= RefreshCoreAsync();

    private async Task RefreshCoreAsync()
    {
        try
        {
            try
            {
                var report = StatusReport.Parse(await HelperClient.RunAsync("status"));
                Merge(report.Tunnels);
                Conflict = report.Foreign.FirstOrDefault();
            }
            catch (HelperFailure e) { Error = e.Message; }

            (Ssid, localIp) = await Task.Run(() => (NetworkInfo.CurrentSsid(), NetworkInfo.LocalIPv4()));
            Raise(nameof(NetworkLabel));
            Raise(nameof(ConflictText));
            foreach (var t in Tunnels) t.Changed();   // handshake ages, Wi-Fi name
            if (Selected is null || !Tunnels.Contains(Selected)) Selected = Active ?? Tunnels.FirstOrDefault();
            Changed?.Invoke();
        }
        finally { refreshing = null; }
    }

    /// Updates items in place, in sorted order, so bindings and selection survive.
    private void Merge(IReadOnlyList<TunnelState> states)
    {
        for (var i = 0; i < states.Count; i++)
        {
            var at = IndexOf(states[i].Name);
            if (at < 0) Tunnels.Insert(i, new TunnelItem(this, states[i].Name));
            else if (at != i) Tunnels.Move(at, i);
            Tunnels[i].Update(states[i]);
            Tunnels[i].Number = i + 1;
        }
        while (Tunnels.Count > states.Count) Tunnels.RemoveAt(Tunnels.Count - 1);
    }

    private int IndexOf(string name)
    {
        for (var i = 0; i < Tunnels.Count; i++) if (Tunnels[i].Name == name) return i;
        return -1;
    }

    public async Task SetEnabledAsync(TunnelItem tunnel, bool on)
    {
        // Only one tunnel at a time: turning one on first turns the others off.
        var others = on ? Tunnels.Where(t => t.IsUp && t != tunnel).ToList() : [];
        var involved = others.Append(tunnel).ToList();
        if (involved.Any(t => t.Busy)) return;
        involved.ForEach(t => t.Busy = true);
        var started = Stopwatch.GetTimestamp();
        try
        {
            foreach (var other in others) await HelperClient.RunAsync("down", other.Name);
            if (on) await HelperClient.RunAsync("up", tunnel.Name, await AllowedIpsAsync(tunnel.Name));
            else await HelperClient.RunAsync("down", tunnel.Name);
        }
        catch (HelperFailure e) { Error = e.Message; }
        var remaining = MinimumBusy - Stopwatch.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
        await RefreshAsync();   // before Busy clears, so the button flips straight to the new state
        involved.ForEach(t => t.Busy = false);
    }

    /// AllowedIPs override for "Route all traffic", or null to use the config as written.
    private async Task<string?> AllowedIpsAsync(string name)
    {
        if (RouteAllFor(name) is not { } all) return null;
        if (all) return SplitRoutes.All;
        var routes = SplitRoutes.FromConfigLines(await HelperClient.RunAsync("addresses", name));
        return routes.Length > 0 ? routes : throw new HelperFailure($"{name} has no Address to build a split tunnel from.");
    }

    public bool? RouteAllFor(string name) => settings.RouteAll.TryGetValue(name, out var v) ? v : null;

    /// Changes the toggle; a connected tunnel reconnects so the new routes apply.
    public async Task SetRouteAllAsync(TunnelItem tunnel, bool on)
    {
        settings.RouteAll[tunnel.Name] = on;
        settings.Save();
        tunnel.Changed();
        Changed?.Invoke();
        if (!tunnel.IsUp) return;
        await SetEnabledAsync(tunnel, false);
        await SetEnabledAsync(tunnel, true);
    }

    // On-Demand

    public IReadOnlyList<string> NetworksFor(string tunnel) =>
        settings.OnDemand.TryGetValue(tunnel, out var list) ? list : [];

    /// Defaults to on for tunnels that already have networks.
    public bool IsOnDemandEnabled(string tunnel) =>
        settings.OnDemandEnabled.TryGetValue(tunnel, out var on) ? on : NetworksFor(tunnel).Count > 0;

    public void SetOnDemandEnabled(string tunnel, bool on)
    {
        settings.OnDemandEnabled[tunnel] = on;
        settings.Save();
        if (on) ssidRead = false;                                  // check the current network right away
        else if (autoConnected == tunnel) autoConnected = null;    // stays connected; just won't auto-disconnect
        Find(tunnel)?.Changed();
    }

    public void AddOnDemand(string tunnel, string network)
    {
        network = network.Trim();
        if (network.Length == 0 || NetworksFor(tunnel).Contains(network)) return;
        if (!settings.OnDemand.TryGetValue(tunnel, out var list)) settings.OnDemand[tunnel] = list = [];
        list.Add(network);
        settings.Save();
        ssidRead = false;
        Find(tunnel)?.Changed();
    }

    public void RemoveOnDemand(string tunnel, string network)
    {
        if (!settings.OnDemand.TryGetValue(tunnel, out var list)) return;
        list.Remove(network);
        if (list.Count == 0) settings.OnDemand.Remove(tunnel);
        settings.Save();
        Find(tunnel)?.Changed();
    }

    /// Acts only when the Wi-Fi network changes, so a manual connect/disconnect sticks until then.
    private async Task ApplyOnDemandAsync()
    {
        if (Tunnels.Any(t => t.Busy) || (ssidRead && lastSsid == Ssid)) return;
        ssidRead = true;
        lastSsid = Ssid;
        var target = Ssid is { } s ? Tunnels.FirstOrDefault(t => IsOnDemandEnabled(t.Name) && NetworksFor(t.Name).Contains(s)) : null;
        if (target is not null)
        {
            if (target.IsUp) return;
            autoConnected = target.Name;
            await SetEnabledAsync(target, true);
        }
        else if (autoConnected is { } name && Find(name) is { IsUp: true } tunnel)
        {
            autoConnected = null;
            await SetEnabledAsync(tunnel, false);
        }
    }

    private TunnelItem? Find(string name) => Tunnels.FirstOrDefault(t => t.Name == name);

    // Configs

    public Task<string> ReadConfigAsync(string name) => HelperClient.RunAsync("read", name);

    public Task WriteConfigAsync(string name, string text) => HelperClient.RunAsync("write", name, input: text);

    /// Copies a .conf file into the helper's config folder as a new tunnel and selects it.
    public async Task ImportAsync(string path)
    {
        var text = await File.ReadAllTextAsync(path);
        if (!ConfigText.LooksLikeConfig(text))
            throw new HelperFailure($"{Path.GetFileName(path)} isn't a WireGuard configuration (no [Interface] section).");
        var name = TunnelNames.FromFileName(path);
        await HelperClient.RunAsync("import", name, input: text);
        await RefreshAsync();
        Selected = Find(name) ?? Selected;
    }
}
