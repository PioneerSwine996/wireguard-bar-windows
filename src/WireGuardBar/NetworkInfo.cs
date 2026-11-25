using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Windows.Networking.Connectivity;

namespace WireGuardBar;

public static class NetworkInfo
{
    /// Name of the connected Wi-Fi network, or null. Falls back to the connection profile's name
    /// (which Windows sets to the SSID) when the SSID itself is withheld for lack of location access.
    public static string? CurrentSsid()
    {
        try
        {
            foreach (var profile in Windows.Networking.Connectivity.NetworkInformation.GetConnectionProfiles())
            {
                if (!profile.IsWlanConnectionProfile || profile.GetNetworkConnectivityLevel() == NetworkConnectivityLevel.None) continue;
                var ssid = profile.WlanConnectionProfileDetails.GetConnectedSsid();
                return string.IsNullOrEmpty(ssid) ? profile.ProfileName : ssid;
            }
        }
        catch (Exception) { }   // WinRT throws COMExceptions when the WLAN service is off; treat as no Wi-Fi
        return null;
    }

    /// This PC's IPv4 address on the local network: an interface with a default gateway, preferring
    /// Wi-Fi, skipping WireGuard's own adapters.
    public static string? LocalIPv4() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        && !n.Description.Contains("WireGuard", StringComparison.OrdinalIgnoreCase))
            .Select(n => (n.NetworkInterfaceType, Props: n.GetIPProperties()))
            .Where(x => x.Props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
            .OrderByDescending(x => x.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
            .SelectMany(x => x.Props.UnicastAddresses)
            .Select(a => a.Address)
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString();
}
