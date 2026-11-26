#if COGNEX_META
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

namespace CognexMeta;

public static class Program
{
    private static int _exit;

    public static int Main(string[] args)
    {
        if (args.Length == 0) { Usage(); return 2; }
        try
        {
            switch (args[0].ToLowerInvariant())
            {
                case "stats": return Stats(args[1]);
                case "meta": return Meta(args);
                case "md": return Md(args);
                case "core": return Core(args);
                case "paramtable": return ParamTable(args);
                case "strings": return Strings(args);
                case "doubles": return Doubles(args);
                case "find": return Find(args);
                case "where": return Where(args);
                case "nconst": return NConst(args);
                case "il": return Il(args);
                default: Usage(); return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FATAL: " + ex);
            return 1;
        }
    }

    private static void Usage()
    {
        Console.Error.WriteLine("""
        cognex-meta — read-only metadata / native-PE forensics for Cognex VisionPro assemblies

          stats      <assembly.dll>
              counts by kind, focus-type list, doc coverage. Cheap first look.

          meta       <assembly.dll> <out.json> [doc.xml] [--focus REGEX]
              full metadata dump to JSON (types, members, enum constants,
              [DefaultValue] attributes, IL constant hits).

          md         <out.json> <out.md> [--focus REGEX] [--level full|compact]
              render api-surface.md from a dump.

          paramtable <out.json> <out.md> [--focus REGEX]
              render the parameter/default-value tables.

          il         <assembly.dll> [--focus REGEX]
              print IL constant hits to stdout (inspection aid).

          strings    <native.dll> <outdir> [--min N]
              ASCII + UTF-16LE strings with RVA/offset, exports, imports,
              keyword-filtered evidence with hexdump context.

          doubles    <native.dll> <outdir> [--section .rdata]
              grouped plausible double constants with hexdump context, plus
              exact-bit-pattern search for known reference values.
        """);
    }

    private static string? Opt(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }

    private static void EnsureDir(string path)
    {
        var d = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(d)) Directory.CreateDirectory(d);
    }

