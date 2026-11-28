using System.Diagnostics;
using ShapeAnchor.Core;
using ShapeAnchor.Training;

namespace ShapeAnchor.Benchmarks;

/// <summary>
/// Runs every built-in preset over one folder so a preset claim can be checked instead of
/// trusted. Reports, per preset: how many images produced a result, the score bands, the mean
/// score and the largest instance count seen (the last one matters for the multi-target preset).
/// </summary>
public static class PresetCheck
{
    /// <summary>
    /// Print every preset side by side, marking the values that differ from the first one. Read
    /// by reflection so the table cannot drift from the catalog.
    /// </summary>
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
        Console.WriteLine($"{"",-34}" + string.Concat(presets.Select(p => $"{Trim(p.Name),10}")));

        foreach (var prop in props)
        {
            object first = prop.GetValue(presets[0])!;
            var cells = presets.Select(p => prop.GetValue(p)!).ToArray();
            bool anyDiff = cells.Any(v => !Equals(v, first));
            if (!anyDiff) continue;

            string label = labels.TryGetValue(prop.Name, out var l) ? l : prop.Name;
            var line = new System.Text.StringBuilder();
            line.Append(label.PadRight(34));
            foreach (var v in cells)
            {
                string s = v switch
                {
                    bool b => b ? "✓" : "✗",
                    double d => Math.Abs(d) < 1e-9 ? "0" : d.ToString("0.###"),
                    _ => v.ToString()!
                };
                bool diff = !Equals(v, first);
                line.Append((s + (diff ? "*" : "")).PadLeft(10));
            }
            Console.WriteLine(line.ToString());
        }

        Console.WriteLine();
        foreach (var (p, i) in presets.Select((p, i) => (p, i)))
            Console.WriteLine($"[{i}] {p.Name} — {p.Description}");

        static string Trim(string s)
        {
            int cut = s.IndexOfAny(new[] { '（', '(' });
            return (cut > 0 ? s[..cut] : s).Trim();
        }
    }
    public static void Run(string directory, bool detail = false)
    {
        if (!Directory.Exists(directory)) { Console.WriteLine($"[preset] directory not found: {directory}"); return; }

        var perImage = new List<(SearchPreset preset, List<(string file, double score, double angle, double scale, int count)> rows)>();
        var files = ImageLoader.Enumerate(directory);
        if (files.Count < 2) { Console.WriteLine("[preset] need at least two images"); return; }

        var baseImage = ImageLoader.Load(files[0]);
        var roi = PatternTrainer.ContentBounds(baseImage);
        Console.WriteLine($"[preset] folder = {directory}  images = {files.Count}  " +
                          $"base = {Path.GetFileName(files[0])}  roi = {roi}");
        Console.WriteLine();
        Console.WriteLine($"{"preset",-16} {"found",6} {">=0.90",7} {"0.7-0.9",8} {"<0.7",5} " +
                          $"{"mean",6} {"inst",5} {"train",7} {"time",8}");

        foreach (var preset in PresetCatalog.BuiltIn)
        {
            var tool = new ShapeAnchorTool
            {
                AcceptThreshold = preset.AcceptThreshold,
                ContrastThreshold = preset.ContrastThreshold,
                UseClutter = preset.ScoreUsingClutter,
                RotationAngleMin = preset.RotationAngleMin,
                RotationAngleMax = preset.RotationAngleMax,
                ScaleMin = preset.ScaleMin,
                ScaleMax = preset.ScaleMax,
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
                TrainMaxFeatures = preset.TrainMaxFeatures,
                TrainMinSpacing = preset.TrainMinSpacing
            };

            var trainWatch = Stopwatch.StartNew();
            var pattern = tool.Train(baseImage, roi, roi.X + (roi.Width - 1) / 2.0, roi.Y + (roi.Height - 1) / 2.0,
                                     preset.Grain);
            trainWatch.Stop();

            int found = 0, strong = 0, mid = 0, weak = 0, maxInstances = 0;
            double scoreSum = 0;
            var detailRows = new List<(string file, double score, double angle, double scale, int count)>();
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
                              $"{trainWatch.Elapsed.TotalMilliseconds,6:F0}ms {(watch.Elapsed.TotalMilliseconds / files.Count),7:F1}ms");

            var scaleHist = detailRows
                .Where(r => r.count > 0)
                .GroupBy(r => Math.Round(r.scale, 2))
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key:0.00}×{g.Count()}");
            Console.WriteLine($"{"",-16} {"",33} scale: {string.Join("  ", scaleHist)}");

            if (detail) perImage.Add((preset, detailRows));
        }

        if (detail)
        {
            Console.WriteLine();
            Console.WriteLine("逐图明细（得分 / 角度 / 实例数）：");
            var header = "preset".PadRight(16) + string.Join("", files.Select(f =>
                Path.GetFileName(f).Replace(".bmp", "").Replace(".png", "").PadLeft(13)));
            Console.WriteLine(header);
            foreach (var (preset, rows) in perImage)
            {
                var line = preset.Name.PadRight(16);
                foreach (var r in rows)
                    line += (r.count > 0 ? $"{r.score:F2}@{r.angle,4:F0}x{r.scale:F2}" : "   —    ").PadLeft(13);
                Console.WriteLine(line);
            }
        }

        Console.WriteLine();
        Console.WriteLine("[preset] 说明：小角度快搜按设计会拒掉超出 ±10° 的帧；多目标的 inst 列应大于 1；");
        Console.WriteLine("         反光/剪影与暗场两组应把对应难例从『无结果』拉回可接受分数。");
    }
}
