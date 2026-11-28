using System.Diagnostics;
using System.Text;
using ShapeAnchor;
using ShapeAnchor.Benchmarks;
using ShapeAnchor.Core;
using ShapeAnchor.Edge;
using ShapeAnchor.Projection;
using ShapeAnchor.Training;

Console.OutputEncoding = Encoding.UTF8;

bool selfTest = Has("--selftest");
bool benchOnly = Has("--bench");
bool quiet = Has("--quiet");

bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

// ---------------------------------------------------------------------------
//  --explain <image> [--preset <name>] : every refined hypothesis, ungated
// ---------------------------------------------------------------------------
// ---- shared overrides for the preset tools (must be parsed before any mode branch) ----
{
    int i;
    if ((i = Array.FindIndex(args, a => a.Equals("--mask", StringComparison.OrdinalIgnoreCase))) > 0 && i + 1 < args.Length)
        PresetCheck.SearchMaskPath = args[i + 1];
    if ((i = Array.FindIndex(args, a => a.Equals("--train-mask", StringComparison.OrdinalIgnoreCase))) > 0 && i + 1 < args.Length)
        PresetCheck.TrainMaskPath = args[i + 1];
    if ((i = Array.FindIndex(args, a => a.Equals("--scale", StringComparison.OrdinalIgnoreCase))) > 0 && i + 2 < args.Length)
    { PresetCheck.ScaleMinOverride = double.Parse(args[i + 1]); PresetCheck.ScaleMaxOverride = double.Parse(args[i + 2]); }
    if ((i = Array.FindIndex(args, a => a.Equals("--coherence", StringComparison.OrdinalIgnoreCase))) > 0 && i + 1 < args.Length)
        PresetCheck.CoherenceOverride = double.Parse(args[i + 1]);
    if ((i = Array.FindIndex(args, a => a.Equals("--max-features", StringComparison.OrdinalIgnoreCase))) > 0 && i + 1 < args.Length)
        PresetCheck.MaxFeaturesOverride = int.Parse(args[i + 1]);
    if ((i = Array.FindIndex(args, a => a.Equals("--train-floor", StringComparison.OrdinalIgnoreCase))) > 0 && i + 1 < args.Length)
        PresetCheck.TrainFloorOverride = double.Parse(args[i + 1]);
}
int explainIndex = Array.FindIndex(args, a => a.Equals("--explain", StringComparison.OrdinalIgnoreCase));
if (explainIndex >= 0 && explainIndex + 1 < args.Length)
{
    int pe = Array.FindIndex(args, a => a.Equals("--preset", StringComparison.OrdinalIgnoreCase));
    string presetName = pe > 0 && pe + 1 < args.Length ? args[pe + 1] : "";
    int be = Array.FindIndex(args, a => a.Equals("--base", StringComparison.OrdinalIgnoreCase));
    string basePath = be > 0 && be + 1 < args.Length ? args[be + 1] : null;
    PresetCheck.Explain(args[explainIndex + 1], presetName, basePath);
    return 0;
}

// ---------------------------------------------------------------------------
//  --presets <dir> : run every built-in preset over a folder
// ---------------------------------------------------------------------------
int presetIndex = Array.FindIndex(args, a => a.Equals("--presets", StringComparison.OrdinalIgnoreCase));
if (presetIndex >= 0 && presetIndex + 1 < args.Length)
{
    if (args.Any(a => a.Equals("--list", StringComparison.OrdinalIgnoreCase)))
    {
        PresetCheck.List();
        return 0;
    }
    PresetCheck.Run(args[presetIndex + 1],
        args.Any(a => a.Equals("--detail", StringComparison.OrdinalIgnoreCase)));
    return 0;
}

