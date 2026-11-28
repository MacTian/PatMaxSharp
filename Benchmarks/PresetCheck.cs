using System.Diagnostics;
using ShapeAnchor.Core;
using ShapeAnchor.Training;

namespace ShapeAnchor.Benchmarks;

/// <summary>
/// Runs the built-in presets over a folder so a preset claim can be checked instead of trusted.
/// Also explains one image: every refined hypothesis with its coverage and polarity coherence,
/// which is what showed that a wrong pose was winning the score.
/// </summary>
public static class PresetCheck
{
    /// <summary>Build a tool from a preset; acceptAll keeps hypotheses the gates would drop.</summary>
    /// <summary>Command-line override for the training feature floor (experiments).</summary>
    public static double? TrainFloorOverride;
    public static int? MaxFeaturesOverride;
    public static double? CoherenceOverride;
    public static double? ScaleMinOverride, ScaleMaxOverride;

    private static ShapeAnchorTool MakeTool(SearchPreset preset, bool acceptAll)
    {
        return new ShapeAnchorTool
        {
            AcceptThreshold = acceptAll ? 0.0 : preset.AcceptThreshold,
            ContrastThreshold = acceptAll ? 0.0 : preset.ContrastThreshold,
            UseClutter = preset.ScoreUsingClutter,
            RotationAngleMin = preset.RotationAngleMin,
            RotationAngleMax = preset.RotationAngleMax,
            ScaleMin = ScaleMinOverride ?? preset.ScaleMin,
            ScaleMax = ScaleMaxOverride ?? preset.ScaleMax,
            PrecisionStep = preset.PrecisionStep,
            SubpixelRefinement = preset.SubpixelRefinement,
            EnableClustering = preset.EnableClustering,
            ClusterRadius = preset.ClusterRadius,
            XYOverlap = preset.XYOverlap,
            PreSmoothSigma = preset.PreSmoothSigma,
            MaxCandidates = preset.MaxCandidates,
            TimeoutMs = preset.TimeoutMs,
            IgnorePolarity = preset.IgnorePolarity,
            AutoEdgeThreshold = preset.AutoEdgeThreshold,
            EdgeThreshold = preset.EdgeThreshold,
            TrainMaxFeatures = MaxFeaturesOverride ?? preset.TrainMaxFeatures,
            TrainMinSpacing = preset.TrainMinSpacing,
            TrainFeatureFloor = TrainFloorOverride ?? preset.TrainFeatureFloor,
            MinPolarityCoherence = acceptAll ? 0.0
                : (CoherenceOverride ?? preset.MinPolarityCoherence)   // -1 = auto
        };
    }

