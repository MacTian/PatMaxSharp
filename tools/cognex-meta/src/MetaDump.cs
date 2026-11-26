#if COGNEX_META
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace CognexMeta;

public sealed class MetaDocument
{
    public string SourcePath { get; set; } = "";
    public long FileSize { get; set; }
    public string Sha256 { get; set; } = "";
    public string AssemblyName { get; set; } = "";
    public string AssemblyVersion { get; set; } = "";
    public string ModuleMvid { get; set; } = "";
    public string TargetFramework { get; set; } = "";
    public string XmlDocPath { get; set; } = "";
    public Dictionary<string, string> AssemblyAttributes { get; set; } = new();
    public int TypeCount { get; set; }
    public int PublicTypeCount { get; set; }
    public int MemberCount { get; set; }
    public List<TypeEntry> Types { get; set; } = new();
}

public sealed class TypeEntry
{
    public string FullName { get; set; } = "";
    public string Token { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";              // class / struct / interface / enum / delegate
    public string Attributes { get; set; } = "";        // textual flags
    public string Visibility { get; set; } = "";
    public bool IsNested { get; set; }
    public string BaseType { get; set; } = "";
    public List<string> Interfaces { get; set; } = new();
    public string Summary { get; set; } = "";
    public List<string> AttributesApplied { get; set; } = new();
    public List<MemberEntry> Fields { get; set; } = new();
    public List<MemberEntry> Properties { get; set; } = new();
    public List<MemberEntry> Methods { get; set; } = new();
    public List<MemberEntry> Events { get; set; } = new();
    public List<EnumValue> EnumValues { get; set; } = new();
    public List<IlConstHit> IlConstants { get; set; } = new();
}

public sealed class MemberEntry
{
    public string Name { get; set; } = "";
    public string Token { get; set; } = "";
    public string Signature { get; set; } = "";         // C#-ish
    public string Type { get; set; } = "";              // field/property type or return type
    public string Visibility { get; set; } = "";
    public string Modifiers { get; set; } = "";         // static/readonly/const/virtual/abstract...
    public string ConstantValue { get; set; } = "";     // const field value or [DefaultValue]
    public string DefaultValueAttr { get; set; } = "";  // [DefaultValue(x)] if present
    public string Summary { get; set; } = "";
    public string Remarks { get; set; } = "";
    public string Returns { get; set; } = "";
    public string Value { get; set; } = "";
    public string CogSummary { get; set; } = "";
    public string CogRemarks { get; set; } = "";
    public string CogValue { get; set; } = "";
    public string CogIdlMember { get; set; } = "";
    public List<string> AttributesApplied { get; set; } = new();
    public string Accessors { get; set; } = "";         // get/set
    public string Parameters { get; set; } = "";        // "(int x = 3, double y)"
}

public sealed class EnumValue
{
    public string Name { get; set; } = "";
    public long Numeric { get; set; }
    public string Raw { get; set; } = "";
    public string Summary { get; set; } = "";
}

/// <summary>
/// A constant observed in IL immediately before a store/call inside a method body.
/// This is how C# expresses "default value assigned in constructor" and
/// "value assigned by Initialize()": both compile to ldc.* + call set_X / stfld.
/// </summary>
public sealed class IlConstHit
{
    public string Owner { get; set; } = "";      // method the constant appears in
    public string Target { get; set; } = "";     // set_Xxx or field name
    public string Kind { get; set; } = "";       // call / stfld / stsfld
    public string Constant { get; set; } = "";
    public string ConstantType { get; set; } = ""; // int32 / float32 / float64 / string ...
    public int ILOffset { get; set; }
}

public sealed class DocEntry
{
    public string Summary { get; set; } = "";
    public string Remarks { get; set; } = "";
    public string Returns { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class MetaDump
{
    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly PEReader _pe;
    private readonly MetadataReader _md;
    private readonly SigProvider _sig;
    private readonly Dictionary<string, DocEntry> _docs = new(StringComparer.Ordinal);

    public MetaDump(string path, string? xmlDocPath)
    {
        SourcePath = path;
        var bytes = File.ReadAllBytes(path);
        FileSize = bytes.LongLength;
        Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        _pe = new PEReader(ImmutableArrayHelper.AsImmutable(bytes));
        _md = _pe.GetMetadataReader();
        _sig = new SigProvider(_md);

        if (!string.IsNullOrEmpty(xmlDocPath) && File.Exists(xmlDocPath))
        {
            XmlDocPath = xmlDocPath;
            LoadDocs(xmlDocPath!);
        }
    }

    public string SourcePath { get; }
    public long FileSize { get; }
    public string Sha256 { get; }
    public string XmlDocPath { get; set; } = "";

    private void LoadDocs(string xmlPath)
    {
        var doc = new System.Xml.XmlDocument();
        using var fs = File.OpenRead(xmlPath);
        doc.Load(fs);
        var members = doc.SelectNodes("/doc/members/member");
        if (members == null) return;
        foreach (System.Xml.XmlNode n in members)
        {
            string name = n.Attributes?["name"]?.Value ?? "";
            if (name.Length < 3) continue;
            string body = name.Substring(2);
            int paren = body.IndexOf('(');
            if (paren > 0) body = body.Substring(0, paren);
            var e = new DocEntry
            {
                Summary = Clean(n.SelectSingleNode("summary")?.InnerText),
                Remarks = Clean(n.SelectSingleNode("remarks")?.InnerText),
                Returns = Clean(n.SelectSingleNode("returns")?.InnerText),
                Value = Clean(n.SelectSingleNode("value")?.InnerText),
            };
            _docs[name] = e;
            // Secondary key: name without the kind prefix, so P:/T:/M:/F: all resolve.
            _docs.TryAdd(body, e);
        }
    }

    private static string Clean(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var sb = new StringBuilder();
        bool ws = false;
        foreach (char c in s!)
        {
            if (char.IsWhiteSpace(c)) { ws = true; continue; }
            if (ws && sb.Length > 0) sb.Append(' ');
            ws = false;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private DocEntry Doc(string key) => _docs.TryGetValue(key, out var e) ? e : new DocEntry();

    // ------------------------------------------------------------------
    public MetaDocument Dump()
    {
        var md = new MetaDocument
        {
            SourcePath = SourcePath,
            FileSize = FileSize,
            Sha256 = Sha256,
            XmlDocPath = XmlDocPath,
        };

        var asm = _md.GetAssemblyDefinition();
        md.AssemblyName = _md.GetString(asm.Name);
        md.AssemblyVersion = asm.Version.ToString();
        md.ModuleMvid = _md.GetGuid(_md.GetModuleDefinition().Mvid).ToString();

        foreach (var ah in asm.GetCustomAttributes())
        {
            var (name, args) = DecodeAttr(ah);
            if (args.Count > 0)
                md.AssemblyAttributes[ShortAttr(name)] = string.Join(", ", args);
        }
        if (md.AssemblyAttributes.TryGetValue("TargetFramework", out var tf)) md.TargetFramework = tf;

        var types = new List<TypeEntry>();
        foreach (var th in _md.TypeDefinitions)
        {
            var td = _md.GetTypeDefinition(th);
            string name = _md.GetString(td.Name);
            if (name == "<Module>") continue;
            md.TypeCount++;
            var te = ReadType(th, td);
            if (te.Visibility == "public") md.PublicTypeCount++;
            types.Add(te);
        }
        md.Types = types
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .ToList();
        md.MemberCount = types.Sum(t => t.Fields.Count + t.Properties.Count + t.Methods.Count + t.Events.Count);
        return md;
    }

    private TypeEntry ReadType(TypeDefinitionHandle th, TypeDefinition td)
    {
        string name = _md.GetString(td.Name);
        string ns = _md.GetString(td.Namespace);
        string full = _sig.TypeName(th);

        bool isEnum = IsEnumBase(td);
        bool isDelegate = IsDelegateBase(td);
        bool isInterface = (td.Attributes & System.Reflection.TypeAttributes.Interface) != 0;
        string kind = isEnum ? "enum"
            : isInterface ? "interface"
            : isDelegate ? "delegate"
            : (td.Attributes & System.Reflection.TypeAttributes.Sealed) != 0
                ? (IsValueTypeBase(td) ? "struct" : "sealed class")
                : (IsValueTypeBase(td) ? "struct" : "class");

        var te = new TypeEntry
        {
            FullName = full,
            Token = MetadataTokens.GetToken(th).ToString("X8"),
            Namespace = ns,
            Name = name,
            Kind = kind,
            Visibility = TypeVisibility(td),
            IsNested = !td.GetDeclaringType().IsNil,
            Attributes = TypeFlagsText(td.Attributes),
            BaseType = td.BaseType.IsNil ? "" : _sig.HandleName(td.BaseType),
            Summary = Doc("T:" + StripArity(full)).Summary,
        };

        foreach (var ih in td.GetInterfaceImplementations())
        {
            var ii = _md.GetInterfaceImplementation(ih);
            te.Interfaces.Add(_sig.HandleName(ii.Interface));
        }
        foreach (var ah in td.GetCustomAttributes())
        {
            var (n, a) = DecodeAttr(ah);
            ApplyCogType(te, ShortAttr(n), a);
            te.AttributesApplied.Add(a.Count > 0 ? $"[{ShortAttr(n)}({Compact(a)})]" : $"[{ShortAttr(n)}]");
        }

        // fields
        foreach (var fh in td.GetFields())
        {
            var f = _md.GetFieldDefinition(fh);
            string fname = _md.GetString(f.Name);
            string ftype = "";
            try { ftype = f.DecodeSignature(_sig, null); } catch { ftype = "?"; }
            var flags = f.Attributes;
            var mods = new List<string>();
            if ((flags & System.Reflection.FieldAttributes.Static) != 0) mods.Add("static");
            if ((flags & System.Reflection.FieldAttributes.Literal) != 0) mods.Add("const");
            if ((flags & System.Reflection.FieldAttributes.InitOnly) != 0) mods.Add("readonly");
            string constVal = "";
            var ch = f.GetDefaultValue();
            if (!ch.IsNil) constVal = DecodeConstant(ch, out _);
            var me = new MemberEntry
            {
                Name = fname,
                Token = MetadataTokens.GetToken(fh).ToString("X8"),
                Type = ftype,
                Signature = $"{(mods.Count > 0 ? string.Join(" ", mods) + " " : "")}{ftype} {fname}",
                Visibility = FieldVisibility(flags),
                Modifiers = string.Join(" ", mods),
                ConstantValue = constVal,
                Summary = Doc("F:" + full + "." + fname).Summary,
            };
            foreach (var ah in f.GetCustomAttributes())
            {
                var (n, a) = DecodeAttr(ah);
                ApplyCog(me, ShortAttr(n), a);
                me.AttributesApplied.Add(a.Count > 0 ? $"[{ShortAttr(n)}({Compact(a)})]" : $"[{ShortAttr(n)}]");
                if (ShortAttr(n) == "DefaultValue" && a.Count > 0) me.DefaultValueAttr = a[0];
            }
            te.Fields.Add(me);

            // enum members
            if (isEnum && (flags & System.Reflection.FieldAttributes.Literal) != 0)
            {
                long num = 0;
                string raw = constVal;
                if (!ch.IsNil)
                {
                    var c = _md.GetConstant(ch);
                    raw = DecodeConstant(ch, out long l);
                    num = l;
                    _ = c;
                }
                te.EnumValues.Add(new EnumValue { Name = fname, Numeric = num, Raw = raw, Summary = me.Summary });
            }
        }

        // properties
        foreach (var ph in td.GetProperties())
        {
            var p = _md.GetPropertyDefinition(ph);
            string pname = _md.GetString(p.Name);
            MethodSignature<string> psig;
            try { psig = p.DecodeSignature(_sig, null); }
            catch { psig = default; }
            var acc = p.GetAccessors();
            var accs = new List<string>();
            if (!acc.Getter.IsNil) accs.Add("get");
            if (!acc.Setter.IsNil) accs.Add("set");
            foreach (var o in acc.Others) accs.Add(_md.GetString(_md.GetMethodDefinition(o).Name));
            var pe = new MemberEntry
            {
                Name = pname,
                Token = MetadataTokens.GetToken(ph).ToString("X8"),
                Type = psig.ReturnType ?? "",
                Signature = $"{psig.ReturnType} {pname} {{ {string.Join("; ", accs)}; }}",
                Visibility = PropVisibility(acc),
                Accessors = string.Join("/", accs),
                Summary = Doc("P:" + full + "." + pname).Summary,
                Remarks = Doc("P:" + full + "." + pname).Remarks,
                Value = Doc("P:" + full + "." + pname).Value is string v && v.Length > 0 ? v : "",
            };
            if (psig.ParameterTypes is { Length: > 0 })
                pe.Parameters = "(" + string.Join(", ", psig.ParameterTypes) + ")";
            foreach (var ah in p.GetCustomAttributes())
            {
                var (n, a) = DecodeAttr(ah);
                ApplyCog(pe, ShortAttr(n), a);
                pe.AttributesApplied.Add(a.Count > 0 ? $"[{ShortAttr(n)}({Compact(a)})]" : $"[{ShortAttr(n)}]");
                if (ShortAttr(n) == "DefaultValue" && a.Count > 0) pe.DefaultValueAttr = a[0];
            }
            te.Properties.Add(pe);
        }

        // events
        foreach (var eh in td.GetEvents())
        {
            var ev = _md.GetEventDefinition(eh);
            string en = _md.GetString(ev.Name);
            te.Events.Add(new MemberEntry
            {
                Name = en,
                Signature = "event " + _sig.HandleName(ev.Type),
                Type = _sig.HandleName(ev.Type),
                Visibility = "public",
                Summary = Doc("E:" + full + "." + en).Summary,
            });
        }

        // methods
        foreach (var mh in td.GetMethods())
        {
            var m = _md.GetMethodDefinition(mh);
            string mname = _md.GetString(m.Name);
            MethodSignature<string> msig;
            try { msig = m.DecodeSignature(_sig, null); }
            catch { msig = default; }
            var mods = new List<string>();
            if ((m.Attributes & System.Reflection.MethodAttributes.Static) != 0) mods.Add("static");
            if ((m.Attributes & System.Reflection.MethodAttributes.Abstract) != 0) mods.Add("abstract");
            if ((m.Attributes & System.Reflection.MethodAttributes.Virtual) != 0 && (m.Attributes & System.Reflection.MethodAttributes.Final) == 0) mods.Add("virtual");
            if ((m.Attributes & System.Reflection.MethodAttributes.Final) != 0 && (m.Attributes & System.Reflection.MethodAttributes.Virtual) != 0) mods.Add("sealed");
            var pars = new List<string>();
            foreach (var ph in m.GetParameters())
            {
                var pr = _md.GetParameter(ph);
                string pn = _md.GetString(pr.Name);
                if (string.IsNullOrEmpty(pn)) pn = "arg" + pr.SequenceNumber;
                string pdef = "";
                var pch = pr.GetDefaultValue();
                if (!pch.IsNil) pdef = " = " + DecodeConstant(pch, out _);
                pars.Add(pn + pdef);
            }
            string paramTypes = msig.ParameterTypes.IsDefaultOrEmpty ? "" : string.Join(", ", msig.ParameterTypes);
            var me = new MemberEntry
            {
                Name = mname,
                Type = msig.ReturnType ?? "",
                Signature = $"{(mods.Count > 0 ? string.Join(" ", mods) + " " : "")}{msig.ReturnType} {mname}({paramTypes})",
                Visibility = MethodVisibility(m.Attributes),
                Modifiers = string.Join(" ", mods),
                Parameters = "(" + string.Join(", ", pars) + ")",
                Summary = Doc("M:" + full + "." + mname).Summary,
                Remarks = Doc("M:" + full + "." + mname).Remarks,
                Returns = Doc("M:" + full + "." + mname).Returns,
            };
            foreach (var ah in m.GetCustomAttributes())
            {
                var (n, a) = DecodeAttr(ah);
                ApplyCog(me, ShortAttr(n), a);
                me.AttributesApplied.Add(a.Count > 0 ? $"[{ShortAttr(n)}({Compact(a)})]" : $"[{ShortAttr(n)}]");
            }
            te.Methods.Add(me);
        }

        // IL constants (default value assignment inference)
        te.IlConstants = ScanConstants(th, td, full);

        return te;
    }

    // ------------------------------------------------------------------
    /// <summary>
    /// Walk method bodies looking for `ldc.*` immediately feeding `call set_X` or `stfld/stsfld`.
    /// That is exactly how C# emits "this.X = 3;" in a ctor or an Initialize() method.
    /// </summary>
    private List<IlConstHit> ScanConstants(TypeDefinitionHandle th, TypeDefinition td, string full)
    {
        var hits = new List<IlConstHit>();
        foreach (var mh in td.GetMethods())
        {
            var m = _md.GetMethodDefinition(mh);
            if (m.RelativeVirtualAddress == 0) continue;
            MethodBodyBlock body;
            try { body = _pe.GetMethodBody(m.RelativeVirtualAddress); }
            catch { continue; }
            byte[] il;
            try { il = body.GetILBytes() ?? Array.Empty<byte>(); } catch { continue; }
            if (il.Length == 0) continue;
            string owner = _md.GetString(m.Name);

            object? pending = null;
            string pendingType = "";
            int pendingOffset = 0;

            int i = 0;
            while (i < il.Length)
            {
                int opStart = i;
                byte op = il[i++];
                int operandSize;
                int opcode = op;
                if (op == 0xFE)
                {
                    if (i >= il.Length) break;
                    byte op2 = il[i++];
                    opcode = 0xFE00 | op2;
                    operandSize = IlTables.TwoByteOperandSize(op2);
                }
                else
                {
                    operandSize = IlTables.OneByteOperandSize(op);
                }

                if (operandSize < 0) break; // unknown opcode: stop this body, keep what we have
                if (opcode == 0x45) // switch
                {
                    if (i + 4 > il.Length) break;
                    int n = BitConverter.ToInt32(il, i);
                    operandSize = 4 + 4 * n;
                }
                if (i + operandSize > il.Length) break;

                switch (opcode)
                {
                    case 0x16: case 0x17: case 0x18: case 0x19:
                    case 0x1A: case 0x1B: case 0x1C: case 0x1D: case 0x1E:
                        pending = opcode - 0x16; pendingType = "int32"; pendingOffset = opStart; break;
                    case 0x15: pending = -1; pendingType = "int32"; pendingOffset = opStart; break;
                    case 0x1F: pending = (sbyte)il[i]; pendingType = "int32"; pendingOffset = opStart; break;
                    case 0x20: pending = BitConverter.ToInt32(il, i); pendingType = "int32"; pendingOffset = opStart; break;
                    case 0x21: pending = BitConverter.ToInt64(il, i); pendingType = "int64"; pendingOffset = opStart; break;
                    case 0x22: pending = BitConverter.ToSingle(il, i); pendingType = "float32"; pendingOffset = opStart; break;
                    case 0x23: pending = BitConverter.ToDouble(il, i); pendingType = "float64"; pendingOffset = opStart; break;
                    case 0x72: // ldstr
                        {
                            int tok = BitConverter.ToInt32(il, i);
                            try
                            {
                                var sh = MetadataTokens.UserStringHandle(tok & 0x00FFFFFF);
                                pending = _md.GetUserString(sh); pendingType = "string";
                            }
                            catch { pending = null; }
                            pendingOffset = opStart;
                            break;
                        }
                    case 0x28: case 0x6F: case 0x73: // call / callvirt / newobj
                        {
                            int tok = BitConverter.ToInt32(il, i);
                            string target = TokenName(tok, out bool isSetter);
                            if (pending != null && isSetter && target.StartsWith("set_", StringComparison.Ordinal))
                            {
                                hits.Add(new IlConstHit
                                {
                                    Owner = owner, Target = target, Kind = opcode == 0x6F ? "callvirt" : "call",
                                    Constant = Format(pending), ConstantType = pendingType, ILOffset = pendingOffset
                                });
                            }
                            else if (pending != null && opcode == 0x73)
                            {
                                hits.Add(new IlConstHit
                                {
                                    Owner = owner, Target = target, Kind = "newobj(ctor arg)",
                                    Constant = Format(pending), ConstantType = pendingType, ILOffset = pendingOffset
                                });
                            }
                            pending = null; break;
                        }
                    case 0x7D: case 0x80: // stfld / stsfld
                        {
                            int tok = BitConverter.ToInt32(il, i);
                            string target = TokenName(tok, out _);
                            if (pending != null)
                            {
                                hits.Add(new IlConstHit
                                {
                                    Owner = owner, Target = target, Kind = opcode == 0x7D ? "stfld" : "stsfld",
                                    Constant = Format(pending), ConstantType = pendingType, ILOffset = pendingOffset
                                });
                            }
                            pending = null; break;
                        }
                    default:
                        break;
                }
                i += operandSize;
            }
        }
        return hits;
    }

    private static string Format(object o) => o switch
    {
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        string s => "\"" + s + "\"",
        _ => Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) ?? ""
    };

    private string TokenName(int token, out bool isSetter)
    {
        isSetter = false;
        try
        {
            var h = MetadataTokens.EntityHandle(token);
            switch (h.Kind)
            {
                case HandleKind.MethodDefinition:
                    {
                        var m = _md.GetMethodDefinition((MethodDefinitionHandle)h);
                        isSetter = true;
                        return _md.GetString(m.Name);
                    }
                case HandleKind.MemberReference:
                    {
                        var mr = _md.GetMemberReference((MemberReferenceHandle)h);
                        isSetter = true;
                        return _md.GetString(mr.Name);
                    }
                case HandleKind.FieldDefinition:
                    return _md.GetString(_md.GetFieldDefinition((FieldDefinitionHandle)h).Name);
                default:
                    return "?";
            }
        }
        catch { return "?"; }
    }

    // ------------------------------------------------------------------
    private (string name, List<string> args) DecodeAttr(CustomAttributeHandle h)
    {
        var ca = _md.GetCustomAttribute(h);
        string name = "";
        try
        {
            if (ca.Constructor.Kind == HandleKind.MemberReference)
            {
                var mr = _md.GetMemberReference((MemberReferenceHandle)ca.Constructor);
                name = _sig.HandleName(mr.Parent);
            }
            else if (ca.Constructor.Kind == HandleKind.MethodDefinition)
            {
                var m = _md.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor);
                name = _sig.TypeName(m.GetDeclaringType());
            }
        }
        catch { }

        var args = new List<string>();
        try
        {
            var v = ca.DecodeValue(_sig);
            foreach (var a in v.FixedArguments)
                args.Add(FormatArg(a.Value));
            foreach (var a in v.NamedArguments)
                args.Add($"{a.Name}={FormatArg(a.Value)}");
        }
        catch
        {
            try
            {
                var bytes = _md.GetBlobBytes(ca.Value);
                args.Add("blob:" + Convert.ToHexString(bytes));
            }
            catch { }
        }
        return (name, args);
    }

    // ---- Cognex documentation attributes --------------------------------
    // VisionPro ships its reference prose inside custom attributes. The text either is
    // literal prose (which is where "Default value is X" / legal ranges survive) or an
    // <include file='CognexPatmaxAlign.merged.xml' path='//interface[@name="ICogPMAlignRunParams"]/...'/>
    // element naming the C++ COM interface member it was authored against. The include
    // file is not shipped, but the path itself is provenance for the native contract.
    private static readonly Regex IdlRx = new(
        @"//interface\[@name=""(?<iface>[^""]+)""\]/function\[@vbname=""(?<fn>[^""]+)""\]",
        RegexOptions.Compiled);

    private static string Unq(string s) => s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"' ? s.Substring(1, s.Length - 2) : s;

    private static void ApplyCog(MemberEntry me, string attr, List<string> args)
    {
        if (args.Count == 0) return;
        string raw = Unq(args[0]);
        var m = IdlRx.Match(raw);
        if (m.Success && me.CogIdlMember.Length == 0)
            me.CogIdlMember = m.Groups["iface"].Value + "::" + m.Groups["fn"].Value;
        if (raw.Contains("<include", StringComparison.Ordinal)) return;
        switch (attr)
        {
            case "CogDocSummary": if (me.CogSummary.Length == 0) me.CogSummary = raw; break;
            case "CogDocRemarks": if (me.CogRemarks.Length == 0) me.CogRemarks = raw; break;
            case "CogDocValue": if (me.CogValue.Length == 0) me.CogValue = raw; break;
        }
    }

    private static void ApplyCogType(TypeEntry te, string attr, List<string> args)
    {
        if (args.Count == 0) return;
        string raw = Unq(args[0]);
        if (raw.Contains("<include", StringComparison.Ordinal)) return;
        if (attr == "CogDocSummary" && te.Summary.Length == 0) te.Summary = raw;
    }

    private static string CompactArg(string s) => s.Contains("<include", StringComparison.Ordinal) ? "<IDL doc>" : s;
    private static string Compact(List<string> a) => string.Join(", ", a.Select(CompactArg));
    private static string FormatArg(object? o) => o switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        float f => f.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        char c => "'" + c + "'",
        System.Collections.Immutable.ImmutableArray<CustomAttributeTypedArgument<string>> arr
            => "[" + string.Join(", ", arr.Select(x => FormatArg(x.Value))) + "]",
        _ => Convert.ToString(o, System.Globalization.CultureInfo.InvariantCulture) ?? ""
    };

    private static string ShortAttr(string full)
    {
        if (string.IsNullOrEmpty(full)) return "?";
        int i = full.LastIndexOf('.');
        string n = i >= 0 ? full.Substring(i + 1) : full;
        if (n.EndsWith("Attribute", StringComparison.Ordinal) && n.Length > 9) n = n.Substring(0, n.Length - 9);
        return n;
    }

    private string DecodeConstant(ConstantHandle h, out long numeric)
    {
        numeric = 0;
        try
        {
            var c = _md.GetConstant(h);
            var blob = _md.GetBlobReader(c.Value);
            switch (c.TypeCode)
            {
                case ConstantTypeCode.Boolean: numeric = blob.ReadBoolean() ? 1 : 0; return numeric != 0 ? "true" : "false";
                case ConstantTypeCode.Char: { var ch = blob.ReadChar(); numeric = ch; return "'" + ch + "'"; }
                case ConstantTypeCode.SByte: { var v = blob.ReadSByte(); numeric = v; return v.ToString(); }
                case ConstantTypeCode.Byte: { var v = blob.ReadByte(); numeric = v; return v.ToString(); }
                case ConstantTypeCode.Int16: { var v = blob.ReadInt16(); numeric = v; return v.ToString(); }
                case ConstantTypeCode.UInt16: { var v = blob.ReadUInt16(); numeric = v; return v.ToString(); }
                case ConstantTypeCode.Int32: { var v = blob.ReadInt32(); numeric = v; return v.ToString(); }
                case ConstantTypeCode.UInt32: { var v = blob.ReadUInt32(); numeric = v; return v.ToString(); }
                case ConstantTypeCode.Int64: { var v = blob.ReadInt64(); numeric = v; return v.ToString(); }
                case ConstantTypeCode.UInt64: { var v = blob.ReadUInt64(); numeric = (long)v; return v.ToString(); }
                case ConstantTypeCode.Single: { var v = blob.ReadSingle(); numeric = (long)v; return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
                case ConstantTypeCode.Double: { var v = blob.ReadDouble(); numeric = (long)v; return v.ToString("R", System.Globalization.CultureInfo.InvariantCulture); }
                case ConstantTypeCode.String: return "\"" + blob.ReadUTF16(blob.Length) + "\"";
                case ConstantTypeCode.NullReference: return "null";
                default: return "?";
            }
        }
        catch { return "?"; }
    }

    // ------------------------------------------------------------------
    private bool IsEnumBase(TypeDefinition td) => BaseNameIs(td, "System.Enum");
    private bool IsValueTypeBase(TypeDefinition td) => BaseNameIs(td, "System.ValueType");
    private bool IsDelegateBase(TypeDefinition td) => BaseNameIs(td, "System.MulticastDelegate");

    private bool BaseNameIs(TypeDefinition td, string name)
    {
        if (td.BaseType.IsNil) return false;
        try { return _sig.HandleName(td.BaseType) == name; } catch { return false; }
    }

    public static string StripArity(string s)
    {
        int i = s.IndexOf('`');
        return i < 0 ? s : s.Substring(0, i);
    }

    private static string TypeVisibility(TypeDefinition td)
    {
        var a = td.Attributes & System.Reflection.TypeAttributes.VisibilityMask;
        return a switch
        {
            System.Reflection.TypeAttributes.Public => "public",
            System.Reflection.TypeAttributes.NestedPublic => "public",
            System.Reflection.TypeAttributes.NestedFamily => "protected",
            System.Reflection.TypeAttributes.NestedFamORAssem => "protected internal",
            System.Reflection.TypeAttributes.NestedAssembly => "internal",
            _ => "internal"
        };
    }

    private static string FieldVisibility(System.Reflection.FieldAttributes f) => (f & System.Reflection.FieldAttributes.FieldAccessMask) switch
    {
        System.Reflection.FieldAttributes.Public => "public",
        System.Reflection.FieldAttributes.Family => "protected",
        System.Reflection.FieldAttributes.FamORAssem => "protected internal",
        System.Reflection.FieldAttributes.Assembly => "internal",
        _ => "private"
    };

    private static string MethodVisibility(System.Reflection.MethodAttributes m) => (m & System.Reflection.MethodAttributes.MemberAccessMask) switch
    {
        System.Reflection.MethodAttributes.Public => "public",
        System.Reflection.MethodAttributes.Family => "protected",
        System.Reflection.MethodAttributes.FamORAssem => "protected internal",
        System.Reflection.MethodAttributes.Assembly => "internal",
        _ => "private"
    };

    private string PropVisibility(PropertyAccessors acc)
    {
        foreach (var m in new[] { acc.Getter, acc.Setter })
        {
            if (m.IsNil) continue;
            var md = _md.GetMethodDefinition(m);
            string v = MethodVisibility(md.Attributes);
            if (v != "private") return v;
        }
        return "private";
    }

    private static string TypeFlagsText(System.Reflection.TypeAttributes a)
    {
        var parts = new List<string>();
        if ((a & System.Reflection.TypeAttributes.Abstract) != 0) parts.Add("abstract");
        if ((a & System.Reflection.TypeAttributes.Sealed) != 0) parts.Add("sealed");
        if ((a & System.Reflection.TypeAttributes.Interface) != 0) parts.Add("interface");
        if ((a & System.Reflection.TypeAttributes.Serializable) != 0) parts.Add("serializable");
        if ((a & System.Reflection.TypeAttributes.BeforeFieldInit) != 0) parts.Add("beforefieldinit");
        if ((a & System.Reflection.TypeAttributes.LayoutMask) == System.Reflection.TypeAttributes.ExplicitLayout) parts.Add("explicitlayout");
        if ((a & System.Reflection.TypeAttributes.LayoutMask) == System.Reflection.TypeAttributes.SequentialLayout) parts.Add("sequentiallayout");
        if ((a & System.Reflection.TypeAttributes.StringFormatMask) == System.Reflection.TypeAttributes.UnicodeClass) parts.Add("unicode");
        return string.Join(", ", parts);
    }
}

internal static class ImmutableArrayHelper
{
    public static System.Collections.Immutable.ImmutableArray<byte> AsImmutable(byte[] bytes)
        => System.Collections.Immutable.ImmutableArray.Create(bytes);
}
#endif
