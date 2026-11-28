using ShapeAnchor.Core;
using ShapeAnchor.Edge;

namespace ShapeAnchor.Training;

/// <summary>
/// Training controls.
///
/// Named after the training knobs PMAlign actually exposes. Note that there is no
/// CogPMAlignTrainParams type in Cognex.VisionPro.PMAlign.dll - those knobs are spread over
/// CogPMAlignPattern (TrainAlgorithm / TrainMode / EdgeThreshold / GrainLimit* / Elasticity /
/// IgnorePolarity / HighSensitivityParameter / ExpectedDeformationRate) and
/// CogPMAlignComposite (TuningElasticity / ImageFraction / Timeout). Earlier notes in this
/// project that cited a "CogPMAlignTrainParams" surface were wrong on the type name.
/// </summary>
public sealed class TrainOptions
{
    /// <summary>Upper bound on stored features. PatMax keeps a few hundred to a few thousand.</summary>
    public int MaxFeatures { get; set; } = 600;

    /// <summary>Minimum distance between two stored features, pixels (Poisson-disk sampling).</summary>
    public double MinSpacing { get; set; } = 2.0;

    /// <summary>Discard features weaker than this fraction of the strongest edge in the region.</summary>
    public double ContrastThreshold { get; set; } = 0.08;

    /// <summary>Smoothing applied before edge extraction; the coarse/fine "grain" control.</summary>
    public double Grain { get; set; } = 1.0;

    /// <summary>Gradient magnitude below this is ignored.</summary>
    public double NoiseFloor { get; set; } = 8.0;

    /// <summary>Exponent denominator N of the score formula.</summary>
    public double N { get; set; } = 2.0;

    /// <summary>
    /// Ignore edge polarity: treat dark-on-light and light-on-dark as the same feature
    /// (CogPMAlignPattern.IgnorePolarity, default false).
    /// </summary>
    public bool IgnorePolarity { get; set; }
}

/// <summary>
/// Builds a pattern model out of a training region.
///
/// Feature selection is deliberately not "take every edge pixel": PatMax keeps a sparse,
/// spatially spread set of strong edge features, because (a) a smaller feature set makes the
/// exhaustive search cheap, and (b) spatially spread features make the least-squares pose
/// fit well conditioned. The three stages below — gradient-direction non-maximum suppression
/// (Canny-style thinning with a single threshold and no hysteresis), magnitude ordering, and
/// greedy min-distance selection over an occupancy grid (a Poisson-disk approximation)
/// — reproduce that behaviour.
/// </summary>
public static class PatternTrainer
{
    /// <summary>
    /// Train a model from <paramref name="roi"/> of <paramref name="image"/>. Features are
    /// accepted in three stages (gradient-direction thinning, strongest-first ordering, greedy
    /// min-distance selection) and the model is sealed before it is returned, so it is directly
    /// usable by the search engine.
    /// </summary>
    public static PatternModel Train(
        GrayImage image,
        RectI roi,
        double originX,
        double originY,
        GaborEdgeDetector detector,
        TrainOptions options = null,
        byte[] mask = null)
    {
        var opt = options ?? new TrainOptions();
        var region = roi.Clamp(image.Width, image.Height);
        if (region.IsEmpty) throw new ArgumentException("train region is empty after clamping", nameof(roi));

        var source = image;
        if (opt.Grain > 1.001)
            source = Blur(image, Math.Min(3.0, (opt.Grain - 1.0) * 0.5 + 0.5));

        var savedFloor = detector.NoiseFloor;
        detector.NoiseFloor = Math.Min(savedFloor, opt.NoiseFloor);
        EdgeMap edges;
        try
        {
            edges = detector.Detect(source);
        }
        finally
        {
            detector.NoiseFloor = savedFloor;
        }

        var model = new PatternModel
        {
            Roi = region,
            OriginX = originX,
            OriginY = originY,
            TrainWidth = image.Width,
            TrainHeight = image.Height,
            Grain = opt.Grain,
            N = opt.N,
            PolarityAgnostic = opt.IgnorePolarity
        };

        // ---- stage 1: thinned, strong edge pixels inside the region ----
        // Keep away from the image border: the convolution clamps at the edge, so pixels
        // within the kernel radius carry a synthetic gradient that has nothing to do with
        // the part. Those bogus features used to make the model bounding box larger than
        // the image, which in turn left the coarse stage with no valid position at all.
        int margin = Math.Max(2, detector.HalfSize);
        var candX = new List<int>();
        var candY = new List<int>();
        var candMag = new List<float>();

        int x0 = Math.Max(margin, region.X);
        int x1 = Math.Min(image.Width - margin, region.Right);
        int y0 = Math.Max(margin, region.Y);
        int y1 = Math.Min(image.Height - margin, region.Bottom);

        float maxMag = 0f;
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = y * image.Width + x;
                if (!edges.IsEdge[i]) continue;
                if (mask != null && mask[i] == 0) continue;   // Don't care: never enters the model
                float mag = edges.Magnitude[i];
                if (mag > maxMag) maxMag = mag;
            }
        if (maxMag <= 0) return model;

