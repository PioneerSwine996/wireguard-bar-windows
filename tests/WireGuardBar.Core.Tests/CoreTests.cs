using WireGuardBar.Core;

public class StatusTests
{
    [Fact]
    public void ParsesConfigsInterfacesAndPeers()
    {
        var now = DateTimeOffset.UtcNow;
        var status = $"config\twork\nconfig\thome\nwork\t(hidden)\tPUB\t51820\toff\n" +
                     $"work\tPEER\t(hidden)\t1.2.3.4:51820\t0.0.0.0/0\t{now.ToUnixTimeSeconds() - 30}\t2048\t1024\t25\n" +
                     "Office\t(hidden)\tPUB2\t51821\toff\n";
        var report = StatusReport.Parse(status);

        Assert.Equal(["home", "work"], report.Tunnels.Select(t => t.Name));
        Assert.False(report.Tunnels[0].IsUp);
        var work = report.Tunnels[1];
        Assert.Equal("1.2.3.4:51820", work.Endpoint);
        Assert.Equal(2048, work.Received);
        Assert.Equal(1024, work.Sent);
        Assert.True(work.IsConnected(now));
        Assert.Equal(["Office"], report.Foreign);
    }

    [Fact]
    public void StaleOrMissingHandshakeIsNotConnected()
    {
        var t = StatusReport.Parse("config\twg0\r\nwg0\t(hidden)\tPUB\t0\toff\r\nwg0\tPEER\t(none)\t(none)\t10.0.0.0/8\t0\t0\t0\toff\r\n").Tunnels[0];
        Assert.True(t.IsUp);
        Assert.Null(t.LatestHandshake);
        Assert.Null(t.Endpoint);
        Assert.False(t.IsConnected(DateTimeOffset.UtcNow));
    }
}

public class NameTests
{
    [Theory]
    [InlineData("Home (No-PiHole).conf", "Home-No-PiHole")]
    [InlineData("wg0.conf", "wg0")]
    [InlineData("my office vpn server in frankfurt.conf", "my-office-vpn-server-in-frankfur")]
    [InlineData("(((.conf", "tunnel")]
    [InlineData("CON.conf", "tunnel")]
    public void ImportedFileNamesBecomeValidTunnelNames(string file, string expected) =>
        Assert.Equal(expected, TunnelNames.FromFileName(file));

