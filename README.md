# PatMaxSharp

> **A complete C# reimplementation of the Cognex PatMax (PMAlign) algorithm** — reverse-engineered from `Cognex.VisionPro.PMAlign.dll`.

🌐 **Choose your language / 选择语言：**

| 语言 | 文档 |
|---|---|
| 🇨🇳 **中文** | [README_CN.md](README_CN.md) |
| 🇬🇧 **English** | [README_EN.md](README_EN.md) |

---

## Quick Start

```bash
cd PatMaxSharp
dotnet run -c Release
```

## Features

- **Gabor edge detection** (12 orientations, SIMD + parallel)
- **Dipole field** → run-length feature chains → Featurelets
- **1D derivative-of-Gaussian convolution** (100% recovered kernel formula)
- **Feature-voting geometric Hough** matching
- **Scoring**: `W·(M/W)^(1/N)/S`
- **Least-squares pose fitting** (affine / rotation / similarity)
- **PatFlex thin-plate spline** (TPS)
- **Multi-pattern** support (4 run modes)

## Algorithm Pipeline

```
Gabor edge → dipoles → chains → Featurelets → feature voting
→ peak detection → scoring → least-squares pose → transforms
```

## Performance

| Mode | Time (256×256) | Throughput | Speedup |
|---|---|---|---|
| Scalar serial | 130.55 ms/frame | 0.5 Mpx/s | 1.0× |
| **SIMD + parallel** | **22.40 ms/frame** | **2.9 Mpx/s** | **5.8×** |

## Project Structure

```
PatMaxSharp/
├── Core/          GrayImage, Dipole, PatternModel, PatternMulti
├── Edge/          GaborEdgeDetector
├── Projection/    DiffOfGauss1D, ProjectionMethod
├── Matching/      PeakDetector, Scorer
├── Fit/           LeastSquaresFitter
├── Transform/     XformTps
├── PatMaxTool.cs  Train + Run pipeline
└── Program.cs     Full demo
```

---

## License

MIT — for learning and research purposes only.

> ⚠️ Reverse-engineering learning artifact. Specifications derived from public metadata
> (type library, RTTI, strings, parameter formulas) in the Cognex binary.