    // ------------------------------------------------------------------
    private static int Stats(string dll)
    {
        using var fs = File.OpenRead(dll);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        var sig = new SigProvider(md);
        int enums = 0, ifaces = 0, delegates = 0, publicTypes = 0, types = 0, members = 0;
        var focus = new List<string>();
        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            string name = md.GetString(td.Name);
            if (name == "<Module>") continue;
            types++;
            string full = sig.TypeName(th);
            bool pub = (td.Attributes & System.Reflection.TypeAttributes.VisibilityMask) is
                System.Reflection.TypeAttributes.Public or System.Reflection.TypeAttributes.NestedPublic;
            if (pub) publicTypes++;
            if ((td.Attributes & System.Reflection.TypeAttributes.Interface) != 0) ifaces++;
            string baseName = td.BaseType.IsNil ? "" : sig.HandleName(td.BaseType);
            if (baseName == "System.Enum") enums++;
            if (baseName == "System.MulticastDelegate") delegates++;
            members += td.GetFields().Count + td.GetProperties().Count + td.GetMethods().Count;
            if (Markdown.IsFocus(full, Markdown.FocusRx(Markdown.DefaultFocus))) focus.Add(full);
        }
        Console.WriteLine($"types={types} public={publicTypes} enums={enums} interfaces={ifaces} delegates={delegates} members={members}");
        Console.WriteLine($"focus-types({focus.Count}):");
        foreach (var f in focus.OrderBy(x => x, StringComparer.Ordinal)) Console.WriteLine("  " + f);
        return _exit;
    }

    // ------------------------------------------------------------------
    private static int Meta(string[] args)
    {
        string dll = args[1];
        string outJson = args[2];
        string? xml = args.Length > 3 && !args[3].StartsWith("--") ? args[3] : null;
        var dump = new MetaDump(dll, xml);
        var doc = dump.Dump();
        EnsureDir(outJson);
        File.WriteAllText(outJson, System.Text.Json.JsonSerializer.Serialize(doc, MetaDump.JsonOpts), new UTF8Encoding(false));
        Console.WriteLine($"wrote {outJson} ({new FileInfo(outJson).Length:N0} bytes)");
        Console.WriteLine($"types={doc.Types.Count} public={doc.PublicTypeCount} members={doc.MemberCount}");
        return _exit;
    }

    private static MetaDocument Load(string json)
        => System.Text.Json.JsonSerializer.Deserialize<MetaDocument>(File.ReadAllText(json), MetaDump.JsonOpts)!;

    private static int Core(string[] args)
    {
        var doc = Load(args[1]);
        string outMd = args[2];
        EnsureDir(outMd);
        CoreSurface.Write(doc, outMd);
        Console.WriteLine($"wrote {outMd} ({new FileInfo(outMd).Length:N0} bytes)");
        return _exit;
    }

    private static int Md(string[] args)
    {
        var doc = Load(args[1]);
        string outMd = args[2];
        string focus = Opt(args, "--focus") ?? Markdown.DefaultFocus;
        string level = Opt(args, "--level") ?? "full";
        EnsureDir(outMd);
        Markdown.WriteApiSurface(doc, outMd, focus, level);
        Console.WriteLine($"wrote {outMd} ({new FileInfo(outMd).Length:N0} bytes)");
        return _exit;
    }

    private static int ParamTable(string[] args)    {
        var doc = Load(args[1]);
        string outMd = args[2];
        string focus = Opt(args, "--focus") ?? Markdown.DefaultFocus;
        EnsureDir(outMd);
        Markdown.WriteParamTable(doc, outMd, focus);
        Console.WriteLine($"wrote {outMd} ({new FileInfo(outMd).Length:N0} bytes)");
        return _exit;
    }

    private static int Il(string[] args)
    {
        var dump = new MetaDump(args[1], null);
        var doc = dump.Dump();
        foreach (var t in doc.Types.Where(t => t.IlConstants.Count > 0))
        {
            Console.WriteLine($"== {t.FullName}");
            foreach (var h in t.IlConstants)
                Console.WriteLine($"   {h.Owner,-32} {h.Kind,-16} {h.Target,-40} {h.Constant} ({h.ConstantType})");
        }
        return _exit;
    }

    // ------------------------------------------------------------------
    /// <summary>
    /// Recover double constants materialised as `mov r64, imm64` (REX.W B8+r) inside the
    /// native code of a mixed-mode assembly, and attribute each one to the function that
    /// contains it using the PE .pdata RUNTIME_FUNCTION table.
    /// cogxpma.dll is WIBU-encrypted on disk, but a C++/CLI assembly keeps its native
    /// .text in the clear, so this is where the tuned literals are actually observable.
    /// </summary>
    private static int NConst(string[] args)
    {
        string file = args[1];
        string outPath = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : "";
        var scan = new NativeScan(file);
        var pe = scan.Pe;
        var bytes = scan.Bytes;

        // .pdata: sorted function ranges
        var ranges = new List<(int begin, int end)>();
        var dir = pe.PEHeaders.PEHeader?.ExceptionTableDirectory ?? default;
        if (dir.RelativeVirtualAddress != 0)
        {
            int off = scan.RvaToOffset(dir.RelativeVirtualAddress);
            for (int o = off; o >= 0 && o + 12 <= off + dir.Size && o + 12 <= bytes.Length; o += 12)
            {
                int b = BitConverter.ToInt32(bytes, o);
                int e = BitConverter.ToInt32(bytes, o + 4);
                if (b == 0 && e == 0) break;
                if (e > b) ranges.Add((b, e));
            }
        }
        ranges.Sort((x, y) => x.begin.CompareTo(y.begin));
        string FuncOf(int rva)
        {
            int lo = 0, hi = ranges.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (ranges[mid].begin <= rva) { best = mid; lo = mid + 1; } else hi = mid - 1;
            }
            if (best >= 0 && rva <= ranges[best].end) return $"0x{ranges[best].begin:X8}..0x{ranges[best].end:X8}";
            return "";
        }

        var text = scan.Sections.FirstOrDefault(s => s.Name == ".text");
        var sb = new StringBuilder();
        sb.AppendLine("# native double constants recovered from mixed-mode .text (`48 B8+r imm64`)");
        sb.AppendLine($"# source: {file}");
        sb.AppendLine($"# main image base 0x{scan.ImageBase:X}  .text rva 0x{(text?.VirtualAddress ?? 0):X8} size 0x{(text?.SizeOfRawData ?? 0):X}");
        sb.AppendLine($"# pdata functions: {ranges.Count}");
        sb.AppendLine("# columns: instrRVA value  function  storeTarget  context");

        int count = 0;
        var byFunc = new Dictionary<string, List<(int rva, double v, string target)>>();
        if (text != null)
        {
            int start = text.PointerToRawData;
            int end = Math.Min(bytes.Length - 10, text.PointerToRawData + text.SizeOfRawData);
            for (int p = start; p <= end; p++)
            {
                byte b0 = bytes[p];
                if (b0 != 0x48) continue;         // REX.W only
                byte op = bytes[p + 1];
                if (op < 0xB8 || op > 0xBF) continue;
                double v = BitConverter.ToDouble(bytes, p + 2);
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                double a = Math.Abs(v);
                if (a == 0) continue;
                if (a < 1e-12 || a > 1e12) continue;
                double rounded = Math.Round(v, 10);
                if (rounded == 0) continue;
                if (Math.Abs((v - rounded) / rounded) > 1e-9) continue;  // keep "short decimal" literals
                int rva = scan.OffsetToRva(p);
                // mov [rcx+disp8], rax  (48 89 41 xx) immediately after the immediate?
                string target = "";
                int q = p + 10;
                if (q + 3 < bytes.Length && bytes[q] == 0x48 && bytes[q + 1] == 0x89 &&
                    (bytes[q + 2] >= 0x41 && bytes[q + 2] <= 0x4F))
                    target = $"[{(char)('A' + (bytes[q + 2] - 0x40))}+0x{bytes[q + 3]:X2}]";
                string fn = FuncOf(rva);
                sb.AppendLine($"0x{rva:X8}  {v.ToString("R", System.Globalization.CultureInfo.InvariantCulture),-24} {fn,-24} {target}");
                if (fn.Length > 0)
                {
                    if (!byFunc.TryGetValue(fn, out var list)) { list = new List<(int, double, string)>(); byFunc[fn] = list; }
                    list.Add((rva, v, target));
                }
                count++;
                p += 9;
            }
        }
        sb.AppendLine();
        sb.AppendLine($"# total: {count}");
        if (outPath.Length > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            Console.WriteLine($"wrote {outPath} ({new FileInfo(outPath).Length:N0} bytes)");
        }
        Console.WriteLine($"native mov-imm64 double constants: {count} in {byFunc.Count} functions");
        return _exit;
    }
    // ------------------------------------------------------------------
    /// <summary>
    /// Given RVA(s), report the managed method body or field-RVA blob that contains
    /// them. This is what turns "a double is at 0xEAF2E5" into "member X carries it".
    /// </summary>
    private static int Where(string[] args)
    {
        string file = args[1];
        using var pe = new PEReader(File.OpenRead(file));
        var md = pe.GetMetadataReader();
        var sig = new SigProvider(md);

        var bodies = new List<(int rva, int len, string name)>();
        foreach (var mh in md.MethodDefinitions)
        {
            var m = md.GetMethodDefinition(mh);
            if (m.RelativeVirtualAddress == 0) continue;
            int il = 0;
            try { il = pe.GetMethodBody(m.RelativeVirtualAddress).GetILBytes()?.Length ?? 0; } catch { }
            bodies.Add((m.RelativeVirtualAddress, il, sig.MethodName(mh)));
        }
        var fields = new List<(int rva, int len, string name)>();
        foreach (var fh in md.FieldDefinitions)
        {
            var f = md.GetFieldDefinition(fh);
            int rva = f.GetRelativeVirtualAddress();
            if (rva == 0) continue;
            fields.Add((rva, 0, sig.FieldName(fh)));
        }
        bodies.Sort((a, b) => a.rva.CompareTo(b.rva));

        foreach (var a in args.Skip(2))
        {
            int target = a.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToInt32(a.Substring(2), 16)
                : int.Parse(a);
            var hit = bodies.LastOrDefault(b => b.rva <= target && target - b.rva <= b.len + 16);
            var fhit = fields.LastOrDefault(f => f.rva <= target && target - f.rva < 4096);
            Console.WriteLine($"rva=0x{target:X8}");
            Console.WriteLine(hit.name != null
                ? $"  method: {hit.name}  body rva=0x{hit.rva:X8} ilLen={hit.len}  IL offset=0x{target - hit.rva:X}"
                : "  method: (none)");
            if (fhit.name != null) Console.WriteLine($"  nearby field-RVA blob: {fhit.name} rva=0x{fhit.rva:X8} (+0x{target - fhit.rva:X})");
        }
        return _exit;
    }
    // ------------------------------------------------------------------
    /// <summary>
    /// Exact IEEE-754 bit-pattern search for known constants. Works on any file;
    /// reports section/RVA where the containing section is executable or data.
    /// </summary>
    private static int Find(string[] args)
    {
        string file = args[1];
        var scan = new NativeScan(file);
        var values = new List<double>();
        foreach (var a in args.Skip(2))
            if (double.TryParse(a, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d))
                values.Add(d);
        Console.WriteLine($"# file {file}  size={scan.Bytes.Length:N0}  sha256={Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(scan.Bytes)).ToLowerInvariant()}");
        foreach (var v in values)
        {
            var hits = scan.FindDouble(v);
            var bySection = hits.Select(o => scan.Locate(o).section?.Name ?? "(headers)").GroupBy(s => s)
                                .OrderByDescending(g => g.Count())
                                .Select(g => $"{g.Key}:{g.Count()}");
            Console.WriteLine($"{v.ToString("R", System.Globalization.CultureInfo.InvariantCulture),-24} bits={Convert.ToHexString(BitConverter.GetBytes(v))} hits={hits.Count,-6} {string.Join(" ", bySection)}");
            foreach (var o in hits.Take(4))
                Console.WriteLine($"    rva=0x{scan.OffsetToRva(o):X8} off=0x{o:X8} section={scan.Locate(o).section?.Name}");
        }
        return _exit;
    }
    // ------------------------------------------------------------------
    private static readonly string[] Keywords =
    {
        "patmax", "pmalign", "pmal", "pattern", "score", "contrast", "grain", "angle", "peak",
        "clutter", "coverage", "feature", "dipole", "projection", "gabor", "sigma", "threshold",
        "affine", "elastic", "train", "find", "accept", "overlap", "pose", "origin", "scal",
        "toleranc", "expected", "numtofind", "numbertofind", "cognex", "visionpro", "cogxpma",
        "assert", "invalid", "error", "warn", "fail", "out of range", "must be", "cannot",
        "msvc", "microsoft", "compiler", "pdb", "debug", "release", "build", "version"
    };

    private static int Strings(string[] args)
    {
        string dll = args[1];
        string outDir = args[2];
        int min = int.TryParse(Opt(args, "--min"), out int m) ? m : 6;
        Directory.CreateDirectory(outDir);

        var scan = new NativeScan(dll);
        var sw = Stopwatch.StartNew();
        var all = scan.ExtractStrings(min);
        sw.Stop();

        var sb = new StringBuilder();
        sb.AppendLine("# native string inventory");
        sb.AppendLine($"# source: {dll}");
        sb.AppendLine($"# sha256: {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(scan.Bytes)).ToLowerInvariant()}");
        sb.AppendLine($"# imageBase: 0x{scan.ImageBase:X}");
        sb.AppendLine($"# minLength: {min}  total: {all.Count}  extraction: {sw.ElapsedMilliseconds} ms");
        sb.AppendLine("# columns: rva  fileOffset  section  enc  len  text");
        foreach (var s in all.OrderBy(x => x.Rva))
            sb.AppendLine($"0x{s.Rva:X8}  0x{s.FileOffset:X8}  {s.Section,-8} {(s.Wide ? "W" : "A")} {s.Length,4}  {s.Text}");
        File.WriteAllText(Path.Combine(outDir, "native-strings-all.txt"), sb.ToString(), new UTF8Encoding(false));

        // exports / imports
        var ex = scan.ReadExports();
        var im = scan.ReadImports();
        var ei = new StringBuilder();
        ei.AppendLine("# exports");
        foreach (var e in ex.OrderBy(e => e.Ordinal))
            ei.AppendLine($"ord={e.Ordinal,-4} rva=0x{e.Rva:X8} {(e.Forwarder.Length > 0 ? "fwd=" + e.Forwarder : "")} {e.Name}");
        ei.AppendLine();
        ei.AppendLine("# imports");
        foreach (var mod in im)
            ei.AppendLine($"{mod.Module}: {string.Join(", ", mod.Functions)}");
        File.WriteAllText(Path.Combine(outDir, "native-export-import.txt"), ei.ToString(), new UTF8Encoding(false));

        // keyword evidence with hexdump
        var hits = all.Where(s => Keywords.Any(k => s.Text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                      .Where(s => s.Length >= 6)
                      .OrderBy(s => s.Rva)
                      .ToList();
        var ev = new StringBuilder();
        ev.AppendLine("# keyword-filtered native strings (evidence)");
        ev.AppendLine($"# hits: {hits.Count}");
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var h in hits)
        {
            if (!seen.Add(h.Text)) continue;   // dedupe identical texts
            ev.AppendLine();
            ev.AppendLine($"## rva=0x{h.Rva:X8} off=0x{h.FileOffset:X} {h.Section} {(h.Wide ? "UTF16LE" : "ASCII")} len={h.Length}");
            ev.AppendLine($"> {h.Text}");
            ev.AppendLine("```");
            ev.Append(NativeScan.HexDump(scan.Bytes, h.FileOffset, 16, 48));
            ev.AppendLine("```");
        }
        File.WriteAllText(Path.Combine(outDir, "native-strings-keywords.md"), ev.ToString(), new UTF8Encoding(false));

        Console.WriteLine($"strings total={all.Count} keywordHits={hits.Count} distinct={seen.Count}");
        Console.WriteLine($"exports={ex.Count} importModules={im.Count}");
        Console.WriteLine($"wrote {outDir}\\native-strings-all.txt, native-strings-keywords.md, native-export-import.txt");
        return _exit;
    }

    // ------------------------------------------------------------------
    private static readonly double[] ReferenceDoubles =
    {
        2.0506096654409878, 4.1012193308819755, 0.3, 4.0, 0.5, 1.4142135623730951,
        0.7071067811865476, 2.0, 3.0, 0.25, 0.1, 0.05, 0.01, 0.9, 180.0, Math.PI,
    };

    private static int Doubles(string[] args)
    {
        string dll = args[1];
        string outDir = args[2];
        string section = Opt(args, "--section") ?? ".rdata";
        Directory.CreateDirectory(outDir);
        var scan = new NativeScan(dll);
        var hits = scan.ScanDoubles(section);

        var sb = new StringBuilder();
        sb.AppendLine($"# {section} plausible double constants: {hits.Count}");
        sb.AppendLine($"# source {dll}");
        sb.AppendLine("# grouped runs (gap <= 8 bytes counts as the same group)");
        int gi = 0;
        for (int i = 0; i < hits.Count; i++)
        {
            int j = i;
            while (j + 1 < hits.Count && hits[j + 1].FileOffset - hits[j].FileOffset <= 8) j++;
            gi++;
            sb.AppendLine();
            sb.AppendLine($"## group {gi}: rva 0x{hits[i].Rva:X8} … 0x{hits[j].Rva:X8}  ({j - i + 1} values)");
            for (int k = i; k <= j; k++)
                sb.AppendLine($"0x{hits[k].Rva:X8}  0x{hits[k].FileOffset:X8}  {hits[k].Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}   bits={hits[k].Bits}");
            sb.AppendLine("```");
            sb.Append(NativeScan.HexDump(scan.Bytes, hits[i].FileOffset, 0, Math.Min(160, (j - i + 1) * 8 + 32)));
            sb.AppendLine("```");
            i = j;
        }
        File.WriteAllText(Path.Combine(outDir, "native-doubles.txt"), sb.ToString(), new UTF8Encoding(false));

        // exact bit-pattern search for reference values
        var refSb = new StringBuilder();
        refSb.AppendLine("# exact IEEE-754 bit-pattern search (`grep` for the 8-byte pattern)");
        foreach (var v in ReferenceDoubles)
        {
            var found = scan.FindDouble(v);
            refSb.AppendLine();
            refSb.AppendLine($"## {v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}  bits={Convert.ToHexString(BitConverter.GetBytes(v))}  hits={found.Count}");
            foreach (var off in found.Take(6))
            {
                var (sec, _) = scan.Locate(off);
                refSb.AppendLine($"rva=0x{scan.OffsetToRva(off):X8} off=0x{off:X8} section={sec?.Name}");
                refSb.AppendLine("```");
                refSb.Append(NativeScan.HexDump(scan.Bytes, off, 16, 48));
                refSb.AppendLine("```");
            }
        }
        File.WriteAllText(Path.Combine(outDir, "native-doubles-reference.txt"), refSb.ToString(), new UTF8Encoding(false));

        Console.WriteLine($"doubles in {section}: {hits.Count}");
        foreach (var v in ReferenceDoubles)
        {
            var f = scan.FindDouble(v);
            if (f.Count > 0)
                Console.WriteLine($"  ref {v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)} hits={f.Count} first rva=0x{scan.OffsetToRva(f[0]):X8}");
        }
        Console.WriteLine($"wrote {outDir}\\native-doubles.txt, native-doubles-reference.txt");
        return _exit;
    }
}
#endif
