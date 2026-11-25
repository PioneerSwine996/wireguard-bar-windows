using System.ComponentModel;
using System.Runtime.CompilerServices;
using WireGuardBar.Core;

namespace WireGuardBar;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Raise(name);
    }

    /// null refreshes every binding on this object.
    protected void Raise(string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// One tunnel as the UI sees it. Kept across refreshes (updated in place) so list selection sticks.
public sealed class TunnelItem(TunnelStore store, string name) : Observable
{
    private TunnelState state = new(name, false, []);
    private int number;
    private bool busy, isSelected;

    public string Name { get; } = name;
    public int Number { get => number; set => Set(ref number, value); }
    public bool Busy { get => busy; set { if (busy != value) { busy = value; Raise(); } } }
    public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }

    public bool IsUp => state.IsUp;
    public bool IsConnected => state.IsConnected(DateTimeOffset.UtcNow);
    /// Green = connected, orange = up but the server isn't answering, gray = off.
    public string Status => IsConnected ? "connected" : IsUp ? "up" : "off";

    public string ActionText => IsUp ? "Disconnect" : "Connect";
    public string ConnectedText => !IsUp ? "No" : IsConnected ? "Yes" : "No recent handshake";
    public string SentText => IsUp ? Format.Bytes(state.Sent) : "—";
    public string ReceivedText => IsUp ? Format.Bytes(state.Received) : "—";
    public string HandshakeText => IsUp ? Format.Handshake(state.LatestHandshake) : "—";
    public string EndpointText => state.Endpoint ?? "—";

    /// "1.2.3.4:51820" → "1.2.3.4", "[fd00::1]:51820" → "fd00::1".
    public string ServerLabel => state.Endpoint is { } e && e.LastIndexOf(':') is > 0 and var i
        ? e[..i].Trim('[', ']')
        : "Server";

    // Switches: getters show the real state; setters ask the store, which refreshes when done.
    public bool EnabledSwitch { get => IsUp; set => _ = store.SetEnabledAsync(this, value); }
    public bool RouteAll { get => store.RouteAllFor(Name) ?? true; set => _ = store.SetRouteAllAsync(this, value); }
    public bool OnDemandEnabled { get => store.IsOnDemandEnabled(Name); set => store.SetOnDemandEnabled(Name, value); }

    public IReadOnlyList<string> Networks => store.NetworksFor(Name);
    public string AddCurrentText => store.Ssid is { } s ? $"Add Current Network ({s})" : "Not connected to Wi-Fi";
    public bool CanAddCurrent => store.Ssid is { } s && !Networks.Contains(s);

    public void Update(TunnelState fresh)
    {
        state = fresh;
        Raise();
    }

    public void Changed() => Raise();
}
