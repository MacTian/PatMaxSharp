namespace ShapeAnchor.Core;

/// <summary>
/// One named parameter set. The catalog lives in the library (not the UI) so the command line
/// can run every preset over a folder of images and prove it behaves as advertised.
/// </summary>
public sealed class SearchPreset
{
    public string Name { get; set; } = "默认";
    public string Description { get; set; } = "";
    public string Usage { get; set; } = "";

    // scoring
    public double AcceptThreshold { get; set; } = 0.70;
    public double ContrastThreshold { get; set; } = 0.08;
    public bool ScoreUsingClutter { get; set; } = true;

    // search window
    public double RotationAngleMin { get; set; } = -180;
    public double RotationAngleMax { get; set; } = 180;
    public double ScaleMin { get; set; } = 1.0;
    public double ScaleMax { get; set; } = 1.0;
    public int ApproximateNumberToFind { get; set; } = 1;
    public int MaxResults { get; set; } = 10;
    public double XYOverlap { get; set; } = 0.5;

    // training
    public bool IgnorePolarity { get; set; }
    public bool AutoEdgeThreshold { get; set; } = true;
    public double EdgeThreshold { get; set; } = 8.0;
    public double Grain { get; set; } = 1.0;
    public int TrainMaxFeatures { get; set; } = 600;
    public double TrainMinSpacing { get; set; } = 2.0;

    // performance
    public int PrecisionStep { get; set; } = 2;
    public bool SubpixelRefinement { get; set; } = true;
    public bool EnableClustering { get; set; } = true;
    public double ClusterRadius { get; set; } = 8.0;
    public double PreSmoothSigma { get; set; }
    public int MaxCandidates { get; set; } = 20;
    public double TimeoutMs { get; set; }

    public override string ToString() => Name;
}