// ---------------------------------------------------------------------------
//  --probe <image> [x y w h] : train one region and explain the outcome
// ---------------------------------------------------------------------------
int probeIndex = Array.FindIndex(args, a => a.Equals("--probe", StringComparison.OrdinalIgnoreCase));
if (probeIndex >= 0 && probeIndex + 1 < args.Length)
{
    var probeImage = ImageLoader.Load(args[probeIndex + 1]);
    var rect = probeIndex + 5 < args.Length
        ? new RectI(int.Parse(args[probeIndex + 2]), int.Parse(args[probeIndex + 3]),
                    int.Parse(args[probeIndex + 4]), int.Parse(args[probeIndex + 5]))
        : PatternTrainer.ContentBounds(probeImage);
    var crop = probeImage.Crop(rect);

    var probeDetector = new GaborEdgeDetector();
    var probeEdges = probeDetector.Detect(crop);
    var probeTool = new ShapeAnchorTool(probeDetector);
    var probePattern = probeTool.Train(crop, crop.Width / 2.0, crop.Height / 2.0);

    Console.WriteLine($"image   : {Path.GetFileName(args[probeIndex + 1])} {probeImage.Width}x{probeImage.Height}");
    Console.WriteLine($"rect    : {rect}");
    Console.WriteLine($"crop    : {crop.Width}x{crop.Height}  min={crop.Data.Min()} max={crop.Data.Max()} mean={crop.Data.Select(b => (int)b).Average():F1}");
    Console.WriteLine($"edges   : pixels={probeEdges.EdgePixelCount} maxMag={probeEdges.MaxMagnitude:F1} floor={probeDetector.LastEffectiveFloor:F2}");
    Console.WriteLine($"pattern : features={probePattern.FeatureCount} W={probePattern.W:F1} radius={probePattern.ExtentRadius:F1}");
    return 0;
}

// ---------------------------------------------------------------------------
//  --cal <dir> : validate against a folder of real PNG images
// ---------------------------------------------------------------------------
int calIndex = Array.FindIndex(args, a => a.Equals("--cal", StringComparison.OrdinalIgnoreCase));
if (calIndex >= 0)
{
    string directory = calIndex + 1 < args.Length ? args[calIndex + 1] : ".";
    bool autoFloor = !args.Any(a => a.Equals("--no-autofloor", StringComparison.OrdinalIgnoreCase));
    bool ncc = args.Any(a => a.Equals("--ncc", StringComparison.OrdinalIgnoreCase));   // opt-in
    bool ignorePolarity = args.Any(a => a.Equals("--ignore-polarity", StringComparison.OrdinalIgnoreCase));
    bool cognexPreset = args.Any(a => a.Equals("--cognex-demo", StringComparison.OrdinalIgnoreCase));
    int angleIdx = Array.FindIndex(args, a => a.Equals("--angle", StringComparison.OrdinalIgnoreCase));
    if (angleIdx > 0 && angleIdx + 2 < args.Length)
    {
        CalibrationCheck.AngleMin = double.Parse(args[angleIdx + 1]);
        CalibrationCheck.AngleMax = double.Parse(args[angleIdx + 2]);
        Console.WriteLine($"[cal] angle override {CalibrationCheck.AngleMin}..{CalibrationCheck.AngleMax}");
    }
    CalibrationCheck.Run(directory, preview: true, autoNoiseFloor: autoFloor, normalizedCorrelation: ncc,
        ignorePolarity: ignorePolarity, cognexDemoPreset: cognexPreset);
    return 0;
}

