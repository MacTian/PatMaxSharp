namespace PatMaxSharp.Core;

/// <summary>
/// CogPMAlignTrainAlgorithmConstants — values read from the installed assembly's Constant table.
/// </summary>
public enum TrainAlgorithmConstants
{
    PatMax = 0,
    PatQuick = 1,
    PatMaxAndPatQuick = 2,
    PatFlex = 3,
    PatMaxHighSensitivity = 4,
    PatMaxPerspective = 5
}

/// <summary>
/// CogPMAlignRunAlgorithmConstants — values read from the installed assembly's Constant table.
/// </summary>
public enum RunAlgorithmConstants
{
    PatMax = 0,
    PatQuick = 1,
    BestTrained = 2,
    PatFlex = 3,
    PatMaxPerspective = 4
}

/// <summary>
/// Trained PatMax pattern model.
///
/// Storage is struct-of-arrays: the matcher walks these arrays millions of times per
/// search, and sequential float arrays let it stream without pointer chasing.
///
/// Field semantics (W = total feature weight, N = exponent denominator, a trained flag and
/// the training algorithm id) follow early analysis notes on the native pattern object
/// layout. Those notes also quoted byte offsets into that object
/// (pattern +0x8c / +0x90 / +0x230 / +0x788); the native core is WIBU-packed, so the offsets
/// cannot be checked statically and are not reproduced here. Only the semantics are used,
/// and they are re-derived from this class's own behaviour.
/// </summary>
public sealed class PatternModel
{
    // ---- feature arrays (length >= Count; grown geometrically) ----
    private float[] _px = new float[256];
    private float[] _py = new float[256];
    private float[] _nx = new float[256];
    private float[] _ny = new float[256];
    private float[] _mag = new float[256];
    private float[] _wt = new float[256];
    private sbyte[] _pol = new sbyte[256];

    /// <summary>Number of stored features.</summary>
    public int Count { get; private set; }

    /// <summary>Backing array of feature X coordinates (valid up to <see cref="Count"/>).</summary>
    public float[] Px => _px;

    /// <summary>Backing array of feature Y coordinates (valid up to <see cref="Count"/>).</summary>
    public float[] Py => _py;

    /// <summary>Backing array of unit edge-normal X components.</summary>
    public float[] Nx => _nx;

    /// <summary>Backing array of unit edge-normal Y components.</summary>
    public float[] Ny => _ny;

    /// <summary>Backing array of feature gradient magnitudes (training contrast).</summary>
    public float[] MagArray => _mag;

    /// <summary>Backing array of scorer weights.</summary>
    public float[] Weights => _wt;

    /// <summary>Backing array of edge polarities (+1 dark-to-bright, -1 bright-to-dark).</summary>
    public sbyte[] Polarity => _pol;

    /// <summary>Total feature weight W.</summary>
    public double W { get; private set; } = 1.0;

    /// <summary>Exponent denominator N of the score formula (Score = (M/W)^(1/N)).</summary>
    public double N { get; set; } = 2.0;

    /// <summary>Pattern origin in training-image coordinates.</summary>
    public double OriginX { get; set; }
    /// <summary>Pattern origin Y in training-image coordinates.</summary>
    public double OriginY { get; set; }

    /// <summary>Region of the training image that contributed features.</summary>
    public RectI Roi { get; set; }

    /// <summary>Size of the training image (needed to rebuild cell keys).</summary>
    public int TrainWidth { get; set; }
    /// <summary>Height of the training image.</summary>
    public int TrainHeight { get; set; }

    /// <summary>
    /// Training algorithm (CogPMAlignTrainAlgorithmConstants, values verified against the
    /// installed assembly's Constant table — see docs/evidence/params-reference.md).
    /// </summary>
    public int TrainAlgorithm { get; set; } = (int)PatMaxSharp.Core.TrainAlgorithmConstants.PatMax;

    /// <summary>Effective grain (coarse/fine) used to build the smoothing kernel.</summary>
    public double Grain { get; set; } = 1.0;

    /// <summary>
    /// Ignore edge polarity: the matcher compares |directional response| instead of the signed
    /// one, so a part seen as dark-on-light also matches when it is seen as light-on-dark
    /// (backlight, silhouette, inverted illumination). This is CogPMAlignPattern.IgnorePolarity,
    /// which the VisionPro PatMax demo runs with enabled.
    /// </summary>
    public bool PolarityAgnostic { get; set; }

    /// <summary>Reference contrast: 75th percentile of feature magnitudes at train time, floored at 1.0.</summary>
    public double ContrastRef { get; set; } = 1.0;

    /// <summary>Maximum distance from the origin to any feature (model radius, pixels).</summary>
    public double ExtentRadius { get; private set; } = 1.0;

