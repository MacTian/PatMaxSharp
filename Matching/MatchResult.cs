using PatMaxSharp.Core;

namespace PatMaxSharp;

/// <summary>
/// One match result.
///
/// The field set follows the public PMAlign result surface. Early analysis notes called the
/// native record ccPMResult and quoted a per-field offset for each member (score +0x48,
/// contrast +0x4c, ...); the native core is WIBU-packed, so neither that name nor those
/// offsets can be checked statically, and no offset is claimed here.
///
/// Score is the PatMax-style score in [0,1]: the weighted feature coverage raised to
/// 1/N, so a half-occluded part still scores ~0.7 instead of collapsing.
/// </summary>
public sealed class MatchResult
{
    /// <summary>PatMax score in [0,1]: coverage^(1/N), reduced by clutter and normalisation.</summary>
    public double Score { get; set; }

    /// <summary>Mean signed directional response of the matched edges, normalised by the level reference.</summary>
    public double Contrast { get; set; }

    /// <summary>RMS residual of the pose fit in pixels; NaN when sub-pixel refinement is switched off.</summary>
    public double FitError { get; set; }

    /// <summary>Weighted feature coverage in [0,1] (matched feature weight / total feature weight).</summary>
    public double Coverage { get; set; }

    /// <summary>True when Score reached the accept threshold and Contrast reached the contrast threshold.</summary>
    public bool Accepted { get; set; }

    /// <summary>
    /// Clutter estimate in [0,1] (0 = clean, 1 = maximally cluttered). The search engine
    /// computes it as 0.5 * (unmatched features / total features) when UseClutter is on and
    /// reports 0 otherwise - a proxy for "how much of the model found no edge", not a
    /// measurement of unmodelled edge energy inside the footprint. Reported alongside Score
    /// because Score / Coverage / Clutter are the three values PatMax exposes.
    /// </summary>
    public double Clutter { get; set; }

    /// <summary>Pattern origin position in the search image (pixels).</summary>
    public double X { get; set; }
    /// <summary>Pattern origin Y in the search image (pixels).</summary>
    public double Y { get; set; }

    /// <summary>Rotation in radians.</summary>
    public double Angle { get; set; }

    /// <summary>Uniform scale.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Number of model features that found a supporting edge at the reported pose.</summary>
    public int MatchedFeatures { get; set; }

    /// <summary>Number of model features that found no supporting edge (Total - Matched).</summary>
    public int BadFeatures { get; set; }

    /// <summary>Raw coarse correlation peak that produced this hypothesis.</summary>
    public double PeakValue { get; set; }

    /// <summary>Index of the trained model that produced the result (multi-pattern).</summary>
    public int ModelIndex { get; set; }

    /// <summary>Model id (multi-pattern).</summary>
    public string ModelId { get; set; } = "";

    public override string ToString() =>
        $"Result(Score={Score:F3}, Pos=({X:F2},{Y:F2}), Angle={Angle * 180 / Math.PI:F2}deg, " +
        $"Scale={Scale:F3}, Coverage={Coverage:P0}, FitErr={FitError:F2}, " +
        $"Matched={MatchedFeatures}, Bad={BadFeatures}, Accepted={Accepted})";
}
