using System.Numerics;
using System.Threading.Tasks;
using PatMaxSharp.Core;

namespace PatMaxSharp.Edge;

/// <summary>Which operator produces the gradient field.</summary>
public enum EdgeMethod
{
    /// <summary>
    /// Isotropic derivative-of-Gaussian, computed with two separable passes.
    /// Default: continuous edge orientation, ~60 multiply-adds per pixel.
    /// </summary>
    GradientOfGaussian = 0,

    /// <summary>
    /// 12-orientation Gabor bank (sine phase, anisotropic envelope). Fidelity path — much
    /// slower, available for cross-checking the differential operator.
    ///
    /// Evidence: the default parameter block was found verbatim in the *managed* assembly
    /// Cognex.VisionPro.PMAlign.dll, whose mixed-mode native .text is plain x64. The block
    /// {20.0, 1.0, sigmaX = 2.0506096654409878, sigmaY = 4.1012193308819755, kernel
    /// half-window 7, phase 1} starts at function RVA 0x00EAF2C0 (file offset 0x00EAE8C0);
    /// the instruction-by-instruction hexdump is in docs/evidence/native-strings.md, section
    /// 6.3. sigmaX, sigmaY and the half-window 7 are read straight out of those immediates.
    /// "12 orientations" is not one of the immediates: it is what this class's own half-window
    /// formula yields for half-window 7 (ComputeOrientationCount(7) = 12). Which field means
    /// what is a high-confidence inference, not a symbol-resolved fact.
    /// </summary>
    GaborBank = 1
}

/// <summary>
/// Edge field of one image.
///
/// Stores a signed directional derivative field (Gx, Gy) rather than a quantised
/// orientation index, so downstream code can evaluate the derivative along *any*
/// direction with a single dot product. This is what makes sub-degree angle
/// estimation and sub-pixel pose fitting possible without an angle lookup.
/// </summary>
public sealed class EdgeMap
{
    public int Width { get; }
    public int Height { get; }
    public float[] Gx { get; }
    public float[] Gy { get; }
    public float[] Magnitude { get; }
    public bool[] IsEdge { get; }

    /// <summary>Largest gradient magnitude in the field (used for contrast normalisation).</summary>
    public float MaxMagnitude { get; internal set; }

    /// <summary>Number of pixels above the noise floor.</summary>
    public int EdgePixelCount { get; internal set; }

    public EdgeMap(int w, int h)
    {
        Width = w;
        Height = h;
        int n = w * h;
        Gx = new float[n];
        Gy = new float[n];
        Magnitude = new float[n];
        IsEdge = new bool[n];
    }

    public float GetMagnitude(int x, int y) => Magnitude[y * Width + x];

    /// <summary>Edge normal direction (radians) — computable on demand, never stored.</summary>
    public float GetAngle(int x, int y)
    {
        int i = y * Width + x;
        return (float)Math.Atan2(Gy[i], Gx[i]);
    }

    public bool IsEdgePixel(int x, int y) => IsEdge[y * Width + x];

    /// <summary>Signed directional derivative at integer coordinates along unit direction (nx, ny).</summary>
    public float DirectionalAt(int x, int y, float nx, float ny)
    {
        int i = y * Width + x;
        return Gx[i] * nx + Gy[i] * ny;
    }
}

/// <summary>
/// Edge detector producing the (Gx, Gy) field consumed by the matcher.
///
/// Two operators share one output contract:
///   * <see cref="EdgeMethod.GradientOfGaussian"/> — separable, SIMD, ~10x faster.
///   * <see cref="EdgeMethod.GaborBank"/> — the 12-orientation bank used as the fidelity
///     reference (its default parameters and their evidence are documented on
///     <see cref="EdgeMethod.GaborBank"/>).
///
/// The Gabor bank previously in this project had two defects that made it unusable as an
/// edge detector: (1) all orientations shared one <c>response</c> buffer inside
/// <c>Parallel.For</c>, a data race that corrupted results non-deterministically; and
/// (2) the kernels used the cosine phase, which is even-symmetric and therefore responds
/// to bright lines rather than to step edges, and carries no polarity. Both are fixed here.
/// </summary>
public sealed class GaborEdgeDetector
{
    // ---- defaults recovered from the managed assembly native .text (see the GaborBank doc) ----
    /// <summary>
    /// Envelope sigma along the carrier, read from the recovered default parameter block of the
    /// managed assembly (function RVA 0x00EAF2C0, file offset 0x00EAE8C0, immediate at
    /// RVA 0x00EAF2E3). See the GaborBank enum doc and docs/evidence/native-strings.md section 6.3.
    /// </summary>
    public const double DefaultSigmaX = 2.0506096654409878;