        float minMag = Math.Max((float)opt.NoiseFloor, maxMag * (float)Math.Clamp(opt.ContrastThreshold, 0, 0.95));

        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                int i = y * image.Width + x;
                if (!edges.IsEdge[i]) continue;
                if (mask != null && mask[i] == 0) continue;   // Don't care
                float mag = edges.Magnitude[i];
                if (mag < minMag) continue;

                // thin along the gradient: keep only ridge maxima
                float gx = edges.Gx[i], gy = edges.Gy[i];
                float inv = 1f / Math.Max(1e-6f, mag);
                int dx = (int)Math.Round(gx * inv * 0.9f);
                int dy = (int)Math.Round(gy * inv * 0.9f);
                if (dx == 0 && dy == 0) { dx = 1; }

                bool ridge = true;
                int ax = Math.Clamp(x + dx, 0, image.Width - 1);
                int ay = Math.Clamp(y + dy, 0, image.Height - 1);
                int bx = Math.Clamp(x - dx, 0, image.Width - 1);
                int by = Math.Clamp(y - dy, 0, image.Height - 1);
                if (edges.Magnitude[ay * image.Width + ax] > mag) ridge = false;
                else if (edges.Magnitude[by * image.Width + bx] > mag) ridge = false;
                if (!ridge) continue;

