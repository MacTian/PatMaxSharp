using ShapeAnchor.Core;

namespace ShapeAnchor.Benchmarks;

/// <summary>
/// Procedural test part with a unique, asymmetric shape, plus a renderer that can place it
/// at a known pose inside a scene. Ground truth is exact, so position/angle/scale errors
/// and score behaviour can be measured instead of eyeballed.
/// </summary>
public static class SyntheticPart
{
    public const int Size = 160;
    private const int Supersample = 4;
    public const double Center = (Size - 1) / 2.0;   // 79.5

    /// <summary>Clean pattern image (the thing to train on), centred at <see cref="Center"/>.</summary>
    public static GrayImage Base()
    {
        int ss = Supersample;
        int hi = Size * ss;
        var acc = new float[Size * Size];

        for (int y = 0; y < hi; y++)
        {
            double v = (y + 0.5) / ss - 0.5;
            for (int x = 0; x < hi; x++)
            {
                double u = (x + 0.5) / ss - 0.5;
                acc[(y / ss) * Size + (x / ss)] += (float)Shade(u, v);
            }
        }

        var img = new GrayImage(Size, Size);
        float inv = 1f / (ss * ss);
        for (int i = 0; i < acc.Length; i++)
            img.Data[i] = (byte)Math.Clamp(acc[i] * inv, 0, 255);
        return img;
    }

    /// <summary>Continuous intensity of the part at nominal coordinates (u, v), origin at the centre.</summary>
    private static double Shade(double u, double v)
    {
        double cx = Center, cy = Center;
        double dx = u - cx, dy = v - cy;
        double r = Math.Sqrt(dx * dx + dy * dy);

        double value = 70;

        // chord bar (asymmetric, breaks rotational symmetry)
        double bar = 75 * Math.PI / 180;
        double bAlong = dx * Math.Cos(bar) + dy * Math.Sin(bar);
        double bAcross = -dx * Math.Sin(bar) + dy * Math.Cos(bar);
        if (Math.Abs(bAlong) <= 26 && Math.Abs(bAcross) <= 6.5 && r <= 62) value = 120;

        if (r <= 55) value = 190;                     // body disc
        if (r > 52 && r <= 62) value = 225;           // outer ring

        // seven teeth (360 / 7 is not an integer, so no rotation maps the tooth train onto
        // itself) plus one longer tooth as a keyway — the part must be genuinely asymmetric,
        // otherwise an occluded scene is ambiguous by construction and no matcher can win.
        for (int k = 0; k < 7; k++)
        {
            double a = k * (360.0 / 7) * Math.PI / 180;
            double along = dx * Math.Cos(a) + dy * Math.Sin(a);
            double across = -dx * Math.Sin(a) + dy * Math.Cos(a);
            double outer = k == 0 ? 78 : 72;
            if (Math.Abs(across) <= 4.5 && along >= 60 && along <= outer) value = 235;
        }

        // three mounting holes (asymmetric placement)
        double[] holeAngles = { 20, 140, 265 };
        foreach (double deg in holeAngles)
        {
            double a = deg * Math.PI / 180;
            double hx = 32 * Math.Cos(a), hy = 32 * Math.Sin(a);
            double hd = Math.Sqrt((dx - hx) * (dx - hx) + (dy - hy) * (dy - hy));
            if (hd <= 9) value = 35;
        }

        return value;
    }

    /// <summary>Scene rendering controls.</summary>
    public sealed class SceneOptions
    {
        public double Background { get; set; } = 60;
        public double BackgroundGradient { get; set; } = 12;
        public double NoiseStd { get; set; } = 0;
        public int ClutterShapes { get; set; } = 0;
        public RectI? Occlusion { get; set; }
        public double OcclusionValue { get; set; } = 128;
        public int Seed { get; set; } = 1;
    }

    /// <summary>
    /// Render the part at pose (cx, cy, angle, scale) into a scene of the given size.
    /// The returned image has the part's centre at (cx, cy).
    /// </summary>
    public static GrayImage Render(int width, int height, double cx, double cy,
        double angleRad, double scale, SceneOptions options = null)
    {
        var opt = options ?? new SceneOptions();
        var rnd = new Random(opt.Seed);
        var baseImg = Base();
        var dst = new GrayImage(width, height);

        double cos = Math.Cos(-angleRad), sin = Math.Sin(-angleRad);
        double invScale = 1.0 / Math.Max(1e-6, scale);

        for (int y = 0; y < height; y++)
        {
            double dy = y - cy;
            double gradRow = opt.BackgroundGradient * Math.Sin(y / 41.0);
            for (int x = 0; x < width; x++)
            {
                double dx = x - cx;
                double mx = (cos * dx - sin * dy) * invScale + Center;
                double my = (sin * dx + cos * dy) * invScale + Center;

                double value;
                if (mx >= -0.5 && my >= -0.5 && mx <= Size - 0.5 && my <= Size - 0.5)
                    value = baseImg.Sample(mx, my);
                else
                    value = opt.Background + gradRow + opt.BackgroundGradient * Math.Cos(x / 37.0);

                dst[x, y] = (byte)Math.Clamp(value, 0, 255);
            }
        }

        // clutter: random shapes that are NOT the part
        for (int c = 0; c < opt.ClutterShapes; c++)
        {
            bool rect = rnd.Next(2) == 0;
            int ox = rnd.Next(0, Math.Max(1, width - 40));
            int oy = rnd.Next(0, Math.Max(1, height - 40));
            int sw = 18 + rnd.Next(26);
            int sh = 18 + rnd.Next(26);
            byte val = (byte)(90 + rnd.Next(120));

            for (int y = oy; y < Math.Min(height, oy + sh); y++)
                for (int x = ox; x < Math.Min(width, ox + sw); x++)
                {
                    bool inside = rect
                        ? true
                        : ((x - ox - sw / 2.0) * (x - ox - sw / 2.0) + (y - oy - sh / 2.0) * (y - oy - sh / 2.0))
                          <= (sw / 2.0) * (sw / 2.0);
                    if (inside) dst[x, y] = val;
                }
        }

        if (opt.Occlusion.HasValue)
        {
            var r = opt.Occlusion.Value.Clamp(width, height);
            for (int y = r.Y; y < r.Bottom; y++)
                for (int x = r.X; x < r.Right; x++)
                    dst[x, y] = (byte)opt.OcclusionValue;
        }

        if (opt.NoiseStd > 0)
        {
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // Box-Muller
                    double u1 = 1.0 - rnd.NextDouble();
                    double u2 = rnd.NextDouble();
                    double n = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2);
                    dst[x, y] = (byte)Math.Clamp(dst[x, y] + n * opt.NoiseStd, 0, 255);
                }
        }

        return dst;
    }
}
