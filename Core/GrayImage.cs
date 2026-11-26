using System.Runtime.CompilerServices;

namespace PatMaxSharp.Core;

/// <summary>
/// 8-bit grayscale image (equivalent to Cog8BitImage).
/// Immutable in practice: every transform returns a new instance, which keeps the
/// matching pipeline free of accidental aliasing between pyramid levels.
/// </summary>
public sealed class GrayImage
{
    /// <summary>Image width, pixels.</summary>
    public int Width { get; }

    /// <summary>Image height, pixels.</summary>
    public int Height { get; }

    /// <summary>Row-major 8-bit samples, length Width * Height.</summary>
    public byte[] Data { get; }

    /// <summary>Allocate a black image.</summary>
    public GrayImage(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "image must be non-empty");
        Width = width;
        Height = height;
        Data = new byte[width * height];
    }

    /// <summary>
    /// Wrap an existing buffer (not copied). The buffer must hold at least Width * Height
    /// bytes; it may be larger.
    /// </summary>
    public GrayImage(int width, int height, byte[] data)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "image must be non-empty");
        if (data.Length < width * height) throw new ArgumentException("data too small", nameof(data));
        Width = width;
        Height = height;
        Data = data;
    }

    /// <summary>Raw pixel access without bounds checking.</summary>
    public byte this[int x, int y]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Data[y * Width + x];
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Data[y * Width + x] = value;
    }

    /// <summary>Pixel value with clamp (no interpolation). Out-of-range reads return 0.</summary>
    public byte At(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height) return 0;
        return Data[y * Width + x];
    }

    /// <summary>Bilinear sample at sub-pixel coordinates (edge clamp, never throws).</summary>
    public double Sample(double x, double y)
    {
        if (x < 0) x = 0;
        if (y < 0) y = 0;
        if (x > Width - 1) x = Width - 1;
        if (y > Height - 1) y = Height - 1;

        int x0 = (int)x, y0 = (int)y;
        int x1 = x0 + 1 >= Width ? x0 : x0 + 1;
        int y1 = y0 + 1 >= Height ? y0 : y0 + 1;
        double fx = x - x0, fy = y - y0;

        double v00 = Data[y0 * Width + x0];
        double v10 = Data[y0 * Width + x1];
        double v01 = Data[y1 * Width + x0];
        double v11 = Data[y1 * Width + x1];
        return (v00 * (1 - fx) + v10 * fx) * (1 - fy) + (v01 * (1 - fx) + v11 * fx) * fy;
    }

    /// <summary>Deep copy (the pixel buffer is duplicated).</summary>
    public GrayImage Clone() => new(Width, Height, (byte[])Data.Clone());

    /// <summary>Copy a rectangular region. The rect is clamped to the image bounds.</summary>
    public GrayImage Crop(RectI rect)
    {
        var r = rect.Clamp(Width, Height);
        if (r.IsEmpty) throw new ArgumentException($"empty crop region {rect} for image {Width}x{Height}", nameof(rect));

        var data = new byte[r.Area];
        for (int j = 0; j < r.Height; j++)
            Buffer.BlockCopy(Data, (r.Y + j) * Width + r.X, data, j * r.Width, r.Width);
        return new GrayImage(r.Width, r.Height, data);
    }

    /// <summary>
    /// 2x box downscale. Used to build the search pyramid; averaging (rather than
    /// decimation) keeps high-frequency texture from aliasing into false edges.
    /// </summary>
    public GrayImage Downsample2()
    {
        int w = Math.Max(1, Width / 2), h = Math.Max(1, Height / 2);
        var dst = new byte[w * h];
        int w2 = Width;
        for (int y = 0; y < h; y++)
        {
            int r0 = (2 * y) * w2;
            int r1 = (2 * y + 1 < Height ? 2 * y + 1 : 2 * y) * w2;
            int dr = y * w;
            for (int x = 0; x < w; x++)
            {
                int c0 = 2 * x;
                int c1 = c0 + 1 < Width ? c0 + 1 : c0;
                int sum = Data[r0 + c0] + Data[r0 + c1] + Data[r1 + c0] + Data[r1 + c1];
                dst[dr + x] = (byte)((sum + 2) >> 2);
            }
        }
        return new GrayImage(w, h, dst);
    }

    /// <summary>Global mean and standard deviation (contrast statistics).</summary>
    public (double Mean, double StdDev) Statistics()
    {
        long sum = 0;
        long sumSq = 0;
        var d = Data;
        for (int i = 0; i < d.Length; i++)
        {
            sum += d[i];
            sumSq += d[i] * d[i];
        }
        double n = d.Length;
        double mean = sum / n;
        double var = Math.Max(0.0, sumSq / n - mean * mean);
        return (mean, Math.Sqrt(var));
    }

    /// <summary>
    /// Synthetic test image: smooth background gradient plus one highlighted object
    /// (bright square with a dark circular hole) — a unique, localisable signature.
    /// </summary>
    public static GrayImage CreateTest(int width, int height, int seed = 0)
    {
        var img = new GrayImage(width, height);
        var rnd = new Random(seed);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                img[x, y] = (byte)(128 + 40 * Math.Sin(x / 32.0) * Math.Cos(y / 32.0));

        int bw = Math.Max(8, width / 4), bh = Math.Max(8, height / 4);
        int bx = width / 2 - bw / 2, by = height / 2 - bh / 2;
        for (int y = by; y < Math.Min(height, by + bh); y++)
            for (int x = bx; x < Math.Min(width, bx + bw); x++)
                img[x, y] = 240;

        int cx = bx + bw / 2, cy = by + bh / 2, r = Math.Max(3, Math.Min(bw, bh) / 4);
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
                if (x >= 0 && y >= 0 && x < width && y < height &&
                    (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                    img[x, y] = 20;
        return img;
    }
}
