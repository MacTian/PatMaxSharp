namespace PatMaxSharp.Core;

/// <summary>
/// Trained PatMax pattern model.
///
/// Field offsets mirror the reverse-engineered layout:
///   W (total feature weight)  -> pattern + 0x90
///   N (exponent denominator)  -> pattern + 0x8c
///   Trained flag               -> pattern + 0x230
///   TrainAlgorithm             -> pattern + 0x788
/// </summary>
public sealed class PatternModel
{
    public List<Featurelet> Featurelets { get; } = new();

    /// <summary>Total feature weight W.</summary>
    public double W { get; private set; } = 1.0;

    /// <summary>Exponent denominator N.</summary>
    public double N { get; set; } = 1.0;

    /// <summary>Pattern origin in training image coordinates.</summary>
    public double OriginX { get; set; }
    public double OriginY { get; set; }

    /// <summary>Training algorithm (0=PatMax, 1=PatQuick, 3=PatFlex, 4=PatMaxHighSensitivity, 5=PatMaxPerspective).</summary>
    public int TrainAlgorithm { get; set; } = 0;

    public bool Trained => Featurelets.Count > 0;

    public int FeatureCount => Featurelets.Count;

    /// <summary>Effective grain (coarse/fine) used to build the DoG kernel.</summary>
    public double Grain { get; set; } = 1.0;

    /// <summary>
    /// Edge-magnitude template captured at train time. Used by the robust
    /// normalized-cross-correlation matcher (PatMax-style edge template).
    /// </summary>
    public float[] Template { get; set; } = Array.Empty<float>();
    public int TemplateWidth { get; set; }
    public int TemplateHeight { get; set; }
    public bool HasTemplate => Template.Length > 0;

    public void AddFeaturelet(Featurelet f)
    {
        Featurelets.Add(f);
        W += f.Weight;
        if (N < 1.0) N = 1.0;
    }

    public override string ToString() =>
        $"Pattern(features={Featurelets.Count}, W={W:F2}, N={N:F2}, grain={Grain:F2}, algo={TrainAlgorithm})";
}
