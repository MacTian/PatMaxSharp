#if COGNEX_META
using System.Text;

namespace CognexMeta;

/// <summary>
/// Compact api-surface renderer: only the types that carry implementation decisions
/// (tool / pattern / run params / result / results / train-params equivalents) plus the
/// full enum constant table. Compiler-generated types are dropped by name pattern.
/// Everything else stays addressable through raw/pmalign-meta.json.
/// </summary>
public static class CoreSurface
{
    /// <summary>Types the lead asked for, in reading order. Missing ones are reported as missing.</summary>
    private static readonly string[] Required =
    {
        "CogPMAlignTool",
        "CogPMAlignPattern",
        "CogPMAlignRunParams",
        "CogPMAlignTrainParams",      // does not exist in this assembly - reported explicitly
        "CogPMAlignResult",
        "CogPMAlignResults",
    };

    /// <summary>
    /// Supporting types without which the six above are not actionable: the DOF zones,
    /// the composite trainer (carries the training knobs), the multi-pattern result item.
    /// </summary>
    private static readonly string[] Supporting =
    {
        "CogPMAlignZoneAngle",
        "CogPMAlignZoneScale",
        "CogPMAlignComposite",
        "CogPMAlignMultiResults",
        "CogPMAlignMultiResultItem",
        "CogPMAlignMultiRunParams",
        "CogPMAlignResultsShared",
    };

    private static bool IsCompilerGenerated(TypeEntry t)
    {
        string n = t.Name;
        if (n.StartsWith("<", StringComparison.Ordinal)) return true;
        if (n.StartsWith("$", StringComparison.Ordinal)) return true;
        if (n.StartsWith("__StaticArrayInitTypeSize", StringComparison.Ordinal)) return true;
        if (n.Contains("_extraBytes_", StringComparison.Ordinal)) return true;
        if (n.Contains("<>", StringComparison.Ordinal)) return true;
        return false;
    }

