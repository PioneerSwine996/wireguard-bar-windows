using System.Text.RegularExpressions;

namespace WireGuardBar.Core;

/// WireGuard for Windows names the tunnel service and interface after the .conf file:
/// 1-32 of [A-Za-z0-9_=+.-], and not a reserved DOS device name.
public static partial class TunnelNames
{
    public const int MaxLength = 32;

    [GeneratedRegex("^[a-zA-Z0-9_=+.-]{1,32}$")]
    private static partial Regex Allowed();

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public static bool IsValid(string? name) =>
        name is not null
        && Allowed().IsMatch(name)
        && !name.EndsWith('.')                 // Windows strips trailing dots from file names
        && !Reserved.Contains(name.Split('.')[0]);

    /// "Home (No-PiHole).conf" → "Home-No-PiHole".
    public static string FromFileName(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var words = Regex.Split(stem, "[^a-zA-Z0-9_=+.]+").Where(w => w.Length > 0);
        var name = string.Join('-', words);
        if (name.Length > MaxLength) name = name[..MaxLength];
        name = name.TrimEnd('.', '-');
        return IsValid(name) ? name : "tunnel";
    }
}
