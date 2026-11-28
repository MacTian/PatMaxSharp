# tools/cognex-meta - Cognex VisionPro 程序集只读取证工具

一个单文件可读、无第三方依赖的 .NET 9 控制台程序，用来从 **Cognex VisionPro 的托管程序集** 与
**原生 PE** 里静态提取事实：元数据签名、`[DefaultValue]` 属性、厂商文档属性、IL 常量模式、
原生字符串、导出/导入表、以及 x64 代码里的 `mov rax, imm64` 数值常量。

**只读**：所有输入文件都以 `File.ReadAllBytes` / `FileStream` 打只读打开，不加载程序集、不解密、不写回。

## 为什么不用 `Assembly.LoadFrom`

目标程序集是 .NET Framework 4.8 的 `Cognex.VisionPro.PMAlign`，依赖 VisionPro 的一堆兄弟程序集。
`Assembly.LoadFrom` 会触发依赖解析（并可能在 .NET 9 上失败），而 `System.Reflection.Metadata.MetadataReader`
直接读 ECMA-335 表，不需要任何依赖、也不会执行被分析程序集的代码。本工具只用后者。

## 与主项目互不干扰

仓库根 `ShapeAnchor.csproj` 使用 SDK 默认 `**/*.cs` 通配，会扫到本目录。两道保险：

1. 本目录所有 `.cs` 整体包在 `#if COGNEX_META ... #endif` 里（未定义该符号时编译结果为空文件）；
2. 根 `csproj` 已 `Compile Remove="tools\**"` / `<None Remove="tools\**"`，本目录完全不参与主项目编译。

本目录自带 `EnableDefaultCompileItems=false` + 显式 `<Compile Include="src\**\*.cs" />` 与 `net9.0`，
所以可以独立 `dotnet build` / `dotnet run`，不影响 `net8.0` 的主项目。

## 构建与运行

```powershell
cd tools/cognex-meta
dotnet build -c Release
```

之后有两种等价用法（`dotnet run -- <模式> ...`，或直接调 `.\bin\Release\net9.0\cognex-meta.exe <模式> ...`）。

### 模式一览

| 模式 | 作用 |
|---|---|
| `stats <dll>` | 快速体检：类型/枚举/接口/委托数量、成员总数、匹配重点正则的类型清单 |
| `meta <dll> <out.json> [doc.xml]` | **全量导出**：类型、成员签名、枚举常量、`[DefaultValue]`、`CogDoc*` 原文、IL 常量命中 → JSON |
| `core <out.json> <out.md>` | 渲染**决策相关子集**：核心类型 public 成员 + 全部 enum 常量值（剔除编译器生成类型） |
| `md <out.json> <out.md> [--focus RE] [--level full\|compact]` | 渲染全量 api-surface（按正则筛重点类型） |
| `paramtable <out.json> <out.md> [--focus RE]` | 渲染参数表：默认值(attr) / 默认值(IL) / 文本默认值 / 合法范围 / 原生接口名 / 官方语义 |
| `il <dll>` | 把 IL 常量命中打到 stdout（排查用） |
| `find <file> <v1> [v2 …]` | IEEE-754 8 字节位型精确搜索，报告 RVA/文件偏移/所属节 |
| `where <dll> <rva…>` | 某个 RVA 落在哪个托管方法体（IL 偏移）/ 哪个字段 RVA blob 里 |
| `nconst <dll> [out.txt]` | 扫明文 x64 `mov r64, imm64`，还原 double 常量，并按 `.pdata` 归属函数 |
| `strings <native.dll> <outdir> [--min N]` | ASCII + UTF-16LE 字符串（含 RVA/偏移/节/编码），导出/导入表，关键词证据 + hexdump |
| `doubles <native.dll> <outdir> [--section .rdata]` | 指定节的"疑似 double 常量"分组扫描 + 参考值精确搜索 |

## 复现本次文档（按顺序）

