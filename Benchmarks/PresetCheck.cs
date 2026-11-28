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
    public static void Run(string directory, bool detail = false)
    {
        if (!Directory.Exists(directory)) { Console.WriteLine($"[preset] directory not found: {directory}"); return; }

        var perImage = new List<(SearchPreset preset, List<(string file, double score, double angle, int count)> rows)>();
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
            var detailRows = new List<(string file, double score, double angle, int count)>();
            var watch = Stopwatch.StartNew();

            foreach (var file in files)
            {
                var image = ImageLoader.Load(file);
                int want = Math.Max(1, Math.Max(preset.MaxResults, preset.ApproximateNumberToFind));
                var results = tool.Run(image, want);
                detailRows.Add((Path.GetFileName(file),
                                results.Count > 0 ? results[0].Score : 0,
                                results.Count > 0 ? results[0].AngleDegrees : 0,
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

            if (detail) perImage.Add((preset, detailRows));
        }

        if (detail)
        {
            Console.WriteLine();
            Console.WriteLine("逐图明细（得分 / 角度 / 实例数）：");
            var header = "preset".PadRight(16) + string.Join("", files.Select(f =>
                Path.GetFileName(f).Replace(".bmp", "").Replace(".png", "").PadLeft(9)));
            Console.WriteLine(header);
            foreach (var (preset, rows) in perImage)
            {
                var line = preset.Name.PadRight(16);
                foreach (var r in rows)
                    line += (r.count > 0 ? $"{r.score:F2}@{r.angle,4:F0}" : "   —    ").PadLeft(9);
                Console.WriteLine(line);
            }
        }

        Console.WriteLine();
        Console.WriteLine("[preset] 说明：小角度快搜按设计会拒掉超出 ±10° 的帧；多目标的 inst 列应大于 1；");
        Console.WriteLine("         反光/剪影与暗场两组应把对应难例从『无结果』拉回可接受分数。");
    }
}
