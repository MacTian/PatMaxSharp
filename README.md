# PatMaxSharp

**C# 实现的 Cognex PatMax (PMAlign) 对标工程 —— 训练图上一个区域，在任意搜索图中定位该目标，输出亚像素位置、角度、尺度。**
**A C# work-alike of Cognex PatMax (PMAlign) — train one region, locate that target in any search image, return a sub-pixel position, angle and scale.**

📖 **完整文档 / Full documentation:** [🇨🇳 中文](README_CN.md) · [🇬🇧 English](README_EN.md)

---

## 关键指标 / Headline numbers

| | |
|---|---|
| **精度 / Accuracy**（合成真值 / synthetic truth） | 位置 **0.012–0.098 px**，角度 **0.043–0.112°**，尺度 **0.0006–0.0017**；10/10 用例 PASS / all 10 cases PASS |
| **速度 / Speed**（单次完整搜索 / full single search） | 256×256 **11.04 ms**，640×480 **14.21 ms**，1280×960 **24.69 ms**（i9-12950HX） |
| **边缘前端 / Edge front end**（640×480） | 可分离 DoG，SIMD 并行 **2.37 ms / 129.5 Mpx/s** |
| **鲁棒性 / Robustness** | 遮挡 22% / occlusion 22%，噪声 σ=20 / noise σ=20，干扰物 ×6 / 6 clutter objects，多目标 3 实例 / 3 instances，空场景 0 误报 / 0 false accepts |
| **门禁 / Gate** | `dotnet run -c Release -- --selftest` → 10 用例 / 10 cases，失败退出码 1 / exit code 1 on failure |
| **独立实现 / Independence** | 不链接、不调用 `cogxpma.dll` / `Cognex.VisionPro.PMAlign.dll`；无 Cognex 源码或二进制 / no Cognex source or binaries |

## 快速开始 / Quick start

```csharp
var tool = new PatMaxSharp.PatMaxTool
{
    AcceptThreshold = 0.6,
    RotationAngleMin = -30, RotationAngleMax = 30
};
// 训练整图中的一个区域（原点默认取区域中心）/ train a region (origin = region centre)
var pattern = tool.TrainRegion(image, new RectI(120, 90, 160, 140));
// 在另一张图上搜索，最多 5 个实例 / search another image for up to 5 instances
foreach (var r in tool.Run(searchImage, 5))
    Console.WriteLine($"{r.Score:F3} ({r.X:F2},{r.Y:F2}) {r.Angle * 180 / Math.PI:F2}° x{r.Scale:F3}");
```

```bash
dotnet run -c Release                 # 演示 + 精度 + 性能 / demo + accuracy + performance
dotnet run -c Release -- --selftest   # 门禁，10 用例 / gate, 10 cases
dotnet run -c Release -- --bench      # 只跑性能 / performance only
dotnet run -c Release -- --cal <目录>  # 真图验证 / real-image check
dotnet run --project PatMaxSharp.UI -c Release   # WPF 前端 / WPF front end
```

## 流水线 / Pipeline

```
Train   灰度 → 可分离 DoG 梯度场 → 脊线细化 → 幅值排序 → 泊松盘采样
        → 特征集 {位置, 单位法线, 极性, 强度} + W / N / 对比度基准

        grey → separable DoG gradient field → ridge thinning → sort by magnitude → Poisson-disc sampling
        → feature set {position, unit normal, polarity, strength} + W / N / contrast baseline

Search  边缘金字塔 → 最粗层 (角度 × 尺度) 穷举，方向/极性敏感相关 → 逐层细化（位置窗 → 角度窗含抛物线插值 → 位置窗）
        → 全特征评分 score = coverage^(1/N)（含 clutter 惩罚 + 对比度门限）
        → 沿特征法线亚像素边缘定位 → 稳健 2D 相似变换最小二乘 → 位姿 (x, y, θ, s)

        edge pyramid → exhaustive (angle × scale) at the coarsest level, direction/polarity-sensitive correlation
        → level-by-level refinement (position window → angle window with parabolic interpolation → position window)
        → full-feature score score = coverage^(1/N) (clutter penalty + contrast gate)
        → sub-pixel edge location along each feature normal → robust 2D similarity least squares → pose (x, y, θ, s)
```

关键点：位姿拟合的对应点来自**实测亚像素边缘**，不是把预测点拟合到自身。
The decisive point: pose correspondences come from **measured sub-pixel edges**, not from fitting predicted points to themselves.

