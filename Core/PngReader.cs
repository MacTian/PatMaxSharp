using System.IO.Compression;

namespace PatMaxSharp.Core;

/// <summary>
/// Minimal PNG reader (8/16-bit, non-interlaced, colour types 0/2/3/4/6).
///
/// The library targets net8.0 and must stay portable, so it cannot depend on
/// System.Drawing. A PNG decode is small enough to own: parse IHDR/PLTE/IDAT,
/// inflate, undo the five row filters, convert to 8-bit grey.
/// </summary>
public static class PngReader
{
    /// <summary>Read a PNG file from disk and decode it to 8-bit grey.</summary>
    public static GrayImage Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return Decode(bytes);
    }

    /// <summary>
    /// Decode PNG bytes to 8-bit grey: colour is reduced with Rec.601 weights, palette and
    /// alpha are handled, and 16-bit samples are truncated to their high byte.
    /// </summary>
    public static GrayImage Decode(byte[] bytes)
    {
        if (bytes.Length < 8 || bytes[0] != 0x89 || bytes[1] != 'P' || bytes[2] != 'N' || bytes[3] != 'G')
            throw new InvalidDataException("not a PNG file");

        int width = 0, height = 0, bitDepth = 0, colorType = 0, interlace = 0;
        byte[] palette = null;
        using var idat = new MemoryStream();

        int pos = 8;
        while (pos + 8 <= bytes.Length)
        {
            int length = BigEndian(bytes, pos);
            string type = System.Text.Encoding.ASCII.GetString(bytes, pos + 4, 4);
            int data = pos + 8;
            if (data + length > bytes.Length) break;

            switch (type)
            {
                case "IHDR":
                    width = BigEndian(bytes, data);
                    height = BigEndian(bytes, data + 4);
                    bitDepth = bytes[data + 8];
                    colorType = bytes[data + 9];
                    interlace = bytes[data + 12];
                    break;
                case "PLTE":
                    palette = new byte[length];
                    Array.Copy(bytes, data, palette, 0, length);
                    break;
                case "IDAT":
                    idat.Write(bytes, data, length);
                    break;
                case "IEND":
                    pos = bytes.Length;
                    continue;
            }
            pos = data + length + 4;   // skip CRC
        }

        if (width <= 0 || height <= 0) throw new InvalidDataException("PNG without a valid IHDR");
        if (interlace != 0) throw new NotSupportedException("interlaced PNG is not supported");
        if (bitDepth != 8 && bitDepth != 16) throw new NotSupportedException($"bit depth {bitDepth} is not supported");

        int channels = colorType switch
        {
            0 => 1,          // grey
            2 => 3,          // RGB
            3 => 1,          // palette index
            4 => 2,          // grey + alpha
            6 => 4,          // RGBA
            _ => throw new NotSupportedException($"colour type {colorType} is not supported")
        };

        int bytesPerSample = bitDepth / 8;
        int bpp = channels * bytesPerSample;
        int stride = width * bpp;
        long expected = (long)(stride + 1) * height;

        byte[] raw;
        idat.Position = 0;
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        using (var buffer = new MemoryStream())
        {
            zlib.CopyTo(buffer);
            raw = buffer.ToArray();
        }
        if (raw.Length < expected) throw new InvalidDataException("truncated PNG data");

        Unfilter(raw, stride, bpp, height);

        var image = new GrayImage(width, height);
        var d = image.Data;
        int outIndex = 0;

        for (int y = 0; y < height; y++)
        {
            int row = y * (stride + 1) + 1;
            for (int x = 0; x < width; x++)
            {
                int i = row + x * bpp;
                byte grey;
                switch (colorType)
                {
                    case 0:
                    case 4:
                        grey = raw[i];                      // grey (alpha ignored)
                        break;
                    case 2:
                    case 6:
                        grey = (byte)((raw[i] * 299 + raw[i + 1] * 587 + raw[i + 2] * 114) / 1000);
                        break;
                    default: // 3 = palette
                        int pi = raw[i] * 3;
                        grey = pi + 2 < palette.Length
                            ? (byte)((palette[pi] * 299 + palette[pi + 1] * 587 + palette[pi + 2] * 114) / 1000)
                            : (byte)0;
                        break;
                }
                d[outIndex++] = grey;
            }
        }
        return image;
    }

    private static void Unfilter(byte[] raw, int stride, int bpp, int height)
    {
        const int rowBytes = 0; // documentation only
        _ = rowBytes;

        for (int y = 0; y < height; y++)
        {
            int filter = raw[y * (stride + 1)];
            int start = y * (stride + 1) + 1;
            int prev = start - (stride + 1);

            switch (filter)
            {
                case 0:
                    break;
                case 1: // Sub
                    for (int i = bpp; i < stride; i++) raw[start + i] += raw[start + i - bpp];
                    break;
                case 2: // Up
                    if (y > 0)
                        for (int i = 0; i < stride; i++) raw[start + i] += raw[prev + i];
                    break;
                case 3: // Average
                    for (int i = 0; i < stride; i++)
                    {
                        int left = i >= bpp ? raw[start + i - bpp] : 0;
                        int up = y > 0 ? raw[prev + i] : 0;
                        raw[start + i] += (byte)((left + up) >> 1);
                    }
                    break;
                case 4: // Paeth
                    for (int i = 0; i < stride; i++)
                    {
                        int a = i >= bpp ? raw[start + i - bpp] : 0;
                        int b = y > 0 ? raw[prev + i] : 0;
                        int c = i >= bpp && y > 0 ? raw[prev + i - bpp] : 0;
                        raw[start + i] += (byte)Paeth(a, b, c);
                    }
                    break;
                default:
                    throw new InvalidDataException($"unknown PNG row filter {filter}");
            }
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc) return a;
        return pb <= pc ? b : c;
    }

    private static int BigEndian(byte[] b, int i) =>
        (b[i] << 24) | (b[i + 1] << 16) | (b[i + 2] << 8) | b[i + 3];
}
