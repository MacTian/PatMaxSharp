namespace PatMaxSharp.Transform;

/// <summary>
/// Thin-Plate Spline (TPS) deformation — the PatFlex model.
///
/// Reconstructed from cc2XformDeform (reports/05-xform.md):
///   kernel K(r) = r^2 * log2(r^2)      (threshold 1e-15)
///   object layout: control count +0x70, coordinates +0x78, weights +0x98
///   control parameters: ControlPointsX / ControlPointsY / Smoothness /
///                       MaxDeformationRate / Refinement / DeformationFit
///
/// TPS model:
///   f(x) = a0 + ax*x + ay*y + Σ_i w_i K(||x - P_i||)
///   subject to Σ w_i = 0, Σ w_i P_i = 0
/// </summary>
public sealed class XformTps
{
    /// <summary>Control points (regular grid over the search region).</summary>
    public List<(double X, double Y)> ControlPoints { get; } = new();

    /// <summary>TPS weights w_i.</summary>
    public double[] Weights { get; private set; } = Array.Empty<double>();

    /// <summary>Affine part [a0, ax, ay].</summary>
    public double A0 { get; private set; }
    public double Ax { get; private set; }
    public double Ay { get; private set; }

    /// <summary>TPS kernel: K(r) = r²·log2(r²).</summary>
    public static double Kernel(double r2)
    {
        if (r2 < 1e-15) return 0.0;
        return r2 * Math.Log2(r2);
    }

    /// <summary>Create a regular control-point grid over a rectangle.</summary>
    public static XformTps CreateGrid(double minX, double minY, double maxX, double maxY,
                                      int countX, int countY)
    {
        var tps = new XformTps();
        for (int j = 0; j < countY; j++)
            for (int i = 0; i < countX; i++)
            {
                double x = countX <= 1 ? (minX + maxX) / 2 : minX + (maxX - minX) * i / (countX - 1);
                double y = countY <= 1 ? (minY + maxY) / 2 : minY + (maxY - minY) * j / (countY - 1);
                tps.ControlPoints.Add((x, y));
            }
        tps.Weights = new double[tps.ControlPoints.Count];
        return tps;
    }

    /// <summary>
    /// Fit TPS from source control points to target points (least-squares with the
    /// TPS side conditions Σw=0, Σw·P=0).
    /// </summary>
    public void Fit(List<(double X, double Y)> source, List<(double X, double Y)> target)
    {
        int n = ControlPoints.Count;
        if (source.Count != target.Count || n == 0 || source.Count < 3) return;

        // Use the control points as the TPS centers; solve for weights + affine.
        // System (n+3) x (n+3):
        //   [ K  P  1 ] [w]   [v]
        //   [ Pᵀ 0  0 ] [a] = [0]
        int m = n + 3;
        var A = new double[m, m];
        var b = new double[m];

        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
                A[i, j] = Kernel(Dist2(ControlPoints[i], ControlPoints[j]));
            A[i, n] = ControlPoints[i].X;
            A[i, n + 1] = ControlPoints[i].Y;
            A[i, n + 2] = 1.0;
            b[i] = target[i].Y;   // fit Y channel (X channel analogous)
        }
        for (int j = 0; j < n; j++)
        {
            A[n, j] = ControlPoints[j].X;
            A[n + 1, j] = ControlPoints[j].Y;
            A[n + 2, j] = 1.0;
        }

        var sol = SolveLinear(A, b);
        if (sol == null) return;
        Weights = sol.Take(n).ToArray();
        Ax = sol[n]; Ay = sol[n + 1]; A0 = sol[n + 2];
    }

    private static double Dist2((double X, double Y) a, (double X, double Y) b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return dx * dx + dy * dy;
    }

    /// <summary>Apply the TPS deformation to a point (Y channel; X channel analogous).</summary>
    public double ApplyY(double x, double y)
    {
        double v = A0 + Ax * x + Ay * y;
        for (int i = 0; i < ControlPoints.Count; i++)
        {
            double dx = x - ControlPoints[i].X, dy = y - ControlPoints[i].Y;
            v += Weights[i] * Kernel(dx * dx + dy * dy);
        }
        return v;
    }

    private static double[] SolveLinear(double[,] A, double[] b)
    {
        int n = b.Length;
        var M = new double[n, n + 1];
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++) M[i, j] = A[i, j];
            M[i, n] = b[i];
        }
        // Gaussian elimination with partial pivoting
        for (int col = 0; col < n; col++)
        {
            int piv = col;
            for (int r = col + 1; r < n; r++)
                if (Math.Abs(M[r, col]) > Math.Abs(M[piv, col])) piv = r;
            if (Math.Abs(M[piv, col]) < 1e-12) return null;
            if (piv != col)
                for (int k = col; k <= n; k++) (M[col, k], M[piv, k]) = (M[piv, k], M[col, k]);
            double d = M[col, col];
            for (int k = col; k <= n; k++) M[col, k] /= d;
            for (int r = 0; r < n; r++)
            {
                if (r == col) continue;
                double f = M[r, col];
                for (int k = col; k <= n; k++) M[r, k] -= f * M[col, k];
            }
        }
        var x = new double[n];
        for (int i = 0; i < n; i++) x[i] = M[i, n];
        return x;
    }
}
