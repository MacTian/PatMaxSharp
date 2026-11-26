# `docs/evidence/` -- Cognex VisionPro PMAlign 静态取证记录

本目录只放**外部可观测的事实**：程序集元数据里的类型/成员签名、`[DefaultValue]` 属性 blob、
Constant 表里的枚举值、原生 PE 的节表与明文 `.rsrc` 字符串。
不放反编译源码，不放第三方代码，不放任何被解密/被解包的加壳内容。

---

## 1. 这个目录是什么，为什么存在

PatMaxSharp 是一个独立的 PatMax / PMAlign 形状匹配实现。为了让**评分函数与参数设计**与业内
公开可见的行为对齐，作者以**本机安装的 Cognex VisionPro**（`C:\Program Files\Cognex\VisionPro`）
作为工程对照对象，用只读方式记录它的外部契约：有哪些参数、默认值是多少、合法范围是什么、
枚举怎么取值、结果字段是什么语义、边缘前处理的默认核参数是多少。

这些数字直接决定了仓库里的若干实现细节（例如 `Edge/GaborEdgeDetector.cs` 的 sigma 与核半窗、
`Matching/SearchOptions` 的阈值量纲）。**没有这份记录，代码里的常数就是没有出处的魔法数字**，
评审者无法判断它是抄的、猜的还是随手写的。所以它跟着代码一起提交。

三条定位：

- **对照证据，不是实现来源**：仓库里没有任何一行代码来自 VisionPro，也没有复制其源码。
- **可复核**：每个数字都标注了元数据表 / 属性 blob / RVA / 文件偏移，重跑工具即可重现。
- **守信于边界**（见 §4）：原生核心被加壳加密，凡静态看不到的东西，这里一律不当作证据。

---

## 2. 文件清单

大小实测于 2026-11-26（Windows 资源管理器口径的字节数）。

| 文件 | 大小 | 入库 | 内容与用途 | 重新生成方式 |
|---|---|---|---|---|
| `README.md` | 约 15 KB | 是 | 本索引：目录定位与存在理由、文件清单、取证方法与边界、关键结论、复现步骤、商标说明。 | 本文件 |
| `api-surface.md` | 56,632 B（55.3 KiB） | 是 | `Cognex.VisionPro.PMAlign.dll` 的元数据导出**决策子集**：核心类型 public 成员签名、28 个 public enum 及其 Constant 表原值、其余 public 类型索引。回答"接口长什么样"。 | `core`（见 §5） |
| `params-reference.md` | 26,519 B（25.9 KiB） | 是 | 参数语义与默认值表 + 与仓库现有实现的三类比对（已实现 / 语义不符 / 完全缺失），含个别属性的实测矛盾。**这是整套取证里唯一"人工整理 + 结论"的文档**，机器生成的部分在 `raw/pmalign-params.generated.md`。 | 人工整理；底稿来自 `paramtable` |
| `native-strings.md` | 23,876 B（23.3 KiB） | 是 | `cogxpma.dll` 原生取证：PE 节表与熵、加壳证据、导出/导入表、`.rsrc` 明文参数串（带 RVA + hexdump）、数值常量的逐条追溯、与既有文档 RVA 声称的核对。回答"原生侧能看到什么、看不到什么"。 | 人工整理；底稿来自 `strings` / `doubles` / `find` / `nconst` |
| `raw/pmalign-meta.json` | 1,291,343 B（1.23 MiB） | **否**（`.gitignore`） | 443 个类型的全量元数据 dump（成员签名、枚举常量、`[DefaultValue]`、`CogDoc*` 原文、IL 常量命中）。是上面前两份 `.md` 的机器底稿。 | `meta` |
| `raw/pmalign-params.generated.md` | 86,713 B（84.7 KiB） | **否**（`.gitignore`） | 未删减的机器生成参数表（默认值 attr / 默认值 IL / 文本默认值 / 合法范围 / 原生接口名 / 官方语义六列）。与 `params-reference.md` 重复度高，只作底稿。 | `paramtable` |
| `raw/native-strings-all.txt` | 595,842 B（582 KiB） | **否**（`.gitignore`） | 原生字符串全量清单（RVA / 文件偏移 / 节 / 编码 / 长度 / 文本）。**仓库里这份是人工截断的节选**（70,115 条中保留前 12,000 条，文件头已注明），重跑会得到完整版、体积约 3.4 MB。 | `strings` |
| `raw/native-strings-keywords.md` | 397,920 B（389 KiB） | **否**（`.gitignore`） | 关键词命中的原生字符串证据（799 条命中，去重后每条带 3 行 hexdump 上下文）。结论已提炼进 `native-strings.md` §5。 | `strings` |
| `raw/managed-native-constants.txt` | 53,236 B（52.0 KiB） | 是 | 托管程序集混合模式原生 `.text` 里的 800 条 double 常量，按 `.pdata` 归属到 432 个函数。**这是唯一能复核原生参数块的原始数据**，必须留在仓库里。 | `nconst` |
| `raw/native-export-import.txt` | 13,491 B（13.2 KiB） | 是 | 29 个导出名（COM 入口 + 硬件/许可查询）+ 40 个导入模块。证明"原生核心没有以 PatMax 命名的 C 导出"。 | `strings` |
| `raw/native-doubles.txt` | 26,254 B（25.6 KiB） | 是 | `.rsrc` 的"疑似 double"分组扫描（含 hexdump），用来证明该节的 double 命中**全是文本噪声**。 | `doubles --section .rsrc` |
| `raw/native-doubles-reference.txt` | 8,650 B（8.4 KiB） | 是 | 已知参考值的 8 字节位型精确搜索命中上下文。 | `doubles`（内含 reference 搜索）或 `find` |