    /// <summary>sigmaY / sigmaX for the recovered block: exactly 2.0 (4.1012193308819755 / 2.0506096654409878).</summary>
    public const double DefaultSensitivityParameter = 2.0;

    /// <summary>Operator that produces the (Gx, Gy) field.</summary>
    public EdgeMethod Method { get; set; } = EdgeMethod.GradientOfGaussian;

    /// <summary>Envelope sigma along the carrier (default <see cref="DefaultSigmaX"/>).</summary>
    public double SigmaX { get; }

    /// <summary>Envelope sigma across the carrier: <see cref="SigmaX"/> times the sensitivity parameter.</summary>
    public double SigmaY { get; }

    /// <summary>Kernel half-window: ceil(sqrt(2) * max(sigmaX, sigmaY) + 0.95), which is 7 for the defaults.</summary>
    public int HalfSize { get; }

    /// <summary>Number of oriented kernels in the bank (12 for the defaults, derived from <see cref="HalfSize"/>).</summary>
    public int OrientationCount { get; }

    /// <summary>Angular step between neighbouring kernels, radians: pi / <see cref="OrientationCount"/>.</summary>
    public double OrientationStep { get; }

    /// <summary>Carrier wavelength in pixels.</summary>
    public double Wavelength { get; set; } = 4.0;

    /// <summary>Gradient magnitude below this is treated as noise (no votes, no matches).</summary>
    public double NoiseFloor { get; set; } = 8.0;

    /// <summary>Backwards-compatible alias of <see cref="NoiseFloor"/>.</summary>
    public double MagThresholdZero
    {
        get => NoiseFloor;
        set => NoiseFloor = value;
    }

    /// <summary>Kept for API compatibility; the contrast ramp is applied by the scorer.</summary>
    public double MagThresholdSpan { get; set; } = 64.0;
    /// <summary>
    /// Second, lower noise gate, declared for API compatibility. Declared and stored only:
    /// <see cref="Detect"/> masks edges with <see cref="NoiseFloor"/> alone.
    /// </summary>
    public double NoiseThreshold { get; set; } = 2.0;

    /// <summary>Use System.Numerics vector paths where the length allows it.</summary>
    public bool UseSimd { get; set; } = true;

    /// <summary>Run convolution bands and orientations on the thread pool.</summary>
    public bool UseParallel { get; set; } = true;

    private readonly float[][] _gaborKernels;
    private readonly float[] _derivKernel;
    private readonly float[] _smoothKernel;

    /// <summary>
    /// Build the kernel bank. The defaults are the recovered parameter block; sigmaY is always
    /// sigmaX * <paramref name="sensitivityParameter"/>.
    /// </summary>
    public GaborEdgeDetector(
        double sigmaX = DefaultSigmaX,
        double sensitivityParameter = DefaultSensitivityParameter,
        double? wavelength = null,
        int? orientationCount = null)
    {
        SigmaX = sigmaX;
        SigmaY = sigmaX * sensitivityParameter;
        HalfSize = (int)Math.Ceiling(Math.Sqrt(2.0) * Math.Max(SigmaX, SigmaY) + 0.95);
        OrientationCount = orientationCount ?? ComputeOrientationCount(HalfSize);
        OrientationStep = Math.PI / OrientationCount;
        if (wavelength.HasValue) Wavelength = wavelength.Value;

        _gaborKernels = new float[OrientationCount][];
        for (int i = 0; i < OrientationCount; i++)
            _gaborKernels[i] = BuildGaborKernel(i * OrientationStep);

        // Isotropic DoG pair: derivative along the convolution axis, Gaussian across it.
        int r = Math.Max(1, (int)Math.Ceiling(3.0 * SigmaX));
        _derivKernel = BuildDerivativeKernel(SigmaX, r);
        _smoothKernel = BuildSmoothingKernel(SigmaX, r);
        DeriveRadius = r;
    }

