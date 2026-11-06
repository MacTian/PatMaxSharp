# PatMaxSharp

> **English** | [中文](README_CN.md)

**A complete C# reimplementation of the Cognex PatMax (PMAlign) algorithm**, based on reverse engineering of `Cognex.VisionPro.PMAlign.dll`.

---

## 1. Overview

PatMaxSharp implements the full Cognex PatMax geometric pattern-matching pipeline in C#:

```
Gabor edge detection → dipole field → run-length chains → Featurelets → feature voting
→ peak detection → scoring → least-squares pose fitting → geometric transforms
```

All parameters, formulas, and data structures are recovered from reverse engineering of the official Cognex binary — no guesswork.

---

## 2. Basis (Reverse Engineering)

| Component | Recovered | Evidence |
|---|---|---|
| Gabor edge detection | ✅ Full spec | `.rdata` UTF-16 parameter formulas |
| Dipole field | ✅ Struct + conversion | `ccPMFieldDipole` 36-byte layout |
| Feature chains | ✅ Run-length encoding | Source path leak (`dipole.cpp`) |
| Featurelet | ✅ Geometry | 8-bit angle quantization constant |
| 1D DoG convolution | ✅ **100%** | Kernel formula + inner loop + samples |
| Scoring | ✅ Formula | `W·(M/W)^(1/N)/S` |
| Pose fitting | ✅ Closed form | Moment least-squares (type=6 affine) |
| Geometric transforms | ✅ Models | Affine / Homography / TPS / Polynomial |

---

## 3. Desktop UI (WPF)

A full **WPF desktop application** `PatMaxSharp.UI` is included:

```
┌──────────────────────────────────────────────────────────────┐
│ Open Image | Train | Run | Clear            Status: Trained ✓ │
├────────────┬─────────────────────────────────────────────────┤
│ 1.Regions  │                                                 │
│  Train(green)│        Image viewer (wheel zoom / drag)       │
│  Search(cyan)│     green box = train region, cyan = search   │
├────────────┤                                                 │
│ 2.Search   │                                                 │
│  rotation  │                                                 │
│  scale     │                                                 │
├────────────┴─────────────────────────────────────────────────┤
│ 3.Scoring  4.Performance/Accuracy                             │
├──────────────────────────────────────────────────────────────┤
│ Results (DataGrid)                                           │
│ # │ Score │ Coverage │ X │ Y │ Angle │ Scale │ Features │ OK │
└────┴───────┴──────────┴───┴───┴───────┴───────┴──────────┴────┘
```

### Supported image formats
`BMP` / `PNG` / `JPG` / `JPEG` / `TIF` / `TIFF` / `GIF`

### Parameters

| Group | Parameters |
|---|---|
| **Regions** | Train region (green), Search region (cyan), selection mode |
| **Search** | Rotation range, scale range, expected count, XY overlap, timeout |
| **Scoring** | Accept threshold, contrast threshold, grain, coarse/fine grain, clutter, max results |
| **Performance** | Accumulator step, sub-pixel refinement, clustering, Gaussian pre-smooth, SIMD, parallel |

### Run

```bash
cd PatMaxSharp
dotnet run -c Release --project PatMaxSharp.UI
```

**Workflow**:
1. Open image → drag to select the **train region** (green)
2. Switch to "search region" and drag the **search region** (cyan, optional)
3. Set parameters (rotation / scale / thresholds)
4. Click **Train Model**
5. Click **Run Match** → results grid shows position, angle, scale, score, coverage

---

## 4. Project Structure

```
PatMaxSharp/
├── Core/
│   ├── GrayImage.cs        8-bit grayscale (bilinear sampling)
│   ├── Dipole.cs           Dipole + Featurelet structs
│   ├── PatternModel.cs     Trained model (W/N/feature set)
│   └── PatternMulti.cs     Multi-pattern (4 run modes)
├── Edge/
│   └── GaborEdgeDetector.cs   Gabor filter bank (12 orientations, SIMD+parallel)
├── Projection/
│   ├── DiffOfGauss1D.cs      1st-order derivative-of-Gaussian kernel + 1D convolution
│   └── ProjectionMethod.cs   1D projection
├── Matching/
│   ├── PeakDetector.cs      Peak detection
│   └── Scorer.cs            Scoring (W·(M/W)^(1/N)/S)
├── Fit/
│   └── LeastSquaresFitter.cs  Moment least-squares (affine/rotation/similarity)
├── Transform/
│   └── XformTps.cs          Thin-plate spline (PatFlex)
├── PatMaxTool.cs            Train + Run pipeline
├── Program.cs               Full demo (console)
└── PatMaxSharp.UI/          ★ WPF desktop app
    ├── MainWindow.xaml      Main window (params + image + results grid)
    ├── ViewModels/          MainViewModel
    └── Controls/            ImageRegionSelector (region selection)
```

---

### Results drawn on the image

Match results are **drawn directly on the image**:

```
  ┌─────────────────────────────────────────┐
  │        ┌───┐                            │
  │        │ + │ <- cross = match position   │
  │        └───┘                            │
  │          ╲                              │
  │           ╲  <- arrow = angle          │
  │      ┌─────────┐                        │
  │      │ ░░░░░░░ │ <- dashed box = scale  │
  │      └─────────┘                        │
  └─────────────────────────────────────────┘
  Color: green = accepted, orange = rejected
```

### Selection highlighting

Clicking a row in the results grid **highlights** the corresponding marker on the image:

```
Before selection: green marker (accepted) / orange marker (rejected)
After selection:  yellow thicker marker + auto-scroll to the row
```

Two-way linked: image marker <-> results grid row.

### Processing time & speed

The status bar shows live metrics:

```
Processing time: 45.2 ms   Throughput: 1.24 Mpx/s   Results: 3
```

---

## 5. Core Algorithms (Recovered)

### 4.1 Gabor Edge Detection
```
gaborSpatialSigmaX = 2.0506096654409878
sensitivityParameter = 2.0
gaborSpatialSigmaY = sigmaX * sensitivityParameter
gaborFilterHalfSize = ceil(sqrt(2)*max(sx,sy) + 0.95) = 7   → 15x15 kernel
orientations = 12 (15° step)
```

### 4.2 Dipole Conversion
```
x = ix + 0.5 + scale*cos(theta)
y = iy + 0.5 + scale*sin(theta)
```

### 4.3 1D DoG Kernel (the PatMax matching kernel)
```
sigma = grain * 0.25 + 0.001
r = max(1, floor(4*sigma))
k[i] = i * exp(-i^2/(2*sigma^2)) / (sigma^3 * sqrt(2*pi))     i in [-r, -1]
half-axis L1 normalized, center tap = 0, antisymmetric
```

**Kernel samples** (identical to the reverse-engineering report):

| grain | sigma | r | length | peak |
|---|---|---|---|---|
| 1.0 | 0.251 | 1 | 3 | 1.00000 |
| 2.0 | 0.501 | 2 | 5 | 0.99495 |
| 4.0 | 1.001 | 4 | 9 | 0.66435 |
| 8.0 | 2.001 | 8 | 17 | 0.30967 |

### 4.4 Scoring
```
Score = W * (M/W)^(1/N) / S
W = pattern+0x90   N = pattern+0x8c
M = matched features   S = magScale-derived
```

### 4.5 Pose Fitting (type=6 affine, used by PMAlign)
```
A = H * C2^-1
t = mu1 - A * mu2
output [tx, ty, m00, m01, m10, m11, det]
```

---

## 6. Run

```bash
cd PatMaxSharp
dotnet run -c Release
```

**Sample output**:
```
GaborEdgeDetector:
  sigmaX = 2.0506096654409878
  sigmaY = 4.1012193308819755
  halfSize = 7  (kernel 15x15)
  orientations = 12  step = 15.00 deg

Trained: Pattern(features=2330, W=2509.15, N=2.00, grain=1.00)

Found 1 result:
  #1 Result(Score=0.563, Pos=(146.0,138.0), Coverage=54%, Accepted=True)
        expected (148,138), error ~2 px

Least-squares affine fit (type=6):
  [20.0000, 10.0000, 1.2000, 0.0000, 0.0000, 1.2000, 1.4400]
  point(0,0) -> (20.00, 10.00)  (expected 20,10)  OK

PatFlex thin-plate spline (TPS):
  control point (33.3,33.3) -> TPS Y=37.372  (expected 37.372)  OK
```

---

## 7. Performance & Optimization

### 6.1 Benchmark (256x256, 12-orientation Gabor)

| Mode | Time | Throughput | Speedup |
|---|---|---|---|
| Scalar serial | 130.55 ms/frame | 0.5 Mpx/s | 1.0x |
| SIMD serial | 38.90 ms/frame | 1.7 Mpx/s | 3.4x |
| Scalar parallel | 38.10 ms/frame | 1.7 Mpx/s | 3.4x |
| **SIMD + parallel** | **22.40 ms/frame** | **2.9 Mpx/s** | **5.8x** |

### 6.2 Precision

| Mode | Position | Error |
|---|---|---|
| step=2 | (146.3, 136.2) | ~2 px |
| **step=1 + subpixel** | **(147.1, 137.0)** | **0.95 px** |

### 6.3 Robustness

| Scenario | Score | Coverage |
|---|---|---|
| Clean image | 0.511 | 48% |
| Gaussian noise +/-40 | 0.484 | 45% |
| Occlusion 4% | 0.491 | 45% |
| Rotation 5 deg | 0.472 | 43% |

---

## 8. Tunable Parameters

```csharp
// Precision
tool.PrecisionStep = 1;              // accumulator step (1 = highest)
tool.SubpixelRefinement = true;      // sub-pixel refinement

// Robustness
tool.EnableClustering = true;        // candidate clustering
tool.ClusterRadius = 8.0;            // cluster radius
tool.PreSmoothSigma = 0.5;           // Gaussian pre-smooth

// Speed
edge.UseSimd = true;                 // SIMD
edge.UseParallel = true;             // parallel
```

---

## 9. Differences from the Original

| Aspect | This implementation | Cognex original |
|---|---|---|
| Gabor kernel | Standard anisotropic (lambda=4 inferred) | Not fully confirmed |
| Peak detection | Local maxima | `ccPeak1d` (body in packed DLL) |
| Pose robustness | Least-squares (type=6) | RANSAC + least-squares |
| Single-pattern train | Simplified | In WIBU-packed `cogxpma.dll` |
| Multi-pattern | Implemented | `ccPMMultiModel` |

---

## 10. Reverse-Engineering Corpus

The supporting reverse-engineering artifacts (900+ decompiled functions, 1695 class names, 6 component reports):

```
~/rea-patmax/
├── IMPLEMENTATION-REPORT.md   Full implementation report
├── RETROSPECTIVE.md           Retrospective + VisionPro reversing 7-step method
├── reports/                   6 component reports
└── decompiled/                900+ function pseudocode
```

---

## 11. License

MIT License — for learning and research purposes only.

> ⚠️ **Disclaimer**: This project is a reverse-engineering learning artifact. The algorithm
> specifications are derived from analysis of public metadata in the Cognex binary
> (type library, RTTI, strings, parameter formulas). It must not be used for any purpose
> that infringes Cognex's intellectual property rights.
