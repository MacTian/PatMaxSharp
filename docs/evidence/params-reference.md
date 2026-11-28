# PMAlign 参数语义与默认值表 + 与 ShapeAnchor 现有实现的三类比对

本文所有数字都来自**静态取证**，没有运行 VisionPro、没有反编译第三方源码。

## 0. 证据来源与分级

| 标记 | 含义 | 来源 |
|---|---|---|
| **实测** | 从二进制直接读出，可逐位复核 | 元数据表 / 属性 blob / 机器码立即数 |
| **官方文本** | 厂商自己写在程序集里的说明文字 | `[CogDocSummary]` / `[CogDocRemarks]` / `Cognex.VisionPro.PMAlign.xml` |
| **推断** | 我的解释，不是二进制里写着的事实 | 由实测值 + 官方文本推出，已标注 |

四类原始证据（都可复现，见 §9）：

1. **元数据签名**：`Cognex.VisionPro.PMAlign.dll` 的 ECMA-335 表（类型/属性/字段/方法签名、Constant 表、枚举字面量）。
2. **`[DefaultValue(x)]` 自定义属性**：属性 blob 里解码出的实参。VisionPro 的属性网格与序列化用的就是它，**默认值以它为准**。
3. **厂商文档属性** `[CogDocSummary]` 等：字符串里含 "Default value is 5.0"、"Valid range is from 0.0 to 65535.0" 这类原文；同时它的 `<include path='//interface[@name="ICogPMAlignRunParams"]/function[@vbname="EdgeThreshold"]'>` 指出了该属性对应的**原生 COM 接口成员名**，也就是真正落到 `cogxpma.dll` 的参数名。
4. **IL 常量模式**：方法体内 `ldc.*` → `call set_X` / `stfld`（属性默认值赋值的编译形态）。

> 注意：`[DefaultValue]` 与 IL 观测在个别属性上**不一致**，例如 `CogPMAlignPattern.AutoEdgeThresholdEnabled` 的 `[DefaultValue]` 是 `true`，而其官方文本仍写着 "Default value is false"。以属性值为准（属性网格实际显示 `true`）。这类矛盾在 §8 单独列出。

---

## 1. 一眼结论：核心默认值快表（全部实测）

| 参数 | 值 | 单位/范围 | 类型 |
|---|---|---|---|
| `AcceptThreshold` | **0.5** | 0 … 1 | 运行 |
| `ContrastThreshold` | **10** | 0 … 65535（8-bit 图上限 255） | 运行 |
| `CoarseAcceptThreshold` | **0.33** | 0 … 1，且必须 ≤ `AcceptThreshold` | 运行 |
| `CoarseAcceptThresholdEnabled` | **false** | | 运行 |
| `EdgeThreshold`（运行） | **5.0** | 0 … 65535 | 运行 |
| `EdgeThreshold`（训练） | **10.0** | 0 … 65535 | 训练 |
| `AutoEdgeThresholdEnabled` | **true** | | 训练 |
| `GrainLimitFine` | **1** | 1.0 … 25.5 | 训练/运行 |
| `GrainLimitCoarse` | **4** | 1.0 … 25.5，≥ fine | 训练/运行 |
| `GrainLimitAutoSelect` | **true** | | 训练 |
| `GrainLimitsUsePattern` | **true** | | 运行 |
| `XYOverlap` | **0.8** | 0 … 1（面积重叠比例） | 运行 |
| `OutsideRegionThreshold` | **0** | 0 … 0.999 | 运行 |
| `ScoreUsingClutter` | **true** | | 运行 |
| `ApproximateNumberToFind` | **1** | 整数 | 运行 |
| `Timeout` | **5000** | ms | 运行 |
| `TimeoutEnabled` | **false** | | 运行 |
| `RunAlgorithm` | **2** = `BestTrained` | 枚举 | 运行 |
| `RunMode` | **0** = `SearchImage` | 枚举 | 运行 |
| `TrainAlgorithm` | **2** = `PatMaxAndPatQuick` | 枚举 | 训练 |
| `TrainMode` | **0** = `Image` | 枚举 | 训练 |
| `RuntimeScoringMethod` | **0** = `DoNotScoreFeatures` | 枚举 | 训练 |
| `Elasticity` | **0** | 像素 | 训练 |
| `IgnorePolarity` | **false** | | 训练 |
| `HighSensitivityParameter` | **2** | | 训练（仅 PatMaxHighSensitivity） |
| `ExpectedDeformationRate` | **0.3f** | 见 §8（float32 存储） | 训练（仅 PatFlex） |
| `RepeatingPatterns` | **true** | | 训练 |
| `ZoneAngle.Low` | **−0.785398163397448** = −π/4 | 弧度 | DOF |
| `ZoneAngle.High` | **0.785398163397448** = π/4 | 弧度 | DOF |
| `ZoneAngle.Overlap` | **6.28318530717959** = 2π | 弧度 | DOF |
| `ZoneScale.Low` | **0.8** | 比例 | DOF |
| `ZoneScale.High` | **1.2** | 比例 | DOF |
| `ZoneScale.Overlap` | **1.4** | 比例比 | DOF |
| `Zone*.Nominal` | Angle **0** / Scale **1** | | DOF |
| `Zone*.Configuration` | **0** = `Nominal` | 枚举 | DOF |

