using System.Diagnostics;
using PatMaxSharp.Core;

namespace PatMaxSharp.Benchmarks;

/// <summary>
/// Runs the matcher over a folder of real PNG images: train on the first image, locate the
/// part in every other image, report pose/score/time. Used with the calibration image set
/// (9 translations in a 3x3 grid plus three rotation variants) to check the engine on real
/// data rather than only on synthetic ground truth.
/// </summary>
public static class CalibrationCheck
{
    public static void Run(string directory, bool preview)
    {
        if (!Directory.Exists(directory)) { Console.WriteLine($"[cal] directory not found: {directory}"); return; }

        var files = Directory.GetFiles(directory, "*.png")
            .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (files.Count < 2) { Console.WriteLine("[cal] need at least two PNG files"); return; }

        var baseImage = PngReader.Load(files[0]);
        if (preview) DumpPreview(baseImage, files[0]);

        var roi = ContentBounds(baseImage, out byte threshold);
        Console.WriteLine($"[cal] base = {Path.GetFileName(files[0])}  {baseImage.Width}x{baseImage.Height}  " +
                          $"content bbox = {roi}  (threshold {threshold})");

        var tool = new PatMaxTool
        {
            AcceptThreshold = 0.5,
            CoarseAcceptThreshold = 0.33,
            RotationAngleMin = -180,
            RotationAngleMax = 180,
            TrainMaxFeatures = 600
        };

        var trainWatch = Stopwatch.StartNew();
        var pattern = tool.TrainRegion(baseImage, roi);
        trainWatch.Stop();
        Console.WriteLine($"[cal] trained: {pattern}, {trainWatch.Elapsed.TotalMilliseconds:F1} ms");
        Console.WriteLine();
        Console.WriteLine($"{"file",-12} {"score",7} {"x",9} {"y",9} {"angle",9} {"scale",7} {"cov",7} {"time",8}");

        var poses = new Dictionary<string, MatchResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var image = PngReader.Load(file);
            var watch = Stopwatch.StartNew();
            var results = tool.Run(image, 1);
            watch.Stop();

            string name = Path.GetFileName(file);
            if (results.Count == 0)
            {
                // nothing above the accept threshold: re-run with the gate open so the
                // report still shows what the best hypothesis looked like
                var open = tool.AcceptThreshold; tool.AcceptThreshold = 0.0;
                var best = tool.Run(image, 1);
                tool.AcceptThreshold = open;
                if (best.Count > 0)
                    Console.WriteLine($"{name,-12} {(best[0].Score < 0.001 ? "none" : best[0].Score.ToString("F3")),7} " +
                                      $"{best[0].X,9:F2} {best[0].Y,9:F2} {best[0].Angle * 180 / Math.PI,8:F2}deg " +
                                      $"{best[0].Scale,7:F3} {best[0].Coverage,7:P0} " +
                                      $"{watch.Elapsed.TotalMilliseconds,7:F1}ms   (below threshold)");
                else
                {
                    Console.WriteLine($"{name,-12} {"none",7} {"-",9} {"-",9} {"-",9} {"-",7} {"-",7} " +
                                      $"{watch.Elapsed.TotalMilliseconds,7:F1}ms");
                    Console.WriteLine($"             diag: {tool.LastDiagnostics}");
                }
                continue;
            }
            var r = results[0];
            poses[name] = r;
            Console.WriteLine($"{name,-12} {r.Score,7:F3} {r.X,9:F2} {r.Y,9:F2} " +
                              $"{r.Angle * 180 / Math.PI,8:F2}deg {r.Scale,7:F3} {r.Coverage,7:P0} " +
                              $"{watch.Elapsed.TotalMilliseconds,7:F1}ms");
        }

        Console.WriteLine();
        Console.WriteLine($"[cal] found {poses.Count}/{files.Count}");

        // rotation variants: report the angle relative to the base pose
        if (poses.TryGetValue(Path.GetFileName(files[0]), out var basePose))
        {
            foreach (var pair in poses)
            {
                if (!pair.Key.StartsWith("r", StringComparison.OrdinalIgnoreCase)) continue;
                double delta = (pair.Value.Angle - basePose.Angle) * 180 / Math.PI;
                while (delta > 180) delta -= 360;
                while (delta < -180) delta += 360;
                Console.WriteLine($"[cal] {pair.Key}: relative rotation {delta,8:F2} deg");
            }
        }
    }

    /// <summary>Bounding box of everything above the midpoint between the darkest and brightest pixel.</summary>
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
