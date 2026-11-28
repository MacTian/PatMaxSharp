namespace ShapeAnchor.Core;

/// <summary>
/// Integer axis-aligned rectangle (train region / search region).
/// Mirrors the semantics of CogRectangleAffine's bounding box in PMAlign.
/// </summary>
public readonly struct RectI
{
    /// <summary>Left edge (inclusive), pixels.</summary>
    public readonly int X;

    /// <summary>Top edge (inclusive), pixels.</summary>
    public readonly int Y;

    /// <summary>Width in pixels (0 or less makes the rectangle empty).</summary>
    public readonly int Width;

    /// <summary>Height in pixels (0 or less makes the rectangle empty).</summary>
    public readonly int Height;

    /// <summary>Build from the top-left corner plus a size (no clamping).</summary>
    public RectI(int x, int y, int width, int height)
    {
        X = x; Y = y; Width = width; Height = height;
    }

    /// <summary>Right edge (exclusive), pixels.</summary>
    public int Right => X + Width;

    /// <summary>Bottom edge (exclusive), pixels.</summary>
    public int Bottom => Y + Height;

    /// <summary>True when the rectangle has no interior.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Pixel count (0 when empty).</summary>
    public int Area => IsEmpty ? 0 : Width * Height;

    /// <summary>Build from left/top/right/bottom edges (right/bottom exclusive).</summary>
    public static RectI FromLTRB(int left, int top, int right, int bottom) =>
        new(left, top, right - left, bottom - top);

    /// <summary>Build a (2r+1)-square centred on the rounded (cx, cy).</summary>
    public static RectI FromCenter(double cx, double cy, int radius) =>
        new((int)Math.Round(cx) - radius, (int)Math.Round(cy) - radius, 2 * radius + 1, 2 * radius + 1);

    /// <summary>Clip to an image of the given size; returns an empty rect when nothing survives.</summary>
    public RectI Clamp(int width, int height)
    {
        int l = Math.Max(0, X), t = Math.Max(0, Y);
        int r = Math.Min(width, Right), b = Math.Min(height, Bottom);
        return r <= l || b <= t ? new RectI(0, 0, 0, 0) : FromLTRB(l, t, r, b);
    }

    /// <summary>True when the pixel lies inside (right/bottom edges exclusive).</summary>
    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;

    public override string ToString() => $"[{X},{Y} {Width}x{Height}]";
}