合计（含本索引、共 12 个文件）：全目录约 2.60 MB（2.48 MiB）；其中被 `.gitignore` 排除的四个文件
2,371,818 B（2.26 MiB），实际入库约 218 KiB（本索引 + 三份结论性 `.md` + 四份小原始文件，共 8 个文件）。
本文自身大小随编辑浮动，精确值以 `Get-ChildItem -Recurse docs/evidence | Measure-Object Length -Sum` 实测为准。

排除理由：这四个文件**完全由工具生成、可逐字节重跑**，且体积占全目录的 91%。
它们留在磁盘上供本地复核，但不进版本库；`git status --ignored` 仍能看到它们。

---

## 3. 取证方法与边界

### 3.1 只做静态读取

四类输入，全部只读（`File.ReadAllBytes` / `FileStream`，不加载程序集、不执行目标代码）：

1. **ECMA-335 元数据表** -- `System.Reflection.Metadata.MetadataReader` 直接读表，拿到类型/字段/
   方法/属性的定义与签名 blob。用它是为了绕开 `Assembly.LoadFrom` 的依赖解析（目标是 .NET Framework 4.8
   的 `Cognex.VisionPro.PMAlign`，依赖一大堆兄弟程序集，且会执行目标代码路径）。
2. **`[DefaultValue(x)]` 属性 blob** -- `CustomAttribute.DecodeValue` 解码出实参。VisionPro 的属性网格与
   序列化用的就是它，因此**默认值以它为准**，而不是以文档字符串为准。
3. **Constant 表** -- 枚举字面量的原值（`0/1/2` 到底是哪个枚举成员，全部照表抄，不做推断）。
4. **原生 PE** -- 节表与每节熵、导出/导入目录、明文 `.rsrc` 字符串（RVA + hexdump）、
   以及 x64 代码里 `48 B8+r imm64`（`mov r64, imm64`）的 8 字节立即数还原。

### 3.2 明确没做的事

- **没有运行 VisionPro**，没有实例化 `CogPMAlignTool`，没有训练/运行任何模板。
  所有默认值都来自属性 blob 与常量，不来自"跑一遍看看"。
- **没有解密 WIBU**，没有在内存里 dump 解密后的节，没有 hook 任何加载路径（见 §4）。
- **没有反编译第三方源码**：不产出 IL→C# 的还原代码，只保留**签名、常量、枚举值、明文资源字符串**
  这类事实性数据。文档里的中文说明是本次分析写的，类型名/成员名/数值是二进制原样。
- **没有修改任何原始二进制**。
- **没有 `dotnet build` 主项目**：工具项目 `tools/cognex-meta` 独立编译（`net9.0`），主项目是 `net8.0`，
  两者互不影响。

### 3.3 证据分级

每份文档都按同一套标记写：**实测**（二进制里直接读出，可逐位复核）、**官方文本**（厂商自己写在程序集里的
`CogDoc*` 说明文字）、**推断**（作者的解释，已标注）。推断不会被写成事实，凡结论依赖推断的地方都在原地说明。

---

## 4. 一条关键结论：原生核心不可复核

