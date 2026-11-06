using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PatMaxSharp.UI.ViewModels;

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
    [JsonPropertyName("scaleMin")] public double ScaleMin { get; set; } = 0.8;
    [JsonPropertyName("scaleMax")] public double ScaleMax { get; set; } = 1.2;
    [JsonPropertyName("approximateNumberToFind")] public int ApproximateNumberToFind { get; set; } = 1;
    [JsonPropertyName("xyOverlap")] public double XYOverlap { get; set; } = 0.5;
    [JsonPropertyName("timeoutMs")] public double TimeoutMs { get; set; } = 5000;

    // ---- Scoring parameters ----
    [JsonPropertyName("acceptThreshold")] public double AcceptThreshold { get; set; } = 0.5;
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
    public static List<ParameterPreset> BuiltIn() => new()
    {
        new ParameterPreset
        {
            Name = "默认 (Default)",
            Description = "通用场景的平衡配置",
            AcceptThreshold = 0.5, Grain = 1.0, MaxResults = 10
        },
        new ParameterPreset
        {
            Name = "高精度 (High Precision)",
            Description = "亚像素细化 + 细粒度，速度较慢",
            PrecisionStep = 1, SubpixelRefinement = true, Grain = 2.0,
            CoarseGrainLimit = 8, FineGrainLimit = 4, AcceptThreshold = 0.6
        },
        new ParameterPreset
        {
            Name = "高速 (High Speed)",
            Description = "粗粒度 + 大 step，适合快速筛选",
            PrecisionStep = 4, SubpixelRefinement = false, Grain = 5.0,
            CoarseGrainLimit = 12, FineGrainLimit = 6, MaxResults = 5
        },
        new ParameterPreset
        {
            Name = "强鲁棒 (Robust)",
            Description = "高斯平滑 + 大聚类，抗噪/抗遮挡",
            PreSmoothSigma = 1.5, ClusterRadius = 20, EnableClustering = true,
            AcceptThreshold = 0.4, Grain = 1.5
        },
        new ParameterPreset
        {
            Name = "多目标 (Multi-Target)",
            Description = "查找多个目标，低重叠阈值",
            ApproximateNumberToFind = 5, XYOverlap = 0.2, MaxResults = 20,
            AcceptThreshold = 0.45
        }
    };
}