```powershell
$vp  = "C:\Program Files\Cognex\VisionPro"
$dll = "$vp\ReferencedAssemblies\Cognex.VisionPro.PMAlign.dll"
$xml = "$vp\ReferencedAssemblies\Cognex.VisionPro.PMAlign.xml"
$nat = "$vp\bin\cogxpma.dll"
$raw = "..\..\docs\evidence\raw"

dotnet run -c Release -- stats $dll
dotnet run -c Release -- meta  $dll "$raw\pmalign-meta.json" $xml
dotnet run -c Release -- core  "$raw\pmalign-meta.json" ..\..\docs\evidence\api-surface.md
dotnet run -c Release -- paramtable "$raw\pmalign-meta.json" "$raw\pmalign-params.generated.md"

dotnet run -c Release -- strings $nat $raw --min 6
dotnet run -c Release -- doubles $nat $raw --section .rsrc
dotnet run -c Release -- find    $nat 2.0506096654409878 4.1012193308819755 0.3 4
dotnet run -c Release -- find    $dll 2.0506096654409878 4.1012193308819755
dotnet run -c Release -- nconst  $dll "$raw\managed-native-constants.txt"
dotnet run -c Release -- where   $dll 0x00EAF2C0
```

输出文件（本次分析产生的）：

| 文件 | 内容 |
|---|---|
| `docs/evidence/api-surface.md` | 决策相关类型的 public 成员 + 全部 enum 常量值 |
| `docs/evidence/params-reference.md` | 参数语义/默认值表 + 与现有实现的三类比对 |
| `docs/evidence/native-strings.md` | 原生字符串/导出表/数值常量取证（含 RVA + hexdump） |
| `docs/evidence/raw/pmalign-meta.json` | 443 个类型的全量元数据（1.3 MB） |
| `docs/evidence/raw/pmalign-params.generated.md` | 未删减的机器生成参数表 |
| `docs/evidence/raw/native-strings-all.txt` | 70,115 条原生字符串（**已截断**到前 12,000 条以控制体积） |
| `docs/evidence/raw/native-strings-keywords.md` | 799 条关键词命中，每条带 hexdump |
| `docs/evidence/raw/native-export-import.txt` | 29 个导出 + 40 个导入模块 |
| `docs/evidence/raw/native-doubles.txt` | `.rsrc` 疑似 double 分组（含 hexdump） |
| `docs/evidence/raw/native-doubles-reference.txt` | 参考值位型搜索命中的上下文 |
| `docs/evidence/raw/managed-native-constants.txt` | 800 条混合模式原生 double 常量 + `.pdata` 函数归属 |

## 实现要点（便于复核正确性）

- **签名文本化**：`SigProvider` 实现 `ISignatureTypeProvider<string, object?>` + `ICustomAttributeTypeProvider<string>`，
  把签名 blob 直接渲染成 C# 风格文本；泛型/数组/指针/`modopt`（`CogPMAlignResult.ModelID` 的 `IsLong`）都覆盖。
- **`[DefaultValue]` 解码**：`CustomAttribute.DecodeValue(provider)`；失败时回退为原始 blob 的 hex，不静默丢数据。
- **厂商文档属性**：`CogDocSummary` / `CogDocRemarks` / `CogDocValue` 的字面文本保留；
  形如 `<include path='//interface[@name="ICogPMAlignRunParams"]/function[@vbname="EdgeThreshold"]'>` 的
  元素不展开（`CognexPatmaxAlign.merged.xml` 未随产品发布），但把 → `接口::成员` 提取为 `CogIdlMember`（原生契约证据）。
- **IL 常量**：自带 ECMA-335 单/双字节操作码长度表，走方法体找 `ldc.*` → `call set_X` / `stfld` / `stsfld`。
  进攻性解码遇到未知操作码就中止该函数体并保留已得结果（不做猜测填充）。
- **原生字符串**：按节扫描，`RVA = 节.VirtualAddress + (偏移 − 节.PointerToRawData)`。
- **double 扫描**：8 字节对齐，要求有限、非零、`round(v,10)` 后相对误差 < 1e-9，且**排除 8 字节全可打印 ASCII**
  （否则 `"30BD-3940-11D3-ABFD"` 这类文本会被读成 `956834.096078`）。
- **`nconst`**：只认 `REX.W` 前缀的 `B8+r`（`mov r64, imm64`）；`.pdata` 用二分查找定位所属 `RUNTIME_FUNCTION`；
  leaf 函数（无 unwind 记录）如实报为空，不猜。

## 已知局限

- 结构化异常/`try` 块内的操作码流不会被特殊处理（强行线性解码），只影响极少函数体，且不会污染已收集的常量。
- `where` 只覆盖 IL 方法体与字段 RVA blob；混合模式的原生代码段需用 `nconst` 的 `.pdata` 归属。
- 不做反编译、不做控制流重建--本工具的目标是**可复核的事实**，不是可读源码。