    [Theory]
    [InlineData("wg0", true)]
    [InlineData("..", false)]
    [InlineData("a.", false)]
    [InlineData("nul", false)]
    [InlineData("com1.x", false)]
    [InlineData("..\\evil", false)]
    [InlineData("a/b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("123456789012345678901234567890123", false)]
    public void ValidatesNames(string? name, bool ok) => Assert.Equal(ok, TunnelNames.IsValid(name));
}

public class ConfigTextTests
{
    const string Config = "[Interface]\nPrivateKey = SECRET\nAddress = 10.0.0.2/32\nDNS = 10.0.0.1\n\n[Peer]\nPublicKey = P\nPresharedKey = PSK\nallowedips = 0.0.0.0/0\nEndpoint = h:1\n";

    [Fact]
    public void SplitTunnelRoutesComeFromAddressAndDns()
    {
        var lines = "Address = 192.168.2.3/32, fd17:dd3a:bc90:a64::3/128\nDNS = 192.168.2.1, fd17:dd3a:bc90:a64::1, home.lan\n";
        Assert.Equal("192.168.2.0/24, fd17:dd3a:bc90:a64::/64, 192.168.2.1/32, fd17:dd3a:bc90:a64::1/128", SplitRoutes.FromConfigLines(lines));
        Assert.Equal("10.8.0.0/16", SplitRoutes.FromConfigLines("Address = 10.8.0.2/16"));
        Assert.Equal("10.8.0.0/24", SplitRoutes.FromConfigLines("Address = 10.8.0.2"));
        Assert.Equal("1.1.1.1/32", SplitRoutes.FromConfigLines("DNS = 1.1.1.1\nDNS = 1.1.1.1"));
        Assert.Equal("", SplitRoutes.FromConfigLines("DNS = 1"));
    }

    [Fact]
    public void OverrideReplacesOnlyAllowedIps()
    {
        var copy = ConfigText.WithAllowedIps(Config, "10.0.0.0/24");
        Assert.Contains("AllowedIPs = 10.0.0.0/24\n", copy);
        Assert.Equal(Config.Replace("allowedips = 0.0.0.0/0", "AllowedIPs = 10.0.0.0/24"), copy);
    }

    [Fact]
    public void AddressLinesNeverIncludeKeys()
    {
        var lines = ConfigText.AddressLines(Config.Replace("\n", "\r\n"));
        Assert.Equal("Address = 10.0.0.2/32\nDNS = 10.0.0.1", lines);
    }

    [Theory]
    [InlineData("10.0.0.0/24, ::/0", true)]
    [InlineData("10.0.0.0/24\nPostUp = calc.exe", false)]
    [InlineData("x", false)]
    [InlineData("", false)]
    public void ValidatesAllowedIps(string s, bool ok) => Assert.Equal(ok, ConfigText.IsValidAllowedIps(s));

    [Fact]
    public void HidesPrivateAndPresharedKeys()
    {
        var dump = "wg0\tPRIV\tPUB\t51820\toff\nwg0\tPEER\tPSK\t1.2.3.4:1\t0.0.0.0/0\t0\t0\t0\toff";
        var hidden = ConfigText.HideKeys(dump);
        Assert.DoesNotContain("PRIV", hidden);
        Assert.DoesNotContain("PSK", hidden);
        Assert.Contains("PUB", hidden);
        Assert.Contains("PEER", hidden);
    }
}

public class OrbTests
{
    /// Golden vectors for `connecting` at 20 px from thinking-orbs spec/orbs-golden.json (same as the macOS tests).
    public static readonly TheoryData<double, double[]> Golden = new()
    {
        { 0.6, [12.345722, 12.323515, -0.910862, 0.652664, 0.05, 0.522284, 11.371738, 6.195471, -0.862807, 0.45114, 0.519132, 1, 9.069407, 14.028289, -0.856107, 0.546848, 0.517624, 1, 16.216325, 9.675124, -0.628139, 0.646447, 0.466331, 1, 2.492066, 10.875654, -0.327494, 0.480312, 0.398686, 1, 15.207149, 5.107705, 0.449844, 0.960455, 0.223785, 1, 16.719838, 11.196911, 0.521584, 0.925632, 0.207644, 1, 10.319238, 16.19023, 0.632198, 0.650227, 0.182755, 1, 5.573026, 8.166562, 0.800785, 0.816565, 0.144823, 1] },
        { 1.7, [9.463514, 5.457987, -0.820464, 0.584018, 0.509604, 1, 8.214309, 14.612835, -0.785942, 0.554569, 0.501837, 1, 15.524244, 10.164237, -0.723012, 0.472595, 0.487678, 1, 2.194715, 11.595745, -0.091098, 0.759562, 0.345497, 1, 15.463637, 4.83865, 0.342535, 0.990356, 0.05, 0.835634, 15.281435, 4.807364, 0.377967, 0.926703, 0.239957, 1, 16.686373, 11.048203, 0.533176, 0.65117, 0.205035, 1, 10.705135, 16.367863, 0.598867, 0.793287, 0.190255, 1, 6.063945, 7.72457, 0.822818, 0.697856, 0.139866, 1] },
        { 3.3, [14.1764, 11.554531, -0.830485, 0.387323, 0.511859, 1, 7.305879, 15.525023, -0.640017, 0.387178, 0.469004, 1, 6.825043, 4.581621, -0.619486, 0.448501, 0.464384, 1, 15.693894, 4.534095, 0.163145, 0.942025, 0.05, 0.790786, 2.573333, 12.50122, 0.201111, 0.795436, 0.27975, 1, 15.201586, 4.339606, 0.276797, 0.573135, 0.262721, 1, 11.282813, 16.663498, 0.529625, 1.02264, 0.205834, 1, 16.143204, 10.88808, 0.630877, 0.891334, 0.183053, 1, 6.778048, 7.016922, 0.835915, 1.110366, 0.136919, 1] },
        { 5.1, [13.041535, 12.026498, -0.889543, 0.558377, 0.525147, 1, 7.187483, 16.264513, -0.513043, 0.658679, 0.440435, 1, 5.636161, 4.274419, -0.436152, 0.567248, 0.423134, 1, 14.875144, 3.712069, 0.104201, 0.851987, 0.301555, 1, 3.067233, 12.98179, 0.331796, 0.624425, 0.250346, 1, 3.012796, 11.77191, 0.433721, 1.014923, 0.05, 0.85843, 12.666422, 16.589578, 0.458729, 0.614063, 0.221786, 1, 16.864359, 10.396186, 0.511181, 0.919416, 0.209984, 1, 8.533603, 7.16428, 0.916927, 0.84121, 0.118691, 1] },
    };

    [Theory, MemberData(nameof(Golden))]
    public void ConnectingOrbMatchesGolden(double t, double[] want)
    {
        var frame = OrbWeb.Frame(20, t);
        var got = frame.Dots.SelectMany(d => new[] { d.X, d.Y, d.Z, d.R, d.White, d.A }).ToArray();
        Assert.Empty(frame.Lines);
        Assert.Equal(want.Length, got.Length);
        for (var i = 0; i < want.Length; i++) Assert.True(Math.Abs(got[i] - want[i]) < 1e-4, $"t={t} [{i}]: got {got[i]}, want {want[i]}");
    }
}

public class ProtocolTests
{
    [Fact]
    public async Task RoundTripsAndRejectsOversizedFrames()
    {
        var ms = new MemoryStream();
        await Protocol.WriteAsync(ms, new HelperRequest("up", "wg0", "10.0.0.0/24"), default);
        ms.Position = 0;
        Assert.Equal(new HelperRequest("up", "wg0", "10.0.0.0/24"), await Protocol.ReadAsync<HelperRequest>(ms, default));

        var bad = new MemoryStream([0xFF, 0xFF, 0xFF, 0x7F]);
        await Assert.ThrowsAsync<InvalidDataException>(() => Protocol.ReadAsync<HelperRequest>(bad, default));
    }
}
