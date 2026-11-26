#if COGNEX_META
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;

namespace CognexMeta;

/// <summary>
/// Native PE forensics: ASCII + UTF-16LE string extraction with RVA mapping,
/// export/import table recovery, and double-constant scanning of .rdata.
/// Read-only: the file is opened for reading and never modified.
/// </summary>
public sealed class NativeScan
{
    private readonly string _path;
    private readonly byte[] _bytes;
    private readonly PEReader _pe;
    private readonly List<SectionHeader> _sections;
    private readonly ulong _imageBase;

    public sealed record SectionHeader(string Name, int VirtualAddress, int VirtualSize, int PointerToRawData, int SizeOfRawData);

    public NativeScan(string path)
    {
        _path = path;
        _bytes = File.ReadAllBytes(path);
        _pe = new PEReader(System.Collections.Immutable.ImmutableArray.Create(_bytes));
        _sections = new List<SectionHeader>();
        foreach (var s in _pe.PEHeaders.SectionHeaders)
            _sections.Add(new SectionHeader(s.Name, s.VirtualAddress, s.VirtualSize, s.PointerToRawData, s.SizeOfRawData));
        _imageBase = _pe.PEHeaders.PEHeader?.ImageBase ?? 0;
    }

    public byte[] Bytes => _bytes;
    public PEReader Pe => _pe;
    public IReadOnlyList<SectionHeader> Sections => _sections;
    public ulong ImageBase => _imageBase;

    public int RvaToOffset(int rva)
    {
        foreach (var s in _sections)
        {
            int size = Math.Max(s.VirtualSize, s.SizeOfRawData);
            if (rva >= s.VirtualAddress && rva < s.VirtualAddress + size)
                return s.PointerToRawData + (rva - s.VirtualAddress);
        }
        return -1;
    }

    public (SectionHeader? section, int offsetInSection) Locate(int fileOffset)
    {
        foreach (var s in _sections)
        {
            if (fileOffset >= s.PointerToRawData && fileOffset < s.PointerToRawData + s.SizeOfRawData)
                return (s, fileOffset - s.PointerToRawData);
        }
        return (null, -1);
    }

    public int OffsetToRva(int fileOffset)
    {
        var (s, o) = Locate(fileOffset);
        return s == null ? -1 : s.VirtualAddress + o;
    }

    // ------------------------------------------------------------------
    public sealed record NativeString(string Section, int Rva, int FileOffset, int Length, bool Wide, string Text);

    public List<NativeString> ExtractStrings(int minLen = 6)
    {
        var list = new List<NativeString>();
        foreach (var s in _sections)
        {
            if (s.SizeOfRawData <= 0) continue;
            int start = s.PointerToRawData;
            int end = Math.Min(_bytes.Length, s.PointerToRawData + s.SizeOfRawData);

            // ASCII / single-byte printable runs
            int i = start;
            while (i < end)
            {
                if (IsPrintable(_bytes[i]))
                {
                    int j = i;
                    while (j < end && IsPrintable(_bytes[j])) j++;
                    int len = j - i;
                    if (len >= minLen)
                        list.Add(new NativeString(s.Name, s.VirtualAddress + (i - start), i, len, false,
                            Encoding.ASCII.GetString(_bytes, i, len)));
                    i = j;
                }
                else i++;
            }

            // UTF-16LE runs
            i = start;
            while (i + 1 < end)
            {
                if (IsPrintable(_bytes[i]) && _bytes[i + 1] == 0)
                {
                    int j = i;
                    int chars = 0;
                    while (j + 1 < end && IsPrintable(_bytes[j]) && _bytes[j + 1] == 0) { j += 2; chars++; }
                    if (chars >= minLen)
                        list.Add(new NativeString(s.Name, s.VirtualAddress + (i - start), i, chars, true,
                            Encoding.Unicode.GetString(_bytes, i, chars * 2)));
                    i = j;
                }
                else i += 1;
            }
        }
        return list;
    }

    private static bool IsPrintable(byte b) => (b >= 0x20 && b <= 0x7E) || b == 0x09;