// ---------------------------------------------------------------------------
//  --selftest : accuracy gate (exit code 1 on any failure, CI friendly)
// ---------------------------------------------------------------------------
if (selfTest)
{
    Console.WriteLine("== ShapeAnchor accuracy self-test ==");
    var accuracy = BenchmarkSuite.RunAccuracy();
    var multi = BenchmarkSuite.RunMultiInstance();
    var negative = BenchmarkSuite.RunNegative();

    int failures = 0;
    Console.WriteLine();
    Console.WriteLine($"{"case",-24} {"result",-6} {"score",7} {"posErr",8} {"angErr",8} {"scaleErr",9} {"time",8}");
    Console.WriteLine(new string('-', 76));
    foreach (var c in accuracy.Concat(multi).Concat(negative))
    {
        Console.WriteLine($"{c.Name,-24} {(c.Passed ? "PASS" : "FAIL"),-6} {c.Score,7:F3} " +
                          $"{c.PosErrorPx,8:F3} {c.AngleErrorDeg,8:F3} {c.ScaleError,9:F4} {c.TimeMs,7:F1}ms");
        if (!c.Passed)
        {
            failures++;
            Console.WriteLine($"    -> {c.Detail}");
        }
    }

    int total = accuracy.Count + multi.Count + negative.Count;
    Console.WriteLine();
    Console.WriteLine(failures == 0 ? $"ALL {total} CASES PASSED" : $"{failures} CASE(S) FAILED");
    return failures == 0 ? 0 : 1;
}