派生关系（实测 + 算术）：`ZoneAngle.High = π/4`、`ZoneAngle.Overlap = 2π`、`ZoneScale.Overlap = 1.4`、`ZoneScale.High/Low = 1.2/0.8`（一乘一除，对称 ±20%）。

---

## 2. `CogPMAlignRunParams` - 运行参数全表

原生接口：`ICogPMAlignRunParams`。默认值列来自 `[DefaultValue]`；"官方文本"列是厂商原文（英文照抄，避免我的转述引入偏差）。

| 属性 | 类型 | 默认值 | 合法范围 | 官方语义（原文摘录） |
|---|---|---|---|---|
| `AcceptThreshold` | double | `0.5` | 0 … 1 | Acceptance threshold on the score of a result. Only instances that receive scores ≥ this threshold are accepted. |
| `ContrastThreshold` | double | `10` | 0 … 65535 | Minimum acceptable contrast. Only instances where the average difference in pixel values across all feature boundaries exceeds this threshold are considered. 备注：8-bit 图下 >255 会抛 `ArgumentException`。 |
| `CoarseAcceptThreshold` | double | `0.33` | 0 … 1 | 若 `CoarseAcceptThresholdEnabled`，粗定位 score ≥ 该值的结果才进入精定位，再与 `AcceptThreshold` 比较；两者互相夹紧（设大者会自动抬高另一个）。 |
| `CoarseAcceptThresholdEnabled` | bool | `false` | | Whether to use CoarseAcceptThreshold. |
| `EdgeThreshold` | double | `5` | 0 … 65535 | Run-time edge threshold, under which edges will be ignored. |
| `AutoEdgeThresholdEnabled` | bool | `true` | | 若 true，训练用**自动计算**的边缘阈值；false 用用户给的。 |
| `GrainLimitFine` | double | `1` | 1.0 … 25.5 | Fine grain limit，`GrainLimitsUsePattern=false` 时生效；coarse ≥ fine，设 fine > coarse 会同时抬高 coarse。 |
| `GrainLimitCoarse` | double | `4` | 1.0 … 25.5 | 同上，coarse 侧。 |
| `GrainLimitsUsePattern` | bool | `true` | | true 用 Pattern 里的 grain 限制，false 用本对象的。 |
| `XYOverlap` | double | `0.8` | 0 … 1 | 两个候选**面积重叠比例 > XYOverlap** 且所有 DOF 也重叠时，丢弃较弱者。 |
| `OutsideRegionThreshold` | double | `0` | 0 … 0.999 | 允许落在搜索区域外而不影响 score 的特征比例；PatFlex / PatMaxPerspective 时必须为 0。 |
| `ScoreUsingClutter` | bool | `true` | | PatMax 是否把"多余特征"计入分数（true ⇒ 多余特征压低分数）。仅 PatMax 有效。 |
| `ApproximateNumberToFind` | int | `1` | ≥1 | 期望结果数；实际可能多也可能少。 |
| `Timeout` | double | `5000` | ms | 超过则尽快停止并返回 `E_Cog_TimeoutExpired`。 |
| `TimeoutEnabled` | bool | `false` | | false 时 `Timeout` 被忽略。 |
| `RunAlgorithm` | `CogPMAlignRunAlgorithmConstants` | `2` = `BestTrained` | | 必须是训练时启用的算法之一。 |
| `RunMode` | `CogPMAlignRunModeConstants` | `0` = `SearchImage` | | `RefineStartPose` 时只在 `StartPose` 附近细化。 |
| `StartPose` | `CogTransform2DLinear` | `null` | | 起始位姿。 |
| `SearchRegionMode` | `CogRegionModeConstants` | `0` | | 搜索区域如何作用于输入图。 |
| `SearchImageMask` | `CogImage8Grey` | `null` | | Care / Don't Care 掩膜，配合 `SearchImageMaskOffsetX/Y` 对齐。 |
| `SearchImageMaskOffsetX/Y` | int | `0` | 像素 | 掩膜相对输入图左上角的偏移。 |
| `SaveMatchInfo` | bool | `false` | | 保存用于生成 match 图形的信息（`ICogPMAlignResult::CreateResultGraphics` 需要）。 |
| `ZoneAngle` / `ZoneScale` / `ZoneScaleX` / `ZoneScaleY` | 子对象 | 见 §4 | | 各自由度（DOF）的搜索区配置。 |
| `ChangedEventSuspended` / `HasChanged` | int / bool | - | | 基类基础设施，非算法参数。 |

