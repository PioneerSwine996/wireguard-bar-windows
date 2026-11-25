namespace WireGuardBar;

public static class Format
{
    public static string Bytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} bytes";
        string[] units = ["KB", "MB", "GB", "TB"];
        double value = bytes;
        var i = -1;
        while (value >= 1024 && i < units.Length - 1) { value /= 1024; i++; }
        return value.ToString(i == 0 ? "0" : "0.#") + " " + units[i];
    }

    public static string Handshake(DateTimeOffset? date)
    {
        if (date is null) return "Never";
        var s = Math.Max(0, (int)(DateTimeOffset.UtcNow - date.Value).TotalSeconds);
        return s switch
        {
            < 60 => $"{s}s ago",
            < 3600 => $"{s / 60}m {s % 60}s ago",
            _ => $"{s / 3600}h {s % 3600 / 60}m ago",
        };
    }
}
