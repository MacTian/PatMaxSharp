namespace PatMaxSharp.Matching;

/// <summary>
/// 1D peak detection over a projection.
///
/// Parameters recovered from reverse engineering (reports appendix B.3):
///   maxAcceptablePeakFraction   peak must be >= this fraction of global max
///   minAcceptablePeakFallOff    minimum drop on both sides to qualify
///   Peak Separation Threshold   minimum distance between peaks
///   truePeak                    flag for sub-sample refinement
/// </summary>
public sealed class PeakDetector
{
    public double MaxAcceptablePeakFraction { get; set; } = 0.30;
    public double MinAcceptablePeakFallOff { get; set; } = 4.0;
    public int PeakSeparationThreshold { get; set; } = 4;

    public readonly record struct Peak(double Position, double Value, int Index);

    /// <summary>Find local maxima satisfying the PatMax peak criteria.</summary>
    public List<Peak> FindPeaks(double[] projection)
    {
        var peaks = new List<Peak>();
        if (projection == null || projection.Length < 3) return peaks;

        double globalMax = projection.Max();
        if (globalMax <= 0) return peaks;
        double minValue = globalMax * MaxAcceptablePeakFraction;

        for (int i = 1; i < projection.Length - 1; i++)
        {
            double v = projection[i];
            double left = projection[i - 1];
            double right = projection[i + 1];

            // local maximum
            if (v < left || v < right) continue;
            // peak value threshold
            if (v < minValue) continue;
            // fall-off on both sides
            if (v - left < MinAcceptablePeakFallOff) continue;
            if (v - right < MinAcceptablePeakFallOff) continue;

            peaks.Add(new Peak(i, v, i));
        }

        // enforce separation (keep the strongest in each neighborhood)
        peaks.Sort((a, b) => b.Value.CompareTo(a.Value));
        var kept = new List<Peak>();
        foreach (var p in peaks)
        {
            bool tooClose = kept.Any(k => Math.Abs(k.Index - p.Index) < PeakSeparationThreshold);
            if (!tooClose) kept.Add(p);
        }
        kept.Sort((a, b) => a.Index.CompareTo(b.Index));
        return kept;
    }
}