    /// <summary>Diameter of the model footprint, used for overlap suppression.</summary>
    public double Diameter => 2.0 * ExtentRadius;

    /// <summary>True when the model holds at least one feature.</summary>
    public bool Trained => Count > 0;

    /// <summary>Number of stored features (alias of <see cref="Count"/>).</summary>
    public int FeatureCount => Count;

    /// <summary>True when the model captured a template (compatibility flag).</summary>
    public bool HasTemplate => Count > 0;

    /// <summary>Featurelet view of the model (allocated on first access, cached).</summary>
    public List<Featurelet> Featurelets
    {
        get
        {
            if (_featurelets == null || _featurelets.Count != Count)
            {
                var list = new List<Featurelet>(Count);
                for (int i = 0; i < Count; i++)
                    list.Add(MakeFeaturelet(i));
                _featurelets = list;
            }
            return _featurelets;
        }
    }
    private List<Featurelet> _featurelets;

    /// <summary>Materialise feature <paramref name="i"/> as a <see cref="Featurelet"/>.</summary>
    public Featurelet GetFeature(int i) => MakeFeaturelet(i);

    private Featurelet MakeFeaturelet(int i) => new()
    {
        X = _px[i], Y = _py[i],
        Nx = _nx[i], Ny = _ny[i],
        Polarity = _pol[i],
        Magnitude = _mag[i],
        Weight = _wt[i],
        ChainIndex = 0,
        ChainPosition = 0
    };

    // ------------------------------------------------------------------
    //  Building
    // ------------------------------------------------------------------
    /// <summary>Drop every feature and its cached Featurelet list (the model becomes untrained).</summary>
    public void Reset()
    {
        Count = 0;
        W = 0;
        _featurelets = null;
    }

    /// <summary>
    /// Append one feature. Coordinates are in training-image pixels, (nx, ny) is the unit edge
    /// normal, <paramref name="polarity"/> is +1 or -1, and <paramref name="weight"/> is the
    /// scorer weight. Call <see cref="Seal"/> once all features are in.
    /// </summary>
    public void AddFeature(float x, float y, float nx, float ny, float magnitude, sbyte polarity, float weight)
    {
        if (Count == _px.Length) Grow();
        int i = Count++;
        _px[i] = x; _py[i] = y;
        _nx[i] = nx; _ny[i] = ny;
        _mag[i] = magnitude;
        _pol[i] = polarity;
        _wt[i] = weight;
        _featurelets = null;
    }

    /// <summary>Compatibility entry point (used by older call sites and the UI).</summary>
    public void AddFeaturelet(Featurelet f) =>
        AddFeature(f.X, f.Y, f.Nx, f.Ny, f.Magnitude, f.Polarity, (float)f.Weight);

    private void Grow()
    {
        int n = _px.Length * 2;
        Array.Resize(ref _px, n);
        Array.Resize(ref _py, n);
        Array.Resize(ref _nx, n);
        Array.Resize(ref _ny, n);
        Array.Resize(ref _mag, n);
        Array.Resize(ref _wt, n);
        Array.Resize(ref _pol, n);
    }

    /// <summary>
    /// Finalise derived quantities after all features are added: total weight,
    /// contrast reference (robust percentile), model radius.
    /// </summary>
    public void Seal()
    {
        double w = 0, maxR2 = 0;
        for (int i = 0; i < Count; i++)
        {
            w += _wt[i];
            double dx = _px[i] - OriginX, dy = _py[i] - OriginY;
            double r2 = dx * dx + dy * dy;
            if (r2 > maxR2) maxR2 = r2;
        }
        W = w > 0 ? w : 1.0;
        ExtentRadius = Math.Max(1.0, Math.Sqrt(maxR2));

        if (Count > 0)
        {
            var mags = new float[Count];
            Array.Copy(_mag, mags, Count);
            Array.Sort(mags);
            int idx = (int)(0.75 * (Count - 1));
            ContrastRef = Math.Max(1.0, mags[idx]);
        }
        else ContrastRef = 1.0;

        TrimArrays();
        _featurelets = null;
    }

    private void TrimArrays()
    {
        if (Count == 0 || Count == _px.Length) return;
        Array.Resize(ref _px, Count);
        Array.Resize(ref _py, Count);
        Array.Resize(ref _nx, Count);
        Array.Resize(ref _ny, Count);
        Array.Resize(ref _mag, Count);
        Array.Resize(ref _wt, Count);
        Array.Resize(ref _pol, Count);
    }

    public override string ToString() =>
        $"Pattern(features={Count}, W={W:F2}, N={N:F2}, grain={Grain:F2}, " +
        $"roi={Roi}, origin=({OriginX:F1},{OriginY:F1}), r={ExtentRadius:F1})";
}
