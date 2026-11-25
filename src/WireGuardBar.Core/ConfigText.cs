using System.Text.RegularExpressions;

namespace WireGuardBar.Core;

/// Text operations on configs and `wg show all dump` output. The helper runs these as SYSTEM,
/// so everything here treats its input as untrusted.
public static partial class ConfigText
{
    public const int MaxConfigBytes = 64 * 1024;

    [GeneratedRegex(@"^[0-9a-fA-F:./, ]+$")]
    private static partial Regex AllowedIpsChars();

    [GeneratedRegex(@"^[ \t]*AllowedIPs[ \t]*=.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex AllowedIpsLine();

    [GeneratedRegex(@"^[ \t]*(Address|DNS)[ \t]*=.*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex AddressOrDnsLine();

    public static bool IsValidAllowedIps(string? s) =>
        s is { Length: > 0 and <= 512 } && AllowedIpsChars().IsMatch(s);

    public static bool LooksLikeConfig(string text) =>
        text.Contains("[Interface]", StringComparison.OrdinalIgnoreCase);

    /// Copy of the config with every peer's AllowedIPs replaced (the original is never edited).
    public static string WithAllowedIps(string config, string allowedIps) =>
        AllowedIpsLine().Replace(config, _ => $"AllowedIPs = {allowedIps}");

    /// Address and DNS lines only (never keys), for computing split-tunnel routes.
    public static string AddressLines(string config) =>
        string.Join('\n', AddressOrDnsLine().Matches(config).Select(m => m.Value.TrimEnd('\r')));

    /// Hides the interface private key (5-field lines) and peer preshared keys (9-field lines).
    public static string HideKeys(string dump) =>
        string.Join('\n', dump.Split('\n').Select(line =>
        {
            var f = line.TrimEnd('\r').Split('\t');
            if (f.Length == 5) f[1] = "(hidden)";
            else if (f.Length == 9) f[2] = "(hidden)";
            return string.Join('\t', f);
        }));
}