## 本版修复摘要 / What this revision fixed

上一版能跑但“算法没工作”，八处问题逐条修掉：

The previous revision ran but the algorithm did not work; eight problems were fixed:

| # | 上一版 / Before | 本版 / Now |
|---|---|---|
| 1 | 12 朝向共用 `response` 缓冲（`Parallel.For` 数据竞争）/ 12 orientations shared one `response` buffer (data race) | 有符号方向导数场，任意方向一次点乘，无共享写缓冲 / signed directional-derivative field, one dot product per direction, no shared buffer |
| 2 | Gabor 余弦相位（偶对称），响应对亮线、无极性 / Gabor cosine phase, responds to bright lines, no polarity | 可分离 DoG 梯度场（奇对称，对阶跃边），特征带极性 / separable DoG (odd symmetry, step edges), explicit polarity |
| 3 | 模板 = 整图滑窗 → 结果恒等于训练原点 / template = whole-image sliding window → result always the training origin | 稀疏特征集在金字塔上相关，峰值可落任意处 / sparse feature set correlated over a pyramid, peak anywhere |
| 4 | 拟合源点 = 目标点 → 恒为单位变换 / fit source = target → always identity | 对应点来自实测亚像素边缘 / correspondences from measured sub-pixel edges |
| 5 | `magnitude > 0` 即命中 → 空场景报 5 个目标 / any `magnitude > 0` counted → 5 targets on an empty scene | 三重门 + clutter 惩罚 → 空场景 0 误报 / three gates + clutter penalty → 0 false accepts |
| 6 | `BuildChains` O(n²) / O(n²) | 空间哈希 + BFS / spatial hash + BFS |
| 7 | UI 的 `ScaleMin/Max` 未传给算法 / UI scale range never reached the algorithm | 全部参数经 `BuildOptions` 映射 / all parameters mapped through `BuildOptions` |
| 8 | 无测试无基准 / no tests, no benchmarks | 基准套件 + `--selftest` 退出码 + 双平台 CI / benchmark suite + `--selftest` exit code + two-platform CI |

详细复盘见 [中文版第 4 节](README_CN.md#4-review-结论上一版为何推倒重写) / [English Section 4](README_EN.md#4-review-findings-why-the-previous-revision-was-rewritten)。

## 与原版的关系 / Relationship to the original

| | |
|---|---|
| **并排对齐 / Aligned** | `AcceptThreshold` 0.5、`CoarseAcceptThreshold` 0.33、`ScoreUsingClutter` true、`IgnorePolarity` false 与原版默认值一致 / match the original defaults |
| **换算表达 / Re-expressed** | `ContrastThreshold` 10（0–65535）→ 归一化 0–1；`EdgeThreshold` 10/5 → `NoiseFloor` 8.0；`GrainLimitFine/Coarse` → `MaxLevels` + `Grain`；`XYOverlap` 0.8 → `ClusterRadius` 8 px；`ApproximateNumberToFind` → `Run(image, maxResults)` |
| **硬证据 / Hard evidence** | 边缘前处理默认参数块（σx = 2.0506096654409878，σy = 4.1012193308819755 = 2σx，核半窗 7）存在于托管程序集的原生 `.text`（RVA `0x00EAF2C0`）；取证见 `docs/evidence/` / present in the managed assembly's native `.text` at RVA `0x00EAF2C0`; see `docs/evidence/` |
| **边界 / Boundary** | `cogxpma.dll` 是 WIBU 加壳（18 节中 17 个 `__wibu*`，熵 7.99–8.00），原生侧的 RVA/结构体偏移静态不可复核；评分公式是行为工程复现，非反汇编结论 / `cogxpma.dll` is WIBU-packed, so native-side RVAs and struct offsets are not statically verifiable; the scoring formula is an engineering reproduction of behaviour, not a disassembly result |
| **未覆盖 / Not covered** | 透视 PatMaxPerspective、PatFlex 闭环、彩色/多光谱、非线性光照 / perspective, PatFlex loop, colour/multispectral, non-linear illumination |

## 许可 / Licence

MIT，仅供学习与研究。/ MIT, for study and research.

本项目为独立实现，不含 Cognex 源码或二进制。PatMax / PMAlign / Cognex / VisionPro 为其各自所有者的商标，此处仅用于说明对标目标。
This is an independent implementation and contains no Cognex source or binaries. PatMax / PMAlign / Cognex / VisionPro are trademarks of their respective owners and are named here only to identify the reference target.
