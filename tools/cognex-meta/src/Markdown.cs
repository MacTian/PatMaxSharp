#if COGNEX_META
using System.Text;
using System.Text.RegularExpressions;

namespace CognexMeta;

public static class Markdown
{
    public const string DefaultFocus = @"PMAlign|Params$|Result$|Results$|Constants?$|Consts$|TrainAlgorithm|Pattern$|Region$|Pose$|Score|Edge|Grain|Contrast";

    public static Regex FocusRx(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsFocus(string fullName, Regex rx) => rx.IsMatch(fullName);

    private static readonly HashSet<string> NumericTypes = new(StringComparer.Ordinal)
    {
        "int", "uint", "short", "ushort", "long", "ulong", "byte", "sbyte",
        "double", "float", "decimal", "bool"
    };

    public static bool IsNumericOrEnum(MemberEntry m)
        => NumericTypes.Contains(m.Type?.Trim() ?? "")
           || (m.Type?.Contains("Enum") ?? false)
           || (m.DefaultValueAttr?.Length > 0)
           || (m.ConstantValue?.Length > 0);

    // ==================================================================
    public static void WriteApiSurface(MetaDocument md, string outPath, string focusPattern, string level)
    {
        var rx = FocusRx(focusPattern);
        var sb = new StringBuilder();

        sb.AppendLine("# Cognex.VisionPro.PMAlign.dll — 托管元数据全量导出");
        sb.AppendLine();
        sb.AppendLine("> 证据类型：**实测观察到**（直接读取 ECMA-335 元数据表，非反编译推断，非 Assembly.LoadFrom）。");
        sb.AppendLine("> 导出工具：`tools/cognex-meta`（`System.Reflection.Metadata.MetadataReader`，只读）。");
        sb.AppendLine("> 复现：`dotnet run --project tools/cognex-meta -- meta \"<dll>\" out.json \"<doc.xml>\"`");
        sb.AppendLine("> 本文件由工具生成，未手工改写结论；手工分析见 `params-reference.md`。");
        sb.AppendLine();

        sb.AppendLine("## 0. 文件事实");
        sb.AppendLine();
        sb.AppendLine("| 项 | 值 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine($"| 路径 | `{md.SourcePath}` |");
        sb.AppendLine($"| 大小 (bytes) | {md.FileSize:N0} |");
        sb.AppendLine($"| SHA-256 | `{md.Sha256}` |");
        sb.AppendLine($"| Assembly | `{md.AssemblyName}` |");
        sb.AppendLine($"| AssemblyVersion | `{md.AssemblyVersion}` |");
        sb.AppendLine($"| Module MVID | `{md.ModuleMvid}` |");
        sb.AppendLine($"| TargetFramework | `{md.TargetFramework}` |");
        sb.AppendLine($"| XML 文档 | `{(string.IsNullOrEmpty(md.XmlDocPath) ? "(未提供)" : md.XmlDocPath)}` |");
        sb.AppendLine($"| 类型总数 | {md.TypeCount}（public {md.PublicTypeCount}） |");
        sb.AppendLine($"| 成员总数（字段+属性+方法+事件） | {md.MemberCount:N0} |");
        sb.AppendLine();

        if (md.AssemblyAttributes.Count > 0)
        {
            sb.AppendLine("## 1. 程序集级自定义属性");
            sb.AppendLine();
            sb.AppendLine("| 属性 | 值 |");
            sb.AppendLine("|---|---|");
            foreach (var kv in md.AssemblyAttributes.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.AppendLine($"| `{kv.Key}` | {Escape(kv.Value)} |");
            sb.AppendLine();
        }

        // ---- type index -------------------------------------------------
        sb.AppendLine("## 2. 类型索引");
        sb.AppendLine();
        sb.AppendLine($"共 {md.Types.Count} 个类型（含 internal / 私有嵌套）。`种类` 由元数据标志位推出；");
        sb.AppendLine("`接口` 只列直接实现；`#F/#P/#M` 为字段/属性/方法数量。");
        sb.AppendLine();
        sb.AppendLine("| 类型 | Token | 种类 | 可见性 | 基类 | 接口 | #F/#P/#M |");
        sb.AppendLine("|---|---|---|---|---|---|---|");
        foreach (var t in md.Types)
        {
            string baseShort = Short(t.BaseType);
            string ifaces = t.Interfaces.Count == 0 ? "" : Short(string.Join(", ", t.Interfaces));
            sb.AppendLine($"| `{t.FullName}` | `{t.Token}` | {t.Kind} | {t.Visibility} | {(baseShort.Length > 0 ? "`" + baseShort + "`" : "")} | {ifaces} | {t.Fields.Count}/{t.Properties.Count}/{t.Methods.Count} |");
        }
        sb.AppendLine();

        // ---- enums ------------------------------------------------------
        var enums = md.Types.Where(t => t.Kind == "enum").ToList();
        sb.AppendLine("## 3. 全部 enum 类型与成员常量值");
        sb.AppendLine();
        sb.AppendLine($"共 {enums.Count} 个 enum。常量值来自元数据 Constant 表（`Field.DefaultValue`），即编译期真值。");
        sb.AppendLine();
        foreach (var e in enums)
        {
            sb.AppendLine($"<details><summary><code>{e.FullName}</code> — {e.EnumValues.Count} 个成员，底层/可见性 {e.Visibility}</summary>");
            sb.AppendLine();
            if (e.Summary.Length > 0) { sb.AppendLine($"> {Escape(e.Summary)}"); sb.AppendLine(); }
            sb.AppendLine("| 成员 | 值 | 说明 |");
            sb.AppendLine("|---|---|---|");
            foreach (var v in e.EnumValues)
                sb.AppendLine($"| `{v.Name}` | `{v.Raw}` | {Escape(v.Summary)} |");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        // ---- focus types ------------------------------------------------
        var focus = md.Types.Where(t => t.Visibility == "public" && IsFocus(t.FullName, rx)).ToList();
        sb.AppendLine($"## 4. 重点类型成员详解（public 且匹配 `{focusPattern}`，共 {focus.Count} 个）");
        sb.AppendLine();
        sb.AppendLine("字段/属性/方法签名均为元数据签名 blob 的文本化结果（`DecodeSignature`），类型名未做程序集限定。");
        sb.AppendLine("`[DefaultValue(x)]` 列为自定义属性实参解码值；`const` 列为 Constant 表值；");
        sb.AppendLine("`IL 常量` 段为方法体内 `ldc.*` → `call set_X` / `stfld` 模式命中，是**属性默认值**的直接证据。");
        sb.AppendLine();
        foreach (var t in focus)
        {
            WriteTypeDetail(sb, t, full: level != "compact");
        }

        // ---- remaining public types, compact ---------------------------
        var others = md.Types.Where(t => t.Visibility == "public" && !IsFocus(t.FullName, rx)).ToList();
        sb.AppendLine($"## 5. 其余 public 类型（非重点，紧凑列出，共 {others.Count} 个）");
        sb.AppendLine();
        foreach (var t in others)
        {
            var bits = new List<string>();
            if (t.Fields.Count > 0) bits.Add("F: " + string.Join(", ", t.Fields.Where(f => f.Visibility == "public").Select(f => f.Name)));
            if (t.Properties.Count > 0) bits.Add("P: " + string.Join(", ", t.Properties.Where(p => p.Visibility == "public").Select(p => p.Name)));
            if (t.Methods.Count > 0)
            {
                var ms = t.Methods.Where(m => m.Visibility == "public" && !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_"))
                                   .Select(m => m.Name).Distinct().Take(40).ToList();
                if (ms.Count > 0) bits.Add("M: " + string.Join(", ", ms));
            }
            sb.AppendLine($"- `{t.FullName}` ({t.Kind}, base `{Short(t.BaseType)}`)");
            foreach (var b in bits) sb.AppendLine($"  - {Escape(b)}");
        }
        sb.AppendLine();

        // ---- IL constant index -----------------------------------------
        var ilTypes = md.Types.Where(t => t.IlConstants.Count > 0 && t.Visibility == "public").ToList();
        sb.AppendLine($"## 6. IL 常量 → 成员赋值 全量索引（{ilTypes.Count} 个 public 类型有命中）");
        sb.AppendLine();
        sb.AppendLine("模式说明：C# 把 `this.X = 3;` 编译成 `ldc.i4.3; call set_X`，把字段赋值编译成 `ldc.*; stfld`。");
        sb.AppendLine("下表因此是「构造/初始化方法里给成员赋了哪些字面量」的机器证据，**不是**反编译源码。");
        sb.AppendLine();
        foreach (var t in ilTypes)
        {
            sb.AppendLine($"<details><summary><code>{t.FullName}</code> — {t.IlConstants.Count} 条</summary>");
            sb.AppendLine();
            sb.AppendLine("| 写入方法 | 目标 | 方式 | 常量 | 类型 | IL off |");
            sb.AppendLine("|---|---|---|---|---|---|");
            foreach (var h in t.IlConstants.Where(h => h.ConstantType is "float64" or "float32" or "int32" or "int64"))
                sb.AppendLine($"| `{h.Owner}` | `{h.Target}` | {h.Kind} | `{h.Constant}` | {h.ConstantType} | {h.ILOffset} |");
            sb.AppendLine();
            sb.AppendLine("</details>");
            sb.AppendLine();
        }

        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
    }

    private static void WriteTypeDetail(StringBuilder sb, TypeEntry t, bool full)
    {
        sb.AppendLine($"<details><summary><code>{t.FullName}</code> — {t.Kind}，{t.Fields.Count} 字段 / {t.Properties.Count} 属性 / {t.Methods.Count} 方法</summary>");
        sb.AppendLine();
        if (t.Summary.Length > 0) { sb.AppendLine($"> **{Escape(t.Summary)}**"); sb.AppendLine(); }
        sb.AppendLine($"- Token: `{t.Token}`，可见性 `{t.Visibility}`，标志 `{t.Attributes}`");
        sb.AppendLine($"- 基类: `{t.BaseType}`");
        if (t.Interfaces.Count > 0)
            sb.AppendLine($"- 接口: {string.Join(", ", t.Interfaces.Select(i => "`" + i + "`"))}");
        if (t.AttributesApplied.Count > 0)
            sb.AppendLine($"- 类型特性: {Escape(string.Join(" ", t.AttributesApplied.Take(12)))}");
        sb.AppendLine();

        if (t.EnumValues.Count > 0)
        {
            sb.AppendLine("| 枚举成员 | 值 | 说明 |");
            sb.AppendLine("|---|---|---|");
            foreach (var v in t.EnumValues) sb.AppendLine($"| `{v.Name}` | `{v.Raw}` | {Escape(v.Summary)} |");
            sb.AppendLine();
        }

        var props = t.Properties.Where(p => p.Visibility == "public").ToList();
        if (props.Count > 0)
        {
            sb.AppendLine("**public 属性**");
            sb.AppendLine();
            sb.AppendLine("| 属性 | Token | 类型 | 访问器 | 默认值 (attr) | 默认值 (IL) | 语义 |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var p in props)
            {
                string il = IlDefaultFor(t, p.Name);
                sb.AppendLine($"| `{p.Name}` | `{p.Token}` | `{p.Type}` | {(p.Accessors.Length > 0 ? p.Accessors : "")} | {(p.DefaultValueAttr.Length > 0 ? "`" + p.DefaultValueAttr + "`" : "")} | {(il.Length > 0 ? "`" + il + "`" : "")} | {Escape(p.Summary)} |");
            }
            sb.AppendLine();
        }

        var flds = t.Fields.Where(f => f.Visibility == "public").ToList();
        if (flds.Count > 0)
        {
            sb.AppendLine("**public 字段 / 常量**");
            sb.AppendLine();
            sb.AppendLine("| 字段 | 类型 | 修饰符 | 常量值 | 语义 |");
            sb.AppendLine("|---|---|---|---|---|");
            foreach (var f in flds)
                sb.AppendLine($"| `{f.Name}` | `{f.Type}` | {f.Modifiers} | {(f.ConstantValue.Length > 0 ? "`" + f.ConstantValue + "`" : "")} | {Escape(f.Summary)} |");
            sb.AppendLine();
        }

        var meths = t.Methods.Where(m => m.Visibility == "public")
            .Where(m => !m.Name.StartsWith("get_") && !m.Name.StartsWith("set_") && !m.Name.StartsWith("add_") && !m.Name.StartsWith("remove_"))
            .ToList();
        if (meths.Count > 0 && full)
        {
            sb.AppendLine("**public 方法**");
            sb.AppendLine();
            sb.AppendLine("| 签名 | 语义 |");
            sb.AppendLine("|---|---|");
            foreach (var m in meths)
                sb.AppendLine($"| `{Escape(m.Signature)}` | {Escape(m.Summary)} |");
            sb.AppendLine();
        }
        else if (meths.Count > 0)
        {
            sb.AppendLine("**public 方法（紧凑）**: " + string.Join(", ", meths.Select(m => "`" + m.Name + "`")));
            sb.AppendLine();
        }

        var evs = t.Events.Where(e => e.Visibility == "public").ToList();
        if (evs.Count > 0)
        {
            sb.AppendLine("**public 事件**: " + string.Join(", ", evs.Select(e => "`" + e.Name + "`")));
            sb.AppendLine();
        }
        sb.AppendLine("</details>");
        sb.AppendLine();
    }

    private static string IlDefaultFor(TypeEntry t, string propName)
    {
        string target = "set_" + propName;
        var hit = t.IlConstants.FirstOrDefault(h => h.Target == target);
        if (hit != null) return $"{hit.Constant} ({hit.Owner})";
        var fld = t.IlConstants.FirstOrDefault(h => h.Target.EndsWith("::" + propName, StringComparison.Ordinal));
        if (fld != null) return $"{fld.Constant} ({fld.Owner}, field)";
        return "";
    }

    // ==================================================================
    // Regexes over the vendor's own prose. These are the only sources for "declared
    // default" and "legal range" that exist outside the native binary, so they are
    // extracted verbatim rather than paraphrased.
    private static readonly Regex DefaultRx = new(
        @"default(?: value)? (?:is|of|to|=) (?<v>-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex RangeRx = new(
        @"range (?:is |of )?(?:from )?(?<lo>-?\d+(?:\.\d+)?) to (?<hi>-?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TautologyRx = new(
        @"^Gets or sets (?:the )?[A-Za-z0-9_]+ property\.?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string DeclaredDefault(string text)
    {
        var m = DefaultRx.Match(text);
        return m.Success ? m.Groups["v"].Value : "";
    }

    private static string DeclaredRange(string text)
    {
        var m = RangeRx.Match(text);
        if (m.Success) return "[" + m.Groups["lo"].Value + ", " + m.Groups["hi"].Value + "]";
        int i = text.IndexOf("range", StringComparison.OrdinalIgnoreCase);
        if (i >= 0)
        {
            int j = text.IndexOf('[', i);
            int k = j >= 0 ? text.IndexOf(']', j) : -1;
            if (j >= 0 && k > j) return text.Substring(j, k - j + 1);
        }
        return "";
    }

    private static string Prose(MemberEntry m)
    {
        string s = m.CogSummary.Length > 0 ? m.CogSummary : m.Summary;
        if (s.Length == 0) s = m.CogValue;
        return TautologyRx.IsMatch(s ?? "") ? "" : (s ?? "");
    }

    private static string EnumsSuffix(MetaDocument md, string typeName, string value)
    {
        var t = md.Types.FirstOrDefault(x => x.Kind == "enum" &&
            (x.FullName == typeName || x.FullName.EndsWith("." + typeName, StringComparison.Ordinal)));
        if (t == null || !long.TryParse(value, out long n)) return "";
        var v = t.EnumValues.FirstOrDefault(e => e.Numeric == n);
        return v == null ? "" : " = `" + t.Name + "." + v.Name + "`";
    }

    public static void WriteParamTable(MetaDocument md, string outPath, string focusPattern)
    {
        var rx = FocusRx(focusPattern);
        var sb = new StringBuilder();
        sb.AppendLine("# 参数语义与默认值表（机器生成部分）");
        sb.AppendLine();
        sb.AppendLine("来源：`Cognex.VisionPro.PMAlign.dll` 元数据签名 + Constant 表 + `[DefaultValue]` 自定义属性 + 方法体 IL 常量模式，");
        sb.AppendLine("以及程序集内嵌的厂商文档属性（`CogDocSummary` / `CogDocRemarks` / `CogDocValue`）与随程序集发布的 `Cognex.VisionPro.PMAlign.xml`。");
        sb.AppendLine();
        sb.AppendLine("| 列 | 含义 |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| `[DefaultValue]` | 属性上的 `[DefaultValue(x)]` 实参。VisionPro 属性网格/序列化实际使用的值，**最可信**。 |");
        sb.AppendLine("| `IL 观测` | 构造/初始化方法体内 `ldc.*` → `call set_X`(或 `stfld`) 的实参，括号内为出现该写入的方法名。 |");
        sb.AppendLine("| `文本默认值` | 从厂商 `CogDoc*` 原文里用正则抽出的 `default value is N` 句子中的 N。 |");
        sb.AppendLine("| `合法范围` | 同上，从 `range is [a, b]` / `from a to b` 抽出。 |");
        sb.AppendLine("| `原生接口` | 该属性的 `CogDoc*` 属性里 `<include path='//interface[@name=…]/function[@vbname=…]'>` 指向的 C++ COM 接口成员，即真正落到 `cogxpma.dll` 的参数名。 |");
        sb.AppendLine();

        var types = md.Types.Where(t => t.Visibility == "public" && IsFocus(t.FullName, rx)).ToList();
        foreach (var t in types)
        {
            var props = t.Properties
                .Where(p => p.Visibility == "public")
                .Select(p => (p, il: IlDefaultFor(t, p.Name), kind: "prop"))
                .ToList();
            var flds = t.Fields
                .Where(f => f.Visibility == "public" && !f.Name.StartsWith("Sf", StringComparison.Ordinal) && f.Name != "value__")
                .Select(f => (p: new MemberEntry
                {
                    Name = f.Name,
                    Type = f.Type,
                    DefaultValueAttr = f.DefaultValueAttr,
                    Summary = f.Summary,
                    CogSummary = f.CogSummary,
                    CogValue = f.CogValue,
                    CogIdlMember = f.CogIdlMember,
                    ConstantValue = f.ConstantValue
                }, il: f.ConstantValue.Length > 0 ? "const " + f.ConstantValue : "", kind: "field"))
                .ToList();
            var rows = props.Concat(flds).ToList();
            if (rows.Count == 0) continue;

            sb.AppendLine($"## `{t.FullName}`");
            sb.AppendLine();
            if (t.Summary.Length > 0) { sb.AppendLine($"> {Escape(t.Summary)}"); sb.AppendLine(); }
            sb.AppendLine("| 参数 | 类型 | [DefaultValue] | IL 观测 | 文本默认值 | 合法范围 | 原生接口 | 语义（官方原文） |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var (p, il, kind) in rows)
            {
                string prose = Prose(p);
                string corpus = prose + " " + (p.CogRemarks ?? "");
                string dd = DeclaredDefault(corpus);
                string rng = DeclaredRange(corpus);
                string attr = p.DefaultValueAttr.Length > 0
                    ? "`" + p.DefaultValueAttr + "`" + EnumsSuffix(md, p.Type, p.DefaultValueAttr)
                    : "";
                string mark = kind == "field" ? " (field)" : "";
                sb.AppendLine($"| `{p.Name}`{mark} | `{p.Type}` | {attr} | {(il.Length > 0 ? "`" + il + "`" : "")} | {(dd.Length > 0 ? "`" + dd + "`" : "")} | {(rng.Length > 0 ? "`" + rng + "`" : "")} | {(p.CogIdlMember.Length > 0 ? "`" + p.CogIdlMember + "`" : "")} | {Escape(prose)} |");
            }
            sb.AppendLine();
        }
        File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
    }

    public static string Escape(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s!.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
    }

    private static string Short(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var parts = s.Split(new[] { ".", "+" }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : s;
    }
}
#endif
