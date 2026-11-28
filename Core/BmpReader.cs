using System.Buffers.Binary;

namespace ShapeAnchor.Core;

/// <summary>
/// Minimal BMP reader (1/4/8-bit indexed, 16/24/32-bit BI_RGB, bottom-up or top-down).
///
/// Same reason as <see cref="PngReader"/>: the library targets net8.0 and cannot use
/// System.Drawing, but the validation sets on a production machine are usually raw BMP
/// dumps straight out of the camera.
/// </summary>
public static class BmpReader
{
    public static GrayImage Load(string path) => Decode(File.ReadAllBytes(path));

    public static bool LooksLikeBmp(ReadOnlySpan<byte> data) =>
        data.Length > 60 && data[0] == (byte)'B' && data[1] == (byte)'M';

    public static GrayImage Decode(byte[] b)
    {
        if (!LooksLikeBmp(b)) throw new InvalidDataException("not a BMP file");

        int dataOffset = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x0A));
        int dibSize = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x0E));
        int width = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x12));
        int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x16));
        int bpp = BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(0x1C));
        int compression = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x1E));

        if (dibSize < 40) throw new NotSupportedException($"BMP DIB header {dibSize} is not supported (need BITMAPINFOHEADER)");
        if (compression != 0) throw new NotSupportedException($"compressed BMP (type {compression}) is not supported");
        if (width <= 0) throw new InvalidDataException("BMP width must be positive");

        bool topDown = rawHeight < 0;
        int height = Math.Abs(rawHeight);
        if (height == 0) throw new InvalidDataException("BMP height must be non-zero");

        // Palette (indexed formats) sits right after the DIB header.
        byte[] palette = null;
        if (bpp <= 8)
        {
            int paletteOffset = 14 + dibSize;
            int entries = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(0x2E));
            if (entries == 0 || entries > (1 << bpp)) entries = 1 << bpp;
            palette = new byte[entries * 3];
            int available = Math.Min(palette.Length, Math.Max(0, b.Length - paletteOffset));
            Array.Copy(b, paletteOffset, palette, 0, available);
        }

        int bytesPerPixel = bpp / 8;
        int stride = ((width * bpp + 31) / 32) * 4;

        var image = new GrayImage(width, height);
        var dst = image.Data;

        for (int y = 0; y < height; y++)
        {
            int srcRow = dataOffset + (topDown ? y : height - 1 - y) * stride;
            if (srcRow + stride > b.Length) throw new InvalidDataException("truncated BMP pixel data");
            int dstRow = y * width;

            for (int x = 0; x < width; x++)
            {
                byte grey;
                int p = srcRow + x * bytesPerPixel;

                switch (bpp)
                {
                    case 8:
                        {
                            int idx = b[p] * 3;
                            grey = idx + 2 < palette.Length
                                ? (byte)((palette[idx + 2] * 299 + palette[idx + 1] * 587 + palette[idx] * 114) / 1000)
                                : b[p];
                            break;
                        }
                    case 24:
                        grey = (byte)((b[p + 2] * 299 + b[p + 1] * 587 + b[p] * 114) / 1000);
                        break;
                    case 32:
                        {
                            byte alpha = b[p + 3];
                            int lum = (b[p + 2] * 299 + b[p + 1] * 587 + b[p] * 114) / 1000;
                            grey = (byte)(alpha == 0 || alpha == 255 ? lum : lum * alpha / 255);
                            break;
                        }
                    case 16:
                        {
                            int v = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p));
                            int r = ((v >> 10) & 0x1F) * 255 / 31;
                            int g = ((v >> 5) & 0x1F) * 255 / 31;
                            int bl = (v & 0x1F) * 255 / 31;
                            grey = (byte)((r * 299 + g * 587 + bl * 114) / 1000);
                            break;
                        }
                    case 1:
                    case 4:
                        {
                            // packed index; width is padded to whole bytes per row already via stride
                            int bitsPerIndex = bpp;
                            int bitPos = x * bitsPerIndex;
                            int byteVal = b[p + bitPos / 8];
                            int shift = 8 - bitsPerIndex - (bitPos % 8);
                            int idx = (byteVal >> shift) & ((1 << bitsPerIndex) - 1);
                            int pi = idx * 3;
                            grey = pi + 2 < palette.Length
                                ? (byte)((palette[pi + 2] * 299 + palette[pi + 1] * 587 + palette[pi] * 114) / 1000)
                                : (byte)(idx * 255 / ((1 << bitsPerIndex) - 1));
                            break;
                        }
                    default:
                        throw new NotSupportedException($"BMP bit depth {bpp} is not supported");
                }

                dst[dstRow + x] = grey;
            }
        }

        return image;
    }
}
