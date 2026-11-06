using System.Numerics;
using System.Threading.Tasks;
using PatMaxSharp.Core;

namespace PatMaxSharp.Edge;

/// <summary>
/// Gabor filter-bank edge detector (optimized).
///
/// Reverse-engineered constants (reports/01-edge-field.md, appendix B.1):
///   gaborSpatialSigmaX = 2.0506096654409878
///   sensitivityParameter = 2.0
///   gaborSpatialSigmaY = sigmaX * sensitivityParameter
///   gaborFilterHalfSize = ceil(sqrt(2)*max(sx,sy) + 0.95) = 7   (15x15)
///   orientations = 12, step = 15 deg
///
/// Optimizations: float kernels, SIMD inner loop, Parallel.For rows.
/// </summary>
public sealed class GaborEdgeDetector
{
    public const double DefaultSigmaX = 2.0506096654409878;
    public const double DefaultSensitivityParameter = 2.0;

    public double SigmaX { get; }
    public double SigmaY { get; }
    public int HalfSize { get; }
    public int OrientationCount { get; }
    public double OrientationStep { get; }
    public double Wavelength { get; set; } = 4.0;

    public double MagThresholdZero { get; set; } = 8.0;
    public double MagThresholdSpan { get; set; } = 64.0;
    public double NoiseThreshold { get; set; } = 2.0;

    public bool UseSimd { get; set; } = true;
    public bool UseParallel { get; set; } = true;

    private readonly float[][,] _kernels;
    private readonly double[] _orientations;
    private readonly float[] _oriStepRad;

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