// ---------------------------------------------------------------------------
//  Demo: train on one image, locate the part in another
// ---------------------------------------------------------------------------
if (!benchOnly)
{
    Console.WriteLine("=========================================================");
    Console.WriteLine("  ShapeAnchor - Cognex PatMax (PMAlign) work-alike in C#");
    Console.WriteLine("  train from one region, locate instances anywhere");
    Console.WriteLine("=========================================================");
    Console.WriteLine();

    var detector = new GaborEdgeDetector();
    detector.PrintInfo();
    Console.WriteLine();

    Console.WriteLine("1D derivative-of-Gaussian kernel (projection operator of the original):");
    foreach (double grain in new[] { 1.0, 2.0, 4.0 })
    {
        var dog = new DiffOfGauss1D(grain);
        Console.WriteLine($"  grain={grain,4:F1}: sigma={dog.Sigma:F4} radius={dog.Radius} " +
                          $"len={dog.Length} peak={dog.Kernel.Max():F4}");
    }
    Console.WriteLine();

    var tool = new ShapeAnchorTool
    {
        AcceptThreshold = 0.5,
        TrainMaxFeatures = 500,
        RotationAngleMin = -180,
        RotationAngleMax = 180
    };

    var trainingImage = SyntheticPart.Base();
    var trainWatch = Stopwatch.StartNew();
    var pattern = tool.TrainRegion(trainingImage, new RectI(0, 0, trainingImage.Width, trainingImage.Height));
    trainWatch.Stop();

    Console.WriteLine($"Trained: {pattern}");
    Console.WriteLine($"  features={pattern.FeatureCount}, W={pattern.W:F1}, N={pattern.N:F1}, " +
                      $"radius={pattern.ExtentRadius:F1}px, contrastRef={pattern.ContrastRef:F1}, " +
                      $"train time={trainWatch.Elapsed.TotalMilliseconds:F1}ms");
    Console.WriteLine();

    var scene = SyntheticPart.Render(420, 360, 231.4, 168.7, 12.5 * Math.PI / 180, 1.0,
        new SyntheticPart.SceneOptions { Seed = 7, NoiseStd = 4, ClutterShapes = 3 });

    var runWatch = Stopwatch.StartNew();
    var results = tool.Run(scene, 5);
    runWatch.Stop();

    Console.WriteLine("Scene 420x360, part at (231.4, 168.7) rotated 12.5 deg:");
    if (results.Count == 0) Console.WriteLine("  no match");
    foreach (var r in results) Console.WriteLine($"  {r}");
    Console.WriteLine($"  run time {runWatch.Elapsed.TotalMilliseconds:F1} ms  ({tool.LastDiagnostics})");
    Console.WriteLine();

    if (!quiet)
    {
        Console.WriteLine("1D DoG convolution (impulse response):");
        double[] signal = { 0, 0, 0, 10, 0, 0, 0 };
        var dog1 = new DiffOfGauss1D(1.0);
        Console.WriteLine($"  input : [{string.Join(", ", signal)}]");
        Console.WriteLine($"  output: [{string.Join(", ", dog1.Convolve(signal).Select(v => v.ToString("F2")))}]");
        Console.WriteLine();

        Console.WriteLine("Least-squares affine fit (type 6):");
        var src = new List<ShapeAnchor.Fit.LeastSquaresFitter.Point2D>
            { new(0, 0), new(10, 0), new(0, 10), new(10, 10) };
        var dst = new List<ShapeAnchor.Fit.LeastSquaresFitter.Point2D>
            { new(20, 10), new(32, 10), new(20, 22), new(32, 22) };
        var xf = tool.FitPose(src, dst);
        Console.WriteLine($"  [{string.Join(", ", xf.ToArray().Select(v => v.ToString("F3")))}]");
        Console.WriteLine($"  (0,0) -> ({xf.Apply(src[0]).X:F2}, {xf.Apply(src[0]).Y:F2})  expected (20,10)");
        Console.WriteLine();

        Console.WriteLine("PatFlex thin-plate spline:");
        var tps = ShapeAnchor.Transform.XformTps.CreateGrid(0, 0, 100, 100, 4, 4);
        var tpsSrc = new List<(double X, double Y)>();
        var tpsDst = new List<(double X, double Y)>();
        foreach (var cp in tps.ControlPoints)
        {
            tpsSrc.Add(cp);
            tpsDst.Add((cp.X + 2.0 * Math.Sin(cp.Y / 20.0), cp.Y * 1.1 + 3.0 * Math.Cos(cp.X / 25.0)));
        }
        tps.Fit(tpsSrc, tpsDst);
        var probe = tps.ControlPoints[5];
        Console.WriteLine($"  control points={tps.ControlPoints.Count}  " +
                          $"({probe.X:F1},{probe.Y:F1}) -> {tps.ApplyY(probe.X, probe.Y):F3} " +
                          $"(expected {tpsDst[5].Y:F3})");
        Console.WriteLine();
    }

    // ---- accuracy suite ----
    Console.WriteLine("Accuracy suite (exact synthetic ground truth):");
    var accuracyCases = BenchmarkSuite.RunAccuracy();
    var multiCases = BenchmarkSuite.RunMultiInstance();
    Console.WriteLine($"  {"case",-24} {"result",-6} {"score",7} {"posErr",8} {"angErr",8} {"time",8}");
    foreach (var c in accuracyCases.Concat(multiCases))
        Console.WriteLine($"  {c.Name,-24} {(c.Passed ? "PASS" : "FAIL"),-6} {c.Score,7:F3} " +
                          $"{c.PosErrorPx,8:F3} {c.AngleErrorDeg,8:F3} {c.TimeMs,7:F1}ms");
    Console.WriteLine();
}

// ---------------------------------------------------------------------------
//  Performance
// ---------------------------------------------------------------------------
Console.WriteLine("Performance (full search: pyramid + exhaustive angle/scale + sub-pixel fit):");
foreach (var (size, ms, mpx, score) in BenchmarkSuite.RunPerformance())
    Console.WriteLine($"  {size,-10} {ms,8:F2} ms/frame  {mpx,7:F1} Mpx/s  score={score:F3}");
Console.WriteLine();

Console.WriteLine("Edge detector throughput (640x480):");
foreach (var (mode, ms, mpx) in BenchmarkSuite.RunEdgeBenchmark())
    Console.WriteLine($"  {mode,-24} {ms,8:F2} ms  {mpx,7:F1} Mpx/s");
Console.WriteLine();

Console.WriteLine("=========================================================");
Console.WriteLine("  dotnet run -c Release -- --selftest        accuracy gate (exit 1 on failure)");
Console.WriteLine("  dotnet run -c Release -- --bench           performance only");
Console.WriteLine("  dotnet run -c Release -- --cal <folder>    validate against a folder of PNGs");
Console.WriteLine("=========================================================");
return 0;