    private int DeriveRadius { get; }

    private static int ComputeOrientationCount(int halfSize)
    {
        double denom = 2.0 * Math.Ceiling(0.5 * (halfSize + 0.5) * (Math.PI / 2.0));
        return Math.Max(1, (int)Math.Round(Math.PI / (Math.PI / denom)));
    }

    /// <summary>
    /// Odd-phase Gabor (sine) so the response is a true directional derivative:
    /// sign carries the edge polarity, which the matcher relies on.
    /// </summary>
    private float[] BuildGaborKernel(double theta)
    {
        int size = 2 * HalfSize + 1;
        var k = new float[size * size];
        double c = Math.Cos(theta), s = Math.Sin(theta);
        double twoSigmaX2 = 2.0 * SigmaX * SigmaX;
        double twoSigmaY2 = 2.0 * SigmaY * SigmaY;
        double twoPiOverLambda = 2.0 * Math.PI / Math.Max(0.5, Wavelength);

        double sum = 0.0;
        for (int y = -HalfSize; y <= HalfSize; y++)
            for (int x = -HalfSize; x <= HalfSize; x++)
            {
                double xp = x * c + y * s;
                double yp = -x * s + y * c;
                double g = Math.Exp(-(xp * xp / twoSigmaX2 + yp * yp / twoSigmaY2))
                           * Math.Sin(twoPiOverLambda * xp);
                k[(y + HalfSize) * size + (x + HalfSize)] = (float)g;
                sum += Math.Abs(g);
            }
        if (sum > 0)
        {
            float inv = (float)(1.0 / sum);
            for (int i = 0; i < k.Length; i++) k[i] *= inv;
        }
        return k;
    }

    /// <summary>Normalised first-derivative-of-Gaussian: a unit step yields ~1.0 response.</summary>
    private static float[] BuildDerivativeKernel(double sigma, int r)
    {
        var k = new float[2 * r + 1];
        double twoSigma2 = 2.0 * sigma * sigma;
        double pos = 0;
        for (int i = 1; i <= r; i++)
        {
            double v = i * Math.Exp(-(i * i) / twoSigma2);
            k[i + r] = (float)v;
            pos += v;
        }
        if (pos > 0)
        {
            for (int i = 1; i <= r; i++)
            {
                double v = k[i + r] / pos;
                k[i + r] = (float)v;
                k[r - i] = (float)-v;
            }
        }
        k[r] = 0f;
        return k;
    }

    private static float[] BuildSmoothingKernel(double sigma, int r)
    {
        var k = new float[2 * r + 1];
        double twoSigma2 = 2.0 * sigma * sigma;
        double sum = 0;
        for (int i = -r; i <= r; i++)
        {
            double v = Math.Exp(-(i * i) / twoSigma2);
            k[i + r] = (float)v;
            sum += v;
        }
        if (sum > 0)
            for (int i = 0; i < k.Length; i++) k[i] = (float)(k[i] / sum);
        return k;
    }

    // ------------------------------------------------------------------
    //  Public entry points
    // ------------------------------------------------------------------
    /// <summary>
    /// Produce the signed gradient field for one image (magnitude, edge mask, maximum and edge
    /// count are filled in as well).
    /// </summary>
    public EdgeMap Detect(GrayImage img)
    {
        int w = img.Width, h = img.Height;
        int n = w * h;

        var src = new float[n];
        var data = img.Data;
        for (int i = 0; i < n; i++) src[i] = data[i];

        var map = new EdgeMap(w, h);
        if (Method == EdgeMethod.GaborBank) DetectGabor(src, w, h, map);
        else DetectGoG(src, w, h, map);

        // magnitude + noise mask
        float max = 0f;
        int edgeCount = 0;
        float floor = (float)NoiseFloor;
        for (int i = 0; i < n; i++)
        {
            float gx = map.Gx[i], gy = map.Gy[i];
            float mag = (float)Math.Sqrt(gx * gx + gy * gy);
            map.Magnitude[i] = mag;
            bool isEdge = mag > floor;
            map.IsEdge[i] = isEdge;
            if (isEdge) edgeCount++;
            if (mag > max) max = mag;
        }
        map.MaxMagnitude = max;
        map.EdgePixelCount = edgeCount;
        return map;
    }

