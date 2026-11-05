using System.Text;
using PatMaxSharp;
using PatMaxSharp.Core;
using PatMaxSharp.Projection;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("=========================================================");
Console.WriteLine("  PatMaxSharp — Cognex PatMax 算法的 C# 复现");
Console.WriteLine("  基于对 Cognex.VisionPro.PMAlign.dll 的逆向分析");
Console.WriteLine("=========================================================");
Console.WriteLine();

// ---- 1. 验证 Gabor 参数（对照逆向报告）-----------------------------
var edge = new PatMaxSharp.Edge.GaborEdgeDetector();
edge.PrintInfo();
Console.WriteLine();

// ---- 2. 验证 DoG 核（对照逆向报告）-------------------------------
Console.WriteLine("DoG 核（不同 grain）:");
foreach (double grain in new[] { 1.0, 2.0, 4.0, 8.0 })
{
    var dog = new DiffOfGauss1D(grain);
    Console.WriteLine($"  grain={grain,4:F1}: sigma={dog.Sigma:F6} r={dog.Radius} len={dog.Length}  peak={dog.Kernel.Max():F5}");
}
Console.WriteLine();

// ---- 3. 构造测试图像 ---------------------------------------------
int W = 256, H = 256;
var trainImg = GrayImage.CreateTest(W, H, seed: 42);
Console.WriteLine($"训练图像: {W}x{H}");

// ---- 4. 训练 ------------------------------------------------------
var tool = new PatMaxTool();
var pattern = tool.Train(trainImg, trainImg.Width / 2.0, trainImg.Height / 2.0, grain: 1.0);
Console.WriteLine($"训练完成: {pattern}");
Console.WriteLine();

// ---- 5. 构造搜索图像（平移 + 轻微旋转）-----------------------------
var searchImg = GrayImage.CreateTest(W, H, seed: 42);
// 平移 20,10
var shifted = new GrayImage(W, H);
for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
        shifted[x, y] = searchImg.At(x - 20, y - 10);
Console.WriteLine("搜索图像: 训练图平移 (20,10)");

// ---- 6. 运行匹配 --------------------------------------------------
Console.WriteLine();
Console.WriteLine("运行匹配...");
var results = tool.Run(shifted, maxResults: 8);
Console.WriteLine($"找到 {results.Count} 个结果:");
int i = 1;
foreach (var r in results)
{
    Console.WriteLine($"  #{i++} {r}");
}

// ---- 7. 最小二乘拟合验证 ------------------------------------------
Console.WriteLine();
Console.WriteLine("最小二乘仿射拟合验证 (type=6):");
var src = new List<PatMaxSharp.Fit.LeastSquaresFitter.Point2D>
{
    new(0,0), new(10,0), new(0,10), new(10,10)
};
var dst = new List<PatMaxSharp.Fit.LeastSquaresFitter.Point2D>
{
    new(20,10), new(32,10), new(20,22), new(32,22)
};
var xf = tool.FitPose(src, dst);
Console.WriteLine($"  [{string.Join(", ", xf.ToArray().Select(v => v.ToString("F4")))}]");
Console.WriteLine($"  点(0,0) -> ({xf.Apply(src[0]).X:F2}, {xf.Apply(src[0]).Y:F2})  (期望 20,10)");
Console.WriteLine();

// ---- 8. 反卷积验证 -------------------------------------------------
Console.WriteLine("1D DoG 卷积验证:");
double[] signal = { 0, 0, 0, 10, 0, 0, 0 };
var dog1 = new DiffOfGauss1D(1.0);
var conv = dog1.Convolve(signal);
Console.WriteLine($"  输入: [{string.Join(", ", signal)}]");
Console.WriteLine($"  输出: [{string.Join(", ", conv.Select(v => v.ToString("F3")))}]");
Console.WriteLine($"  奇对称优化: [{string.Join(", ", dog1.ConvolveOdd(signal).Select(v => v.ToString("F3")))}]");
// ---- 8b. 性能基准测试 -------------------------------------------
Console.WriteLine();
Console.WriteLine("性能基准 (256x256, 12 朝向 Gabor):");
var benchImg = GrayImage.CreateTest(256, 256, seed: 42);
foreach (var (simd, par, label) in new (bool, bool, string)[]
{
    (false, false, "标量 串行"),
    (true,  false, "SIMD  串行"),
    (false, true,  "标量 并行"),
    (true,  true,  "SIMD  并行"),
})
{
    var d = new PatMaxSharp.Edge.GaborEdgeDetector { UseSimd = simd, UseParallel = par };
    d.Detect(benchImg);   // warmup
    var sw = System.Diagnostics.Stopwatch.StartNew();
    int iters = 20;
    for (int k = 0; k < iters; k++) d.Detect(benchImg);
    sw.Stop();
    Console.WriteLine($"  {label,-10}: {sw.ElapsedMilliseconds / (double)iters,7:F2} ms/帧  " +
                      $"({(256 * 256) / (sw.ElapsedMilliseconds / (double)iters) / 1000:F1} Mpx/s)");
}