/// <summary>
/// Built-in presets. Each one targets a situation that actually shows up on a line, and each is
/// exercised by `dotnet run -- --presets &lt;folder&gt;` so the numbers behind the description are
/// reproducible rather than aspirational.
/// </summary>
public static class PresetCatalog
{
    public static IReadOnlyList<SearchPreset> BuiltIn { get; } = new List<SearchPreset>
    {
        new()
        {
            Name = "通用（默认）",
            Description = "常规单目标定位：360° 内搜索、不做尺度搜索、杂波计入评分",
            Usage = "绝大多数场景的起点。先跑这一组，判错再换专项预设。",
            AcceptThreshold = 0.70,
            RotationAngleMin = -180, RotationAngleMax = 180,
            ScaleMin = 1.0, ScaleMax = 1.0,
            ApproximateNumberToFind = 1, MaxResults = 10,
            TrainMaxFeatures = 600, TrainMinSpacing = 2.0,
            PrecisionStep = 2, SubpixelRefinement = true,
            IgnorePolarity = false, AutoEdgeThreshold = true, EdgeThreshold = 8.0
        },
        new()
        {
            Name = "小角度快搜",
            Description = "夹具已定向，只查 ±10° 偏转；角度空间小一半以上，最快",
            Usage = "来料姿态固定、只关心轻微歪斜时用。超出 ±10° 的件按设计会判为无结果。",
            AcceptThreshold = 0.70,
            RotationAngleMin = -10, RotationAngleMax = 10,
            ScaleMin = 1.0, ScaleMax = 1.0,
            PrecisionStep = 2, SubpixelRefinement = true,
            TrainMaxFeatures = 600, TrainMinSpacing = 2.0
        },
        new()
        {
            Name = "高精度（慢）",
            Description = "计量级：粗层步长 1、特征更密、必须亚像素；接受阈值 0.75",
            Usage = "需要最稳的位姿读数、且节拍允许时用。",
            AcceptThreshold = 0.75,
            RotationAngleMin = -30, RotationAngleMax = 30,
            ScaleMin = 0.95, ScaleMax = 1.05,
            PrecisionStep = 1, SubpixelRefinement = true,
            TrainMaxFeatures = 1200, TrainMinSpacing = 1.5,
            MaxCandidates = 28
        },
        new()
        {
            Name = "大角度 + 尺度",
            Description = "任意姿态且需要找尺寸差异：±180°、尺度 0.9–1.1",
            Usage = "同一零件有远近/不同规格时用；代价是搜索空间约为默认组的 5 倍。",
            AcceptThreshold = 0.70,
            RotationAngleMin = -180, RotationAngleMax = 180,
            ScaleMin = 0.9, ScaleMax = 1.1,
            PrecisionStep = 2, SubpixelRefinement = true
        },
        new()
        {
            Name = "反光 / 剪影",
            Description = "对比度极性会反转（背光、镜面反射）：忽略极性、阈值 0.80",
            Usage = "只在其他预设判不出时用：忽略极性会明显降低区分力，实测会给出高分但角度错误的位姿，务必人工核对叠加。",
            AcceptThreshold = 0.80,
            IgnorePolarity = true,
            RotationAngleMin = -180, RotationAngleMax = 180,
            PrecisionStep = 2, SubpixelRefinement = true
        },
        new()
        {
            Name = "暗场 / 低对比",
            Description = "欠曝或低对比：自动边缘阈值、门限降到 4、接受阈值 0.6",
            Usage = "图像整体偏暗（均值远低于正常）时用；先看状态栏的边缘阈值是否被自动下调。",
            AcceptThreshold = 0.60,
            EdgeThreshold = 4.0,
            AutoEdgeThreshold = true,
            RotationAngleMin = -180, RotationAngleMax = 180,
            PrecisionStep = 2, SubpixelRefinement = true
        },
        new()
        {
            Name = "多目标",
            Description = "一图多件：预期 5 件、XY 重叠 0.3、最多 20 个结果、阈值 0.6",
            Usage = "托盘/整版取像时用。结果表每行一个实例。",
            AcceptThreshold = 0.60,
            ApproximateNumberToFind = 5, MaxResults = 20, XYOverlap = 0.3,
            ClusterRadius = 12,
            RotationAngleMin = -180, RotationAngleMax = 180,
            PrecisionStep = 2, SubpixelRefinement = true
        },
        new()
        {
            Name = "演示对齐·原始参数",
            Description = "逐项对齐 VisionPro PatMax_Demo：忽略极性、角度 ±45°、缩放 0.6–1.6、"
                        + "接受 0.5、对比度 0、XY 重叠 0.8、自动边缘阈值 5、超时 5000 ms、查找 3",
            Usage = "17/17 全部找到，但 17 个解里有 10 个落在缩放下界 0.60（模型的缩小副本），"
                  + "属于退化解——召回不等于位姿正确，务必核对叠加图。",
            AcceptThreshold = 0.5,
            ContrastThreshold = 0.0,
            ScoreUsingClutter = false,
            RotationAngleMin = -45, RotationAngleMax = 45,
            ScaleMin = 0.6, ScaleMax = 1.6,
            ApproximateNumberToFind = 3, MaxResults = 3,
            XYOverlap = 0.8,
            IgnorePolarity = true,
            AutoEdgeThreshold = true, EdgeThreshold = 5.0,
            TrainMaxFeatures = 600, TrainMinSpacing = 2.0,
            PrecisionStep = 2, SubpixelRefinement = true,
            EnableClustering = true, ClusterRadius = 8.0,
            MaxCandidates = 24,
            TimeoutMs = 5000
        },
        new()
        {
            Name = "演示对齐·尺度收紧",
            Description = "同上，但缩放收到 0.9–1.1：同样 17/17，且解不再塌到缩放下界",
            Usage = "在 17 张实拍图上既要全部找到、又要能用的位姿时选这一组；"
                  + "它是演示参数在本数据集上的可用版本。",
            AcceptThreshold = 0.5,
            ContrastThreshold = 0.0,
            ScoreUsingClutter = false,
            RotationAngleMin = -45, RotationAngleMax = 45,
            ScaleMin = 0.9, ScaleMax = 1.1,
            ApproximateNumberToFind = 3, MaxResults = 3,
            XYOverlap = 0.8,
            IgnorePolarity = true,
            AutoEdgeThreshold = true, EdgeThreshold = 5.0,
            TrainMaxFeatures = 600, TrainMinSpacing = 2.0,
            PrecisionStep = 2, SubpixelRefinement = true,
            EnableClustering = true, ClusterRadius = 8.0,
            MaxCandidates = 24,
            TimeoutMs = 5000
        }
    };
    /// <summary>Look a preset up by name (case-insensitive, partial match allowed).</summary>
    public static SearchPreset Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return BuiltIn[0];
        foreach (var p in BuiltIn)
            if (p.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase)) return p;
        foreach (var p in BuiltIn)
            if (p.Name.Contains(name, StringComparison.OrdinalIgnoreCase)) return p;
        return BuiltIn[0];
    }
}