---

## 3. `CogPMAlignPattern` - 训练参数全表

原生接口：`ICogPMAlignPattern`。

| 属性 | 类型 | 默认值 | 合法范围 | 官方语义（原文摘录） |
|---|---|---|---|---|
| `TrainAlgorithm` | `CogPMAlignTrainAlgorithmConstants` | `2` = `PatMaxAndPatQuick` | | 训练启用哪些算法；运行只能用训练时启用的。 |
| `TrainMode` | `CogPMAlignTrainModeConstants` | `0` = `Image` | | 用训练图像素训练，还是用 `TrainShapeModels` 的合成形状描述训练。 |
| `EdgeThreshold` | double | **`10`** | 0 … 65535 | **Train-time** edge threshold, under which edges are ignored. Default value is 10.0.（与运行的 5.0 **不同**） |
| `AutoEdgeThresholdEnabled` | bool | `true` | | 见 §8 的矛盾说明。 |
| `GrainLimitFine` | double | `1` | 1.0 … 25.5 | Fine granularity limit，`GrainLimitAutoSelect=false` 时手写生效。 |
| `GrainLimitCoarse` | double | `4` | 1.0 … 25.5 | Coarse granularity limit，同上。 |
| `GrainLimitAutoSelect` | bool | `true` | | true 时训练自动生成 grain 限制。 |
| `Elasticity` | double | `0` | 像素 | 允许的特征位置变化量；非 0 才能匹配形变实例。 |
| `IgnorePolarity` | bool | `false` | | true 忽略极性（亮↔暗均可匹配）。 |
| `HighSensitivityParameter` | double | `2` | | 仅 `PatMaxHighSensitivity` 训练算法使用的灵敏度参数。 |
| `ExpectedDeformationRate` | double | `0.300000011920929` | | PatFlex 期望形变率；仅 PatFlex 有效。**存储为 float32 0.3f**，见 §8。 |
| `RepeatingPatterns` | bool | `true` | | true 且算法为 PatMax 时，按重复纹理专门训练。 |
| `RuntimeScoringMethod` | `CogPMAlignVisiblePixelMaskScoringMethodConstants` | `0` = `DoNotScoreFeatures` | | 运行时如何处理"不可见像素"。 |
| `TrainRegion` / `TrainRegionMode` | `ICogRegion` / `CogRegionModeConstants` | `null` / `0` | | 训练 ROI；`null` 表示整幅训练图。 |
| `TrainImageMask` / `…OffsetX/Y` | `CogImage8Grey` / int | `null` / `0` | | Care / Don't Care 训练掩膜。 |
| `TrainImage` | `ICogImage` | `null` | | 训练图。 |
| `Origin` | `CogTransform2DLinear` | `null` | | 6-DOF 图案原点，Pattern Space → TrainImage 选定空间。 |
| `TrainShapeModels` / `TrainShapeModelsTransform` | 集合 / `ICogTransform2D` | `null` | | `TrainMode` 为 ShapeModels* 时的形状模型输入。 |
| `PCPString` / `PCPDiagMessage` | string | `""` / - | | 定制串（PCP）与其诊断信息。 |
| `Trained` | bool | 只读 | | 训练完成标志。 |
| `Composite` | `CogPMAlignComposite` | | | 多图合成训练（见 §7）。 |

