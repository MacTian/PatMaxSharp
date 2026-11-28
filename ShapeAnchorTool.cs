using System.Diagnostics;
using ShapeAnchor.Core;
using ShapeAnchor.Edge;
using ShapeAnchor.Fit;
using ShapeAnchor.Matching;
using ShapeAnchor.Projection;
using ShapeAnchor.Training;

namespace ShapeAnchor;

/// <summary>
/// PatMax (PMAlign) work-alike: train a pattern from a region, then locate instances of it.
///
/// Pipeline:
///   Train: gradient field -> ridge thinning -> sparse feature set (position, edge normal,
///          polarity, magnitude) -> W / N / contrast reference
///   Run:   edge pyramid -> exhaustive direction-sensitive correlation at the coarsest
///          level -> level-by-level refinement -> full-feature scoring
///          -> edge-normal sub-pixel correspondence -> robust similarity pose fit
///
/// The public surface stays compatible with the earlier revision (Train/Run/Pattern and the
/// parameter properties), because the WPF front end binds to it directly.
/// </summary>
public sealed class ShapeAnchorTool
{
    /// <summary>Edge detector shared by training and search (owns the noise floor and the Gabor settings).</summary>
    public GaborEdgeDetector EdgeDetector { get; }

    /// <summary>Search engine used by <see cref="Run"/> and <see cref="RunMulti"/>.</summary>
    public SearchEngine Engine { get; } = new();

    /// <summary>Standalone scorer, exposed for callers that score their own hypotheses.</summary>
    public Scorer Scorer { get; } = new();

    /// <summary>Least-squares pose fitter, exposed for <see cref="FitPose"/> and direct use.</summary>
    public LeastSquaresFitter Fitter { get; } = new();

    /// <summary>Peak detector, exposed for callers that post-process their own correlation surfaces.</summary>
    public PeakDetector PeakDetector { get; } = new();

    /// <summary>1D projector, exposed for callers that need the projection of an edge field.</summary>
    public ProjectionMethod Projector { get; } = new();

    /// <summary>Most recently trained model.</summary>
    public PatternModel Pattern { get; private set; }

    /// <summary>Timing/statistics of the last run.</summary>
    public SearchDiagnostics LastDiagnostics { get; } = new();

    // ---- scoring ----
    /// <summary>Minimum score to report a result as accepted.</summary>
    public double AcceptThreshold { get; set; } = 0.7;

    /// <summary>Minimum normalised contrast of the matched edges.</summary>
    public double ContrastThreshold { get; set; } = 0.08;

    /// <summary>Exponent denominator N: score = coverage^(1/N).</summary>
    public double ScoreExponentN { get; set; } = 2.0;

    /// <summary>Coarse-stage gate (CoarseAcceptThreshold, default 0.33).</summary>
    public double CoarseAcceptThreshold { get; set; } = 0.33;

    // ---- search space ----
    /// <summary>Lower bound of the rotation search range, degrees.</summary>
    public double RotationAngleMin { get; set; } = -180;

    /// <summary>Upper bound of the rotation search range, degrees.</summary>
    public double RotationAngleMax { get; set; } = 180;

    /// <summary>Lower bound of the scale search range.</summary>
    public double ScaleMin { get; set; } = 1.0;

    /// <summary>Upper bound of the scale search range.</summary>
    public double ScaleMax { get; set; } = 1.0;

    // ---- performance knobs (compatibility names preserved) ----
    /// <summary>Coarse position stride in level pixels (1 = thorough, 4 = fast).</summary>
    public int PrecisionStep { get; set; } = 2;

    /// <summary>Sub-pixel edge-normal refinement + least-squares pose fit.</summary>
    public bool SubpixelRefinement { get; set; } = true;

    /// <summary>Merge instances closer than <see cref="ClusterRadius"/>.</summary>
    public bool EnableClustering { get; set; } = true;

    /// <summary>Minimum separation between two reported instances, pixels.</summary>
    public double ClusterRadius { get; set; } = 8.0;

    /// <summary>
    /// Allowed overlap between two reported instances (Cognex XYOverlap, 0 = disjoint,
    /// 1 = may fully overlap). Converted to a centre distance using the model diameter.
    /// </summary>
    public double XYOverlap { get; set; } = 0.5;

    /// <summary>
    /// Extra Gaussian blur applied before edge extraction. Only used when the value is above
    /// 0.6, so the 0.5 default leaves the image untouched (edge extraction already smooths
    /// with its derivative-of-Gaussian kernel).
    /// </summary>
    public double PreSmoothSigma { get; set; } = 0.5;