`cogxpma.dll`（31,099,336 B，SHA-256 `d2c077d7d8…58fec0`）**整个被 WIBU / CodeMeter 加密**：

| 观察项 | 实测值 |
|---|---|
| 节数 | 18 |
| 节名 | 17 个为 `__wibu00` … `__wibu10`，剩下 1 个是 `.rsrc` |
| 熵 | 各 `__wibu*` 节 7.99 – 8.00（`.rsrc` 为 5.43，是唯一明文节） |
| `.text` / `.rdata` | **不存在**（这两个节名在该文件里根本没有） |
| 导出表 | 只有 COM 入口（`DllGetClassObject` 等）与 `C_ccADLinkInt_*` / `C_ccHardwareInt_*` 硬件/许可查询，没有以 `PatMax` 命名的 C 导出 |

**推论**：任何"来自 `cogxpma.dll` 的 RVA、结构体偏移、常量地址"在磁盘上都是密文，静态无法复核；
早期文档里诸如"评分函数在 RVA `0xacc9b0`"这类说法（该 RVA 落在 `__wibu00` 内）**本仓库不采纳为证据**，
仓库实现也不依赖它们。想复核这类地址必须先在内存里解密，那已经越出"静态取证"的边界。

### 可复核的那一部分

真正可复核的原生参数块不在 `cogxpma.dll` 里，而在**托管程序集 `Cognex.VisionPro.PMAlign.dll` 的混合模式
原生 `.text`** 中（`.text` 的 RVA 0x1000 对应文件偏移 0x600，即 `rva = file + 0xA00`）：

- 函数起始 **RVA `0x00EAF2C0`**，**文件偏移 `0x00EAE8C0`**，是一个 leaf 函数（无栈帧、无 `.pdata` unwind 记录）。
- 它是一个 56 字节默认参数块的初始化器，逐指令把 `+0x00 … +0x30` 各字段写满，可逐条反汇编复核
  （详见 `native-strings.md` §6.3）：

| 偏移 | 值 | 类型 | 语义 |
|---|---|---|---|
| +0x00 | `20.0` | double | 推断：核半径上界 / 大核裁剪阈值 |
| +0x08 | `1.0` | double | 推断：sigma 下限或权重 |
| +0x10 | `2.0506096654409878` | double | **sigma_x**（推断，旁证见下） |
| +0x18 | `4.1012193308819755` | double | **sigma_y**（推断） |
| +0x20 | `7` | int32 | **核半窗**（推断） |
| +0x28 | `0` | 指针 | 空 |
| +0x30 | `1` | int32 | 推断：枚举 / 布尔（"相位"） |

即边缘前处理的默认参数为 `{20.0, 1.0, sigma_x = 2.0506096654409878, sigma_y = 4.1012193308819755,
核半窗 = 7, 相位 = 1}`。这些**数值本身是实测**（`raw/managed-native-constants.txt` 里逐条可查，
`find` 可精确复现），**字段语义是推断**，两条旁证：

1. `4.1012193308819755` 是 `2.0506096654409878` 的**精确 2 倍**（IEEE-754 乘 2 只改指数位，零误差），
   与仓库 `GaborEdgeDetector.DefaultSensitivityParameter = 2.0` 且 `SigmaY = SigmaX * 2` 完全吻合。
2. `7` 恰好等于仓库的半窗公式 `ceil(sqrt(2) * max(sigma_x, sigma_y) + 0.95) = ceil(6.75) = 7`。

---

## 5. 复现步骤

前置：.NET 9 SDK；VisionPro 安装在同一路径。**只构建取证工具，不要构建主项目。**

```powershell
cd tools/cognex-meta
dotnet build -c Release
```

下面所有命令都在 `tools/cognex-meta/` 目录内执行（与 `tools/cognex-meta/README.md` 的复现段一致），
所以 `$raw` 是相对该目录的路径。不想切目录时，等价写法是
`dotnet run --project tools/cognex-meta -c Release -- <模式> ...`，此时把各路径换成仓库根相对路径
（`raw/native-strings-all.txt` 的文件头就是用这种写法自我记录的）。

变量：

```powershell
$vp  = "C:\Program Files\Cognex\VisionPro"
$dll = "$vp\ReferencedAssemblies\Cognex.VisionPro.PMAlign.dll"   # 托管程序集（混合模式）
$xml = "$vp\ReferencedAssemblies\Cognex.VisionPro.PMAlign.xml"   # 可选：厂商 XML 文档
$nat = "$vp\bin\cogxpma.dll"                                     # 原生核心（WIBU 加壳）
$raw = "..\..\docs\evidence\raw"                                 # 输出目录（当前在 tools/cognex-meta）
```