---

## 4. DOF 区域（`CogPMAlignZoneAngle` / `CogPMAlignZoneScale`）

| 属性 | ZoneAngle 默认 | ZoneScale 默认 | 官方语义 |
|---|---|---|---|
| `Configuration` | `0` = `Nominal` | `0` = `Nominal` | `Nominal`(=0) 只用 `Nominal` 值附近找；`LowHigh`(=1) 用 `Low`/`High` 区间。 |
| `Low` | **−0.785398163397448**（−π/4 rad = −45°） | **0.8** | 区间下限（仅 LowHigh 生效）。 |
| `High` | **0.785398163397448**（π/4 rad = +45°） | **1.2** | 区间上限（仅 LowHigh 生效）。 |
| `Nominal` | **0**（rad） | **1** | 名义值（仅 Nominal 生效）。 |
| `Overlap` | **6.28318530717959**（2π rad = 360°） | **1.4** | 重叠判据：角度 `|Δθ| ≤ Overlap`、尺度 `max/min ≤ Overlap` 视为重叠，丢弃较弱实例。 |

**要点**：默认 `Configuration = Nominal` 意味着**默认不搜索角度/尺度**（`Nominal=0` / `Nominal=1`）。想搜 ±45° 必须显式把 `Configuration` 设为 `LowHigh`。这一点很容易被误读成"默认搜 ±45°"。

---

## 5. `CogPMAlignResult` - 结果字段语义

| 字段 | 类型 | 官方语义（原文） |
|---|---|---|
| `Score` | double | 0.0 … 1.0，越大越接近。 |
| `Coverage` | double | 训练图案特征被找到的比例 0.0 … 1.0；**仅 PatMax，PatQuick 恒为 −1**。 |
| `Clutter` | double | 多余特征数 ÷ 训练图案特征数，0.0 … ∞；**仅 PatMax，PatQuick 恒为 −1**。 |
| `FitError` | double | 特征与训练图案的符合度质量；0 = 完美，∞ = 很差；**仅 PatMax，PatQuick 为 −1**。 |
| `Contrast` | double | 该结果所有特征边界上像素梯度测量的**平均值**。 |
| `Accepted` | bool | `Score ≥ ICogPMAlignRunParams::AcceptThreshold`。 |
| `MaxCoarseAcceptThreshold` | double | 能让该结果被找到的**最大粗阈值**（可用于反推需要多低的 `CoarseAcceptThreshold`）。 |
| `FineStage` | bool | true = 精定位阶段结果，false = 粗定位阶段结果。 |
| `OutsideRegionAreaProportion` | double | 图案面积落在搜索区外的比例。 |
| `OutsideRegionFeatureProportion` | double | 图案特征落在搜索区外的比例。 |
| `ID` / `ModelID` / `ModelName` | int / long / string | 结果唯一 ID（= 在集合中的索引）/ 图案 ID / 图案名（仅多模型）。 |
| `UnwarpedInputImage` | `ICogImage` | PatFlex 反形变后的输入图（未跑 PatFlex 或未 `SaveDeformationInfo` 时为 null）。 |

---

## 6. 枚举全表（实测，Constant 表原值）

**`CogPMAlignTrainAlgorithmConstants`**（训练算法）
| 名 | 值 |
|---|---|
| `PatMax` | 0 |
| `PatQuick` | 1 |
| `PatMaxAndPatQuick` | 2 |
| `PatFlex` | 3 |
| `PatMaxHighSensitivity` | 4 |
| `PatMaxPerspective` | 5 |

**`CogPMAlignRunAlgorithmConstants`**（运行算法）`PatMax`=0, `PatQuick`=1, `BestTrained`=2, `PatFlex`=3, `PatMaxPerspective`=4