// ---- 9. 高精度匹配（细累加器 + 亚像素）--------------------------
Console.WriteLine();
Console.WriteLine("高精度匹配（step=1 + 亚像素细化）:");
tool.PrecisionStep = 1;
tool.SubpixelRefinement = true;
var precise = tool.Run(shifted, maxResults: 3);
foreach (var r in precise)
    Console.WriteLine($"  {r}");
tool.PrecisionStep = 2;

// ---- 10. 多模型匹配 --------------------------------------------
Console.WriteLine();
Console.WriteLine("多模型匹配 (PatternMulti):");
var multi = new PatternMulti();
multi.AddModel("pattern-A", tool.Train(trainImg, trainImg.Width / 2.0, trainImg.Height / 2.0, 1.0));
multi.AddModel("pattern-B", tool.Train(GrayImage.CreateTest(W, H, seed: 7), W / 2.0, H / 2.0, 2.0));
Console.WriteLine($"  {multi}");
var multiResults = tool.RunMulti(shifted, multi, maxResults: 5);
Console.WriteLine($"  找到 {multiResults.Count} 个结果");
foreach (var r in multiResults.Take(3))
    Console.WriteLine($"    {r}");

// ---- 11. PatFlex TPS 形变 --------------------------------------
Console.WriteLine();
Console.WriteLine("PatFlex 薄板样条 (TPS) 拟合:");
var tps = PatMaxSharp.Transform.XformTps.CreateGrid(0, 0, 100, 100, 4, 4);
var srcPts = new List<(double X, double Y)>();
var dstPts = new List<(double X, double Y)>();
var rnd = new Random(123);
foreach (var cp in tps.ControlPoints)
{
    // 模拟一个形变：y 方向拉伸 + 非线性扰动
    srcPts.Add(cp);
    dstPts.Add((cp.X + 2.0 * Math.Sin(cp.Y / 20.0), cp.Y * 1.1 + 3.0 * Math.Cos(cp.X / 25.0)));
}
tps.Fit(srcPts, dstPts);
Console.WriteLine($"  控制点: {tps.ControlPoints.Count}");
var probe = tps.ControlPoints[5];
double fittedY = tps.ApplyY(probe.X, probe.Y);
Console.WriteLine($"  控制点 ({probe.X:F1},{probe.Y:F1}) -> TPS Y={fittedY:F3}  (期望 {dstPts[5].Y:F3})");

// ---- 12. 鲁棒性测试（噪声 + 遮挡）-----------------------------
Console.WriteLine();
Console.WriteLine("鲁棒性测试:");
var rnd2 = new Random(7);
// 12a. 高斯噪声
var noisy = GrayImage.CreateTest(W, H, seed: 42);
for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
        noisy[x, y] = (byte)Math.Clamp(noisy[x, y] + rnd2.Next(-40, 41), 0, 255);
var noiseRes = tool.Run(noisy, maxResults: 3);
Console.WriteLine($"  [高斯噪声 ±40] 最佳: {(noiseRes.Count > 0 ? noiseRes[0].ToString() : "无结果")}");

// 12b. 遮挡 30%（把中心区域置灰）
var occ = GrayImage.CreateTest(W, H, seed: 42);
int ox0 = W / 3, oy0 = H / 3, ocSize = W / 5;
for (int y = oy0; y < Math.Min(H, oy0 + ocSize); y++)
    for (int x = ox0; x < Math.Min(W, ox0 + ocSize); x++)
        occ[x, y] = 128;
var occRes = tool.Run(occ, maxResults: 3);
Console.WriteLine($"  [遮挡 {ocSize * ocSize * 100.0 / (W * H):F0}%] 最佳: {(occRes.Count > 0 ? occRes[0].ToString() : "无结果")}");

// 12c. 旋转 5°
var rot = new GrayImage(W, H);
double rotRad = 5.0 * Math.PI / 180.0;
int cx = W / 2, cy = H / 2;
for (int y = 0; y < H; y++)
    for (int x = 0; x < W; x++)
    {
        double dx = x - cx, dy = y - cy;
        double sx = cx + dx * Math.Cos(rotRad) + dy * Math.Sin(rotRad);
        double sy = cy - dx * Math.Sin(rotRad) + dy * Math.Cos(rotRad);
        rot[x, y] = (byte)(searchImg.Sample(sx, sy));
    }
var rotRes = tool.Run(rot, maxResults: 3);
Console.WriteLine($"  [旋转 5°] 最佳: {(rotRes.Count > 0 ? rotRes[0].ToString() : "无结果")}");

Console.WriteLine();
Console.WriteLine("=========================================================");
Console.WriteLine("  注：RANSAC 与单图案训练本体位于 WIBU 加壳的 cogxpma.dll，");
Console.WriteLine("      本实现使用确定性峰值+最小二乘替代。");
Console.WriteLine("=========================================================");