    /// <summary>Features used in the exhaustive stage.</summary>
    public int CoarseFeatureCount { get; set; } = 32;

    /// <summary>Features used in the intermediate refinement stages.</summary>
    public int MidFeatureCount { get; set; } = 96;

    /// <summary>Hypotheses carried from the coarse stage.</summary>
    public int MaxCandidates { get; set; } = 20;

    /// <summary>Angular step of the exhaustive stage, degrees.</summary>
    public double CoarseAngleStepDeg { get; set; } = 15.0;

    /// <summary>Maximum pyramid levels.</summary>
    public int MaxLevels { get; set; } = 4;

    /// <summary>Score includes unmodelled-edge clutter.</summary>
    public bool UseClutter { get; set; } = true;

    /// <summary>Normalised (gain-invariant) correlation in the coarse stage; off by default (see SearchOptions).</summary>
    public bool NormalizedCorrelation { get; set; }

    /// <summary>
    /// Wall-clock budget for one run, milliseconds, declared for API compatibility. It is
    /// copied into SearchOptions but the search engine does not consult it, so a run is not
    /// cut short by this value.
    /// </summary>
    public double TimeoutMs { get; set; }

    // ---- training ----
    /// <summary>Upper bound on the number of features stored in a trained model.</summary>
    public int TrainMaxFeatures { get; set; } = 120;

    /// <summary>Minimum distance between two stored features, pixels.</summary>
    public double TrainMinSpacing { get; set; } = 2.0;

    /// <summary>Discard features weaker than this fraction of the strongest edge in the region.</summary>
    public double TrainContrastThreshold { get; set; } = 0.08;

    /// <summary>Ignore edge polarity when training (CogPMAlignPattern.IgnorePolarity).</summary>
    public bool IgnorePolarity { get; set; }

    /// <summary>
    /// Training-side edge floor as a fraction of the strongest edge in the region (0..0.95).
    /// Higher values keep only the strong geometric edges and drop shading gradients, which is what
    /// makes a model survive a change of illumination. Maps to TrainOptions.ContrastThreshold.
    /// </summary>
    public double TrainFeatureFloor { get; set; } = 0.08;

    /// <summary>Polarity coherence gate; raised to 0.7 automatically for polarity-agnostic models.</summary>
    public double MinPolarityCoherence { get; set; } = -1;   // -1 = auto

    /// <summary>Automatic edge threshold (adapts the floor to the image contrast).</summary>
    public bool AutoEdgeThreshold
    {
        get => EdgeDetector.AutoNoiseFloor;
        set => EdgeDetector.AutoNoiseFloor = value;
    }

    /// <summary>Edge threshold sent to the detector (the original EdgeThreshold).</summary>
    public double EdgeThreshold
    {
        get => EdgeDetector.NoiseFloor;
        set => EdgeDetector.NoiseFloor = value;
    }

    /// <summary>Gradient magnitude below which nothing is considered an edge.</summary>
    public double NoiseFloor
    {
        get => EdgeDetector.NoiseFloor;
        set => EdgeDetector.NoiseFloor = value;
    }

    /// <summary>Create a tool with a default <see cref="GaborEdgeDetector"/>.</summary>
    public ShapeAnchorTool()
    {
        EdgeDetector = new GaborEdgeDetector();
    }

    /// <summary>Create a tool that shares an existing edge detector (its settings are then live).</summary>
    public ShapeAnchorTool(GaborEdgeDetector detector)
    {
        EdgeDetector = detector;
    }