**`CogPMAlignRunModeConstants`** `SearchImage`=0, `RefineStartPose`=1
**`CogPMAlignTrainModeConstants`** `Image`=0, `ShapeModelsWithImage`=1, `ShapeModelsWithTransform`=2
**`CogPMAlignZoneConstants`** `Nominal`=0, `LowHigh`=1
**`CogPMAlignVisiblePixelMaskScoringMethodConstants`** `DoNotScoreFeatures`=0, `ScoreFeaturesAsClutter`=1
**`CogPMAlignFlexRefinementConstants`** `None`=0, `Coarse`=1, `Medium`=2, `Fine`=3
**`CogPMAlignFlexDeformationFitConstants`** `Perspective`=0, `SurfaceFlex`=1

**诊断码（重要：暴露了算法阶段划分）**

`CogPMAlignTrainDiagnosticConstants`（训练期）
| 名 | 值 | 读数 |
|---|---|---|
| `TooFewFeatures` | **10000** | 基数 |
| `Granularity` | 10001 | |
| `Blurry` | **10100** | 第 2 组 |
| `ConfusingDOF` | 10101 | |
| `Degenerate` | 10102 | |
| `FineFeatures` | **10200** | 第 3 组 |

`CogPMAlignRunDiagnosticConstants`（运行期）
| 名 | 值 |
|---|---|
| `ContrastThreshold` | **20300** |
| `Proximity` | 20301 |
| `ExcessClutter` | 20302 |
| `Degenerate` | 20303 |

**推断**：诊断码是"阶段号×100 + 子码"的编码（训练 100/101/102，运行 203），说明原生核心里存在命名固定的算法阶段（粗/中/精 + 校验）。这与原生 `.rsrc` 里的诊断文案一一对应（见 `native-strings.md` §6），可以作为复刻时"该在哪个阶段报哪类问题"的骨架。

---

## 7. 其他有用实测值

| 位置 | 属性 | 值 |
|---|---|---|
| `CogPMAlignTool` | `CurrentRecordEnable`（默认图形集位掩码） | **458831** |
| `CogPMAlignTool` | `LastRunRecordEnable` | **66** |
| `CogPMAlignTool` | `LastRunRecordDiagEnable` | **1** |
| `CogPMAlignComposite` | `IgnorePolarity` / `TimeoutEnabled` | 无 `[DefaultValue]`（`false`） |
| `CogPMAlignComposite` | `TuningElasticity` / `ImageFraction` | 无 `[DefaultValue]`；语义：特征至少要在该比例的训练图中出现才计入图案 |

`CogPMAlignTool` 的输出终端映射（实测，来自类型上的 `[CogDefaultToolOutputTerminal]`）：
`0 → Results.Item[0].GetPose()`，`1 → …TranslationX`，`2 → …TranslationY`，`3 → …Rotation`，`4 → Results.Item[0].Score`。

---

## 8. 实测矛盾与坑（复刻时容易踩）

1. **`AutoEdgeThresholdEnabled` 属性值 = `true`，官方文本却写 "Default value is false"**（`CogPMAlignPattern` 与 `CogPMAlignRunParams` 两处文本都这么写）。以 `[DefaultValue(true)]` 为准；文本是历史遗留。
2. **`ExpectedDeformationRate` 的默认值是 float32**：属性 blob 解出 `0.300000011920929`，而在 DLL 里能搜到 `9A 99 99 3E`（0.3f）三处，搜不到 `000000403333D33F`（double 0.3 的 `(double)0.3f` 形态）。**结论：该参数以 float 存储**。用 double `0.3` 复刻会在第 8 位有效数字上不同，进而影响任何阈值比较。实测：file offset `0xAE23B3`、`0x10FEB04`、`0x10FF0A8` 处为 `9A99993E`。
3. **`EdgeThreshold` 有两个默认值**：训练 `10.0`（`CogPMAlignPattern`），运行 `5.0`（`CogPMAlignRunParams`）。别把两者当一个参数。
4. **`ContrastThreshold` 与 `EdgeThreshold` 都是"像素梯度单位"**（0…65535；8-bit 图上限 255，超了抛 `ArgumentException`），不是 0…1 的归一化量。任何 0…1 的实现都需要显式换算。
5. **`Configuration` 默认 `Nominal`**（见 §4），默认不搜角度/尺度。
6. **`CoarseAcceptThreshold` 与 `AcceptThreshold` 互相夹紧**：设大 `CoarseAcceptThreshold` 会自动抬高 `AcceptThreshold`，反之亦然。复刻时必须实现这个双向约束，否则行为不同。
7. **`OutsideRegionThreshold` 必须 < 1**（>0.999 抛异常），且 PatFlex / PatMaxPerspective 下必须为 0。

