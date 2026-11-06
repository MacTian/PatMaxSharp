namespace PatMaxSharp.Core;

/// <summary>
/// 8-bit grayscale image (equivalent to Cog8BitImage).
/// </summary>
public sealed class GrayImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Data { get; }

    public GrayImage(int width, int height)
    {
        Width = width;
        Height = height;
        Data = new byte[width * height];
    }

    public GrayImage(int width, int height, byte[] data)
    {
        if (data.Length < width * height) throw new ArgumentException("data too small");
        Width = width;
        Height = height;
        Data = data;
    }

    public byte this[int x, int y]
    {
        get => Data[y * Width + x];
        set => Data[y * Width + x] = value;
    }

    /// <summary>Pixel value with clamp (no interpolation).</summary>
    public byte At(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return 0;
        return Data[y * Width + x];
    }

    /// <summary>Bilinear sample at sub-pixel coordinates.</summary>
    public double Sample(double x, double y)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        double v00 = At(x0, y0),     v10 = At(x0 + 1, y0);
        double v01 = At(x0, y0 + 1), v11 = At(x0 + 1, y0 + 1);
        return (v00 * (1 - fx) + v10 * fx) * (1 - fy) + (v01 * (1 - fx) + v11 * fx) * fy;
    }

    public GrayImage Clone() => new GrayImage(Width, Height, (byte[])Data.Clone());

    /// <summary>Create a solid test image (synthetic pattern for demos).</summary>
    public static GrayImage CreateTest(int width, int height, int seed = 0)
    {
        var img = new GrayImage(width, height);
        var rnd = new Random(seed);
        // Smooth low-frequency background (few edges) so the object stands out.
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                img[x, y] = (byte)(128 + 40 * Math.Sin(x / 32.0) * Math.Cos(y / 32.0));
        // A single, clearly distinct object on a near-uniform background:
        // a bright square with a dark hole — a unique signature.
        int bw = width / 4, bh = height / 4;
        int bx = width / 2 - bw / 2, by = height / 2 - bh / 2;
        for (int y = by; y < by + bh; y++)
            for (int x = bx; x < bx + bw; x++)
                img[x, y] = 240;
        int cx = bx + bw / 2, cy = by + bh / 2, r = Math.Min(bw, bh) / 4;
        for (int y = cy - r; y <= cy + r; y++)
            for (int x = cx - r; x <= cx + r; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                    img[x, y] = 20;
        return img;
    }
}
