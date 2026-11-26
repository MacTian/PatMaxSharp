using PatMaxSharp.Core;

namespace PatMaxSharp.Matching;

/// <summary>
/// PatMax scoring.
///
/// Score form:
///   Score = W * (M/W)^(1/N) / S
///     W = pattern feature total weight
///     N = exponent denominator
///     M = matched feature weight
///     S = projection normalization       (magScale-derived)
///
/// Evidence level: this formula is an *engineering reproduction* of PatMax's documented
/// behaviour (a half-occluded part still scores ~0.7 - see below), not a transcription of
/// machine code. Early analysis notes cited a native RVA (0xacc9b0) and struct offsets
/// (pattern +0x8c / +0x90) as its source; the native core cogxpma.dll is WIBU-packed
/// (17 of 18 sections are __wibu*, entropy 7.99-8.00, no .text and no .rdata), so those
/// numbers cannot be verified statically and are deliberately not repeated here
/// (docs/evidence/native-strings.md, section 7).
///
/// In this implementation every feature contributes a *soft* match value in [0,1] (its
/// directional-derivative response above the contrast floor, normalised by the level contrast
/// reference), so M/W becomes a weighted coverage fraction rather than a hard count. The
/// exponent 1/N stays, because that is what makes a half-occluded part still score ~0.7 —
/// the defining behaviour of PatMax scoring.
///
/// Two calibration notes for this implementation: the caller (SearchEngine) always passes
/// S = 1.0, so the normalisation term is folded into the per-feature response instead of being
/// applied here; and with the default ClutterWeight the ~0.7 half-occlusion figure is pulled
/// down further (0.707 times the clutter penalty), which is why the bundled accuracy case uses
/// a 22 percent occlusion rather than a half-covered part.
///
/// The exponent is passed in by the caller (it is derived from <see cref="PatternModel.N"/>,
/// the model's own N field), so there is exactly one knob: the pattern's N.
/// </summary>
public sealed class Scorer
{
    /// <summary>
    /// Accept threshold (acceptThreshold of the run parameters). Kept for API compatibility:
    /// the search engine gates results with SearchOptions.AcceptThreshold instead, so this
    /// copy is not consulted by the search itself.
    /// </summary>
    public double AcceptThreshold { get; set; } = 0.5;

    /// <summary>
    /// Threshold used to gate coarse candidates (CoarseAcceptThreshold, default 0.33). As with
    /// AcceptThreshold this copy is informational only - the search reads
    /// SearchOptions.CoarseAcceptThreshold.
    /// </summary>
    public double CoarseAcceptThreshold { get; set; } = 0.33;

    /// <summary>Weight of the clutter penalty (0 disables).</summary>
    public double ClutterWeight { get; set; } = 0.5;

    /// <summary>Reported score for a hypothesis. Coverage and clutter are the raw inputs.</summary>
    public readonly record struct ScoreBreakdown(
        double Score, double Coverage, double ClutterPenalty, int Matched, int Total);

    /// <summary>
    /// Combine coverage, contrast and clutter into the final PatMax-style score.
    /// </summary>
    /// <param name="matchedWeight">Sum of weights of features that matched (M).</param>
    /// <param name="totalWeight">Total pattern weight (W).</param>
    /// <param name="matchedCount">Number of matched features.</param>
    /// <param name="totalCount">Total feature count.</param>
    /// <param name="clutterFraction">Fraction of the model footprint covered by unmodelled edges (0..1).</param>
    /// <param name="projectionNormalization">S — contrast/magnitude normalisation (1.0 = nominal).</param>
    /// <param name="exponent">1/N — 0.5 makes 50% coverage score 0.71, as PatMax does.</param>
    public ScoreBreakdown Score(
        double matchedWeight, double totalWeight, int matchedCount, int totalCount,
        double clutterFraction = 0.0, double projectionNormalization = 1.0, double exponent = 0.5)
    {
        if (totalWeight <= 0 || totalCount <= 0) return new ScoreBreakdown(0, 0, 1, 0, 0);

        double coverage = Math.Clamp(matchedWeight / totalWeight, 0.0, 1.0);
        double clutter = ClutterWeight > 0
            ? Math.Clamp(1.0 - ClutterWeight * Math.Clamp(clutterFraction, 0.0, 1.0), 0.0, 1.0)
            : 1.0;
        double norm = projectionNormalization > 0 ? projectionNormalization : 1.0;

        double score = Math.Pow(coverage, Math.Clamp(exponent, 1e-3, 4.0)) * clutter / norm;
        return new ScoreBreakdown(Math.Clamp(score, 0.0, 1.0), coverage, clutter, matchedCount, totalCount);
    }

    /// <summary>Convenience overload that takes the exponent from the model's own N field.</summary>
    public ScoreBreakdown Score(PatternModel pattern, int matchedFeatures, int badFeatures,
        double clutterFraction = 0.0, double projectionNormalization = 1.0)
    {
        if (pattern == null || pattern.FeatureCount <= 0) return new ScoreBreakdown(0, 0, 1, 0, 0);
        double exponent = 1.0 / Math.Max(1.0, pattern.N);
        return Score(matchedFeatures, pattern.W, matchedFeatures, pattern.FeatureCount,
            clutterFraction, projectionNormalization, exponent);
    }
}

/// <summary>
/// Contrast / magnitude statistics of one pyramid level. Used to normalise the
/// directional-derivative responses so scores do not drift with lighting or scale.
/// </summary>
public readonly struct LevelStats
{
    /// <summary>Robust contrast reference (80th percentile of edge magnitudes) used to normalise responses.</summary>
    public readonly float MagnitudeReference;

    /// <summary>Largest edge magnitude at this pyramid level.</summary>
    public readonly float MaxMagnitude;

    private LevelStats(float reference, float max)
    {
        MagnitudeReference = reference;
        MaxMagnitude = max;
    }

    /// <summary>
    /// Reference magnitude = 80th percentile over edge pixels (robust against a few
    /// specular highlights dominating the maximum).
    ///
    /// Computed from a histogram rather than by sorting a list of every edge pixel: this runs
    /// once per pyramid level and the sort used to cost more than the convolution itself.
    /// </summary>
    public static LevelStats Compute(Edge.EdgeMap map)
    {
        if (map.EdgePixelCount == 0) return new LevelStats(1f, Math.Max(1f, map.MaxMagnitude));

        const int bins = 1024;
        var histogram = new int[bins];
        int counted = 0;
        float top = map.MaxMagnitude > 0 ? map.MaxMagnitude : 1f;

        for (int i = 0; i < map.Magnitude.Length; i++)
        {
            if (!map.IsEdge[i]) continue;
            int bin = (int)(map.Magnitude[i] * (bins - 1) / top);
            if (bin < 0) bin = 0;
            else if (bin >= bins) bin = bins - 1;
            histogram[bin]++;
            counted++;
        }

        int target = (int)(0.80 * (counted - 1));
        int running = 0;
        int found = bins - 1;
        for (int b = 0; b < bins; b++)
        {
            running += histogram[b];
            if (running > target) { found = b; break; }
        }

        float reference = found * top / (bins - 1);
        return new LevelStats(Math.Max(1f, reference), Math.Max(1f, top));
    }
}
