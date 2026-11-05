namespace PatMaxSharp.Projection;

/// <summary>
/// 1D Derivative-of-Gaussian kernel — the PatMax matching kernel.
///
/// Reconstructed from ccDiffOfGauss1D (RVA 0x2c8f50, reports/03-convolve.md):
///   r = max(1, floor(4*sigma))
///   C = 1 / (sigma^3 * sqrt(2*pi))
///   k[i] = i * exp(-i^2/(2 sigma^2)) * C     for i in [-r, -1]
///   half-axis L1 normalized (sum|k| = 1)
///   k[r] = 0                                  (center tap)
///   k[r+j] = -k[r-j]                          (antisymmetric)
///
///   sigma = grain * 0.25 + 0.001              (RVA 0x221620)
///   grain in [1.0, 25.5]
/// </summary>
public sealed class DiffOfGauss1D
{
    public double[] Kernel { get; }
    public int Radius { get; }
    public int Length => Kernel.Length;
    public double Sigma { get; }

    public DiffOfGauss1D(double grain)
    {
        Sigma = grain * 0.25 + 0.001;
        Radius = Math.Max(1, (int)Math.Floor(4.0 * Sigma));
        Kernel = BuildKernel(Sigma, Radius);
    }

    public static double[] BuildKernel(double sigma, int r)
    {
        int len = 2 * r + 1;
        double[] k = new double[len];
        double c = 1.0 / (Math.Pow(sigma, 3) * Math.Sqrt(2.0 * Math.PI));
        double twoSigma2 = 2.0 * sigma * sigma;

        double sum = 0.0;
        for (int i = -r; i < 0; i++)
        {
            double v = i * Math.Exp(-(i * i) / twoSigma2) * c;
            k[i + r] = v;
            sum += Math.Abs(v);
        }
        if (sum > 0.0)
        {
            for (int i = -r; i < 0; i++) k[i + r] /= sum;
        }
        k[r] = 0.0;                                  // center tap
        for (int j = 1; j <= r; j++) k[r + j] = -k[r - j];   // antisymmetric
        return k;
    }

    /// <summary>
    /// Valid cross-correlation: out[p] = sum_j in[p+j] * k[j], length N-K+1.
    /// Mirrors ccConvolve1D (RVA 0x2c9780).
    /// </summary>
    public double[] Convolve(double[] input)
    {
        int n = input.Length, klen = Kernel.Length;
        if (klen > n) return Array.Empty<double>();
        int outLen = n - klen + 1;
        var output = new double[outLen];

        // 4-way unrolled accumulation (mirrors the SSE2 kernel)
        for (int p = 0; p < outLen; p++)
        {
            double acc0 = 0, acc1 = 0, acc2 = 0, acc3 = 0;
            int j = 0;
            for (; j + 4 <= klen; j += 4)
            {
                acc0 += input[p + j] * Kernel[j];
                acc1 += input[p + j + 1] * Kernel[j + 1];
                acc2 += input[p + j + 2] * Kernel[j + 2];
                acc3 += input[p + j + 3] * Kernel[j + 3];
            }
            double acc = (acc0 + acc1) + (acc2 + acc3);
            for (; j < klen; j++) acc += input[p + j] * Kernel[j];
            output[p] = acc;
        }
        return output;
    }

    /// <summary>
    /// Antisymmetric-optimized variant: sum_j (in[j]-in[K-1-j])*k[j].
    /// Mirrors ccConvolve1DOdd (RVA 0x2c9990) — half the multiplies.
    /// </summary>
    public double[] ConvolveOdd(double[] input)
    {
        int n = input.Length, klen = Kernel.Length;
        if (klen > n) return Array.Empty<double>();
        int outLen = n - klen + 1;
        int r = (klen - 1) / 2;
        var output = new double[outLen];

        for (int p = 0; p < outLen; p++)
        {
            double acc = 0.0;
            for (int j = 0; j < r; j++)
                acc += (input[p + j] - input[p + klen - 1 - j]) * Kernel[j];
            output[p] = acc;
        }
        return output;
    }

    public void PrintInfo()
    {
        Console.WriteLine($"DiffOfGauss1D: sigma={Sigma:F6} radius={Radius} len={Length}");
        Console.WriteLine("  kernel: [" + string.Join(", ", Kernel.Select(v => v.ToString("F5"))) + "]");
    }
}