    public static void Run(string directory, bool detail = false)
    {
        if (!Directory.Exists(directory)) { Console.WriteLine($"[preset] directory not found: {directory}"); return; }

        var files = ImageLoader.Enumerate(directory);
        if (files.Count < 2) { Console.WriteLine("[preset] need at least two images"); return; }

        var baseImage = ImageLoader.Load(files[0]);
        var roi = PatternTrainer.ContentBounds(baseImage);
        Console.WriteLine($"[preset] folder = {directory}  images = {files.Count}  " +
                          $"base = {Path.GetFileName(files[0])}  roi = {roi}");
        Console.WriteLine();
        Console.WriteLine($"{"preset",-16} {"found",6} {">=0.90",7} {"0.7-0.9",8} {"<0.7",5} " +
                          $"{"mean",6} {"inst",5} {"train",7} {"time",8}");

        var perImage = new List<(SearchPreset preset, List<(string file, double score, double angle,
            double scale, double fit, double coh, int count)> rows)>();

        foreach (var preset in PresetCatalog.BuiltIn)
        {
            var tool = MakeTool(preset, acceptAll: false);

            var trainWatch = Stopwatch.StartNew();
            var pattern = tool.Train(baseImage, roi, roi.X + (roi.Width - 1) / 2.0,
                                     roi.Y + (roi.Height - 1) / 2.0, preset.Grain);
            trainWatch.Stop();

            int found = 0, strong = 0, mid = 0, weak = 0, maxInstances = 0;
            double scoreSum = 0;
            var detailRows = new List<(string file, double score, double angle, double scale,
                                       double fit, double coh, int count)>();
            var watch = Stopwatch.StartNew();

            foreach (var file in files)
            {
                var image = ImageLoader.Load(file);
                int want = Math.Max(1, Math.Max(preset.MaxResults, preset.ApproximateNumberToFind));
                var results = tool.Run(image, want);
                detailRows.Add((Path.GetFileName(file),
                                results.Count > 0 ? results[0].Score : 0,
                                results.Count > 0 ? results[0].AngleDegrees : 0,
                                results.Count > 0 ? results[0].Scale : 0,
                                results.Count > 0 ? results[0].FitError : 0,
                                results.Count > 0 ? results[0].PolarityCoherence : 0,
                                results.Count));
                if (results.Count > 0)
                {
                    found++;
                    scoreSum += results[0].Score;
                    if (results[0].Score >= 0.90) strong++;
                    else if (results[0].Score >= 0.70) mid++;
                    else weak++;
                    maxInstances = Math.Max(maxInstances, results.Count);
                }
                else weak++;
            }
            watch.Stop();

            Console.WriteLine($"{preset.Name,-16} {found,3}/{files.Count,-2} {strong,7} {mid,8} {weak,5} " +
                              $"{(found > 0 ? scoreSum / found : 0),6:F3} {maxInstances,5} " +
                              $"{trainWatch.Elapsed.TotalMilliseconds,6:F0}ms " +
                              $"{(watch.Elapsed.TotalMilliseconds / files.Count),7:F1}ms");

            var scaleHist = detailRows.Where(r => r.count > 0)
                .GroupBy(r => Math.Round(r.scale, 2)).OrderBy(g => g.Key)
                .Select(g => $"{g.Key:0.00}×{g.Count()}");
            Console.WriteLine($"{"",-16} {"",33} scale: {string.Join("  ", scaleHist)}");

            if (detail) perImage.Add((preset, detailRows));
        }

        if (detail)
        {
            Console.WriteLine();
            Console.WriteLine("逐图明细（得分@角度 x尺度 e拟合残差 p极性一致性）：");
            var header = "preset".PadRight(16) + string.Concat(files.Select(f =>
                Path.GetFileName(f).Replace(".bmp", "").Replace(".png", "").PadLeft(23)));
            Console.WriteLine(header);
            foreach (var (preset, rows) in perImage)
            {
                var line = preset.Name.PadRight(16);
                foreach (var r in rows)
                    line += (r.count > 0
                        ? $"{r.score:F2}@{r.angle,4:F0}x{r.scale:F2}e{r.fit:F2}p{r.coh:F2}"
                        : "—").PadLeft(23);
                Console.WriteLine(line);
            }
        }

        Console.WriteLine();
        Console.WriteLine("[preset] p 列 = 极性一致性：真实实例无论对比度是否反转都应接近 1.00，");
        Console.WriteLine("         若某个解 p≈0.5，说明它只是“边缘密度相似”，位姿是错的。");
    }

