using System.Diagnostics;
using PatMaxSharp.Core;
using PatMaxSharp.Training;

namespace PatMaxSharp.Benchmarks;

/// <summary>
/// Runs the matcher over a folder of real images (PNG or BMP): trains on the first image and
/// locates the part in every other image.
///
/// Two kinds of set are handled:
///  * same pose, different illumination/exposure — the interesting number is the pose drift
///    against the base image, because the part did not move;
///  * varying pose (translation grid, rotation variants) — the raw pose is the result.
///
/// The report always prints both, so a set can be judged without knowing which one it is.
/// </summary>
public static class CalibrationCheck
{
    /// <summary>Optional angular window override (command line), so a folder can be checked
    /// under the same restriction the UI would apply.</summary>
    public static double? AngleMin;
    public static double? AngleMax;
    public static void Run(string directory, bool preview, bool autoNoiseFloor = true, bool normalizedCorrelation = false,
        bool ignorePolarity = false, bool cognexDemoPreset = false)
    {
        if (!Directory.Exists(directory)) { Console.WriteLine($"[cal] directory not found: {directory}"); return; }

        var files = ImageLoader.Enumerate(directory);
        if (files.Count < 2) { Console.WriteLine("[cal] need at least two PNG/BMP files"); return; }

        var detector = new PatMaxSharp.Edge.GaborEdgeDetector { AutoNoiseFloor = autoNoiseFloor };
        var baseImage = ImageLoader.Load(files[0]);
        if (preview) DumpPreview(baseImage, files[0]);

        var roi = PatternTrainer.ContentBounds(baseImage);
        Console.WriteLine($"[cal] folder  = {directory}");
        Console.WriteLine($"[cal] images  = {files.Count}  ({string.Join(", ", ImageLoader.SupportedExtensions)})");
        Console.WriteLine($"[cal] base    = {Path.GetFileName(files[0])}  {baseImage.Width}x{baseImage.Height}  " +
                          $"content bbox = {roi}");

        var tool = new PatMaxTool(detector)
        {
            AcceptThreshold = 0.5,
            CoarseAcceptThreshold = 0.33,
            RotationAngleMin = -180,
            RotationAngleMax = 180,
            TrainMaxFeatures = 600
        };

        // Mirror the runtime page of the CogPMAlignTool1 demo running in VisionPro QuickBuild:
        // 3 to find, accept 0.5, coarse-accept method off, angle zone -45..45, scale zone 0.6..1.6,
        // contrast threshold 0, clutter not scored.
        int resultsPerImage = 1;
        if (cognexDemoPreset)
        {
            resultsPerImage = 3;
            tool.RotationAngleMin = -45;
            tool.RotationAngleMax = 45;
            tool.ScaleMin = 0.6;
            tool.ScaleMax = 1.6;
            tool.ContrastThreshold = 0.0;
            tool.UseClutter = false;
            tool.CoarseAcceptThreshold = 0.0;
            tool.NoiseFloor = 5.0;
            tool.IgnorePolarity = true;
        }
        Console.WriteLine($"[cal] preset  = {(cognexDemoPreset ? "Cognex demo (angle +-45, scale 0.6-1.6, contrast 0, clutter off, find 3)" : "library defaults")}");
        if (AngleMin.HasValue) tool.RotationAngleMin = AngleMin.Value;
        if (AngleMax.HasValue) tool.RotationAngleMax = AngleMax.Value;
        Console.WriteLine($"[cal] options = autoNoiseFloor:{autoNoiseFloor}  normalizedCorrelation:{normalizedCorrelation}  " +
                          $"ignorePolarity:{ignorePolarity}  angle {tool.RotationAngleMin:F0}..{tool.RotationAngleMax:F0}");
        tool.NormalizedCorrelation = normalizedCorrelation;
        tool.IgnorePolarity = ignorePolarity;

        var trainWatch = Stopwatch.StartNew();
        var pattern = tool.TrainRegion(baseImage, roi);
        trainWatch.Stop();
        Console.WriteLine($"[cal] trained = {pattern}");
        Console.WriteLine($"[cal] train   = {trainWatch.Elapsed.TotalMilliseconds:F1} ms  " +
                          $"(edge floor used {tool.EdgeDetector.LastEffectiveFloor:F2}, auto={tool.EdgeDetector.AutoNoiseFloor})");
        Console.WriteLine();
        Console.WriteLine($"{"file",-12} {"score",7} {"x",9} {"y",9} {"angle",10} {"scale",7} {"cov",6} " +
                          $"{"drift",8} {"dRot",8} {"time",8}");

        var rows = new List<(string Name, MatchResult Result)>();
        int missed = 0;

        foreach (var file in files)
        {
            var image = ImageLoader.Load(file);
            var watch = Stopwatch.StartNew();
            var results = tool.Run(image, resultsPerImage);
            watch.Stop();
            string name = Path.GetFileName(file);

            if (results.Count == 0 || results[0].Score < 0.001)
            {
                // Nothing above the accept threshold: show the best hypothesis anyway, so a
                // weak result is visible instead of just "none".
                var open = tool.AcceptThreshold;
                tool.AcceptThreshold = 0.0;
                var best = tool.Run(image, 1);
                tool.AcceptThreshold = open;

                if (best.Count > 0 && best[0].Score >= 0.001)
                {
                    Console.WriteLine($"{name,-12} {best[0].Score,7:F3} {best[0].X,9:F2} {best[0].Y,9:F2} " +
                                      $"{best[0].AngleDegrees,9:F2}° {best[0].Scale,7:F3} {best[0].Coverage,6:P0} " +
                                      $"{"-",8} {"-",8} {watch.Elapsed.TotalMilliseconds,7:F1}ms  below threshold");
                    rows.Add((name, best[0]));
                }
                else
                {
                    missed++;
                    Console.WriteLine($"{name,-12} {"none",7} {"-",9} {"-",9} {"-",10} {"-",7} {"-",6} " +
                                      $"{"-",8} {"-",8} {watch.Elapsed.TotalMilliseconds,7:F1}ms");
                }
                continue;
            }

            var r = results[0];
            rows.Add((name, r));

            // A scale pinned to the search-zone boundary is a degenerate solution: the model can
            // shrink onto just the strongest inner edges and still score well, so the number is
            // high but the pose is not the part. Say so instead of letting it read as a hit.
            string note = "";
            if (tool.ScaleMin != tool.ScaleMax)
            {
                if (Math.Abs(r.Scale - tool.ScaleMin) < 0.01) note = $"  scale@{tool.ScaleMin:F2} (zone edge)";
                else if (Math.Abs(r.Scale - tool.ScaleMax) < 0.01) note = $"  scale@{tool.ScaleMax:F2} (zone edge)";
            }

            Console.WriteLine($"{name,-12} {r.Score,7:F3} {r.X,9:F2} {r.Y,9:F2} " +
                              $"{r.AngleDegrees,9:F2}° {r.Scale,7:F3} {r.Coverage,6:P0} " +
                              $"{"",8} {"",8} {watch.Elapsed.TotalMilliseconds,7:F1}ms{note}");
        }

        // ---- drift against the base pose (meaningful when the part did not move) ----
        var baseRow = rows.FirstOrDefault(x => x.Name.Equals(Path.GetFileName(files[0]), StringComparison.OrdinalIgnoreCase));
        if (baseRow.Result != null)
        {
            var b = baseRow.Result;
            Console.WriteLine();
            Console.WriteLine($"{"file",-12} {"dX",8} {"dY",8} {"dPos",8} {"dAngle",8} {"dScale",8}");
            double maxPos = 0, sumSq = 0, maxAng = 0;
            int n = 0;

            foreach (var (name, r) in rows)
            {
                double dx = r.X - b.X, dy = r.Y - b.Y;
                double dPos = Math.Sqrt(dx * dx + dy * dy);
                double dAng = NormalizeDeg(r.AngleDegrees - b.AngleDegrees);
                double dScale = r.Scale - b.Scale;

                Console.WriteLine($"{name,-12} {dx,8:F2} {dy,8:F2} {dPos,8:F2} {dAng,8:F2}° {dScale,8:F3}");

                // the base image itself is not a drift sample
                if (name == baseRow.Name) continue;
                maxPos = Math.Max(maxPos, dPos);
                maxAng = Math.Max(maxAng, Math.Abs(dAng));
                sumSq += dPos * dPos;
                n++;
            }

            Console.WriteLine();
            int strong = rows.Count(x => x.Result.Score >= 0.90);
            int partial = rows.Count(x => x.Result.Score >= 0.70 && x.Result.Score < 0.90);
            int weak = rows.Count(x => x.Result.Score < 0.70);

            Console.WriteLine($"[cal] found {rows.Count}/{files.Count}, missed {missed}");
            Console.WriteLine($"[cal] score bands: >=0.90 {strong}, 0.70-0.90 {partial}, <0.70 {weak}");

            if (n > 0)
            {
                double rms = Math.Sqrt(sumSq / n);
                Console.WriteLine($"[cal] pose spread vs base: RMS {rms:F2} px, max {maxPos:F2} px, " +
                                  $"max rotation {maxAng:F2}°  (n={n})");

                // A small max drift means the part did not move, so the spread IS a stability
                // measurement; a large one means the set is a pose sweep and there is nothing
                // to be stable about.
                if (maxPos <= 2.0 && maxAng <= 2.0)
                    Console.WriteLine($"[cal] verdict: {(maxPos <= 0.5 && maxAng <= 0.5 && missed == 0 ? "STABLE" : "UNSTABLE")}" +
                                      $"  (same-pose set: thresholds 0.5 px, 0.5 deg)");
                else
                    Console.WriteLine("[cal] verdict: pose sweep (the part moves across the set) - " +
                                      "spread is not a stability metric; judge by the score bands above");
            }
        }

        // ---- named rotation variants (r1, r2, ...) ----
        if (baseRow.Result != null)
        {
            var variants = rows.Where(x => x.Name.StartsWith("r", StringComparison.OrdinalIgnoreCase)
                                        && !x.Name.Equals(baseRow.Name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (variants.Count > 0)
            {
                Console.WriteLine();
                foreach (var (name, r) in variants)
                    Console.WriteLine($"[cal] {name}: relative rotation {NormalizeDeg(r.AngleDegrees - baseRow.Result.AngleDegrees),8:F2}°");
            }
        }
    }

    private static double NormalizeDeg(double a)
    {
        while (a > 180) a -= 360;
        while (a < -180) a += 360;
        return a;
    }

    /// <summary>Bounding box of everything above 35% of the image's dynamic range.</summary>
    private static RectI ContentBounds(GrayImage image, out byte threshold)
    {
        byte min = 255, max = 0;
        foreach (byte v in image.Data) { if (v < min) min = v; if (v > max) max = v; }

        threshold = (byte)(min + (max - min) * 0.35);
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.Data[y * image.Width + x] > threshold)
                {
                    if (x < x0) x0 = x;
                    if (y < y0) y0 = y;
                    if (x > x1) x1 = x;
                    if (y > y1) y1 = y;
                }

        if (x1 < 0) return new RectI(0, 0, image.Width, image.Height);
        return RectI.FromLTRB(x0, y0, x1 + 1, y1 + 1);
    }

    /// <summary>Coarse ASCII view so a headless run still shows what the image contains.</summary>
    private static void DumpPreview(GrayImage image, string name)
    {
        const string ramp = " .:-=+*#%@";
        int cols = 64, rows = 24;
        Console.WriteLine($"[cal] preview of {Path.GetFileName(name)} ({image.Width}x{image.Height}), '{ramp}' = dark..bright:");
        for (int r = 0; r < rows; r++)
        {
            var line = new System.Text.StringBuilder(cols);
            for (int c = 0; c < cols; c++)
            {
                int x = (int)((c + 0.5) * image.Width / cols);
                int y = (int)((r + 0.5) * image.Height / rows);
                int v = image.At(x, y);
                int idx = v * (ramp.Length - 1) / 255;
                line.Append(ramp[idx]);
            }
            Console.WriteLine("      " + line);
        }
        Console.WriteLine();
    }
}