                candX.Add(x);
                candY.Add(y);
                candMag.Add(mag);
            }

        if (candMag.Count == 0) return model;

        // ---- stage 2: strongest first ----
        var order = new int[candMag.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        var keys = candMag.ToArray();
        Array.Sort(keys, order);
        Array.Reverse(order);

        // ---- stage 3: Poisson-disk selection over an occupancy grid ----
        double spacing = Math.Max(1.0, opt.MinSpacing);
        int cell = Math.Max(1, (int)Math.Ceiling(spacing));
        int gw = (image.Width + cell - 1) / cell;
        int gh = (image.Height + cell - 1) / cell;
        var occupied = new List<int>[gw * gh];

        int maxFeatures = Math.Clamp(opt.MaxFeatures, 8, 20000);
        double spacingSq = spacing * spacing;

        foreach (int idx in order)
        {
            if (model.Count >= maxFeatures) break;
            int x = candX[idx], y = candY[idx];

            int gx0 = x / cell, gy0 = y / cell;
            bool ok = true;
            for (int gy = Math.Max(0, gy0 - 1); gy <= Math.Min(gh - 1, gy0 + 1) && ok; gy++)
                for (int gx = Math.Max(0, gx0 - 1); gx <= Math.Min(gw - 1, gx0 + 1) && ok; gx++)
                {
                    var bucket = occupied[gy * gw + gx];
                    if (bucket == null) continue;
                    foreach (int other in bucket)
                    {
                        double ddx = candX[other] - x, ddy = candY[other] - y;
                        if (ddx * ddx + ddy * ddy < spacingSq) { ok = false; break; }
                    }
                }
            if (!ok) continue;

            int gi = gy0 * gw + gx0;
            (occupied[gi] ??= new List<int>()).Add(idx);

            int pi = y * image.Width + x;
            float gx2 = edges.Gx[pi], gy2 = edges.Gy[pi];
            float m = edges.Magnitude[pi];
            float invm = 1f / Math.Max(1e-6f, m);
            float nx = gx2 * invm, ny = gy2 * invm;

            // Canonicalise the normal so polarity is meaningful:
            // the normal always points rightwards (or upwards when vertical).
            sbyte polarity = 1;
            if (nx < 0 || (Math.Abs(nx) < 1e-6 && ny < 0))
            {
                nx = -nx; ny = -ny;
                polarity = -1;
            }


            model.AddFeature(x, y, nx, ny, m, polarity, 1.0f);
        }

        model.Seal();
        // Store the effective mask (cropped to the region) so the UI can show what was ignored.
        if (mask != null)
        {
            int mw = Math.Max(0, x1 - x0), mh = Math.Max(0, y1 - y0);
            if (mw > 0 && mh > 0)
            {
                var cropMask = new byte[mw * mh];
                for (int y = 0; y < mh; y++)
                    Array.Copy(mask, (y0 + y) * image.Width + x0, cropMask, y * mw, mw);
                model.Mask = cropMask;
            }
        }
        return model;
    }

    /// <summary>
    /// Bounding box of the object in an image: everything above (min + fraction x range).
    /// Used to train from a folder of images where the part position is not known in advance.
    /// </summary>
    public static RectI ContentBounds(GrayImage image, double fraction = 0.35)
    {
        byte min = 255, max = 0;
        foreach (byte v in image.Data) { if (v < min) min = v; if (v > max) max = v; }
        if (max <= min) return new RectI(0, 0, image.Width, image.Height);

        byte threshold = (byte)(min + (max - min) * Math.Clamp(fraction, 0.0, 1.0));
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.Data[y * image.Width + x] > threshold)
                {
                    if (x < x0) x0 = x;
                    if (y < y0) y0 = y;
                    if (x > x1) x1 = x;
                    if (y > y1) y1 = y;
                }

        if (x1 < 0) return new RectI(0, 0, image.Width, image.Height);
        return RectI.FromLTRB(x0, y0, x1 + 1, y1 + 1);
    }

    /// <summary>Separable Gaussian blur used for the coarse "grain" setting.</summary>
    public static GrayImage Blur(GrayImage src, double sigma)
    {
        if (sigma <= 0.01) return src;
        int r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var kernel = new float[2 * r + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++)
        {
            double v = Math.Exp(-(i * i) / (2 * sigma * sigma));
            kernel[i + r] = (float)v;
            sum += v;
        }
        for (int i = 0; i < kernel.Length; i++) kernel[i] = (float)(kernel[i] / sum);

        int w = src.Width, h = src.Height;
        var tmp = new float[w * h];
        var dst = new byte[w * h];

        for (int y = 0; y < h; y++)
        {
            int row = y * w;
            for (int x = 0; x < w; x++)
            {
                float acc = 0;
                for (int k = -r; k <= r; k++)
                {
                    int sx = Math.Clamp(x + k, 0, w - 1);
                    acc += src.Data[row + sx] * kernel[k + r];
                }
                tmp[row + x] = acc;
            }
        }
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float acc = 0;
                for (int k = -r; k <= r; k++)
                {
                    int sy = Math.Clamp(y + k, 0, h - 1);
                    acc += tmp[sy * w + x] * kernel[k + r];
                }
                dst[y * w + x] = (byte)Math.Clamp(acc, 0, 255);
            }

        return new GrayImage(w, h, dst);
    }
}
