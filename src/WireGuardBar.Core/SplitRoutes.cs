using System.Net;
using System.Net.Sockets;

namespace WireGuardBar.Core;

/// AllowedIPs for the "Route all traffic" toggle.
public static class SplitRoutes
{
    public const string All = "0.0.0.0/0, ::/0";

    /// Split tunnel: only the tunnel's own networks (from `Address`) plus its DNS servers.
    /// Host addresses are widened to their usual LAN size (/32 → /24, /128 → /64).
    /// "Address = 192.168.2.3/32" + "DNS = 192.168.2.1" → "192.168.2.0/24, 192.168.2.1/32".
    public static string FromConfigLines(string text)
    {
        var routes = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Split('=', 2);
            if (parts.Length != 2) continue;
            var isDns = parts[0].Trim().Equals("dns", StringComparison.OrdinalIgnoreCase);
            foreach (var raw in parts[1].Split(','))
            {
                var item = raw.Trim();
                var route = isDns ? HostRoute(item) : Network(item);
                if (route is not null && !routes.Contains(route)) routes.Add(route);
            }
        }
        return string.Join(", ", routes);
    }

    private static string? HostRoute(string ip) => Parse(ip) switch
    {
        { AddressFamily: AddressFamily.InterNetwork } a => $"{a}/32",
        { AddressFamily: AddressFamily.InterNetworkV6 } a => $"{a}/128",
        _ => null,   // DNS search domains aren't addresses
    };

    private static string? Network(string cidr)
    {
        var pieces = cidr.Split('/', 2);
        int? given = pieces.Length == 2 && int.TryParse(pieces[1], out var p) && p >= 0 ? p : null;
        var address = Parse(pieces[0]);
        if (address is null) return null;
        var v4 = address.AddressFamily == AddressFamily.InterNetwork;
        var max = v4 ? 32 : 128;
        var prefix = Math.Min(given is { } g ? (g == max ? (v4 ? 24 : 64) : g) : (v4 ? 24 : 64), max);
        var bytes = address.GetAddressBytes();
        for (var i = 0; i < bytes.Length; i++)
        {
            var bits = Math.Clamp(prefix - i * 8, 0, 8);
            bytes[i] &= bits == 0 ? (byte)0 : (byte)(0xFF << (8 - bits));
        }
        return $"{new IPAddress(bytes)}/{prefix}";
    }

    /// Strict like inet_pton: dotted-quad IPv4 or plain IPv6 (no "1" → 0.0.0.1, no scope ids).
    private static IPAddress? Parse(string s)
    {
        if (s.Contains('%') || !IPAddress.TryParse(s, out var a)) return null;
        if (a.AddressFamily == AddressFamily.InterNetwork && s.Count(c => c == '.') != 3) return null;
        return a;
    }
}