---

## 9. 与 ShapeAnchor 现有公开 API 的三类比对

比对基准（仓库内实际代码，已按当前 HEAD 复核一遍，含 `ShapeAnchorTool` 新增的 `NoiseFloor` 别名）：
`ShapeAnchorTool.cs`、`Matching/SearchOptions.cs`、`Matching/Scorer.cs`、`Matching/PeakDetector.cs`、
`Matching/MatchResult.cs`、`Core/PatternModel.cs`、`Training/PatternTrainer.cs`、
`Edge/GaborEdgeDetector.cs`、`Projection/*`。

> 复核结论：`ShapeAnchorTool` 的公开参数与默认值（`AcceptThreshold = 0.5`、`ContrastThreshold = 0.08`、
> `RotationAngleMin/Max = ∓180`、`ScaleMin/Max = 1.0`、`TrainMaxFeatures = 600`、`MaxLevels = 4` 等）
> 与本次比对时一致，仅多了 `NoiseFloor` 透传属性，故下表结论不变。

### 9.1 已实现（名称与语义都对得上）

| VisionPro | ShapeAnchor | 备注 |
|---|---|---|
| `AcceptThreshold = 0.5` | `ShapeAnchorTool.AcceptThreshold = 0.5`、`SearchOptions.AcceptThreshold = 0.5`、`Scorer.AcceptThreshold = 0.5` | 默认值完全一致 |
| `ScoreUsingClutter = true` | `ShapeAnchorTool.UseClutter = true`、`SearchOptions.UseClutter = true` | 一致 |
| `Timeout` / `TimeoutEnabled(false)` | `ShapeAnchorTool.TimeoutMs = 0`（禁用） | 效果等价（默认不超时） |
| `GrainLimitFine/Coarse` 的"粒度"概念 | `PatternModel.Grain = 1.0`、`TrainOptions.Grain` | 概念有，结构不是一对 |
| `Score` / `Coverage` / `FitError` / `Contrast` 结果字段 | `MatchResult.Score/.Coverage/.FitError/.Contrast` | 字段齐全 |
| `MaxResults`（`ApproximateNumberToFind` 的近似） | `ShapeAnchorTool.Run(image, maxResults)`、`SearchOptions.MaxResults = 1` | 默认 1 与 `ApproximateNumberToFind = 1` 一致，但语义是"硬上限"而非"期望数" |
| 形状模型/多模型 | `PatternMulti`、`MultiRunMode` | 结构存在 |
| PatFlex 类形变拟合 | `Transform/XformTps.cs`（TPS） | 有替代实现，参数未对齐 |
| 边缘检测核心常量 | `GaborEdgeDetector.DefaultSigmaX = 2.0506096654409878`、`DefaultSensitivityParameter = 2.0` | **这两个常量被我这次取证证实**（见 `native-strings.md` §8） |

### 9.2 语义不符（有，但意思/单位/默认值不同 -- 优先级最高）

