using System.Diagnostics;
using ShapeAnchor.Core;
using ShapeAnchor.Matching;

namespace ShapeAnchor.Benchmarks;

/// <summary>
/// Accuracy + performance suite with exact ground truth. Used by `--selftest` in CI and by
/// the numbers published in the README, so every claim in the docs is reproducible.
/// </summary>
public static class BenchmarkSuite
{
    public sealed class Case
    {
        public string Name = "";
        public bool Passed;
        public string Detail = "";
        public double Score;
        public double PosErrorPx;
        public double AngleErrorDeg;
        public double ScaleError;
        public double TimeMs;
        public double TruthX, TruthY, TruthAngleDeg, TruthScale;
        public double FoundX, FoundY, FoundAngleDeg, FoundScale;
    }

    private static ShapeAnchorTool CreateTool()
    {
        var tool = new ShapeAnchorTool
        {
            AcceptThreshold = 0.5,
            ContrastThreshold = 0.05,
            TrainMaxFeatures = 500,
            TrainMinSpacing = 2.0,
            CoarseFeatureCount = 32,
            MidFeatureCount = 96,
            MaxCandidates = 20,
            PrecisionStep = 2,
            SubpixelRefinement = true,
            EnableClustering = true,
            ClusterRadius = 10,
            PreSmoothSigma = 0,          // edge extraction already smooths with a DoG
            RotationAngleMin = -180,
            RotationAngleMax = 180,
            ScaleMin = 1.0,
            ScaleMax = 1.0
        };
        return tool;
    }

    private static PatternModel TrainBase(ShapeAnchorTool tool)
    {
        var baseImg = SyntheticPart.Base();
        return tool.TrainRegion(baseImg, new RectI(0, 0, baseImg.Width, baseImg.Height));
    }

    private static Case Measure(string name, ShapeAnchorTool tool, GrayImage scene,
        double truthX, double truthY, double truthAngleDeg, double truthScale,
        double posTol = 0.25, double angTol = 0.25, double scaleTol = 0.015)
    {
        var sw = Stopwatch.StartNew();
        var results = tool.Run(scene, 1);
        sw.Stop();

        var c = new Case
        {
            Name = name,
            TimeMs = sw.Elapsed.TotalMilliseconds,
            TruthX = truthX, TruthY = truthY,
            TruthAngleDeg = truthAngleDeg, TruthScale = truthScale
        };

        if (results.Count == 0)
        {
            c.Passed = false;
            c.Detail = "no result";
            return c;
        }

        var r = results[0];
        c.Score = r.Score;
        c.FoundX = r.X; c.FoundY = r.Y;
        c.FoundAngleDeg = r.Angle * 180 / Math.PI;
        c.FoundScale = r.Scale;

        c.PosErrorPx = Math.Sqrt(Math.Pow(r.X - truthX, 2) + Math.Pow(r.Y - truthY, 2));
        c.AngleErrorDeg = Math.Abs(NormalizeDeg(c.FoundAngleDeg - truthAngleDeg));
        c.ScaleError = Math.Abs(r.Scale - truthScale);

        c.Passed = c.PosErrorPx <= posTol && c.AngleErrorDeg <= angTol && c.ScaleError <= scaleTol && r.Score > 0.6;
        c.Detail = $"score={r.Score:F3} posErr={c.PosErrorPx:F2}px angErr={c.AngleErrorDeg:F2}deg " +
                   $"scaleErr={c.ScaleError:F4} t={c.TimeMs:F1}ms";
        return c;
    }

    private static double NormalizeDeg(double a)
    {
        while (a > 180) a -= 360;
        while (a < -180) a += 360;
        return a;
    }

