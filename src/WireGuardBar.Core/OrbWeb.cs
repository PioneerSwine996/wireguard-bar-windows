// C# port of the "connecting" thinking orb (mode `web`, 20 px preset) from
// https://github.com/Jakubantalik/thinking-orbs (src/engine/core.ts + web.ts).
// Verified against the repo's golden vectors in the macOS repo's Tests/ConnectingOrbTests.swift.
//
// MIT License
//
// Copyright (c) 2026 Jakub Antalik
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace WireGuardBar.Core;

public readonly record struct Vec3(double X, double Y, double Z);
public readonly record struct OrbDot(double X, double Y, double Z, double R, double White, double A);
public readonly record struct OrbLine(double X1, double Y1, double X2, double Y2, double White, double A, double W);
public sealed record OrbFrame(IReadOnlyList<OrbDot> Dots, IReadOnlyList<OrbLine> Lines);

/// The `web` mode with the `connecting-20` preset baked in.
/// Drawing: lines first, then dots (already z-sorted far→near); on a dark background the ink is
/// mirrored (1 - white) so near dots read bright.
public static class OrbWeb
{
    public const double Size = 20, Speed = 6.63;
    private const int NodeN = 8, Signals = 1;
    private const double Thr = 0.72, NodeR = 1.4 * 1.52, NodeRDepth = 1.8 * 1.52;
    private const double LineW = 0.8, RsPow = 0.6, RMin = 0.3;

    public static OrbFrame Frame(double size, double t)
    {
        var c = size / 2;
        var project = MakeProjector(t * 0.12, 0.32, c, c, c * 0.8);
        var rs = Math.Pow(size / 300, RsPow);

        var nodes = Enumerable.Range(0, NodeN).Select(i =>
        {
            var d = FibDir(i, NodeN);
            double x = d.X + 0.3 * (VNoise(i * 0.31 + 9, t * 0.24) - 0.5) * 2;
            double y = d.Y + 0.3 * (VNoise(i * 0.53 + 27, t * 0.21) - 0.5) * 2;
            double z = d.Z + 0.3 * (VNoise(i * 0.77 + 55, t * 0.27) - 0.5) * 2;
            var l = Math.Sqrt(x * x + y * y + z * z);
            return new Vec3(x / l, y / l, z / l);
        }).ToArray();

        var lines = new List<OrbLine>();
        for (var i = 0; i < NodeN; i++)
        for (var j = i + 1; j < NodeN; j++)
        {
            var (a, b) = (nodes[i], nodes[j]);
            var dist = Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
            if (dist >= Thr) continue;
            var p1 = project(a); var p2 = project(b);
            var depth = ((p1.Z + p2.Z) / 2 + 1) / 2;
            lines.Add(new OrbLine(p1.X, p1.Y, p2.X, p2.Y, 0.42, (1 - dist / Thr) * (0.3 + 0.55 * depth), Math.Max(0.6, LineW * rs)));
        }

        var dots = nodes.Select((n, i) =>
        {
            var p = project(n);
            var depth = (p.Z + 1) / 2;
            var pulse = 1 + 0.25 * Math.Sin(t * 1.4 + i * 2.7);
            return new OrbDot(p.X, p.Y, p.Z, (NodeR + NodeRDepth * depth) * pulse * rs, 0.55 - 0.45 * depth, 1);
        }).ToList();

        // signals: bright packets running between randomly re-picked node pairs
        for (var si = 0; si < Signals; si++)
        {
            double s = si;
            var seg = Math.Floor(t * 0.55 + s * 7.31);
            var a = (int)Math.Floor(HashD(seg, s * 3.1 + 1.7) * NodeN);
            var b = (int)Math.Floor(HashD(seg, s * 5.7 + 4.2) * NodeN);
            if (a == b) continue;
            var f = Frac(t * 0.55 + s * 7.31);
            double x = Lerp(nodes[a].X, nodes[b].X, f), y = Lerp(nodes[a].Y, nodes[b].Y, f), z = Lerp(nodes[a].Z, nodes[b].Z, f);
            var l = Math.Max(1e-6, Math.Sqrt(x * x + y * y + z * z));
            var p = project(new Vec3(x / l, y / l, z / l));
            var depth = (p.Z + 1) / 2;
            dots.Add(new OrbDot(p.X, p.Y, p.Z, (NodeR * 1.5 + NodeRDepth * depth) * rs, 0.05, 0.5 + 0.5 * depth));
        }

        // finalizeFrame: drop invisible marks, clamp radii, z-sort far→near
        var finalDots = dots.Where(d => d.A >= 0.02).Select(d => d with { R = Math.Max(RMin, d.R) }).OrderBy(d => d.Z).ToList();
        return new OrbFrame(finalDots, lines.Where(l => l.A >= 0.02).ToList());
    }

    // core.ts primitives

    public static Func<Vec3, Vec3> MakeProjector(double yaw, double tilt, double cx, double cy, double scale)
    {
        var (st, ct, sy, cyw) = (Math.Sin(tilt), Math.Cos(tilt), Math.Sin(yaw), Math.Cos(yaw));
        return v =>
        {
            var x1 = v.X * cyw + v.Z * sy;
            var z1 = -v.X * sy + v.Z * cyw;
            var y1 = v.Y * ct - z1 * st;
            var z2 = v.Y * st + z1 * ct;
            return new Vec3(cx + x1 * scale, cy - y1 * scale, z2);
        };
    }

    public static Vec3 FibDir(int i, int n)
    {
        var golden = Math.PI * (3 - Math.Sqrt(5));
        var y = 1 - 2 * (i + 0.5) / n;
        var rad = Math.Sqrt(1 - y * y);
        var a = i * golden;
        return new Vec3(rad * Math.Cos(a), y, rad * Math.Sin(a));
    }

    private static double VNoise(double x, double y)
    {
        double xi = Math.Floor(x), yi = Math.Floor(y);
        double fx = x - xi, fy = y - yi;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        double a = HashD(xi, yi), b = HashD(xi + 1, yi), c = HashD(xi, yi + 1), d = HashD(xi + 1, yi + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    private static double HashD(double a, double b)
    {
        var h = Math.Sin(a * 12.9898 + b * 78.233) * 43758.5453;
        return h - Math.Floor(h);
    }

    private static double Frac(double x) => x - Math.Floor(x);
    private static double Lerp(double a, double b, double f) => a + (b - a) * f;
}