    /// <summary>Every refined hypothesis for one image, ungated, with its diagnostics.</summary>
    public static void Explain(string imagePath, string presetName, string basePath = null, int top = 12)
    {
        if (!File.Exists(imagePath)) { Console.WriteLine($"[explain] not found: {imagePath}"); return; }

        var preset = PresetCatalog.Find(presetName);
        // Train on the batch base image (natural first of the folder) unless told otherwise, so the
        // diagnosis reflects a real run instead of a self-match.
        if (string.IsNullOrEmpty(basePath))
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(imagePath));
            var siblings = ImageLoader.Enumerate(folder!);
            basePath = siblings.Count > 0 ? siblings[0] : imagePath;
        }

        var trainImage = ImageLoader.Load(basePath);
        var roi = PatternTrainer.ContentBounds(trainImage);
        var tool = MakeTool(preset, acceptAll: true);
        var pattern = tool.Train(trainImage, roi, roi.X + (roi.Width - 1) / 2.0,
                                 roi.Y + (roi.Height - 1) / 2.0, preset.Grain);

        var image = imagePath == basePath ? trainImage : ImageLoader.Load(imagePath);
        Console.WriteLine($"preset = {preset.Name}   训练图 = {Path.GetFileName(basePath)}   " +
                          $"搜索图 = {Path.GetFileName(imagePath)}");
        Console.WriteLine($"features = {pattern.FeatureCount}   roi = {roi}   " +
                          $"angle {preset.RotationAngleMin:F0}..{preset.RotationAngleMax:F0}   " +
                          $"scale {preset.ScaleMin:F2}..{preset.ScaleMax:F2}   agnostic = {preset.IgnorePolarity}");
        var results = tool.Run(image, Math.Max(1, top));
        Console.WriteLine($"{"#",2} {"score",6} {"cover",6} {"coh",5} {"angle",7} {"scale",6} " +
                          $"{"fit",5} {"matched",8} {"bad",5} {"x",7} {"y",7}");
        for (int i = 0; i < results.Count; i++)
        {
            var r = results[i];
            Console.WriteLine($"{i,2} {r.Score,6:F3} {r.Coverage,6:P0} {r.PolarityCoherence,5:F2} " +
                              $"{r.AngleDegrees,7:F1} {r.Scale,6:F3} {r.FitError,5:F2} " +
                              $"{r.MatchedFeatures,8} {r.BadFeatures,5} {r.X,7:F1} {r.Y,7:F1}");
        }
        if (results.Count == 0) Console.WriteLine("（没有任何假设——搜索阶段就没找到候选）");
    }

    /// <summary>Print every preset side by side, marking the values that differ from the first one.</summary>
    public static void List()
    {
        var presets = PresetCatalog.BuiltIn;
        var props = typeof(SearchPreset).GetProperties()
            .Where(p => p.PropertyType == typeof(double) || p.PropertyType == typeof(int)
                        || p.PropertyType == typeof(bool))
            .ToArray();

        var labels = new Dictionary<string, string>
        {
            ["AcceptThreshold"] = "接受阈值 AcceptThreshold",
            ["ContrastThreshold"] = "对比度阈值 ContrastThreshold",
            ["ScoreUsingClutter"] = "杂波计分 ScoreUsingClutter",
            ["MinPolarityCoherence"] = "极性一致性门 MinPolarityCoherence",
            ["RotationAngleMin"] = "角度下限 RotationAngleMin",
            ["RotationAngleMax"] = "角度上限 RotationAngleMax",
            ["ScaleMin"] = "尺度下限 ScaleMin",
            ["ScaleMax"] = "尺度上限 ScaleMax",
            ["ApproximateNumberToFind"] = "预期数量 ApproximateNumberToFind",
            ["MaxResults"] = "最大结果 MaxResults",
            ["XYOverlap"] = "XY 重叠 XYOverlap",
            ["IgnorePolarity"] = "忽略极性 IgnorePolarity",
            ["AutoEdgeThreshold"] = "自动边缘阈值 AutoEdgeThreshold",
            ["EdgeThreshold"] = "边缘阈值 EdgeThreshold",
            ["Grain"] = "粒度 Grain",
            ["TrainMaxFeatures"] = "特征数上限 TrainMaxFeatures",
            ["TrainMinSpacing"] = "特征间距 TrainMinSpacing",
            ["PrecisionStep"] = "粗层步长 PrecisionStep",
            ["SubpixelRefinement"] = "亚像素 SubpixelRefinement",
            ["EnableClustering"] = "聚类 EnableClustering",
            ["ClusterRadius"] = "聚类半径 ClusterRadius",
            ["PreSmoothSigma"] = "预平滑 σ PreSmoothSigma",
            ["MaxCandidates"] = "候选数 MaxCandidates",
            ["TimeoutMs"] = "超时 TimeoutMs"
        };

        Console.WriteLine("列 0 为默认值；行尾 * 表示该预设与默认不同。\n");
        Console.WriteLine($"{"参数",-34}" + string.Concat(presets.Select((p, i) => $"{"[" + i + "]",10}")));
        Console.WriteLine($"{"",-34}" + string.Concat(presets.Select(p => $"{Short(p.Name),10}")));

        foreach (var prop in props)
        {
            object first = prop.GetValue(presets[0])!;
            var cells = presets.Select(p => prop.GetValue(p)!).ToArray();
            if (cells.All(v => Equals(v, first))) continue;

            string label = labels.TryGetValue(prop.Name, out var l) ? l : prop.Name;
            var line = new System.Text.StringBuilder(label.PadRight(34));
            foreach (var v in cells)
            {
                string s = v switch
                {
                    bool b => b ? "✓" : "✗",
                    double d => Math.Abs(d) < 1e-9 ? "0" : d.ToString("0.###"),
                    _ => v.ToString()!
                };
                line.Append((s + (Equals(v, first) ? "" : "*")).PadLeft(10));
            }
            Console.WriteLine(line.ToString());
        }

        Console.WriteLine();
        foreach (var (p, i) in presets.Select((p, i) => (p, i)))
            Console.WriteLine($"[{i}] {p.Name} — {p.Description}");

        static string Short(string s)
        {
            int cut = s.IndexOfAny(new[] { '（', '(' });
            return (cut > 0 ? s[..cut] : s).Trim();
        }
    }
}