    public static List<Case> RunAccuracy()
    {
        var cases = new List<Case>();
        var tool = CreateTool();
        TrainBase(tool);

        // ---- 1. pure translation, sub-pixel ----
        {
            var scene = SyntheticPart.Render(420, 360, 231.4, 168.7, 0, 1.0,
                new SyntheticPart.SceneOptions { Seed = 11 });
            cases.Add(Measure("translate", tool, scene, 231.4, 168.7, 0, 1.0));
        }

        // ---- 2. rotation 17.5 deg ----
        {
            double ang = 17.5 * Math.PI / 180;
            var scene = SyntheticPart.Render(420, 360, 208.0, 176.0, ang, 1.0,
                new SyntheticPart.SceneOptions { Seed = 12 });
            cases.Add(Measure("rotate 17.5deg", tool, scene, 208.0, 176.0, 17.5, 1.0));
        }

        // ---- 3. rotation -63 deg ----
        {
            double ang = -63.0 * Math.PI / 180;
            var scene = SyntheticPart.Render(420, 360, 214.5, 186.3, ang, 1.0,
                new SyntheticPart.SceneOptions { Seed = 13 });
            cases.Add(Measure("rotate -63deg", tool, scene, 214.5, 186.3, -63.0, 1.0));
        }

        // ---- 4. scale search ----
        {
            var scaled = CreateTool();
            scaled.ScaleMin = 0.9;
            scaled.ScaleMax = 1.15;
            TrainBase(scaled);
            var scene = SyntheticPart.Render(420, 360, 206.0, 182.0, 0, 1.12,
                new SyntheticPart.SceneOptions { Seed = 14 });
            cases.Add(Measure("scale 1.12", scaled, scene, 206.0, 182.0, 0, 1.12,
                posTol: 0.4, angTol: 0.4, scaleTol: 0.02));
        }

        // ---- 5. noise sigma = 20 ----
        {
            var scene = SyntheticPart.Render(420, 360, 223.0, 171.0, 6.0 * Math.PI / 180, 1.0,
                new SyntheticPart.SceneOptions { Seed = 15, NoiseStd = 20 });
            cases.Add(Measure("noise 20", tool, scene, 223.0, 171.0, 6.0, 1.0, posTol: 0.4, angTol: 0.5));
        }

        // ---- 6. ~22% occlusion (the discriminating features survive: the boss and two holes) ----
        {
            var occ = new RectI(150, 195, 85, 85);
            var scene = SyntheticPart.Render(420, 360, 210.0, 180.0, 0, 1.0,
                new SyntheticPart.SceneOptions { Seed = 16, Occlusion = occ, OcclusionValue = 128 });
            cases.Add(Measure("occlusion 22%", tool, scene, 210.0, 180.0, 0, 1.0, posTol: 0.4, angTol: 0.6));
        }

        // ---- 7. clutter ----
        {
            var scene = SyntheticPart.Render(420, 360, 205.0, 190.0, 12.0 * Math.PI / 180, 1.0,
                new SyntheticPart.SceneOptions { Seed = 17, ClutterShapes = 6 });
            cases.Add(Measure("clutter x6", tool, scene, 205.0, 190.0, 12.0, 1.0, posTol: 0.35, angTol: 0.4));
        }

        return cases;
    }

    /// <summary>
    /// Negative cases: a scene that does not contain the part at all. A matcher that never
    /// misses is a matcher that always lies, so these are part of the gate.
    /// </summary>
    public static List<Case> RunNegative()
    {
        var cases = new List<Case>();
        var tool = CreateTool();
        TrainBase(tool);

        // background + clutter only, no part
        {
            var scene = SyntheticPart.Render(420, 360, -500, -500, 0, 1.0,
                new SyntheticPart.SceneOptions { Seed = 51, ClutterShapes = 10 });
            var sw = Stopwatch.StartNew();
            var res = tool.Run(scene, 5);
            sw.Stop();
            int accepted = res.Count(r => r.Accepted);
            cases.Add(new Case
            {
                Name = "empty scene",
                Passed = accepted == 0,
                Score = accepted,
                TimeMs = sw.Elapsed.TotalMilliseconds,
                Detail = $"accepted={accepted} (expected 0) t={sw.Elapsed.TotalMilliseconds:F1}ms"
            });
        }

        // a disk that is similar in size but not the part
        {
            var strict = CreateTool();
            strict.AcceptThreshold = 0.6;      // production-recommended threshold
            TrainBase(strict);
            var scene = new GrayImage(420, 360);
            for (int y = 0; y < 360; y++)
                for (int x = 0; x < 420; x++)
                {
                    double dx = x - 210, dy = y - 180;
                    double r = Math.Sqrt(dx * dx + dy * dy);
                    byte v = (byte)(r < 60 ? 210 : 60);
                    scene[x, y] = v;
                }
            var sw = Stopwatch.StartNew();
            var res = strict.Run(scene, 5);
            sw.Stop();
            int accepted = res.Count(r => r.Accepted);
            double top = res.Count > 0 ? res.Max(r => r.Score) : 0;
            cases.Add(new Case
            {
                Name = "similar disk",
                Passed = accepted == 0,
                Score = top,
                TimeMs = sw.Elapsed.TotalMilliseconds,
                Detail = $"topScore={top:F3} accepted={accepted} (expected 0 with threshold 0.6) t={sw.Elapsed.TotalMilliseconds:F1}ms"
            });
        }
        return cases;
    }

