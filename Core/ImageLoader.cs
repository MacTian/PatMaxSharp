namespace ShapeAnchor.Core;

/// <summary>
/// Format-dispatching image loader: PNG and BMP, chosen by file signature rather than by
/// extension, so a mislabelled file still loads.
/// </summary>
public static class ImageLoader
{
    /// <summary>Extensions this loader understands.</summary>
    public static readonly string[] SupportedExtensions = { ".png", ".bmp" };

    public static GrayImage Load(string path)
    {
        byte[] head = new byte[8];
        using (var fs = File.OpenRead(path))
        {
            int read = fs.Read(head, 0, head.Length);
            if (read < 8) throw new InvalidDataException($"file too short: {path}");
        }

        if (head[0] == 0x89 && head[1] == (byte)'P' && head[2] == (byte)'N' && head[3] == (byte)'G')
            return PngReader.Load(path);

        if (head[0] == (byte)'B' && head[1] == (byte)'M')
            return BmpReader.Load(path);

        throw new NotSupportedException($"unsupported image format: {Path.GetFileName(path)}");
    }

    /// <summary>Enumerate supported images in a folder, sorted naturally by name.</summary>
    public static List<string> Enumerate(string directory)
    {
        var files = Directory.GetFiles(directory)
            .Where(f => SupportedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        files.Sort((a, b) => NaturalCompare(Path.GetFileNameWithoutExtension(a),
                                            Path.GetFileNameWithoutExtension(b)));
        return files;
    }

    /// <summary>Compare so that "2" sorts before "10" (image sequences are numbered).</summary>
    private static int NaturalCompare(string a, string b)
    {
        int ia = 0, ib = 0;
        while (ia < a.Length && ib < b.Length)
        {
            if (char.IsDigit(a[ia]) && char.IsDigit(b[ib]))
            {
                int sa = ia, sb = ib;
                while (ia < a.Length && char.IsDigit(a[ia])) ia++;
                while (ib < b.Length && char.IsDigit(b[ib])) ib++;
                var na = long.Parse(a.AsSpan(sa, ia - sa));
                var nb = long.Parse(b.AsSpan(sb, ib - sb));
                if (na != nb) return na.CompareTo(nb);
            }
            else
            {
                int cmp = char.ToUpperInvariant(a[ia]).CompareTo(char.ToUpperInvariant(b[ib]));
                if (cmp != 0) return cmp;
                ia++;
                ib++;
            }
        }
        return (a.Length - ia).CompareTo(b.Length - ib);
    }
}
