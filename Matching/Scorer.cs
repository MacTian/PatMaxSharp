using PatMaxSharp.Core;

namespace PatMaxSharp.Matching;

/// <summary>
/// PatMax scoring.
///
/// Reconstructed from RVA 0xacc9b0 (reports/03-convolve.md):
///   Score = W * (M/W)^(1/N) / S
///     W = pattern feature total weight   (pattern + 0x90)
///     N = exponent denominator           (pattern + 0x8c)
///     M = matched feature count
///     S = projection normalization       (magScale-derived)
///   Clutter penalty: bad feature count beyond 2*Nfeat*threshold zeroes the score.
///
/// Result field offsets: Score +0x48, Contrast +0x4c, FitError +0x50,
///                       Coverage +0x54, Accepted +0x5c
/// </summary>
public sealed class Scorer
{
    /// <summary>Accept threshold (acceptThreshold, pattern +0x18 in RunParams).</summary>
    public double AcceptThreshold { get; set; } = 0.5;

    public double Score(PatternModel pattern, int matchedFeatures, double projectionNormalization,
                        int badFeatures, double clutterThreshold)
    {
        double n = pattern.N;
        if (n <= 0 || pattern.FeatureCount <= 0) return 0;

        // Clutter penalty: too many bad features zeroes the score
        int limit = (int)(2.0 * pattern.FeatureCount * clutterThreshold);
        if (badFeatures > limit) return 0;

        // M/W ratio normalized to [0,1] (W = total feature weight)
        double ratio = (double)matchedFeatures / pattern.FeatureCount;
        ratio = Math.Min(1.0, Math.Max(0.0, ratio));

        // Score = W * (M/W)^(1/N) / S  →  normalized form: (M/W)^(1/N) / S_norm
        double score = Math.Pow(ratio, 1.0 / n);
        if (projectionNormalization > 0) score /= projectionNormalization;

        // proportional clutter amplification
        if (badFeatures > 0 && limit > 0)
            score *= 1.0 - 0.5 * (double)badFeatures / limit;

        return Math.Max(0.0, Math.Min(1.0, score));
    }
}