| # | 项目 | VisionPro（实测） | ShapeAnchor（现状） | 差异性质 |
|---|---|---|---|---|
| D1 | `ContrastThreshold` 的量纲 | `10`，单位=8-bit 像素梯度，范围 0…65535 | `0.08`，归一化 0…1；已真正用作接受门限（`Matching/SearchEngine.cs:643` `contrast >= opt.ContrastThreshold`） | **量纲差 ~125×**。判据方向已经对了，但数值不能直接照搬 VisionPro 的 10，也无法互相换算 |
| D2 | 角度搜索范围 | `ZoneAngle.Low/High = ∓π/4`（±45°），且 `Configuration=Nominal` 时默认**不搜** | `RotationAngleMin/Max = ∓180°`，默认全搜 | 默认行为相反：VisionPro 默认不转，ShapeAnchor 默认搜满 360° |
| D3 | 尺度搜索范围 | `ZoneScale.Low/High = 0.8/1.2`（±20%） | `ScaleMin/Max = 1.0/1.0` | 默认行为相反：VisionPro 默认允许 ±20%，ShapeAnchor 默认锁死 1.0 |
| D4 | 重叠抑制 | `XYOverlap = 0.8`（**面积重叠比例**，且要求所有 DOF 同时重叠才丢弃） | `SearchOptions.OverlapSuppression = 0.55`（单一阈值） | 数值不同（0.8 vs 0.55）且判据少了"所有 DOF 都重叠"这一半条件 |
| D5 | `TrainAlgorithm` 默认值 | `2 = PatMaxAndPatQuick` | `PatternModel.TrainAlgorithm = 0`（裸 `int`，无枚举） | 默认值不同；且缺枚举，可读性与可校验性都差 |
| D6 | `MinAcceptablePeakFallOff = 4.0` | VisionPro 无同名公开参数（无可对照） | 绝对单位的落差判据（`v - neighbor < 4.0`，`Matching/PeakDetector.cs`），而 `ProjectionMethod.Project` 返回的是**沿搜索方向累加的原始梯度**，量级随 `searchLength` 线性变化 | 参数含义随调用参数漂移，等于不可标定；建议改成相对落差 |
| D7 | `ApproximateNumberToFind` | "期望数量，可能多也可能少" | `maxResults` 硬截断 | 少数量结果时的排序/裁剪行为不同 |

**已在当前版本修复**（复核确认，早期修订版确实存在这两个问题，供你判断是否需要回退说明）：

- 评分不再是"两个旋钮"：`Scorer.Score(...)` 现在接收 `exponent` 形参，
  `Matching/SearchEngine.cs:624-627` 传 `1 / Math.Max(1.0, model.N)`，而 `model.N = ShapeAnchorTool.ScoreExponentN`
  （`ShapeAnchorTool.cs:134`），单一来源。
- `Scorer.ScoreBreakdown` 已没有 `Contrast` 字段（现在只有 `Score, Coverage, ClutterPenalty, Matched, Total`），
  不存在"Contrast 被填成 coverage"的问题；`MatchResult.Contrast` 由 `SearchEngine.cs:630`
  的 `contrastSum / matched * invRef` 真实计算。


### 9.3 完全缺失（VisionPro 有、仓库没有）

按"对精度/速度的实际影响"排序：

| # | 缺失项 | VisionPro 默认 | 为什么重要 |
|---|---|---|---|
| M1 | `CoarseAcceptThreshold` + `CoarseAcceptThresholdEnabled` | `0.33` / `false` | 粗→精两级门槛是**速度主轴**：粗阈值放宽（0.33）+ 精阈值把关（0.5），能在不掉精度的前提下多保留候选。当前实现只有一个 `AcceptThreshold`，粗阶段与精阶段同门槛，要么漏检要么慢 |
| M2 | `EdgeThreshold`（运行 5.0 / 训练 10.0）+ `AutoEdgeThresholdEnabled` | 5 / 10 / true | 当前只有归一化的 `TrainContrastThreshold = 0.08`，没有任何**像素单位**的梯度门槛，弱纹理图与高对比图无法共用一套默认值 |
| M3 | `GrainLimitCoarse/Fine` + `GrainLimitAutoSelect` + `GrainLimitsUsePattern` | 4 / 1 / true / true | 只有单个 `Grain`；粒度**双值 + 自动选择**是尺度阶梯的核心，直接决定粗层的金字塔步长 |
| M4 | `OutsideRegionThreshold` | `0`（<0.999） | 搜索区边界附近的实例评分；没有它，ROI 边缘的实例会被系统性低估 |
| M5 | `Zone*.Configuration`（`Nominal`/`LowHigh`）+ `Zone*.Overlap` | Nominal / 2π / 1.4 | 缺"名义值模式"，即无法表达"只在这个角度附近细找"；也缺角度/尺度各自的重叠阈值 |
| M6 | `RunMode`（`SearchImage` / `RefineStartPose`） | 0 | 缺"跟踪模式"：已知上一帧位姿时只局部细化，是产线提速的关键路径 |
| M7 | `Elasticity`、`IgnorePolarity`、`HighSensitivityParameter`、`ExpectedDeformationRate`、`RepeatingPatterns` | 0 / false / 2 / 0.3f / true | 覆盖形变、极性无关、高灵敏、重复纹理四类真实工况；缺 `IgnorePolarity` 会在反光/背光件上直接漏检 |
| M8 | `RuntimeScoringMethod` + `SearchImageMask`/`TrainImageMask`（Care/Don't Care） | `DoNotScoreFeatures` / null | 掩膜是"只关心这块"的标准做法，比 ROI 更细 |
| M9 | `TrainMode`（`Image` / `ShapeModels*`） | `Image` | 无 CAD/合成形状训练路径 |
| M10 | 结果字段 `Clutter` / `MaxCoarseAcceptThreshold` / `FineStage` / `OutsideRegion*Proportion` / `UnwarpedInputImage` | - | 诊断与"为什么没找到"的定位能力；`MaxCoarseAcceptThreshold` 尤其有用（能直接告出该把粗阈值调到多少） |
| M11 | 诊断码体系（10000/10100/10200、20300-20303）+ 诊断文案 | - | 有了码位就能把"训练失败"细分到 TooFewFeatures / Blurry / ConfusingDOF / Degenerate，而不是一句"训练失败" |
| M12 | `PCPString` | `""` | 厂商定制串；复刻价值低，但读取旧作业文件时需要忽略字段 |

