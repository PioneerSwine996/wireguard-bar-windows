namespace WireGuardBar.Core;

public sealed record Peer(string? Endpoint, DateTimeOffset? LatestHandshake, long Received, long Sent);

public sealed record TunnelState(string Name, bool IsUp, IReadOnlyList<Peer> Peers)
{
    public long Received => Peers.Sum(p => p.Received);
    public long Sent => Peers.Sum(p => p.Sent);
    public DateTimeOffset? LatestHandshake => Peers.Max(p => p.LatestHandshake);
    public string? Endpoint => Peers.Select(p => p.Endpoint).FirstOrDefault(e => e is not null);

    /// WireGuard re-handshakes every 2 minutes while traffic flows, so a handshake in the
    /// last 3 minutes means the server is answering.
    public bool IsConnected(DateTimeOffset now) =>
        IsUp && LatestHandshake is { } h && now - h < TimeSpan.FromMinutes(3);
}

/// `Foreign`: running WireGuard interfaces with no config of ours, e.g. started from the
/// official WireGuard app.
public sealed record StatusReport(IReadOnlyList<TunnelState> Tunnels, IReadOnlyList<string> Foreign)
{
    /// Parses the helper's `status`: `config<TAB>name` lines, then `wg show all dump`
    /// (5 fields = an interface, 9 fields = a peer).
    public static StatusReport Parse(string status)
    {
        var names = new List<string>();
        var interfaces = new List<string>();
        var peers = new Dictionary<string, List<Peer>>();

        foreach (var raw in status.Split('\n'))
        {
            var f = raw.TrimEnd('\r').Split('\t');
            if (f is ["config", var name]) names.Add(name);
            else if (f.Length == 5) interfaces.Add(f[0]);
            else if (f.Length == 9)
            {
                var handshake = long.TryParse(f[5], out var s) && s > 0 ? DateTimeOffset.FromUnixTimeSeconds(s) : (DateTimeOffset?)null;
                if (!peers.TryGetValue(f[0], out var list)) peers[f[0]] = list = [];
                list.Add(new Peer(f[3] == "(none)" ? null : f[3], handshake,
                    long.TryParse(f[6], out var rx) ? rx : 0, long.TryParse(f[7], out var tx) ? tx : 0));
            }
        }

        var tunnels = names.Order(StringComparer.OrdinalIgnoreCase).Select(n =>
        {
            var up = interfaces.Contains(n);
            return new TunnelState(n, up, up && peers.TryGetValue(n, out var p) ? p : []);
        }).ToList();
        return new StatusReport(tunnels, interfaces.Where(i => !names.Contains(i)).ToList());
    }
}