    /// <summary>Separable derivative-of-Gaussian: two row passes, two column passes.</summary>
    private void DetectGoG(float[] src, int w, int h, EdgeMap map)
    {
        int n = w * h;
        var tmpDeriv = new float[n];
        var tmpSmooth = new float[n];

        ConvolveRows(src, tmpDeriv, w, h, _derivKernel);
        ConvolveRows(src, tmpSmooth, w, h, _smoothKernel);
        ConvolveColumns(tmpDeriv, map.Gx, w, h, _smoothKernel);
        ConvolveColumns(tmpSmooth, map.Gy, w, h, _derivKernel);
    }

    private void DetectGabor(float[] src, int w, int h, EdgeMap map)
    {
        int n = w * h;
        var responses = new float[OrientationCount][];
        for (int o = 0; o < OrientationCount; o++) responses[o] = new float[n];

        // Each orientation owns its destination buffer: no shared scratch, no race.
        void RunOrientation(int o) =>
            Convolve2D(src, responses[o], w, h, _gaborKernels[o], HalfSize);

        if (UseParallel) Parallel.For(0, OrientationCount, RunOrientation);
        else for (int o = 0; o < OrientationCount; o++) RunOrientation(o);

        // The bank reconstructs the gradient by vector summation of the oriented
        // responses along their normals (orientation + 90 degrees).
        var cos = new float[OrientationCount];
        var sin = new float[OrientationCount];
        for (int o = 0; o < OrientationCount; o++)
        {
            double normal = o * OrientationStep + Math.PI / 2.0;
            cos[o] = (float)Math.Cos(normal);
            sin[o] = (float)Math.Sin(normal);
        }

        void Combine(int y0, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                {
                    float gx = 0, gy = 0;
                    int idx = row + x;
                    for (int o = 0; o < OrientationCount; o++)
                    {
                        float v = responses[o][idx];
                        gx += v * cos[o];
                        gy += v * sin[o];
                    }
                    map.Gx[idx] = gx;
                    map.Gy[idx] = gy;
                }
            }
        }