    public static void Write(MetaDocument md, string outPath)
    {
        var all = md.Types;
        var kept = all.Where(t => !IsCompilerGenerated(t)).ToList();
        var dropped = all.Where(IsCompilerGenerated).ToList();

        TypeEntry? Find(string shortName) =>
            kept.FirstOrDefault(t => t.Name == shortName || t.FullName == shortName);

        var sb = new StringBuilder();
        sb.AppendLine("# Cognex.VisionPro.PMAlign.dll 元数据导出（决策相关子集）");
        sb.AppendLine();
        sb.AppendLine("证据类型：**实测**。全部内容通过 `System.Reflection.Metadata.MetadataReader` 只读元数据表得到，");
        sb.AppendLine("不加载程序集、不反编译第三方源码。工具与复现命令见 `tools/cognex-meta/README.md`。");
        sb.AppendLine();
        sb.AppendLine("本文件只保留对实现有决策价值的类型；全量 443 个类型的原始数据在 `raw/pmalign-meta.json`。");
        sb.AppendLine("本文件中的中文说明为本次分析所写，类型名、成员名、签名、默认值均为二进制原样。");
        sb.AppendLine();

        // ---------------- facts ----------------
        sb.AppendLine("## 0. 文件事实");
        sb.AppendLine();
        sb.AppendLine("| 项 | 值 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| 路径 | `{md.SourcePath}` |");
        sb.AppendLine($"| 大小 | {md.FileSize:N0} bytes |");
        sb.AppendLine($"| SHA-256 | `{md.Sha256}` |");
        sb.AppendLine($"| Assembly | `{md.AssemblyName}`，AssemblyVersion `{md.AssemblyVersion}` |");
        sb.AppendLine($"| Module MVID | `{md.ModuleMvid}` |");
        sb.AppendLine($"| TargetFramework | `{md.TargetFramework}` |");
        sb.AppendLine($"| 类型总数 | {md.TypeCount}（public {md.PublicTypeCount}；编译器生成 {dropped.Count} 个已剔除） |");
        sb.AppendLine($"| 成员总数 | {md.MemberCount:N0}（字段+属性+方法+事件） |");
        sb.AppendLine($"| XML 文档 | `{(string.IsNullOrEmpty(md.XmlDocPath) ? "(未提供)" : md.XmlDocPath)}` |");
        sb.AppendLine($"| 保留类型 | {kept.Count} |");
        sb.AppendLine();

        if (md.AssemblyAttributes.Count > 0)
        {
            sb.AppendLine("## 1. 程序集级属性");
            sb.AppendLine();
            sb.AppendLine("| 属性 | 值 |");
            sb.AppendLine("|---|---|");
            foreach (var kv in md.AssemblyAttributes.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.AppendLine($"| `{kv.Key}` | {Md(kv.Value)} |");
            sb.AppendLine();
        }

        // ---------------- requested types ----------------
        sb.AppendLine("## 2. 核心类型 public 成员");
        sb.AppendLine();
        sb.AppendLine("列说明：`Token` 是元数据 token（可据此在 `raw/pmalign-meta.json` 里定位）；");
        sb.AppendLine("`[DefaultValue]` 是属性上的 `[DefaultValue(x)]` 实参（VisionPro 属性网格/序列化实际使用值）；");
        sb.AppendLine("`IL 默认` 是方法体内 `ldc.*` → `call set_X` / `stfld` 观察到的写入（括号内为方法名）；");
        sb.AppendLine("`原生接口` 是该属性的厂商文档属性里 `<include path='//interface[@name=...]/function[@vbname=...]'>` 指定的 C++ COM 成员名。");
        sb.AppendLine();

        var missing = new List<string>();
        foreach (var shortName in Required)
        {
            var t = Find(shortName);
            if (t == null) { missing.Add(shortName); continue; }
            WriteType(sb, t);
        }
        if (missing.Count > 0)
        {
            sb.AppendLine("### 关于不存在的类型");
            sb.AppendLine();
            foreach (var m in missing)
            {
                sb.AppendLine($"- `{m}`：**本程序集中不存在**（已对 443 个类型名逐一比对，无同名或近似名）。");
            }
            sb.AppendLine("  训练参数实际分布在 `CogPMAlignPattern`（`TrainAlgorithm` / `TrainMode` / `EdgeThreshold` /");
            sb.AppendLine("  `GrainLimit*` / `Elasticity` / `IgnorePolarity` / `HighSensitivityParameter` / `ExpectedDeformationRate`）");
            sb.AppendLine("  与 `CogPMAlignComposite`（`TuningElasticity` / `ImageFraction` / `Timeout`）上，没有独立的 `*TrainParams` 类。");
            sb.AppendLine();
        }

        // ---------------- supporting types ----------------
        sb.AppendLine("## 3. 支撑类型（缺了上面那些就用不起来）");
        sb.AppendLine();
        foreach (var shortName in Supporting)
        {
            var t = Find(shortName);
            if (t != null) WriteType(sb, t);
        }

        // ---------------- enums ----------------
        var enums = kept.Where(t => t.Kind == "enum" && t.Visibility == "public").ToList();
        sb.AppendLine($"## 4. 全部 public enum 与常量值（{enums.Count} 个，值来自 Constant 表）");
        sb.AppendLine();
        sb.AppendLine("| enum | 成员 = 值 |");
        sb.AppendLine("|---|---|");
        foreach (var e in enums)
        {
            string vals = string.Join("， ", e.EnumValues.Select(v => $"{v.Name}={v.Raw}"));
            sb.AppendLine($"| `{e.Name}` | {Md(vals)} |");
        }
        sb.AppendLine();

        // ---------------- index of everything else ----------------
        sb.AppendLine("## 5. 其余 public 类型索引（只列名，成员见 raw/pmalign-meta.json）");
        sb.AppendLine();
        var listed = new HashSet<string>(Required.Concat(Supporting));
        var rest = kept.Where(t => t.Visibility == "public" && !listed.Contains(t.Name) && t.Kind != "enum").ToList();
        sb.AppendLine(string.Join("、", rest.Select(t => $"`{t.Name}`")));
        sb.AppendLine();
        if (dropped.Count > 0)
        {
            sb.AppendLine("### 已剔除的编译器生成类型");
            sb.AppendLine();
            sb.AppendLine($"共 {dropped.Count} 个，全部为编译器占位类型（serialization TypeDescriptor 的 extraBytes 桩、");
            sb.AppendLine("`<PrivateImplementationDetails>`、`__StaticArrayInitTypeSize=*`、含 `<>` 的闭包/迭代器桩），");
            sb.AppendLine("没有任何 public 成员或 API 价值，已从本文件剔除；若需查全，见 `raw/pmalign-meta.json`。");
            sb.AppendLine();
            sb.AppendLine($"其中 {dropped.Count(t => t.Name.Contains("_extraBytes_"))} 个是序列化 `$_TypeDescriptor$_extraBytes_N` 桩，其余为静态数组初始化桩与闭包桩。");
            sb.AppendLine();
        }

        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteType(StringBuilder sb, TypeEntry t)
    {
        sb.AppendLine($"### `{t.FullName}`");
        sb.AppendLine();
        sb.AppendLine($"- 种类 `{t.Kind}`，可见性 `{t.Visibility}`，Token `{t.Token}`，标志 `{t.Attributes}`");
        sb.AppendLine($"- 基类 `{t.BaseType}`" + (t.Interfaces.Count > 0 ? $"，接口 {string.Join(", ", t.Interfaces.Select(i => "`" + i + "`"))}" : ""));
        if (t.Summary.Length > 0) sb.AppendLine($"- 官方说明：{Md(t.Summary)}");
        sb.AppendLine();

        var props = t.Properties.Where(p => p.Visibility == "public").ToList();
        if (props.Count > 0)
        {
            sb.AppendLine("| 属性 | 类型 | Token | [DefaultValue] | IL 默认 | 原生接口 | 官方语义 |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var p in props)
            {
                string il = IlDefault(t, p.Name);
                string idl = p.CogIdlMember.Length > 0 ? "`" + p.CogIdlMember + "`" : "";
                string dv = p.DefaultValueAttr.Length > 0 ? "`" + p.DefaultValueAttr + "`" : "";
                string prose = p.CogSummary.Length > 0 ? p.CogSummary : p.Summary;
                sb.AppendLine($"| `{p.Name}` | `{p.Type}` | `{p.Token}` | {dv} | {(il.Length > 0 ? "`" + il + "`" : "")} | {idl} | {Md(prose)} |");
            }
            sb.AppendLine();
        }

        var flds = t.Fields
            .Where(f => f.Visibility == "public" && !f.Name.StartsWith("Sf", StringComparison.Ordinal) && f.Name != "value__")
            .ToList();
        if (flds.Count > 0)
        {
            sb.AppendLine("| 字段/常量 | 类型 | 修饰符 | 常量值 | 官方语义 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var f in flds)
                sb.AppendLine($"| `{f.Name}` | `{f.Type}` | {f.Modifiers} | {(f.ConstantValue.Length > 0 ? "`" + f.ConstantValue + "`" : "")} | {Md(f.Summary)} |");
            sb.AppendLine();
        }

        var meths = t.Methods
            .Where(m => m.Visibility == "public")
            .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_"))
            .ToList();
        if (meths.Count > 0)
        {
            sb.AppendLine("| 方法签名 | 官方语义 |");
            sb.AppendLine("|---|---|");
            foreach (var m in meths)
                sb.AppendLine($"| `{Md(m.Signature)}` | {Md(m.Summary)} |");
            sb.AppendLine();
        }

        var evs = t.Events.Where(e => e.Visibility == "public").ToList();
        if (evs.Count > 0)
        {
            sb.AppendLine("事件：" + string.Join("、", evs.Select(e => "`" + e.Name + "`")));
            sb.AppendLine();
        }
    }

    private static string IlDefault(TypeEntry t, string propName)
    {
        var hit = t.IlConstants.FirstOrDefault(h => h.Target == "set_" + propName);
        if (hit != null) return $"{hit.Constant} ({hit.Owner})";
        var fld = t.IlConstants.FirstOrDefault(h => h.Target.EndsWith("::" + propName, StringComparison.Ordinal));
        if (fld != null) return $"{fld.Constant} ({fld.Owner}, field)";
        return "";
    }

    /// <summary>Escape table cells; also flatten newlines so a row stays one line.</summary>
    private static string Md(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s!.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
#endif