按顺序重跑（每条都注明参数含义）：

```powershell
# 0) 体检：类型/枚举/成员数量 + 重点类型清单 + 文档覆盖率。不写文件。
dotnet run -c Release -- stats $dll

# 1) 全量元数据导出 -> raw/pmalign-meta.json（443 个类型）。
#    第 3 个位置参数是可选 XML 文档，用来把 CogDoc* 文本对齐到成员。
dotnet run -c Release -- meta $dll "$raw\pmalign-meta.json" $xml

# 2) 从 dump 渲染决策子集 -> api-surface.md（不重新读 dll，纯渲染）。
dotnet run -c Release -- core "$raw\pmalign-meta.json" ..\..\docs\evidence\api-surface.md

# 3) 从 dump 渲染未删减参数表 -> raw/pmalign-params.generated.md。
dotnet run -c Release -- paramtable "$raw\pmalign-meta.json" "$raw\pmalign-params.generated.md"

# 4) 原生字符串 + 导出/导入 -> raw/native-strings-all.txt、native-strings-keywords.md、native-export-import.txt。
#    --min 6 = 最短 6 字符（工具的默认值就是 6，显式写出是为了让重现命令自解释）。
dotnet run -c Release -- strings $nat $raw --min 6

# 5) 指定节的"疑似 double"分组扫描 -> raw/native-doubles.txt、native-doubles-reference.txt。
#    默认节是 .rdata，但 cogxpma.dll 没有 .rdata，所以必须显式 --section .rsrc。
dotnet run -c Release -- doubles $nat $raw --section .rsrc

# 6) 位型精确搜索：报告每个值命中的 RVA / 文件偏移 / 所属节。
dotnet run -c Release -- find $nat 2.0506096654409878 4.1012193308819755 0.3 4
dotnet run -c Release -- find $dll 2.0506096654409878 4.1012193308819755

# 7) 混合模式原生 .text 的 mov-imm64 double 常量 -> raw/managed-native-constants.txt（800 条）。
dotnet run -c Release -- nconst $dll "$raw\managed-native-constants.txt"

# 8) 某个 RVA 落在哪个托管方法体 / 字段 RVA blob 里。
dotnet run -c Release -- where $dll 0x00EAF2C0
```

关于第 8 步：`0x00EAF2C0` 是 **leaf 原生函数**，没有 `.pdata` unwind 记录，`where` 会如实报空
--这是预期结果，不是工具失败；该函数的归属证据来自第 7 步的 `.pdata` 扫描。

工具的真实用法以 `tools/cognex-meta/README.md` 和 `src/Program.cs` 的 `Usage()` 为准；
模式名、位置参数顺序、`--min` / `--section` / `--focus` / `--level` 的语义都在那里。

体积验收（**回到仓库根**后执行；只读命令，不改仓库）：

```powershell
git check-ignore -v docs/evidence/raw/pmalign-meta.json docs/evidence/raw/native-strings-all.txt `
                       docs/evidence/raw/native-strings-keywords.md docs/evidence/raw/pmalign-params.generated.md
git status --ignored --porcelain docs/evidence
```

---

## 6. 厂商商标与许可

Cognex、VisionPro、PatMax、PMAlign、PatQuick、PatFlex 等名称为 Cognex Corporation 或其各自所有者的
商标或注册商标。本目录**不包含** Cognex 的源代码、二进制、资源文件或反编译产物：它只描述本机安装的
专有软件**外部可观测的事实**（类型与成员签名、常量与默认值、枚举取值、明文资源字符串），用于工程对照
与互操作性讨论。本仓库与 Cognex Corporation 无隶属、赞助或背书关系。若你是权利人并认为此处某项内容
不适当，请开 issue，维护者会移除对应条目。

---

## 7. 维护约定

- 新增证据：优先写成结论性 `.md`；原始片段控制在 100 KB 以内再入库。
- 大体积、可由工具重跑的东西一律加进根 `.gitignore`，并在 §2 表格里补一行说明"如何重新生成"。
- 任何数字改动必须同步更新对应的 RVA / 文件偏移，否则视为失效证据。
- `raw/` 里的小文件是**离线可核查的最低限度**，不要为了整洁删掉它们。
- 所有 `.md` 为无 BOM 的 UTF-8。