    // ------------------------------------------------------------------
    public sealed record Export(string Name, int Ordinal, int Rva, string Forwarder);

    public List<Export> ReadExports()
    {
        var result = new List<Export>();
        var dir = _pe.PEHeaders.PEHeader?.ExportTableDirectory ?? default;
        if (dir.RelativeVirtualAddress == 0) return result;
        int off = RvaToOffset(dir.RelativeVirtualAddress);
        if (off < 0 || off + 40 > _bytes.Length) return result;
        int numberOfNames = BitConverter.ToInt32(_bytes, off + 24);
        int addressOfNames = BitConverter.ToInt32(_bytes, off + 32);
        int addressOfOrdinals = BitConverter.ToInt32(_bytes, off + 36);
        int baseOrdinal = BitConverter.ToInt32(_bytes, off + 16);
        int namesOff = RvaToOffset(addressOfNames);
        int ordOff = RvaToOffset(addressOfOrdinals);
        if (namesOff < 0 || ordOff < 0) return result;
        int exportStart = dir.RelativeVirtualAddress;
        int exportEnd = exportStart + dir.Size;
        for (int i = 0; i < numberOfNames; i++)
        {
            if (namesOff + i * 4 + 4 > _bytes.Length) break;
            int nameRva = BitConverter.ToInt32(_bytes, namesOff + i * 4);
            int nameOff = RvaToOffset(nameRva);
            if (nameOff < 0) continue;
            string name = ReadCString(nameOff);
            int rva = -1;
            if (ordOff + i * 2 + 2 <= _bytes.Length)
            {
                int ord = BitConverter.ToUInt16(_bytes, ordOff + i * 2);
                int funcsOff = RvaToOffset(BitConverter.ToInt32(_bytes, off + 28));
                if (funcsOff >= 0 && funcsOff + ord * 4 + 4 <= _bytes.Length)
                    rva = BitConverter.ToInt32(_bytes, funcsOff + ord * 4);
            }
            string fwd = "";
            if (rva >= exportStart && rva < exportEnd)
            {
                int fo = RvaToOffset(rva);
                if (fo >= 0) fwd = ReadCString(fo);
            }
            result.Add(new Export(name, baseOrdinal + i, rva, fwd));
        }
        return result;
    }

    public sealed record ImportModule(string Module, List<string> Functions);

    public List<ImportModule> ReadImports(int maxFuncsPerModule = 64)
    {
        var result = new List<ImportModule>();
        var dir = _pe.PEHeaders.PEHeader?.ImportTableDirectory ?? default;
        if (dir.RelativeVirtualAddress == 0) return result;
        int off = RvaToOffset(dir.RelativeVirtualAddress);
        if (off < 0) return result;
        for (int d = 0; d < 4096; d++)
        {
            int o = off + d * 20;
            if (o + 20 > _bytes.Length) break;
            int nameRva = BitConverter.ToInt32(_bytes, o + 12);
            int thunkRva = BitConverter.ToInt32(_bytes, o + 0);
            if (nameRva == 0 && thunkRva == 0) break;
            int nameOff = RvaToOffset(nameRva);
            if (nameOff < 0) break;
            string module = ReadCString(nameOff);
            var funcs = new List<string>();
            if (thunkRva != 0)
            {
                int thunkOff = RvaToOffset(thunkRva);
                if (thunkOff >= 0)
                {
                    for (int k = 0; k < maxFuncsPerModule; k++)
                    {
                        int p = thunkOff + k * 8;
                        if (p + 8 > _bytes.Length) break;
                        ulong val = BitConverter.ToUInt64(_bytes, p);
                        if (val == 0) break;
                        if ((val & 0x8000000000000000UL) != 0)
                        {
                            funcs.Add("#" + (val & 0xFFFF));
                        }
                        else
                        {
                            int hn = RvaToOffset((int)(val & 0x7FFFFFFF));
                            if (hn >= 0 && hn + 2 <= _bytes.Length)
                                funcs.Add(ReadCString(hn + 2));
                        }
                    }
                }
            }
            result.Add(new ImportModule(module, funcs));
        }
        return result;
    }

