using PatMaxSharp.Core;
using PatMaxSharp.Edge;

namespace PatMaxSharp.Matching;

/// <summary>
/// Image pyramid of edge fields (levels of half resolution).
///
/// Coarse-to-fine search is what makes a full 360 degree / multi-scale search
/// affordable: a 100-pixel model is only ~25 pixels across at level 2, so the
/// exhaustive stage evaluates ~16x fewer positions, and each finer level only
/// has to look at a small neighbourhood around the surviving candidates.
/// </summary>
public sealed class SearchPyramid
{
    /// <summary>Downsampled images, index 0 = original resolution.</summary>
    public GrayImage[] Images { get; }

    /// <summary>Edge field of each level, same indexing as <see cref="Images"/>.</summary>
    public EdgeMap[] Edges { get; }

    /// <summary>Contrast statistics of each level, same indexing as <see cref="Images"/>.</summary>
    public LevelStats[] Stats { get; }

    /// <summary>Pixel scale of each level (1, 2, 4, ... - the level is downsampled by 2^i).</summary>
    public int[] Scales { get; }

    /// <summary>Number of levels that were built.</summary>
    public int LevelCount => Edges.Length;

    /// <summary>Edge field of one level.</summary>
    public EdgeMap this[int level] => Edges[level];

    private SearchPyramid(GrayImage[] images, EdgeMap[] edges, LevelStats[] stats)
    {
        Images = images;
        Edges = edges;
        Stats = stats;
        Scales = new int[edges.Length];
        for (int i = 0; i < edges.Length; i++) Scales[i] = 1 << i;
    }

    /// <summary>
    /// Build levels until the image (or the model footprint) becomes too small to be
    /// useful. <paramref name="minDim"/> is measured in pixels of the coarsest level.
    /// </summary>
    public static SearchPyramid Build(GrayImage image, GaborEdgeDetector detector, int maxLevels = 4, int minDim = 24)
    {
        maxLevels = Math.Clamp(maxLevels, 1, 6);
        var images = new List<GrayImage> { image };
        while (images.Count < maxLevels)
        {
            var last = images[^1];
            if (Math.Min(last.Width, last.Height) < minDim * 2) break;
            images.Add(last.Downsample2());
        }

        var edges = new EdgeMap[images.Count];
        var stats = new LevelStats[images.Count];
        for (int i = 0; i < images.Count; i++)
        {
            edges[i] = detector.Detect(images[i]);
            stats[i] = LevelStats.Compute(edges[i]);
        }
        return new SearchPyramid(images.ToArray(), edges, stats);
    }

    /// <summary>Level whose pixel size is closest to (but not larger than) <paramref name="scale"/>.</summary>
    public int LevelForScale(int scale)
    {
        int level = 0;
        for (int i = 0; i < Scales.Length; i++)
            if (Scales[i] <= scale) level = i;
        return level;
    }
}
