namespace ShapeAnchor.Fit;

/// <summary>
/// Closed-form least-squares pose estimators.
///
/// The fit-type numbering (3 rigid, 4 similarity, 5 eigenvalue, 6 affine) is inherited from
/// early analysis notes on a native ccLSPointToPointFitter. That class name and its numbering
/// live in the WIBU-packed native core and are not statically verifiable, so the numbers are
/// kept only as a naming convention for the enum. The maths below is plain closed-form moment
/// matching, written from first principles:
///
///   Rigid      = 3   rotation + translation, unit scale
///   Similarity = 4   rotation + translation + uniform scale (closed form) &lt;-- used for
///                    sub-pixel pose refinement
///   Eigenvalue = 5   the *same* closed-form 2D similarity as type 4; the Umeyama /
///                    eigen-decomposition route the name suggests is not implemented
///   Affine     = 6   full affine least squares (PMAlign's nominal model fit)
///
/// The transform is stored as 7 doubles [tx, ty, m00, m01, m10, m11, det] - the field order
/// the early notes gave for the native cc2XformLinear record. That order is a local
/// convention in this project; no native offset is claimed for it.
/// </summary>
public sealed class LeastSquaresFitter
{
    public enum FitType { Rigid = 3, Similarity = 4, Eigenvalue = 5, Affine = 6 }

    /// <summary>A 2D point in pixels (image or model coordinates, depending on the axis).</summary>
    public readonly record struct Point2D(double X, double Y);

    /// <summary>Result transform in the 7-double layout [tx, ty, m00, m01, m10, m11, det].</summary>
    public readonly record struct LinearXform(
        double Tx, double Ty, double M00, double M01, double M10, double M11, double Det)
    {
        public double[] ToArray() => new[] { Tx, Ty, M00, M01, M10, M11, Det };

        public Point2D Apply(Point2D p) =>
            new(M00 * p.X + M01 * p.Y + Tx, M10 * p.X + M11 * p.Y + Ty);

        /// <summary>Rotation angle (radians) implied by the linear part.</summary>
        public double Angle => Math.Atan2(M10, M00);

        /// <summary>Uniform scale implied by the linear part (sqrt of |det|).</summary>
        public double Scale => Math.Sqrt(Math.Abs(Det));
    }

    /// <summary>Result of a 2D similarity fit — exactly what a pose refinement needs.</summary>
    public readonly record struct Pose(double X, double Y, double Angle, double Scale, double RmsError, int Inliers)
    {
        public static Pose Identity => new(0, 0, 0, 1, 0, 0);
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
            case FitType.Similarity:
            case FitType.Eigenvalue:
                {
                    // 2D similarity in closed form: s * exp(i*theta) = sum(p*c') / sum|c'|^2
                    double re = h00 + h11;
                    double im = h10 - h01;
                    double energy = c00 + c11;
                    if (energy < 1e-12) throw new InvalidOperationException("degenerate correspondences");
                    double theta = Math.Atan2(im, re);
                    double scale = Math.Sqrt(re * re + im * im) / energy;
                    double cos = Math.Cos(theta), sin = Math.Sin(theta);
                    double m00 = scale * cos, m01 = -scale * sin;
                    double m10 = scale * sin, m11 = scale * cos;
                    return new LinearXform(
                        muTx - (m00 * muSx + m01 * muSy),
                        muTy - (m10 * muSx + m11 * muSy),
                        m00, m01, m10, m11, scale * scale);
                }
            case FitType.Affine:
            default:
                {
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

    /// <summary>
    /// Robust 2D similarity pose from model points to measured image points.
    ///
    /// This is the step that turns a discrete (position, angle) hypothesis into a
    /// sub-pixel pose: correspondences come from edge-normal refinement, so the fit
    /// sees real measured points instead of the predicted ones (a defect in the
    /// previous revision, where source and target were identical and the fit was
    /// therefore always the identity).
    /// </summary>
    /// <param name="model">Model points relative to the pattern origin.</param>
    /// <param name="measured">Measured image points.</param>
    /// <param name="weights">Optional per-point weights.</param>
    /// <param name="maxRmsError">Correspondences above this residual are dropped (outliers).</param>
    public Pose FitSimilarity(
        IReadOnlyList<Point2D> model, IReadOnlyList<Point2D> measured,
        IReadOnlyList<double> weights = null, double maxRmsError = 2.0)
    {
        if (model.Count != measured.Count || model.Count < 2)
            throw new ArgumentException("need at least 2 corresponding points");

        int n = model.Count;
        var keep = new bool[n];
        for (int i = 0; i < n; i++) keep[i] = true;

        Pose pose = Pose.Identity;
        double rms = 0;

        for (int iter = 0; iter < 3; iter++)
        {
            double sw = 0, mx = 0, my = 0, ipx = 0, ipy = 0;
            for (int i = 0; i < n; i++)
            {
                if (!keep[i]) continue;
                double w = weights != null ? Math.Max(1e-6, weights[i]) : 1.0;
                sw += w;
                mx += model[i].X * w; my += model[i].Y * w;
                ipx += measured[i].X * w; ipy += measured[i].Y * w;
            }
            if (sw <= 0) break;
            mx /= sw; my /= sw; ipx /= sw; ipy /= sw;

            double re = 0, im = 0, energy = 0;
            for (int i = 0; i < n; i++)
            {
                if (!keep[i]) continue;
                double w = weights != null ? Math.Max(1e-6, weights[i]) : 1.0;
                double mxr = model[i].X - mx, myr = model[i].Y - my;
                double pxr = measured[i].X - ipx, pyr = measured[i].Y - ipy;
                // (p * conj(m)) accumulated as a complex product
                re += w * (pxr * mxr + pyr * myr);
                im += w * (pyr * mxr - pxr * myr);
                energy += w * (mxr * mxr + myr * myr);
            }
            if (energy < 1e-12) break;

            double theta = Math.Atan2(im, re);
            double scale = Math.Sqrt(re * re + im * im) / energy;
            if (scale < 0.05 || scale > 20) break;

            double cos = Math.Cos(theta), sin = Math.Sin(theta);
            double tx = ipx - scale * (cos * mx - sin * my);
            double ty = ipy - scale * (sin * mx + cos * my);

            // residual pass -> drop outliers
            double sumSq = 0;
            int inliers = 0;
            var residuals = new double[n];
            for (int i = 0; i < n; i++)
            {
                double ex = scale * (cos * model[i].X - sin * model[i].Y) + tx;
                double ey = scale * (sin * model[i].X + cos * model[i].Y) + ty;
                double dx = ex - measured[i].X, dy = ey - measured[i].Y;
                residuals[i] = Math.Sqrt(dx * dx + dy * dy);
                if (keep[i])
                {
                    sumSq += residuals[i] * residuals[i];
                    inliers++;
                }
            }
            rms = inliers > 0 ? Math.Sqrt(sumSq / inliers) : 0;
            pose = new Pose(tx, ty, theta, scale, rms, inliers);

            if (iter < 2)
            {
                double limit = Math.Max(maxRmsError, rms * 2.5);
                for (int i = 0; i < n; i++) keep[i] = residuals[i] <= limit;
                // guarantee a fit on the next iteration
                int remaining = 0;
                for (int i = 0; i < n; i++) if (keep[i]) remaining++;
                if (remaining < 3) { for (int i = 0; i < n; i++) keep[i] = true; break; }
            }
        }
        return pose;
    }
}