    private string ReadCString(int offset)
    {
        var sb = new StringBuilder();
        int i = offset;
        while (i < _bytes.Length && _bytes[i] != 0 && sb.Length < 512) { sb.Append((char)_bytes[i]); i++; }
        return sb.ToString();
    }

    // ------------------------------------------------------------------
    public sealed record DoubleHit(string Section, int Rva, int FileOffset, double Value, string Bits);

    /// <summary>
    /// Scan a section for IEEE-754 doubles that look like tuned algorithm constants:
    /// finite, of a plausible magnitude, and with a short decimal representation
    /// (i.e. round(v, 6) still describes it well). Grouping of consecutive hits is
    /// done by the caller so runs of related constants stay visible.
    /// </summary>
    public List<DoubleHit> ScanDoubles(string sectionName, double minAbs = 1e-8, double maxAbs = 1e6, int decimals = 6)
    {
        var list = new List<DoubleHit>();
        var sec = _sections.FirstOrDefault(s => s.Name == sectionName);
        if (sec == null) return list;
        int start = sec.PointerToRawData;
        int end = Math.Min(_bytes.Length - 8, sec.PointerToRawData + sec.SizeOfRawData);
        for (int p = start; p <= end; p += 8)
        {
            // Skip windows that are pure printable ASCII: "30BD-3940-11D3-ABFD" reads as a
            // perfectly plausible double, which is pure noise in a text-resource section.
            bool asciiRun = true;
            for (int k = 0; k < 8; k++)
            {
                byte b = _bytes[p + k];
                if (b < 0x20 || b > 0x7E) { asciiRun = false; break; }
            }
            if (asciiRun) continue;

            double v = BitConverter.ToDouble(_bytes, p);
            if (double.IsNaN(v) || double.IsInfinity(v)) continue;
            double a = Math.Abs(v);
            if (a < minAbs || a > maxAbs) continue;
            double rounded = Math.Round(v, decimals);
            if (rounded == 0) continue;
            double rel = Math.Abs((v - rounded) / rounded);
            if (rel > 1e-7) continue; // not a "short decimal" value
            list.Add(new DoubleHit(sec.Name, sec.VirtualAddress + (p - start), p, rounded,
                Convert.ToHexString(BitConverter.GetBytes(v))));
        }
        return list;
    }

    /// <summary>Find an exact IEEE-754 double bit pattern anywhere in the file.</summary>
    public List<int> FindDouble(double value)
    {
        var pat = BitConverter.GetBytes(value);
        var hits = new List<int>();
        int n = _bytes.Length - 8;
        for (int i = 0; i <= n; i++)
        {
            if (_bytes[i] != pat[0]) continue;
            bool ok = true;
            for (int k = 1; k < 8; k++) { if (_bytes[i + k] != pat[k]) { ok = false; break; } }
            if (ok) hits.Add(i);
        }
        return hits;
    }

    public static string HexDump(byte[] data, int fileOffset, int before = 24, int after = 40, int bytesPerLine = 16)
    {
        int start = Math.Max(0, fileOffset - before);
        int end = Math.Min(data.Length, fileOffset + after);
        // align start to bytesPerLine relative to the file so offsets read naturally
        start -= start % bytesPerLine;
        var sb = new StringBuilder();
        for (int p = start; p < end; p += bytesPerLine)
        {
            sb.Append(p.ToString("X8")).Append("  ");
            for (int k = 0; k < bytesPerLine; k++)
            {
                int q = p + k;
                sb.Append(q < end ? data[q].ToString("X2") : "  ");
                sb.Append(k == bytesPerLine / 2 - 1 ? "  " : " ");
            }
            sb.Append(" |");
            for (int k = 0; k < bytesPerLine; k++)
            {
                int q = p + k;
                if (q >= end) break;
                byte b = data[q];
                sb.Append(b >= 0x20 && b <= 0x7E ? (char)b : '.');
            }
            sb.Append('|');
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
#endif
