namespace PatMaxSharp.Fit;

/// <summary>
/// Moment-based closed-form least-squares pose estimator.
///
/// Reconstructed from ccLSPointToPointFitter (reports/04-fitting.md):
///   type 3: rigid (rotation+translation)  theta = atan2(H10-H01, trace(H))
///   type 4: similarity (+uniform scale)
///   type 5: eigenvalue method (Umeyama 2D)
///   type 6: affine (PMAlign actually uses this)
///           A = H * C2^-1 ,  t = mu1 - A * mu2
///
/// Output: cc2XformLinear 7 doubles [tx, ty, m00, m01, m10, m11, det]
/// </summary>
public sealed class LeastSquaresFitter
{
    public enum FitType { Rigid = 3, Similarity = 4, Eigenvalue = 5, Affine = 6 }

    public readonly record struct Point2D(double X, double Y);

    /// <summary>Result transform in cc2XformLinear layout.</summary>
    public readonly record struct LinearXform(
        double Tx, double Ty, double M00, double M01, double M10, double M11, double Det)
    {
        public double[] ToArray() => new[] { Tx, Ty, M00, M01, M10, M11, Det };

        public Point2D Apply(Point2D p) =>
            new Point2D(M00 * p.X + M01 * p.Y + Tx, M10 * p.X + M11 * p.Y + Ty);
    }

    public LinearXform Fit(IReadOnlyList<Point2D> source, IReadOnlyList<Point2D> target, FitType type = FitType.Affine)
    {
        if (source.Count != target.Count || source.Count < 3)
            throw new ArgumentException("need at least 3 corresponding points");

        int n = source.Count;
        double sx = 0, sy = 0, tx = 0, ty = 0;
        for (int i = 0; i < n; i++)
        {
            sx += source[i].X; sy += source[i].Y;
            tx += target[i].X; ty += target[i].Y;
        }
        double muSx = sx / n, muSy = sy / n, muTx = tx / n, muTy = ty / n;

        // cross-covariance H (target x source) and source covariance C2
        double h00 = 0, h01 = 0, h10 = 0, h11 = 0;
        double c00 = 0, c01 = 0, c10 = 0, c11 = 0;
        for (int i = 0; i < n; i++)
        {
            double dxs = source[i].X - muSx, dys = source[i].Y - muSy;
            double dxt = target[i].X - muTx, dyt = target[i].Y - muTy;
            h00 += dxt * dxs; h01 += dxt * dys;
            h10 += dyt * dxs; h11 += dyt * dys;
            c00 += dxs * dxs; c01 += dxs * dys;
            c10 += dys * dxs; c11 += dys * dys;
        }

        switch (type)
        {
            case FitType.Rigid:
            {
                double theta = Math.Atan2(h10 - h01, h00 + h11);
                double cos = Math.Cos(theta), sin = Math.Sin(theta);
                return new LinearXform(
                    muTx - (cos * muSx - sin * muSy),
                    muTy - (sin * muSx + cos * muSy),
                    cos, -sin, sin, cos, 1.0);
            }
            case FitType.Affine:
            default:
            {
                // A = H * C2^-1
                double det = c00 * c11 - c01 * c10;
                if (Math.Abs(det) < 1e-12)
                    throw new InvalidOperationException("singular covariance matrix");
                double inv00 = c11 / det, inv01 = -c01 / det;
                double inv10 = -c10 / det, inv11 = c00 / det;

                double m00 = h00 * inv00 + h01 * inv10;
                double m01 = h00 * inv01 + h01 * inv11;
                double m10 = h10 * inv00 + h11 * inv10;
                double m11 = h10 * inv01 + h11 * inv11;

                double ttx = muTx - (m00 * muSx + m01 * muSy);
                double tty = muTy - (m10 * muSx + m11 * muSy);
                double detA = m00 * m11 - m01 * m10;
                return new LinearXform(ttx, tty, m00, m01, m10, m11, detA);
            }
        }
    }
}
