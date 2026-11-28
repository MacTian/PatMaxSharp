namespace ShapeAnchor.Matching;

/// <summary>
/// 2D peak / non-maximum-suppression utilities for the correlation surface.
///
/// The property names (MaxAcceptablePeakFraction, MinAcceptablePeakFallOff,
/// PeakSeparationThreshold) came from early analysis notes on a native 1D projection
/// detector. The native core is WIBU-packed, so that provenance cannot be checked; the names
/// are kept for API compatibility only.
///
/// Only MaxAcceptablePeakFraction-style gating applies in the 2D path: the caller passes an
/// absolute minValue and suppression is a radius. MinAcceptablePeakFallOff is used by
/// <see cref="FindPeaks"/> (the 1D compatibility path) and has no effect on
/// <see cref="FindPeaks2D"/>.
/// </summary>
public sealed class PeakDetector
{
    /// <summary>1D path: a peak must reach this fraction of the projection maximum to be reported.</summary>
    public double MaxAcceptablePeakFraction { get; set; } = 0.30;

    /// <summary>
    /// 1D path: absolute drop (projection units) required on both sides of a peak. Not used by
    /// the 2D path, and absolute units mean the value interacts with the caller's search length.
    /// </summary>
    public double MinAcceptablePeakFallOff { get; set; } = 4.0;

    /// <summary>1D path: minimum cell distance between two reported peaks.</summary>
    public int PeakSeparationThreshold { get; set; } = 4;

    public readonly record struct Peak(double X, double Y, double Value);

    /// <summary>
    /// 1D peak detection over a projection, kept for compatibility with callers that
    /// project the edge field along a search axis.
    /// </summary>
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
            if (v < projection[i - 1] || v < projection[i + 1]) continue;
            if (v < minValue) continue;
            if (v - projection[i - 1] < MinAcceptablePeakFallOff) continue;
            if (v - projection[i + 1] < MinAcceptablePeakFallOff) continue;
            peaks.Add(new Peak(i, 0, v));
        }

        peaks.Sort((a, b) => b.Value.CompareTo(a.Value));
        var kept = new List<Peak>();
        foreach (var p in peaks)
            if (!kept.Any(k => Math.Abs(k.X - p.X) < PeakSeparationThreshold)) kept.Add(p);
        kept.Sort((a, b) => a.X.CompareTo(b.X));
        return kept;
    }

    /// <summary>
    /// Extract the strongest local maxima of a 2D score surface with greedy
    /// non-maximum suppression. Coordinates are reported in surface cells.
    /// </summary>
    public static List<Peak> FindPeaks2D(double[] surface, int width, int height, int suppressionRadius,
                                         int maxPeaks, double minValue)
    {
        var peaks = new List<Peak>();
        if (surface == null || width <= 0 || height <= 0) return peaks;

        for (int y = 1; y < height - 1; y++)
        {
            int row = y * width;
            for (int x = 1; x < width - 1; x++)
            {
                double v = surface[row + x];
                if (v < minValue) continue;

                bool isMax = true;
                for (int dy = -1; dy <= 1 && isMax; dy++)
                {
                    int nRow = (y + dy) * width;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        if (surface[nRow + x + dx] > v) { isMax = false; break; }
                    }
                }
                if (isMax) peaks.Add(new Peak(x, y, v));
            }
        }

        peaks.Sort((a, b) => b.Value.CompareTo(a.Value));

        var kept = new List<Peak>(Math.Min(maxPeaks, peaks.Count));
        int r2 = suppressionRadius * suppressionRadius;
        foreach (var p in peaks)
        {
            bool tooClose = false;
            foreach (var k in kept)
            {
                double dx = k.X - p.X, dy = k.Y - p.Y;
                if (dx * dx + dy * dy < r2) { tooClose = true; break; }
            }
            if (tooClose) continue;
            kept.Add(p);
            if (kept.Count >= maxPeaks) break;
        }
        return kept;
    }

    /// <summary>
    /// Sub-cell parabolic refinement of a local maximum in x and y.
    /// Returns the offset from the cell centre, in cells (each in [-0.5, 0.5]).
    /// </summary>
    public static (double dx, double dy) SubCellOffset(double[] surface, int width, int x, int y)
    {
        double dx = 0, dy = 0;
        int i = y * width + x;
        double v = surface[i];

        double vl = surface[i - 1], vr = surface[i + 1];
        double denomX = vl - 2 * v + vr;
        if (Math.Abs(denomX) > 1e-12) dx = 0.5 * (vl - vr) / denomX;

        double vd = surface[i - width], vu = surface[i + width];
        double denomY = vd - 2 * v + vu;
        if (Math.Abs(denomY) > 1e-12) dy = 0.5 * (vd - vu) / denomY;

        return (Math.Clamp(dx, -0.5, 0.5), Math.Clamp(dy, -0.5, 0.5));
    }
}
