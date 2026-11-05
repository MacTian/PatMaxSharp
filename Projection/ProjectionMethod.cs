using PatMaxSharp.Core;
using PatMaxSharp.Edge;

namespace PatMaxSharp.Projection;

/// <summary>
/// 1D projection of an edge map along a search direction.
/// Parameter names from reverse engineering:
///   Projection Region Center / Projection Length / Rotation Angle /
///   Search Length / Skew Angle / Scan Mode Enabled.
/// </summary>
public sealed class ProjectionMethod
{
    /// <summary>
    /// Project the edge magnitude along direction <paramref name="angleRad"/> (radians),
    /// over a region centered at (cx, cy) with the given extent.
    /// </summary>
    public double[] Project(GaborEdgeDetector.EdgeMap edges, double cx, double cy,
                            double angleRad, int projectionLength, int searchLength)
    {
        double dx = Math.Cos(angleRad), dy = Math.Sin(angleRad);          // along search
        double px = -dy, py = dx;                                          // perpendicular (projection axis)

        int bins = Math.Max(1, projectionLength);
        var acc = new double[bins];
        int samples = Math.Max(1, searchLength);

        // Integrate along the search direction; bin by the perpendicular coordinate.
        for (int s = 0; s < samples; s++)
        {
            double t = (s - samples / 2.0) / Math.Max(1, samples / 2.0 + 0.5) * (searchLength / 2.0);
            double sx = cx + dx * t;
            double sy = cy + dy * t;
            for (int b = 0; b < bins; b++)
            {
                double u = (b - bins / 2.0) / Math.Max(1, bins / 2.0 + 0.5) * (projectionLength / 2.0);
                double ix = (int)Math.Round(sx + px * u);
                double iy = (int)Math.Round(sy + py * u);
                if (ix >= 0 && iy >= 0 && ix < edges.Width && iy < edges.Height)
                    acc[b] += edges.GetMagnitude((int)ix, (int)iy);
            }
        }
        return acc;
    }
}
