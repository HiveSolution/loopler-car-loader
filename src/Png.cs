using System;
using System.IO;
using System.IO.Compression;

namespace LooplerCarLoader;

// Minimal PNG reader: Unity 6 only has the ReadOnlySpan ImageConversion.LoadImage,
// which Il2CppInterop can't call. 8-bit RGB, RGBA and palette, not interlaced.
static class Png
{
    // RGBA bytes, top row first.
    internal static byte[] Decode(byte[] data, out int width, out int height)
    {
        if (data.Length < 8 || BitConverter.ToUInt64(data, 0) != 0x0A1A0A0D474E5089) throw new InvalidDataException("not a PNG file");
        int w = 0, h = 0, colorType = 0;
        byte[] palette = null, alpha = null;
        var idat = new MemoryStream();
        for (int p = 8; p + 8 <= data.Length;)
        {
            int len = BigEndian(data, p);
            string type = System.Text.Encoding.ASCII.GetString(data, p + 4, 4);
            int body = p + 8;
            switch (type)
            {
                case "IHDR":
                    w = BigEndian(data, body); h = BigEndian(data, body + 4);
                    colorType = data[body + 9];
                    if (data[body + 8] != 8 || colorType is not (2 or 3 or 6) || data[body + 12] != 0)
                        throw new InvalidDataException("save it as an 8-bit RGBA, RGB or indexed PNG without interlacing");
                    break;
                case "PLTE": palette = data[body..(body + len)]; break;
                case "tRNS": alpha = data[body..(body + len)]; break;
                case "IDAT": idat.Write(data, body, len); break;
            }
            p = body + len + 4; // skip CRC
        }

        int bpp = colorType switch { 6 => 4, 2 => 3, _ => 1 }, stride = w * bpp;
        idat.Position = 0;
        var inflated = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(inflated);
        var raw = inflated.ToArray();
        if (raw.Length < h * (stride + 1)) throw new InvalidDataException("image data is cut short");

        var rows = new byte[h * stride];
        for (int y = 0; y < h; y++)
        {
            int filter = raw[y * (stride + 1)], src = y * (stride + 1) + 1, dst = y * stride;
            for (int x = 0; x < stride; x++)
            {
                int a = x >= bpp ? rows[dst + x - bpp] : 0;
                int b = y > 0 ? rows[dst + x - stride] : 0;
                int c = x >= bpp && y > 0 ? rows[dst + x - stride - bpp] : 0;
                rows[dst + x] = (byte)(raw[src + x] + filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    4 => Paeth(a, b, c),
                    _ => 0,
                });
            }
        }

        var rgba = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            switch (colorType)
            {
                case 6: Array.Copy(rows, i * 4, rgba, i * 4, 4); break;
                case 2: Array.Copy(rows, i * 3, rgba, i * 4, 3); rgba[i * 4 + 3] = 255; break;
                default:
                    int idx = rows[i];
                    Array.Copy(palette, idx * 3, rgba, i * 4, 3);
                    rgba[i * 4 + 3] = alpha != null && idx < alpha.Length ? alpha[idx] : (byte)255;
                    break;
            }
        }
        width = w; height = h;
        return rgba;
    }

    static int BigEndian(byte[] d, int i) => d[i] << 24 | d[i + 1] << 16 | d[i + 2] << 8 | d[i + 3];

    static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }
}
