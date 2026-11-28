# 参数对照：ShapeAnchor ↔ Cognex PatMax

数据来源：`docs/evidence/`（`api-surface.md`、`params-reference.md`，由 `tools/cognex-meta` 从真实
`Cognex.VisionPro.PMAlign.dll` 的 ECMA-335 元数据与 `[DefaultValue]` 属性读出），不是凭印象。

图例：**实测** = 从二进制读出；**推理** = 由实测值 + 官方文本推得，非二进制原文。

## 1. 语义一致（数值也一致的直接对齐）

| ShapeAnchor | 默认 | PatMax | PatMax 默认 | 备注 |
|---|---|---|---|---|
| `AcceptThreshold` | 0.70 | `AcceptThreshold` | **0.5** | 我们把默认抬到 0.70：实测 0.5 会接受尺度贴边界的伪解 |
| `CoarseAcceptThreshold` | 0.33 | `CoarseAcceptThreshold` | **0.33** | 数值一致 |
| `XYOverlap` | 0.5 | `XYOverlap` | **0.8** | 语义不同，见 §3 |
| `TimeoutMs` | 0（不限） | `Timeout` + `TimeoutEnabled` | 5000 / **false** | 我们 0 = 不限 ≡ PatMax 关闭超时 |
| `UseClutter`（杂波计分） | true | `ScoreUsingClutter` | **true** | 一致 |
| `MaskScoring` | DoNotScoreFeatures | `RuntimeScoringMethod` | **DoNotScoreFeatures** | 一致 |
| `TrainMask` / `SearchMask` | null | `TrainImageMask` / `SearchImageMask` | null | 已实现 |

## 2. 换算表达（同一概念，单位/形式不同）

| ShapeAnchor | PatMax | 换算关系 |
|---|---|---|
| `EdgeThreshold`（8.0，0–255 幅值） | `EdgeThreshold` 训练 **10.0** / 运行 **5.0**（0–65535） | 训练与运行门限合并为单一 `NoiseFloor` |
| `AutoEdgeThreshold` | `AutoEdgeThresholdEnabled` **true** | 一致 |
| `Grain` | `GrainLimitFine` **1** / `GrainLimitCoarse` **4** | 我们有单一 grain；PatMax 分粗细两档 |
| `ContrastThreshold`（0–1 归一化） | `ContrastThreshold` **10**（0–65535） | 我们改为按该层幅值基准归一化 |
| `ScoreExponentN` | 无 | `score = coverage^(1/N)`；N=1 退化为覆盖率，等价于"分母固定"的严格做法 |
| `MaxResults` | `ApproximateNumberToFind` **1** | 我们多一个"最多返回几个"的上限 |
| `ClusterRadius`（像素） | `XYOverlap` **0.8**（面积重叠比例） | 见 §3 |

## 3. PatMax 有、我们语义不同

| 项 | PatMax | ShapeAnchor | 影响 |
|---|---|---|---|
| 重叠抑制 | `XYOverlap` 0.8 = **面积重叠比例**，且**所有自由度同时重叠**才丢弃弱者 | `ClusterRadius` 像素 + `XYOverlap` 换算成最小中心距 | 我们的判据更粗：可能把一个稍偏移的真实例压掉，也可能漏抑制 |
| 粗层阈值开关 | `CoarseAcceptThresholdEnabled` **默认 false**（默认不启用） | 恒启用，值 0 即等价关闭 | 我们默认启用是为了速度；想要 PatMax 行为就把 0.33 调到 0 |

## 4. 我们独有（PatMax 没有对应参数）

| ShapeAnchor | 作用 | 为什么保留 |
|---|---|---|
| `PrecisionStep`（粗层位置步长） | 粗搜索的像素步长 | 实现细节，调速度用 |
| `MaxCandidates`（候选假设数） | 粗层带进细化的假设数 | 实现细节，换时间与召回 |
| `TrainFeatureFloor`（训练边缘门限，占最强边比例） | 过滤弱边缘，抗光照变化 | 实测：最强 120 特征比 600 更稳，这个旋钮控制"多强算特征" |
| `PreSmoothSigma`（预平滑 σ） | 匹配前高斯平滑 | 噪点很重的图才需要 |
| `TrainMinSpacing`（特征最小间距） | 泊松盘采样间距 | **PatMax 无此参数**，见 §5 |
| `MinPolarityCoherence`（极性一致性门） | 抑制"边缘密度相似"的伪解 | 我们自研：真实实例≈1.00，伪解≈0.5 |
| SIMD / 并行 | 工程开关 | 纯性能 |

## 5. 特征最小间距：PatMax 怎么做的，我们改成自动

**PatMax 没有"特征最小间距"这个参数。** `api-surface.md` 与 `params-reference.md` 里都没有 Spacing 类成员；
特征密度的控制手段是：

| PatMax 参数 | 默认 | 含义 |
|---|---|---|
| `GrainLimitFine` | **1** | 细层粒度（特征采样精细程度） |
| `GrainLimitCoarse` | **4** | 粗层粒度，要求 ≥ fine |
| `GrainLimitAutoSelect` | **true** | **自动选粒度**（默认开启） |
| `GrainLimitsUsePattern` | **true** | 运行时沿用训练得到的粒度 |

也就是说：PatMax 用**粒度**控制采样密度，并且**默认自动**。

因此我们把间距改为自动：

```
自动间距 = clamp(2 × 粒度, 1, 6) px
```

- 粒度 1（默认）→ **2.0 px**，与原先手动默认值一致，所以现有实测数字不变；
- 粒度越大（欠采样/模糊图）→ 间距随之变大，避免在模糊边缘上堆叠冗余特征；
- UI 在「5. 高级」里有「特征间距自动（跟随粒度）」开关，关掉后才用滑杆的手动值；
- 训练完成的提示会打印实际使用的间距与边缘阈值。

## 6. PatMax 有、我们尚未实现的参数

| PatMax | 默认 | 说明 |
|---|---|---|
| `Elasticity` | **0** 像素 | 允许特征位置变化量；非 0 才能匹配形变实例（PatQuick/PatFlex 路线） |
| `OutsideRegionThreshold` | **0** | 搜索区域外响应的容忍度 |
| `TrainAlgorithm` / `RunAlgorithm` | PatMaxAndPatQuick / BestTrained | 我们只实现 PatMax 路径 |
| `GrainLimitCoarse/Fine` 分开设置 | 4 / 1 | 我们只有单一 `Grain` |
