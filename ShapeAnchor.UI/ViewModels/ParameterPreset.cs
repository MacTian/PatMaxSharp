using System.Linq;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShapeAnchor.UI.ViewModels;

/// <summary>
/// A saved set of PatMax parameters (preset).
/// Serialized to JSON and stored in the user's local app data folder.
/// </summary>
public sealed class ParameterPreset
{
    [JsonPropertyName("name")] public string Name { get; set; } = "默认";
    [JsonPropertyName("description")] public string Description { get; set; } = "";

    // ---- Search parameters ----
    [JsonPropertyName("rotationAngleMin")] public double RotationAngleMin { get; set; } = -180;
    [JsonPropertyName("rotationAngleMax")] public double RotationAngleMax { get; set; } = 180;
    [JsonPropertyName("scaleMin")] public double ScaleMin { get; set; } = 1.0;
    [JsonPropertyName("scaleMax")] public double ScaleMax { get; set; } = 1.0;
    [JsonPropertyName("approximateNumberToFind")] public int ApproximateNumberToFind { get; set; } = 1;
    [JsonPropertyName("xyOverlap")] public double XYOverlap { get; set; } = 0.5;
    [JsonPropertyName("timeoutMs")] public double TimeoutMs { get; set; } = 5000;

    // ---- Training parameters ----
    [JsonPropertyName("ignorePolarity")] public bool IgnorePolarity { get; set; }
    [JsonPropertyName("autoEdgeThreshold")] public bool AutoEdgeThreshold { get; set; } = true;
    [JsonPropertyName("edgeThreshold")] public double EdgeThreshold { get; set; } = 8.0;
    [JsonPropertyName("trainMaxFeatures")] public int TrainMaxFeatures { get; set; } = 600;
    [JsonPropertyName("trainMinSpacing")] public double TrainMinSpacing { get; set; } = 2.0;
    [JsonPropertyName("maxCandidates")] public int MaxCandidates { get; set; } = 20;

    // ---- Scoring parameters ----
    [JsonPropertyName("acceptThreshold")] public double AcceptThreshold { get; set; } = 0.7;
    [JsonPropertyName("contrastThreshold")] public double ContrastThreshold { get; set; } = 0.1;
    [JsonPropertyName("grain")] public double Grain { get; set; } = 1.0;
    [JsonPropertyName("coarseGrainLimit")] public int CoarseGrainLimit { get; set; } = 10;
    [JsonPropertyName("fineGrainLimit")] public int FineGrainLimit { get; set; } = 5;
    [JsonPropertyName("scoreUsingClutter")] public bool ScoreUsingClutter { get; set; } = true;
    [JsonPropertyName("maxResults")] public int MaxResults { get; set; } = 10;

    // ---- Performance parameters ----
    [JsonPropertyName("precisionStep")] public int PrecisionStep { get; set; } = 1;
    [JsonPropertyName("subpixelRefinement")] public bool SubpixelRefinement { get; set; } = true;
    [JsonPropertyName("enableClustering")] public bool EnableClustering { get; set; } = true;
    [JsonPropertyName("clusterRadius")] public double ClusterRadius { get; set; } = 8.0;
    [JsonPropertyName("preSmoothSigma")] public double PreSmoothSigma { get; set; } = 0.5;
    [JsonPropertyName("useSimd")] public bool UseSimd { get; set; } = true;
    [JsonPropertyName("useParallel")] public bool UseParallel { get; set; } = true;

    public string ToJson() => JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

    public static ParameterPreset FromJson(string json) =>
        JsonSerializer.Deserialize<ParameterPreset>(json) ?? new ParameterPreset();

    /// <summary>Built-in factory presets.</summary>
    /// <summary>
    /// The built-in presets come from the library catalog, so the list in the UI is the same list
    /// `dotnet run -- --presets <folder>` measures. Nothing here is hand-tuned twice.
    /// </summary>
    public static List<ParameterPreset> BuiltIn() =>
        ShapeAnchor.Core.PresetCatalog.BuiltIn.Select(p => new ParameterPreset
        {
            Name = p.Name,
            Description = $"{p.Description}。{p.Usage}",
            AcceptThreshold = p.AcceptThreshold,
            ContrastThreshold = p.ContrastThreshold,
            ScoreUsingClutter = p.ScoreUsingClutter,
            RotationAngleMin = p.RotationAngleMin,
            RotationAngleMax = p.RotationAngleMax,
            ScaleMin = p.ScaleMin,
            ScaleMax = p.ScaleMax,
            ApproximateNumberToFind = p.ApproximateNumberToFind,
            MaxResults = p.MaxResults,
            XYOverlap = p.XYOverlap,
            IgnorePolarity = p.IgnorePolarity,
            AutoEdgeThreshold = p.AutoEdgeThreshold,
            EdgeThreshold = p.EdgeThreshold,
            Grain = p.Grain,
            TrainMaxFeatures = p.TrainMaxFeatures,
            TrainMinSpacing = p.TrainMinSpacing,
            PrecisionStep = p.PrecisionStep,
            SubpixelRefinement = p.SubpixelRefinement,
            EnableClustering = p.EnableClustering,
            ClusterRadius = p.ClusterRadius,
            PreSmoothSigma = p.PreSmoothSigma,
            MaxCandidates = p.MaxCandidates,
            TimeoutMs = p.TimeoutMs
        }).ToList();
}
