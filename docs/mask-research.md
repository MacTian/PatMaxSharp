# PatMax 掩膜（Mask）功能调研

调研依据：本仓库 `docs/evidence/` 下的原生取证（`api-surface.md`、`params-reference.md` 由 `tools/cognex-meta` 从真实
`Cognex.VisionPro.PMAlign.dll` 元数据导出），不是凭印象描述。

## 1. 原版有哪些掩膜

| 成员 | 类型 | 所在接口 | 语义（原文摘录） |
|---|---|---|---|
| `TrainImageMask` | `CogImage8Grey` | `ICogPMAlignPattern` | “Mask used when training the PMAlign pattern. The mask allows the user to identify areas of the pattern as **Care** or **Don't Care**. The 'Don't Care' areas can also be subclassified into **Score** and **Don't Score** with regards to clutter.” |
| `TrainImageMaskOffsetX/Y` | `int` | `ICogPMAlignPattern` | 掩膜相对 `TrainImage` 左上角的偏移（图像坐标） |
| `GetTrainedPatternImageMask()` | → `CogImage8Grey` | `ICogPMAlignPattern` | 返回**实际生效**的训练掩膜 = `TrainImageMask` 与 `TrainRegionMode` 所需掩膜的合并 |
| `SearchImageMask` | `CogImage8Grey` | `ICogPMAlignRunParams` | 运行时掩膜，按 `SearchImageMaskOffsetX/Y` 对齐到输入图，同样是 Care / Don't Care |
| `SearchImageMaskOffsetX/Y` | `int` | `ICogPMAlignRunParams` | 偏移 |
| `RuntimeScoringMethod` | `CogPMAlignVisiblePixelMaskScoringMethodConstants` | `ICogPMAlignRunParams` | 被掩掉的（“不可见”）像素在评分时怎么处理：`DoNotScoreFeatures`=0 完全不参与 / `ScoreFeaturesAsClutter`=1 记为杂波 |

另外 `CogPMAlignCurrentRecordConstants` / `LastRunRecordDiagConstants` 里掩膜是独立记录项
（`TrainImageMask=4`、`TrainedPatternImageMask=1024`、`SearchImageMask=262144` …），说明它不是 UI 上的画笔，
而是工具数据模型的一部分，可被记录、诊断、导出。

### 关键结论

1. 掩膜是**三档语义**，不是二值开关：Care（参与）/ Don't care but don't score（不参与且不计杂波）/ Don't care but score（不参与匹配但**算作杂波**）。
2. 掩膜有**两个阶段**：训练掩膜决定“模型里有哪些特征”；运行掩膜决定“这一次输入的哪些区域不算数”。
3. 掩膜与 ROI 是**正交**的：ROI 是矩形/仿射框，掩膜是框内的逐像素筛选。

## 2. 本实现（ShapeAnchor）的现状

- 只有矩形区域：`TrainRegion`（训练框）、`SearchRegion`（搜索范围）。
- 没有任何逐像素掩膜：`git grep -i mask -- "*.cs"` 只命中边缘掩膜、类型可见性掩膜等无关代码。
- `docs/evidence/params-reference.md` 第 296 行已把该项列为未实现（**M8**）：
  `RuntimeScoringMethod` + `SearchImageMask`/`TrainImageMask`（Care/Don't Care），当前取 `DoNotScoreFeatures` / null。

也就是说：**没有做**。

## 3. 为什么现在值得做（用本仓库实测的失败案例说明）

| 图像 | 现状 | 掩膜能解决的部分 |
|---|---|---|
| `14.bmp` | 无法匹配 | 拍屏幕：背景是 COGNEX / VISIRO 演示文稿文字，背景边缘强度远高于零件。把文字区标 Don't Care，特征只从零件来 |
| `15.bmp` | 无法匹配 | 强杂波（电路板、杂志堆），零件部分被挡。掩掉背景后模型更干净 |
| `8.bmp` / `12.bmp` | 一致性 0.78–0.85 压线 | 塑料袋/过曝区可作为 Don't Care，但保留 `ScoreFeaturesAsClutter` 让分数反映遮挡程度 |

这三类问题**不是**靠调阈值能解决的：它们的共同点是“框内存在比目标更强的干扰边缘”。掩膜正是为此设计的机制。

## 4. 实现方案（三步，每步可独立验证）

### 第 1 步：训练掩膜（核心，约 80 行）

- `PatternTrainer` 增加 `byte[] mask`（与训练区域同尺寸，0 = Don't care）；
- 特征采集时跳过 `mask==0` 的像素（`DetectionLoop` 与脊线细化处）；
- `PatternModel` 记录掩膜尺寸与偏移，供 `GetTrainedPatternImageMask()` 的等价物与 UI 可视化使用。

**验证**：给 `14.bmp` 手工标出文字区，看模型特征是否全部落在零件轮廓上，以及匹配分数是否从“无结果”变成可接受。

### 第 2 步：运行掩膜 + 计分方式（约 70 行）

- `SearchOptions.SearchMask` + `MaskScoringMethod { DoNotScoreFeatures, ScoreFeaturesAsClutter }`；
- `SearchEngine.ScoreAtBilinear`：命中点落在掩膜内时，按方式决定“从分母里剔除”还是“计入 `bad`（杂波）”；
- **注意**：这会改变 coverage 的分母语义，必须同步调整 `Scorer` 与门禁标定，否则新旧分数不可比。

### 第 3 步：UI 与预设（约 120 行）

- 画布上画笔/矩形增减掩膜（复用现有 `ImageRegionSelector` 的坐标系）；
- 训练区显示掩膜覆盖（半透明红）；
- 评分参数面板增加“运行时掩膜计分方式”下拉；
- 预设里新增一档「掩膜遮蔽背景」，演示 14/15 的用法。

## 5. 风险与取舍

| 风险 | 说明 |
|---|---|
| 分数不可比 | 掩膜改变 coverage 分母，历史阈值与 README 里的实测表都要重新标定 |
| 用法门槛 | 掩膜需要人工标注，属于“按场景调优”的功能，不适合做默认行为 |
| 与特征筛选重叠 | 我们已用“最强 120 特征”抑制弱边缘；掩膜解决的是**空间上**的强干扰，两者互补，不能互相替代 |

## 6. 建议

先做第 1 步并只用 CLI 验证（`--mask <png>` 传入训练掩膜），拿到 `14.bmp` 与 `15.bmp` 的对照数据；有效再进 UI。
这样即使第 2/3 步不做，也已经补上了“模型不干净”这个最影响准确率的缺口。
