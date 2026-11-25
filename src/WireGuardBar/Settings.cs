using System.IO;
using System.Text.Json;

namespace WireGuardBar;

/// Per-user preferences in %APPDATA%\WireGuardBar\settings.json. Never holds keys or configs.
public sealed class Settings
{
    /// On-demand Wi-Fi names per tunnel name.
    public Dictionary<string, List<string>> OnDemand { get; set; } = [];
    /// On-Demand switch per tunnel name; the network list is kept while it's off.
    public Dictionary<string, bool> OnDemandEnabled { get; set; } = [];
    /// "Route all traffic" per tunnel name. Missing = run the config's own AllowedIPs untouched.
    public Dictionary<string, bool> RouteAll { get; set; } = [];
    public bool SidebarCompact { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WireGuardBar", "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, FilePath, overwrite: true);
    }
}
