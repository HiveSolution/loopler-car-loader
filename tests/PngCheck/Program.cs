using System;
using System.IO;
using LooplerCarLoader;

// The example sprite as RGBA, and the same picture flattened onto black as RGB and as a palette PNG.
var dir = AppContext.BaseDirectory;
var root = Path.GetFullPath(Path.Combine(dir, "..", "..", "..", "..", ".."));
var rgba = Png.Decode(File.ReadAllBytes(Path.Combine(root, "examples", "rocket", "rocket.png")), out int w, out int h);
Check(w == 11 && h == 11, $"size {w}x{h}");
Check(rgba[3] == 0, "top-left pixel is transparent");
Check(Pixel(rgba, 5, 0) == (20, 20, 24, 255), "top middle is the black outline");
Check(Pixel(rgba, 5, 1) == (250, 240, 220, 255), "second row middle is the white stripe");

foreach (var name in new[] { "rocket-rgb.png", "rocket-palette.png" })
{
    var other = Png.Decode(File.ReadAllBytes(Path.Combine(root, "tests", "PngCheck", name)), out w, out h);
    for (int i = 0; i < w * h; i++)
    {
        bool opaque = rgba[i * 4 + 3] == 255;
        for (int ch = 0; ch < 3; ch++)
            Check(other[i * 4 + ch] == (opaque ? rgba[i * 4 + ch] : 0), $"{name} pixel {i} channel {ch}");
        Check(other[i * 4 + 3] == 255, $"{name} pixel {i} opaque");
    }
}
Console.WriteLine("PNG reader OK");

static (int, int, int, int) Pixel(byte[] p, int x, int y) { int i = (y * 11 + x) * 4; return (p[i], p[i + 1], p[i + 2], p[i + 3]); }
static void Check(bool ok, string what) { if (!ok) { Console.WriteLine("FAIL: " + what); Environment.Exit(1); } }
