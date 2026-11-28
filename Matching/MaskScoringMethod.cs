namespace ShapeAnchor.Matching;

/// <summary>CogPMAlignVisiblePixelMaskScoringMethodConstants.</summary>
public enum MaskScoringMethod
{
    /// <summary>DoNotScoreFeatures: masked features take no part in the score.</summary>
    DoNotScoreFeatures = 0,

    /// <summary>ScoreFeaturesAsClutter: masked features count against the score as clutter.</summary>
    ScoreFeaturesAsClutter = 1
}