    // ------------------------------------------------------------------
    //  Training
    // ------------------------------------------------------------------
    /// <summary>Train using the whole image as the region.</summary>
    public PatternModel Train(GrayImage image, double originX = 0, double originY = 0, double grain = 1.0)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        return Train(image, new RectI(0, 0, image.Width, image.Height), originX, originY, grain);
    }

    /// <summary>Train from a region of <paramref name="image"/>; the origin is in image coordinates.</summary>
    public PatternModel Train(GrayImage image, RectI roi, double originX, double originY, double grain = 1.0)
    {
        var opt = new TrainOptions
        {
            MaxFeatures = TrainMaxFeatures,
            MinSpacing = TrainMinSpacing,
            ContrastThreshold = TrainFeatureFloor,
            Grain = grain,
            NoiseFloor = NoiseFloor,
            N = ScoreExponentN,
            IgnorePolarity = IgnorePolarity
        };
        Pattern = PatternTrainer.Train(image, roi, originX, originY, EdgeDetector, opt);
        return Pattern;
    }

    /// <summary>Train from a region, with the origin at the region centre (the usual case).</summary>
    public PatternModel TrainRegion(GrayImage image, RectI roi, double grain = 1.0)
    {
        var r = roi.Clamp(image.Width, image.Height);
        if (r.IsEmpty) throw new ArgumentException("train region is empty", nameof(roi));
        return Train(image, r, r.X + (r.Width - 1) / 2.0, r.Y + (r.Height - 1) / 2.0, grain);
    }

    /// <summary>Drop the trained model (清空). A subsequent Run refuses until training again.</summary>
    public void ClearPattern() => Pattern = null;

    // ------------------------------------------------------------------
    //  Running
    // ------------------------------------------------------------------
    /// <summary>
    /// Locate the trained <see cref="Pattern"/> in <paramref name="image"/>. The tool's parameter
    /// properties are copied into a fresh SearchOptions for the call; timing lands in
    /// <see cref="LastDiagnostics"/>. Returns at most <paramref name="maxResults"/> results,
    /// best score first; an empty list means nothing was accepted.
    /// </summary>
    public List<MatchResult> Run(GrayImage image, int maxResults = 10)
    {
        if (Pattern == null || !Pattern.Trained)
            throw new InvalidOperationException("pattern not trained");

        var options = BuildOptions(maxResults);
        var prepared = Prepare(image);

        // The engine reports its own timing; copy into the shared diagnostics object.
        var diag = new SearchDiagnostics();
        var results = Engine.Run(Pattern, prepared, EdgeDetector, options, diag);
        CopyDiagnostics(diag);

        if (!SubpixelRefinement)
            foreach (var r in results) r.FitError = double.NaN;

        return results;
    }

    /// <summary>Run a multi-pattern set and return the merged, ranked results.</summary>
    public List<MatchResult> RunMulti(GrayImage image, PatternMulti multi, int maxResults = 10)
    {
        if (multi == null || !multi.Trained) return new List<MatchResult>();

        var prepared = Prepare(image);
        var merged = new List<MatchResult>();
        int index = 0;

        foreach (var model in multi.SelectModels(_ => 0.0))
        {
            if (model == null || !model.Trained) { index++; continue; }
            var diag = new SearchDiagnostics();
            var options = BuildOptions(maxResults);
            var results = Engine.Run(model, prepared, EdgeDetector, options, diag);
            foreach (var r in results)
            {
                r.ModelIndex = index;
                r.ModelId = multi.IdOf(model) ?? $"model-{index}";
                merged.Add(r);
            }
            index++;
        }

        merged.Sort((a, b) => b.Score.CompareTo(a.Score));
        return merged.Count > maxResults ? merged.GetRange(0, maxResults) : merged;
    }

    private GrayImage Prepare(GrayImage image)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (PreSmoothSigma > 0.6)
            return PatternTrainer.Blur(image, PreSmoothSigma);
        return image;
    }

    /// <summary>Centre distance below which two results count as the same instance.</summary>
    private double Separations()
    {
        double sep = EnableClustering ? Math.Max(0, ClusterRadius) : 0;
        if (Pattern != null && Pattern.Trained)
        {
            // overlap 0.5 means centres may sit a quarter of the diameter apart
            double fromOverlap = Pattern.Diameter * (1.0 - Math.Clamp(XYOverlap, 0.0, 1.0)) * 0.5;
            sep = Math.Max(sep, fromOverlap);
        }
        return sep;
    }

    private SearchOptions BuildOptions(int maxResults) => new()
    {
        AngleMin = RotationAngleMin,
        AngleMax = RotationAngleMax,
        ScaleMin = ScaleMin,
        ScaleMax = ScaleMax,
        AcceptThreshold = AcceptThreshold,
        CoarseAcceptThreshold = CoarseAcceptThreshold,
        ContrastThreshold = ContrastThreshold,
        MaxResults = Math.Max(1, maxResults),
        CoarseFeatureCount = CoarseFeatureCount,
        MidFeatureCount = MidFeatureCount,
        MaxCandidates = MaxCandidates,
        CoarseStride = Math.Max(1, PrecisionStep),
        CoarseAngleStepDeg = CoarseAngleStepDeg,
        SubpixelRefinement = SubpixelRefinement,
        OverlapSuppression = 0.55,
        MinSeparationPixels = Separations(),
        // Ignoring polarity must not mean "anything with edges is fine": an instance whose
        // contrast is inverted is still coherent (~1.0), a wrong pose is not (~0.5).
        MinPolarityCoherence = Pattern != null && Pattern.PolarityAgnostic && MinPolarityCoherence < 0
            ? 0.75
            : Math.Max(0.0, MinPolarityCoherence),
        UseClutter = UseClutter,
        NormalizedCorrelation = NormalizedCorrelation,
        MaxLevels = MaxLevels,
        TimeoutMs = TimeoutMs
    };

    private void CopyDiagnostics(SearchDiagnostics src)
    {
        LastDiagnostics.EdgeMs = src.EdgeMs;
        LastDiagnostics.CoarseMs = src.CoarseMs;
        LastDiagnostics.RefineMs = src.RefineMs;
        LastDiagnostics.VerifyMs = src.VerifyMs;
        LastDiagnostics.TotalMs = src.TotalMs;
        LastDiagnostics.CoarseLevel = src.CoarseLevel;
        LastDiagnostics.CoarseCandidates = src.CoarseCandidates;
        LastDiagnostics.ScoredPositions = src.ScoredPositions;
        LastDiagnostics.PyramidLevels = src.PyramidLevels;
    }

    // ------------------------------------------------------------------
    //  Diagnostics / helpers kept from the earlier revision
    // ------------------------------------------------------------------
    /// <summary>
    /// Convert an edge field into dipoles (one per edge pixel), each displaced by
    /// <paramref name="scale"/> pixels along its own normal so that it sits on the edge.
    /// </summary>
    public List<Dipole> ExtractDipoles(EdgeMap edges, double scale = 0.5)
    {
        var dipoles = new List<Dipole>();
        if (edges == null) return dipoles;

        for (int y = 0; y < edges.Height; y++)
            for (int x = 0; x < edges.Width; x++)
            {
                int i = y * edges.Width + x;
                if (!edges.IsEdge[i]) continue;
                float mag = edges.Magnitude[i];
                float ang = (float)Math.Atan2(edges.Gy[i], edges.Gx[i]);
                dipoles.Add(new Dipole
                {
                    X = x + 0.5f + (float)(scale * Math.Cos(ang)),
                    Y = y + 0.5f + (float)(scale * Math.Sin(ang)),
                    Angle = ang,
                    Strength = mag,
                    Weight = 1.0f
                });
            }
        return dipoles;
    }

    /// <summary>
    /// Group dipoles into chains by radius connectivity (dipoles closer than 'spacing' land in
    /// the same chain), implemented as a breadth-first flood over a spatial hash. This is
    /// proximity connectivity, not strict 8-connectivity. The previous nearest-neighbour loop
    /// was O(n^2) and unusable above a few thousand edge pixels.
    /// </summary>
    public List<List<Dipole>> BuildChains(List<Dipole> dipoles, double spacing = 2.0)
    {
        var chains = new List<List<Dipole>>();
        if (dipoles == null || dipoles.Count == 0) return chains;

        int cell = Math.Max(1, (int)Math.Ceiling(spacing));
        var buckets = new Dictionary<long, List<int>>();
        for (int i = 0; i < dipoles.Count; i++)
        {
            long key = Key((int)(dipoles[i].X / cell), (int)(dipoles[i].Y / cell));
            if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<int>();
            list.Add(i);
        }

        var visited = new bool[dipoles.Count];
        var queue = new Queue<int>();
        double radiusSq = spacing * spacing;

        for (int seed = 0; seed < dipoles.Count; seed++)
        {
            if (visited[seed]) continue;
            visited[seed] = true;
            queue.Clear();
            queue.Enqueue(seed);
            var chain = new List<Dipole>();

            while (queue.Count > 0)
            {
                int cur = queue.Dequeue();
                chain.Add(dipoles[cur]);
                int cx = (int)(dipoles[cur].X / cell), cy = (int)(dipoles[cur].Y / cell);

                for (int gy = cy - 1; gy <= cy + 1; gy++)
                    for (int gx = cx - 1; gx <= cx + 1; gx++)
                    {
                        if (!buckets.TryGetValue(Key(gx, gy), out var bucket)) continue;
                        foreach (int other in bucket)
                        {
                            if (visited[other]) continue;
                            double dx = dipoles[other].X - dipoles[cur].X;
                            double dy = dipoles[other].Y - dipoles[cur].Y;
                            if (dx * dx + dy * dy > radiusSq) continue;
                            visited[other] = true;
                            queue.Enqueue(other);
                        }
                    }
            }
            if (chain.Count >= 2) chains.Add(chain);
        }
        return chains;

        static long Key(int x, int y) => ((long)x << 32) ^ (uint)y;
    }

    /// <summary>Fit a pose from matched feature correspondences (affine, type 6).</summary>
    public LeastSquaresFitter.LinearXform FitPose(
        IReadOnlyList<LeastSquaresFitter.Point2D> source,
        IReadOnlyList<LeastSquaresFitter.Point2D> target)
        => Fitter.Fit(source, target, LeastSquaresFitter.FitType.Affine);
}