        RunBands(h, Combine);
    }

    // ------------------------------------------------------------------
    //  Convolution primitives
    // ------------------------------------------------------------------
    private static int[] BuildPaddingLookup(int length, int radius)
    {
        var lut = new int[length + 2 * radius];
        for (int i = 0; i < lut.Length; i++)
        {
            int v = i - radius;
            if (v < 0) v = 0;
            else if (v >= length) v = length - 1;
            lut[i] = v;
        }
        return lut;
    }

    private void ConvolveRows(float[] src, float[] dst, int w, int h, float[] kernel)
    {
        int r = (kernel.Length - 1) / 2;
        var lut = BuildPaddingLookup(w, r);
        int vecWidth = Vector<float>.Count;
        bool simd = UseSimd && Vector.IsHardwareAccelerated && w >= vecWidth + 2 * r;
        int padLen = w + 2 * r;

        void Pass(int y0, int y1)
        {
            // Padded scanline, so the vectorised inner loop needs no bounds checks.
            var pad = new float[padLen];
            for (int y = y0; y < y1; y++)
            {
                int rowBase = y * w;
                for (int i = 0; i < padLen; i++) pad[i] = src[rowBase + lut[i]];

                int x0 = 0;
                if (simd)
                {
                    for (; x0 <= w - vecWidth; x0 += vecWidth)
                    {
                        var acc = Vector<float>.Zero;
                        for (int k = 0; k < kernel.Length; k++)
                        {
                            float kv = kernel[k];
                            if (kv == 0f) continue;
                            acc += new Vector<float>(pad, x0 + k) * kv;
                        }
                        acc.CopyTo(dst, rowBase + x0);
                    }
                }
                for (; x0 < w; x0++)
                {
                    float acc = 0;
                    for (int k = 0; k < kernel.Length; k++)
                        acc += pad[x0 + k] * kernel[k];
                    dst[rowBase + x0] = acc;
                }
            }
        }

        RunBands(h, Pass);
    }

    private void RunBands(int h, Action<int, int> pass)
    {
        if (UseParallel && h > 32)
        {
            int bands = Math.Min(Environment.ProcessorCount * 2, Math.Max(2, h / 16));
            Parallel.For(0, bands, b => pass(b * h / bands, (b + 1) * h / bands));
        }
        else pass(0, h);
    }

    private void ConvolveColumns(float[] src, float[] dst, int w, int h, float[] kernel)
    {
        int r = (kernel.Length - 1) / 2;
        int vecWidth = Vector<float>.Count;
        bool simd = UseSimd && Vector.IsHardwareAccelerated;

        void Pass(int y0, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                int dstRow = y * w;
                int kx = 0;
                // main accumulation: dst_row = sum_k kernel[k] * src_row(y+k)
                for (; simd && kx <= w - vecWidth; kx += vecWidth)
                {
                    var acc = Vector<float>.Zero;
                    for (int k = -r; k <= r; k++)
                    {
                        int sy = y + k;
                        if (sy < 0) sy = 0; else if (sy >= h) sy = h - 1;
                        acc += new Vector<float>(src, sy * w + kx) * kernel[k + r];
                    }
                    acc.CopyTo(dst, dstRow + kx);
                }
                for (; kx < w; kx++)
                {
                    float acc = 0;
                    for (int k = -r; k <= r; k++)
                    {
                        int sy = y + k;
                        if (sy < 0) sy = 0; else if (sy >= h) sy = h - 1;
                        acc += src[sy * w + kx] * kernel[k + r];
                    }
                    dst[dstRow + kx] = acc;
                }
            }
        }

        RunBands(h, Pass);
    }

    private void Convolve2D(float[] src, float[] dst, int w, int h, float[] kernel, int radius)
    {
        int size = 2 * radius + 1;
        int vecWidth = Vector<float>.Count;
        bool simd = UseSimd && Vector.IsHardwareAccelerated;

        void Pass(int y0, int y1)
        {
            for (int y = y0; y < y1; y++)
            {
                int dstRow = y * w;
                if (simd)
                {
                    for (int x = 0; x <= w - vecWidth; x += vecWidth)
                    {
                        var acc = Vector<float>.Zero;
                        for (int ky = -radius; ky <= radius; ky++)
                        {
                            int sy = y + ky;
                            if (sy < 0) sy = 0; else if (sy >= h) sy = h - 1;
                            int srcRow = sy * w;
                            int kRow = (ky + radius) * size;
                            for (int kx = -radius; kx <= radius; kx++)
                            {
                                float kv = kernel[kRow + kx + radius];
                                if (kv == 0f) continue;
                                int sx = x + kx;
                                if (sx < 0) sx = 0; else if (sx > w - vecWidth) sx = Math.Max(0, w - vecWidth);
                                acc += new Vector<float>(src, srcRow + sx) * kv;
                            }
                        }
                        acc.CopyTo(dst, dstRow + x);
                    }
                }
                int startX = simd ? ((w / vecWidth) * vecWidth) : 0;
                for (int x = startX; x < w; x++)
                {
                    float acc = 0;
                    for (int ky = -radius; ky <= radius; ky++)
                    {
                        int sy = y + ky;
                        if (sy < 0) sy = 0; else if (sy >= h) sy = h - 1;
                        int srcRow = sy * w;
                        int kRow = (ky + radius) * size;
                        for (int kx = -radius; kx <= radius; kx++)
                        {
                            int sx = x + kx;
                            if (sx < 0) sx = 0; else if (sx >= w) sx = w - 1;
                            acc += src[srcRow + sx] * kernel[kRow + kx + radius];
                        }
                    }
                    dst[dstRow + x] = acc;
                }
            }
        }

        RunBands(h, Pass);
    }

    public void PrintInfo()
    {
        Console.WriteLine("GaborEdgeDetector:");
        Console.WriteLine($"  method      = {Method}");
        Console.WriteLine($"  sigmaX      = {SigmaX:F16}");
        Console.WriteLine($"  sigmaY      = {SigmaY:F16}");
        Console.WriteLine($"  halfSize    = {HalfSize}  (kernel {2 * HalfSize + 1}x{2 * HalfSize + 1})");
        Console.WriteLine($"  orientations= {OrientationCount}  step = {OrientationStep * 180 / Math.PI:F2} deg");
        Console.WriteLine($"  wavelength  = {Wavelength:F2}  noiseFloor={NoiseFloor:F1} simd={UseSimd} parallel={UseParallel}");
    }
}
