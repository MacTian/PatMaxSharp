namespace PatMaxSharp.Matching;

/// <summary>
/// Search parameters, named after the CogPMAlignRunParams surface.
/// </summary>
public sealed class SearchOptions
{
    // ---- angular search ----
    /// <summary>Lower bound of the angle search range, degrees (the two bounds are swapped when reversed).</summary>
    public double AngleMin { get; set; } = -180.0;

    /// <summary>Upper bound of the angle search range, degrees.</summary>
    public double AngleMax { get; set; } = 180.0;

    // ---- scale search ----
    /// <summary>Lower bound of the scale search range (a value of 0 or less falls back to 1.0).</summary>
    public double ScaleMin { get; set; } = 1.0;

    /// <summary>Upper bound of the scale search range (a value of 0 or less falls back to the lower bound).</summary>
    public double ScaleMax { get; set; } = 1.0;

    // ---- acceptance ----
    /// <summary>Minimum score to accept a result (acceptThreshold).</summary>
    public double AcceptThreshold { get; set; } = 0.7;

    /// <summary>
    /// Coarse-stage gate (CoarseAcceptThreshold). A hypothesis must reach this score at the
    /// coarsest level before the refinement levels spend time on it — a lower threshold than
    /// <see cref="AcceptThreshold"/>, so a marginal part still gets refined instead of dropped.
    /// </summary>
    public double CoarseAcceptThreshold { get; set; } = 0.33;

    /// <summary>Minimum edge contrast, as a fraction of the level reference magnitude.</summary>
    public double ContrastThreshold { get; set; } = 0.08;

    /// <summary>Maximum number of results returned.</summary>
    public int MaxResults { get; set; } = 1;

    // ---- coarse-to-fine budget ----
    /// <summary>Features used by the exhaustive coarse stage.</summary>
    public int CoarseFeatureCount { get; set; } = 32;

    /// <summary>Features used by intermediate refinement levels.</summary>
    public int MidFeatureCount { get; set; } = 96;

    /// <summary>Hypotheses propagated from the coarse stage.</summary>
    public int MaxCandidates { get; set; } = 20;

    /// <summary>Position stride, in coarse-level pixels (larger = faster, less precise).</summary>
    public int CoarseStride { get; set; } = 2;

    /// <summary>Angular step of the exhaustive stage, degrees.</summary>
    public double CoarseAngleStepDeg { get; set; } = 15.0;

    /// <summary>Angular step of the intermediate stage, degrees.</summary>
    public double MidAngleStepDeg { get; set; } = 3.0;

    /// <summary>Angular step of the final stage, degrees.</summary>
    public double FineAngleStepDeg { get; set; } = 0.5;

    // ---- refinement ----
    /// <summary>Enable edge-normal sub-pixel refinement plus least-squares pose fit.</summary>
    public bool SubpixelRefinement { get; set; } = true;

    /// <summary>Search half-range along the edge normal during pose refinement, pixels.</summary>
    public double NormalSearchRadius { get; set; } = 1.5;

    /// <summary>Sampling step along the edge normal during pose refinement, pixels.</summary>
    public double NormalSearchStep { get; set; } = 0.5;

    /// <summary>Features used by the least-squares pose fit (accuracy saturates around 120).</summary>
    public int PoseFitMaxPoints { get; set; } = 120;

    /// <summary>Suppress overlapping instances closer than this fraction of the model diameter.</summary>
    public double OverlapSuppression { get; set; } = 0.55;

    /// <summary>
    /// Absolute minimum separation between two reported instances, in pixels. Takes
    /// precedence over <see cref="OverlapSuppression"/> when &gt; 0.
    /// </summary>
    public double MinSeparationPixels { get; set; }

    /// <summary>Include unmodelled-edge clutter in the score.</summary>
    public bool UseClutter { get; set; } = true;

    /// <summary>
    /// Divide the coarse correlation by the mean edge strength under the model footprint
    /// (normalised correlation).
    ///
    /// OFF by default, on measurement: it fixes gain differences but it also depresses the
    /// correct pose when structured clutter covers the whole footprint (a part behind plastic
    /// bags scored 0.895 absolute and 0.562 normalised), because there the local energy is high
    /// everywhere and the ratio stops discriminating. Kept as an option for pure gain changes.
    /// </summary>
    public bool NormalizedCorrelation { get; set; }

    /// <summary>Maximum pyramid levels (image is halved per level).</summary>
    public int MaxLevels { get; set; } = 4;

    /// <summary>
    /// Wall-clock budget in milliseconds, declared for API compatibility. The current search
    /// engine does not consult it, so a run is not cut short by this value.
    /// </summary>
    public double TimeoutMs { get; set; }

    /// <summary>Shallow copy (the option bag holds no reference-type state, so this is a full copy).</summary>
    public SearchOptions Clone() => (SearchOptions)MemberwiseClone();
}

/// <summary>Timing and search statistics for one run.</summary>
public sealed class SearchDiagnostics
{
    /// <summary>Time spent building the edge-field pyramid, milliseconds (cumulative stopwatch value).</summary>
    public double EdgeMs { get; set; }

    /// <summary>Time spent in the exhaustive coarse stage, milliseconds.</summary>
    public double CoarseMs { get; set; }

    /// <summary>Time spent refining hypotheses level by level, milliseconds.</summary>
    public double RefineMs { get; set; }

    /// <summary>Time spent verifying candidates and fitting the sub-pixel pose, milliseconds.</summary>
    public double VerifyMs { get; set; }

    /// <summary>Total wall-clock time of the run, milliseconds.</summary>
    public double TotalMs { get; set; }

    /// <summary>Index of the pyramid level the exhaustive stage ran at.</summary>
    public int CoarseLevel { get; set; }

    /// <summary>Number of hypotheses that survived the coarse stage.</summary>
    public int CoarseCandidates { get; set; }

    /// <summary>Number of correlation surface positions evaluated in the coarse stage.</summary>
    public int ScoredPositions { get; set; }

    /// <summary>Number of pyramid levels built for this run.</summary>
    public int PyramidLevels { get; set; }

    /// <summary>True when the run stopped early because TimeoutMs elapsed.</summary>
    public bool TimedOut { get; set; }

    public override string ToString() =>
        $"Search(levels={PyramidLevels}, coarseLevel={CoarseLevel}, cand={CoarseCandidates}, " +
        $"pos={ScoredPositions}, edge={EdgeMs:F1}ms, coarse={CoarseMs:F1}ms, refine={RefineMs:F1}ms, " +
        $"verify={VerifyMs:F1}ms, total={TotalMs:F1}ms)";
}