    public static List<Case> RunMultiInstance()
    {
        var cases = new List<Case>();
        var tool = CreateTool();
        TrainBase(tool);
        tool.EnableClustering = true;
        tool.ClusterRadius = 30;

        int w = 620, h = 460;
        var scene = SyntheticPart.Render(w, h, 0, 0, 0, 1.0,
            new SyntheticPart.SceneOptions { Seed = 21, BackgroundGradient = 8 });

        // Stamp three instances at different poses over the same background.
        var poses = new[]
        {
            (x: 140.0, y: 130.0, a: 0.0, s: 1.0),
            (x: 430.0, y: 150.0, a: 25.0, s: 1.0),
            (x: 300.0, y: 330.0, a: -40.0, s: 1.0)
        };

        foreach (var p in poses)
        {
            var single = SyntheticPart.Render(w, h, p.x, p.y, p.a * Math.PI / 180, p.s,
                new SyntheticPart.SceneOptions { Seed = 22, Background = 0, BackgroundGradient = 0 });
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // background is exactly 0, so any non-zero pixel belongs to the part
                    double v = single[x, y];
                    if (v > 0) scene[x, y] = (byte)v;
                }
        }

        var sw = Stopwatch.StartNew();
        var results = tool.Run(scene, 10);
        sw.Stop();

        int found = 0;
        double worstPos = 0, worstAng = 0;
        foreach (var p in poses)
        {
            MatchResult best = null;
            double bestDist = double.MaxValue;
            foreach (var r in results)
            {
                double d = Math.Sqrt(Math.Pow(r.X - p.x, 2) + Math.Pow(r.Y - p.y, 2));
                if (d < bestDist) { bestDist = d; best = r; }
            }
            if (best != null && bestDist < 2.0)
            {
                found++;
                worstPos = Math.Max(worstPos, bestDist);
                worstAng = Math.Max(worstAng, Math.Abs(NormalizeDeg(best.Angle * 180 / Math.PI - p.a)));
            }
        }

        cases.Add(new Case
        {
            Name = $"multi-instance ({poses.Length} parts)",
            Passed = found == poses.Length && worstPos < 0.6 && worstAng < 0.6,
            Score = found / (double)poses.Length,
            PosErrorPx = worstPos,
            AngleErrorDeg = worstAng,
            TimeMs = sw.Elapsed.TotalMilliseconds,
            Detail = $"found={found}/{poses.Length} worstPos={worstPos:F2}px worstAng={worstAng:F2}deg " +
                     $"t={sw.Elapsed.TotalMilliseconds:F1}ms"
        });
        return cases;
    }

    /// <summary>Performance sweep over realistic image sizes.</summary>
    public static List<(string Size, double TimeMs, double MpxPerSec, double Score)> RunPerformance()
    {
        var rows = new List<(string, double, double, double)>();
        var tool = CreateTool();
        TrainBase(tool);

        foreach (var (w, h) in new[] { (256, 256), (640, 480), (1280, 960) })
        {
            var scene = SyntheticPart.Render(w, h, w * 0.45, h * 0.5, 6 * Math.PI / 180, 1.0,
                new SyntheticPart.SceneOptions { Seed = 31, NoiseStd = 3, ClutterShapes = 3 });

            tool.Run(scene, 1); // warm up JIT
            const int iterations = 10;
            var sw = Stopwatch.StartNew();
            double score = 0;
            for (int i = 0; i < iterations; i++)
            {
                var r = tool.Run(scene, 1);
                score += r.Count > 0 ? r[0].Score : 0;
            }
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / iterations;
            double mpx = (w * (double)h / 1_000_000.0) / (ms / 1000.0);
            rows.Add(($"{w}x{h}", ms, mpx, score / iterations));
        }
        return rows;
    }

    /// <summary>Edge-detector throughput (single stage), for the README performance table.</summary>
    public static List<(string Mode, double TimeMs, double MpxPerSec)> RunEdgeBenchmark()
    {
        var rows = new List<(string, double, double)>();
        var img = SyntheticPart.Render(640, 480, 320, 240, 10 * Math.PI / 180, 1.0,
            new SyntheticPart.SceneOptions { Seed = 41 });
        double mpxPerImage = img.Width * (double)img.Height / 1_000_000.0;

        foreach (var (method, simd, parallel, label) in new[]
        {
            (Edge.EdgeMethod.GradientOfGaussian, false, false, "DoG scalar serial"),
            (Edge.EdgeMethod.GradientOfGaussian, true, false, "DoG SIMD serial"),
            (Edge.EdgeMethod.GradientOfGaussian, true, true, "DoG SIMD parallel"),
            (Edge.EdgeMethod.GaborBank, true, true, "Gabor12 SIMD parallel")
        })
        {
            var det = new Edge.GaborEdgeDetector { Method = method, UseSimd = simd, UseParallel = parallel };
            det.Detect(img);
            const int iterations = 10;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < iterations; i++) det.Detect(img);
            sw.Stop();
            double ms = sw.Elapsed.TotalMilliseconds / iterations;
            rows.Add((label, ms, mpxPerImage / (ms / 1000.0)));
        }
        return rows;
    }
}