### 9.4 改进建议（按性价比排序，均为可落地的小改动）

1. **补 `CoarseAcceptThreshold`（默认 0.33）+ 开关**，并在 `SearchEngine` 粗层用 `score ≥ 0.33` 而不是 `AcceptThreshold`。这是**唯一同时提升召回与速度**的改动，且默认值有实测依据。
2. **把 `ContrastThreshold` 单位改成像素梯度**（默认 10），保留一个 `ContrastThresholdNormalized` 兼容旧调用；同时在 8-bit 图上按官方行为校验 >255 抛错。否则永远无法与 VisionPro 参数对照。
3. **角度/尺度默认值对齐**：`RotationAngleMin/Max` 默认改为 `∓45°` 且补上 `Nominal` 语义（默认不搜）。当前默认搜满 360° 是性能黑洞，也让"默认参数"与 VisionPro 完全不可比。
4. **`Grain` 拆成 `GrainLimitCoarse/Fine`（默认 4/1）+ `GrainLimitAutoSelect`**，范围钳到 [1.0, 25.5]；`PeakDetector.MinAcceptablePeakFallOff` 改为相对落差（除以峰值或邻域能量），让参数与 `searchLength` 解耦。
5. **补结果字段 `MaxCoarseAcceptThreshold` 与 `Clutter`**：前者能让工具自己报"该把粗阈值降到多少"，后者是 PatMax 系 score 的关键输入（`ScoreUsingClutter` 已实现但 score 里用的是自造的 `clutterFraction = bad/count*0.5`，与 VisionPro 的 clutter 定义--多余特征数 ÷ 训练特征数--不是同一个量）。

---

## 10. 复现

```powershell
# 1) 元数据全量导出（含 [DefaultValue]、CogDoc* 文本、IL 常量、枚举常量）
dotnet run --project tools/cognex-meta -- meta `
  "C:\Program Files\Cognex\VisionPro\ReferencedAssemblies\Cognex.VisionPro.PMAlign.dll" `
  docs/evidence/raw/pmalign-meta.json `
  "C:\Program Files\Cognex\VisionPro\ReferencedAssemblies\Cognex.VisionPro.PMAlign.xml"

# 2) 本文的参数表（机器生成版，未删减）
dotnet run --project tools/cognex-meta -- paramtable docs/evidence/raw/pmalign-meta.json docs/evidence/raw/pmalign-params.generated.md

# 3) 完整类型/成员表
dotnet run --project tools/cognex-meta -- md docs/evidence/raw/pmalign-meta.json docs/evidence/api-surface.md --level full
```

机器生成的未删减参数表见 [`raw/pmalign-params.generated.md`](raw/pmalign-params.generated.md)；全量类型与成员见 [`api-surface.md`](api-surface.md)。