        _orientations = new double[OrientationCount];
        _oriStepRad = new float[OrientationCount];
        _kernels = new float[OrientationCount][,];
        for (int i = 0; i < OrientationCount; i++)
        {
            _orientations[i] = i * OrientationStep;
            _kernels[i] = BuildKernel(_orientations[i]);
        }
        for (int i = 0; i < OrientationCount; i++) _oriStepRad[i] = (float)(_orientations[i] + Math.PI / 2.0);
    }

    private static int ComputeOrientationCount(int halfSize)
    {
        double denom = 2.0 * Math.Ceiling(0.5 * (halfSize + 0.5) * (Math.PI / 2.0));
        return Math.Max(1, (int)Math.Round(Math.PI / (Math.PI / denom)));
    }

    private float[,] BuildKernel(double theta)
    {
        int size = 2 * HalfSize + 1;
        var k = new float[size, size];
        double c = Math.Cos(theta), s = Math.Sin(theta);
        double twoSigmaX2 = 2.0 * SigmaX * SigmaX;
        double twoSigmaY2 = 2.0 * SigmaY * SigmaY;
        double twoPiOverLambda = 2.0 * Math.PI / Wavelength;

        double sum = 0.0;
        for (int y = -HalfSize; y <= HalfSize; y++)
            for (int x = -HalfSize; x <= HalfSize; x++)
            {
                double xp = x * c + y * s;
                double yp = -x * s + y * c;
                double g = Math.Exp(-(xp * xp / twoSigmaX2 + yp * yp / twoSigmaY2))
                           * Math.Cos(twoPiOverLambda * xp);
                k[x + HalfSize, y + HalfSize] = (float)g;
                sum += Math.Abs(g);
            }
        if (sum > 0)
        {
            float inv = (float)(1.0 / sum);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    k[x, y] *= inv;
        }
        return k;
    }

    public sealed class EdgeMap
    {
        public int Width { get; }
        public int Height { get; }
        public float[] Magnitude { get; }
        public float[] Angle { get; }
        public bool[] IsEdge { get; }

        public EdgeMap(int w, int h)
        {
            Width = w; Height = h;
            Magnitude = new float[w * h];
            Angle = new float[w * h];
            IsEdge = new bool[w * h];
        }

        public float GetMagnitude(int x, int y) => Magnitude[y * Width + x];
        public float GetAngle(int x, int y) => Angle[y * Width + x];
        public bool IsEdgePixel(int x, int y) => IsEdge[y * Width + x];
    }

    /// <summary>Convolve one orientation kernel over rows [y0, y1).</summary>
    private void ConvolveOrientation(float[] src, int w, int h, float[,] kernel,
                                     float[] dst, int y0, int y1)
    {
        int xs = HalfSize, xe = w - HalfSize;
        int vecWidth = Vector<float>.Count;
        int accOffset = y0 * w;

        for (int y = y0; y < y1; y++)
        {
            int rowBase = y * w;
            for (int x = xs; x < xe; x++) dst[rowBase + x] = 0f;

            for (int ky = -HalfSize; ky <= HalfSize; ky++)
            {
                int sy = y + ky;
                if (sy < 0 || sy >= h) continue;
                int srcRow = sy * w;
                int kRow = ky + HalfSize;

                int kx = -HalfSize;
                if (UseSimd && xe - xs >= vecWidth)
                {
                    int vecEnd = xs + ((xe - xs) / vecWidth) * vecWidth;
                    for (; kx <= HalfSize; kx++)
                    {
                        float kv = kernel[kx + HalfSize, kRow];
                        int x = xs;
                        if (kv == 0f) continue;
                        for (; x <= vecEnd - vecWidth; x += vecWidth)
                        {
                            var v = new Vector<float>(src, srcRow + x + kx);
                            var a = new Vector<float>(dst, rowBase + x);
                            (a + v * kv).CopyTo(dst, rowBase + x);
                        }
                        for (; x < xe; x++)
                            dst[rowBase + x] += src[srcRow + x + kx] * kv;
                    }
                }
                else
                {
                    for (; kx <= HalfSize; kx++)
                    {
                        float kv = kernel[kx + HalfSize, kRow];
                        if (kv == 0f) continue;
                        for (int x = xs; x < xe; x++)
                            dst[rowBase + x] += src[srcRow + x + kx] * kv;
                    }
                }
            }
        }
    }

    public EdgeMap Detect(GrayImage img)
    {
        int w = img.Width, h = img.Height;
        int n = w * h;
        var map = new EdgeMap(w, h);

        var src = new float[n];
        for (int i = 0; i < n; i++) src[i] = img.Data[i];

        var responses = new float[OrientationCount * n];
        var response = new float[n];

        if (UseParallel)
        {
            Parallel.For(0, OrientationCount, o =>
            {
                ConvolveOrientation(src, w, h, _kernels[o], response, HalfSize, h - HalfSize);
                Buffer.BlockCopy(response, 0, responses, o * n * sizeof(float), n * sizeof(float));
            });
        }
        else
        {
            for (int o = 0; o < OrientationCount; o++)
            {
                ConvolveOrientation(src, w, h, _kernels[o], response, HalfSize, h - HalfSize);
                Buffer.BlockCopy(response, 0, responses, o * n * sizeof(float), n * sizeof(float));
            }
        }

        // Combine orientations: magnitude = max, angle = argmax + 90deg
        int vecWidth = Vector<float>.Count;
        for (int y = 0; y < h; y++)
        {
            int x = 0;
            for (; x <= w - vecWidth; x += vecWidth)
            {
                var bestMag = new Vector<float>(float.MinValue);
                var bestOri = new Vector<int>();
                for (int o = 0; o < OrientationCount; o++)
                {
                    var m = new Vector<float>(responses, o * n + y * w + x);
                    var greater = Vector.GreaterThan(m, bestMag);
                    bestMag = Vector.ConditionalSelect(greater, m, bestMag);
                    bestOri = Vector.ConditionalSelect(greater, new Vector<int>(o), bestOri);
                }
                var magArr = new float[vecWidth];
                var oriArr = new int[vecWidth];
                bestMag.CopyTo(magArr);
                bestOri.CopyTo(oriArr);
                for (int i = 0; i < vecWidth; i++)
                {
                    int idx = y * w + x + i;
                    map.Magnitude[idx] = magArr[i];
                    map.Angle[idx] = _oriStepRad[oriArr[i]];
                    map.IsEdge[idx] = magArr[i] > MagThresholdZero;
                }
            }
            for (; x < w; x++)
            {
                int idx = y * w + x;
                float best = float.MinValue; int bestOri = 0;
                for (int o = 0; o < OrientationCount; o++)
                {
                    float m = responses[o * n + idx];
                    if (m > best) { best = m; bestOri = o; }
                }
                map.Magnitude[idx] = best;
                map.Angle[idx] = _oriStepRad[bestOri];
                map.IsEdge[idx] = best > MagThresholdZero;
            }
        }
        return map;
    }

    public void PrintInfo()
    {
        Console.WriteLine($"GaborEdgeDetector:");
        Console.WriteLine($"  sigmaX = {SigmaX:F16}");
        Console.WriteLine($"  sigmaY = {SigmaY:F16}");
        Console.WriteLine($"  halfSize = {HalfSize}  (kernel {2 * HalfSize + 1}x{2 * HalfSize + 1})");
        Console.WriteLine($"  orientations = {OrientationCount}  step = {OrientationStep * 180 / Math.PI:F2}°");
        Console.WriteLine($"  wavelength = {Wavelength:F2}  simd={UseSimd} parallel={UseParallel}");
    }
}
