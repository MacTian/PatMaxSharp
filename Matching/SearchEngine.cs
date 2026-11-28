using System.Diagnostics;
using ShapeAnchor.Core;
using ShapeAnchor.Edge;
using ShapeAnchor.Fit;

namespace ShapeAnchor.Matching;

/// <summary>
/// Coarse-to-fine PatMax-style search engine.
///
/// One run:
///   1. Build an edge-field pyramid of the search image.
///   2. Exhaustive stage at the coarsest useful level: for every (angle, scale) candidate,
///      correlate a small subset of model features against the signed gradient field.
///      The correlation is direction- and polarity-sensitive, which is what separates a
///      real instance from a similar-looking texture.
///   3. Refine the surviving hypotheses level by level - position window, then angle
///      window (with parabolic polish), then position again - growing the feature subset.
///   4. Score the final pose with the full feature set: coverage^(1/N), contrast and
///      clutter aware.
///   5. Refine the pose to sub-pixel accuracy: every matched feature searches along its
///      own edge normal for the true edge position, and a robust 2D similarity fit turns
///      those measurements into the reported (x, y, angle, scale).
///
/// Step 5 is the difference between "template found" and "metrology-grade pose". The
/// previous revision fitted the predicted points to themselves, so the fit was always the
/// identity and the reported position was the training origin.
/// </summary>
public sealed class SearchEngine
{
    /// <summary>
    /// Default search parameters, used when <see cref="Run"/> is called without an explicit
    /// options argument.
    /// </summary>
    public SearchOptions Options { get; set; } = new();

    /// <summary>Scoring rules (score = coverage^(1/N), clutter aware).</summary>
    public Scorer Scorer { get; } = new();

    /// <summary>
    /// Wall-clock deadline of the current run (TimeoutMs). Every stage checks it, so a run
    /// overruns by at most one candidate rather than ignoring the budget outright.
    /// </summary>
    private DateTime _deadline = DateTime.MaxValue;

    private bool Expired => DateTime.UtcNow >= _deadline;

    /// <summary>
    /// Run one search over <paramref name="image"/> for <paramref name="model"/>: build the
    /// edge pyramid, exhaust the coarse stage, refine level by level, score with the full
    /// feature set, then fit a sub-pixel pose. Results come back sorted by score, overlap
    /// suppressed and truncated to Options.MaxResults; an empty list means nothing survived
    /// the coarse gate. Timing and counters are written to <paramref name="diagnostics"/>.
    /// </summary>
    public List<MatchResult> Run(PatternModel model, GrayImage image, GaborEdgeDetector detector,
        SearchOptions options = null, SearchDiagnostics diagnostics = null)
    {
        var opt = options ?? Options;
        var diag = diagnostics ?? new SearchDiagnostics();
        var total = Stopwatch.StartNew();

        if (model == null || !model.Trained) throw new InvalidOperationException("pattern not trained");
        if (image == null) throw new ArgumentNullException(nameof(image));

        _deadline = opt.TimeoutMs > 0
            ? DateTime.UtcNow.AddMilliseconds(opt.TimeoutMs)
            : DateTime.MaxValue;

        var pyramid = SearchPyramid.Build(image, detector, opt.MaxLevels);
        diag.PyramidLevels = pyramid.LevelCount;
        diag.EdgeMs = total.Elapsed.TotalMilliseconds;

        var angleList = BuildAngleList(opt);
        var scaleList = BuildScaleList(opt);
        int coarseLevel = ChooseCoarseLevel(model, pyramid);
        diag.CoarseLevel = coarseLevel;

        var watch = Stopwatch.StartNew();
        var hypotheses = CoarseStage(model, pyramid, coarseLevel, angleList, scaleList, opt, diag);
        diag.CoarseMs = watch.Elapsed.TotalMilliseconds;

        if (hypotheses.Count == 0)
        {
            diag.TotalMs = total.Elapsed.TotalMilliseconds;
            return new List<MatchResult>();
        }

        watch.Restart();
        for (int level = coarseLevel - 1; level >= 0; level--)
            hypotheses = RefineLevel(model, pyramid, level, hypotheses, opt, level == 0);
        diag.RefineMs = watch.Elapsed.TotalMilliseconds;

        watch.Restart();
        var level0 = pyramid[0];
        var stats0 = pyramid.Stats[0];

        // Cheap pre-score with the full feature set (no pose fit), so the expensive part runs
        // only on hypotheses that can still reach the accept threshold.
        float invRef0 = 1f / Math.Max(1f, stats0.MagnitudeReference);
        double minResp0 = Math.Max(0.5, stats0.MagnitudeReference * opt.ContrastThreshold);
        double exponent0 = 1.0 / Math.Max(1.0, model.N);
        double preGate = Math.Clamp(opt.AcceptThreshold * 0.85, 0.0, 1.0);

        var pre = new List<(Hypothesis H, double Score)>(hypotheses.Count);
        foreach (var h in hypotheses)
        {
            var om = BuildOrientedModel(model, int.MaxValue, h.Angle, h.Scale, pyramid.Scales[0]);
            ScoreAtBilinear(level0, om, h.X, h.Y, invRef0, minResp0,
                opt.SearchMask, opt.SearchMaskWidth, opt.SearchMaskHeight, opt.MaskScoring,
                out int m, out double mw, out _, out _, out _, out _);
            double cov = mw / Math.Max(1e-6, om.TotalWeight);
            double s = Math.Pow(Math.Clamp(cov, 0, 1), exponent0);
            if (s >= preGate) pre.Add((h, s));
        }

        pre.Sort((a, b) =>
        {
            int c = b.Score.CompareTo(a.Score);
            return c != 0 ? c : CompareHypotheses(a.H, b.H);
        });

        // Keep hypotheses that are competitive with the best one (that is what a second
        // instance looks like) and drop the also-rans before paying for a pose fit.
        double relativeGate = pre.Count > 0 ? pre[0].Score * 0.80 : double.MaxValue;
        int verifyBudget = Math.Clamp(Math.Max(opt.MaxResults * 2, 3), 3, 8);
        var shortlist = new List<(Hypothesis H, double Score)>(verifyBudget);
        foreach (var item in pre)
        {
            if (item.Score < relativeGate) continue;
            shortlist.Add(item);
            if (shortlist.Count >= verifyBudget) break;
        }

        var results = new List<MatchResult>(shortlist.Count);
        foreach (var (h, _) in shortlist)
        {
            if (Expired) { diag.TimedOut = true; break; }
            var result = Verify(model, level0, stats0, h, pyramid.Scales[0], opt);
            result.PeakValue = h.Score;
            results.Add(result);
        }
        results = SuppressOverlaps(results, model, opt);
        diag.VerifyMs = watch.Elapsed.TotalMilliseconds;

        diag.TotalMs = total.Elapsed.TotalMilliseconds;
        int take = Math.Max(1, opt.MaxResults);
        return results.Count > take ? results.GetRange(0, take) : results;
    }

    // ------------------------------------------------------------------
    //  Search space
    // ------------------------------------------------------------------
    private static List<double> BuildAngleList(SearchOptions opt)
    {
        var list = new List<double>();
        double lo = opt.AngleMin, hi = opt.AngleMax;
        if (hi < lo) (lo, hi) = (hi, lo);
        double span = hi - lo;

        if (span < 1e-9) { list.Add(lo * Math.PI / 180.0); return list; }

        double step = Math.Max(0.5, opt.CoarseAngleStepDeg);
        const int maxAngles = 48;
        if (span / step > maxAngles) step = span / maxAngles;

        for (double a = lo; a <= hi + 1e-9; a += step) list.Add(a * Math.PI / 180.0);
        if (list.Count == 0) list.Add(0);
        return list;
    }

    private static List<double> BuildScaleList(SearchOptions opt)
    {
        var list = new List<double>();
        double lo = Math.Min(opt.ScaleMin, opt.ScaleMax);
        double hi = Math.Max(opt.ScaleMin, opt.ScaleMax);
        if (lo <= 0) lo = 1.0;
        if (hi <= 0) hi = lo;

        if (hi - lo < 1e-9) { list.Add(lo); return list; }

        // Six samples were too coarse: with a 0.6-1.6 window the grid was 0.6/0.8/1.0/1.2/1.4/1.6,
        // so a part at 1.51 was never tested and only its wrong-looking neighbours were refined.
        // The scale stage is cheap next to the angle sweep, so sample every ~5% instead.
        const int maxScales = 14;
        double step = Math.Max(0.05, (hi - lo) / (maxScales - 1));
        for (double s = lo; s <= hi + 1e-9; s += step) list.Add(s);
        if (list.Count == 0) list.Add(1.0);
        return list;
    }

    /// <summary>Coarsest level where the model still spans enough pixels to be discriminative.</summary>
    private static int ChooseCoarseLevel(PatternModel model, SearchPyramid pyramid)
    {
        int level = 0;
        for (int l = 1; l < pyramid.LevelCount; l++)
        {
            double radius = model.ExtentRadius / pyramid.Scales[l];
            double minDim = Math.Min(pyramid[l].Width, pyramid[l].Height);
            if (radius < 8.0) break;
            if (radius * 2.4 > minDim) break;
            level = l;
        }
        return level;
    }

    // ------------------------------------------------------------------
    //  Oriented model view
    // ------------------------------------------------------------------
    private sealed class OrientedModel
    {
        public int Count;
        public float[] Ox = Array.Empty<float>();
        public float[] Oy = Array.Empty<float>();
        public float[] Nx = Array.Empty<float>();
        public float[] Ny = Array.Empty<float>();
        public float[] W = Array.Empty<float>();
        public int[] Ix = Array.Empty<int>();
        public int[] Iy = Array.Empty<int>();
        public float TotalWeight;
        public float MinX, MaxX, MinY, MaxY;

        /// <summary>Compare |response| instead of the signed response (IgnorePolarity).</summary>
        public bool Agnostic;
    }

    /// <summary>
    /// Project the model through (angle, scale) and down to <paramref name="pixelScale"/>
    /// level pixels. Normals are pre-multiplied by polarity, so the scoring inner loop only
    /// needs one dot product and a sign test.
    /// </summary>
    private static OrientedModel BuildOrientedModel(PatternModel model, int maxFeatures,
        double angle, double scale, double pixelScale)
    {
        int total = model.Count;
        int stride = Math.Max(1, (int)Math.Ceiling((double)total / Math.Max(1, maxFeatures)));
        int count = (total + stride - 1) / stride;

        var om = new OrientedModel
        {
            Count = count,
            Ox = new float[count], Oy = new float[count],
            Nx = new float[count], Ny = new float[count],
            W = new float[count],
            Ix = new int[count], Iy = new int[count]
        };

        double cos = Math.Cos(angle), sin = Math.Sin(angle);
        double k = scale / pixelScale;

        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        double totalWeight = 0;

        for (int j = 0; j < count; j++)
        {
            int i = j * stride;
            double rx = model.Px[i] - model.OriginX;
            double ry = model.Py[i] - model.OriginY;

            float ox = (float)((cos * rx - sin * ry) * k);
            float oy = (float)((sin * rx + cos * ry) * k);

            float nx = (float)(cos * model.Nx[i] - sin * model.Ny[i]);
            float ny = (float)(sin * model.Nx[i] + cos * model.Ny[i]);
            float pol = model.Polarity[i];

            om.Ox[j] = ox; om.Oy[j] = oy;
            om.Nx[j] = nx * pol; om.Ny[j] = ny * pol;
            om.W[j] = model.Weights[i];
            om.Ix[j] = (int)Math.Round(ox);
            om.Iy[j] = (int)Math.Round(oy);

            totalWeight += om.W[j];
            if (ox < minX) minX = ox;
            if (ox > maxX) maxX = ox;
            if (oy < minY) minY = oy;
            if (oy > maxY) maxY = oy;
        }

        om.TotalWeight = (float)Math.Max(1e-6, totalWeight);
        om.MinX = minX; om.MaxX = maxX; om.MinY = minY; om.MaxY = maxY;
        om.Agnostic = model.PolarityAgnostic;
        return om;
    }

    // ------------------------------------------------------------------
    //  Scoring primitives
    // ------------------------------------------------------------------
    /// <summary>Nearest-neighbour correlation surface (coarse stage: ~2x faster than bilinear).</summary>
    private static void ScoreSurfaceFast(EdgeMap map, OrientedModel om, int stride,
        int loX, int hiX, int loY, int hiY, float invRef, bool normalize,
        double[] surface, int sw, out double maxValue)
    {
        var gx = map.Gx;
        var gy = map.Gy;
        int w = map.Width, h = map.Height;

        var nx = om.Nx; var ny = om.Ny; var wt = om.W;
        int n = om.Count;
        double invWeight = 1.0 / om.TotalWeight;
        double max = 0;
        int rows = (hiY - loY) / stride + 1;
        int cols = (hiX - loX) / stride + 1;

        for (int iy = 0; iy < rows; iy++)
        {
            int cy = loY + iy * stride;
            int row = iy * sw;
            for (int ix = 0; ix < cols; ix++)
            {
                int cx = loX + ix * stride;
                double acc = 0, energy = 0;
                int inBounds = 0;
                for (int f = 0; f < n; f++)
                {
                    int px = cx + om.Ix[f];
                    int py = cy + om.Iy[f];
                    if ((uint)px >= (uint)w || (uint)py >= (uint)h) continue;
                    int idx = py * w + px;
                    double d = gx[idx] * nx[f] + gy[idx] * ny[f];
                    if (om.Agnostic) d = Math.Abs(d);
                    if (d > 0) acc += d * wt[f];
                    energy += map.Magnitude[idx];
                    inBounds++;
                }

                // Normalised correlation: divide by the mean edge strength under the footprint,
                // so a busy, high-contrast clutter region cannot win by sheer edge density and a
                // globally darker frame cannot lose by sheer gain. A perfect, polarity-correct
                // match returns ~1.
                double s;
                if (normalize && inBounds > 0)
                {
                    // Gain-invariant variant, kept as an option. Measured on a 17-image factory
                    // set it is a wash: it fixes a dim frame but breaks a part seen through
                    // plastic bags, because there the local energy is high everywhere and the
                    // ratio stops discriminating. So it is off by default.
                    double localRef = energy / inBounds;
                    s = acc * invWeight / Math.Max(localRef, 1e-3);
                }
                else s = acc * invWeight * invRef;
                surface[row + ix] = s;
                if (s > max) max = s;
            }
        }
        maxValue = max;
    }

    /// <summary>Clamped bilinear coverage score - the final scoring criterion.</summary>
    private static double ScoreAtBilinear(EdgeMap map, OrientedModel om, double cx, double cy, float invRef,
        double minResponse, byte[] mask, int maskW, int maskH, MaskScoringMethod maskScoring,
        out int matchedCount, out double matchedWeight, out double contrastSum,
        out double polarityCoherence, out double visibleWeight, out int visibleCount)
    {
        var gx = map.Gx; var gy = map.Gy;
        int w = map.Width, h = map.Height;
        double acc = 0, wsum = 0, contrast = 0;
        double posWeight = 0, negWeight = 0;
        double visible = 0;
        int matched = 0, visibleN = 0;
        int n = om.Count;

        for (int f = 0; f < n; f++)
        {
            double px = cx + om.Ox[f];
            double py = cy + om.Oy[f];
            int x0 = (int)px, y0 = (int)py;
            if (x0 < 0 || y0 < 0 || x0 >= w - 1 || y0 >= h - 1) continue;
            double fx = px - x0, fy = py - y0;
            int i0 = y0 * w + x0, i1 = i0 + w;

            double gxb = (gx[i0] * (1 - fx) + gx[i0 + 1] * fx) * (1 - fy) + (gx[i1] * (1 - fx) + gx[i1 + 1] * fx) * fy;
            double gyb = (gy[i0] * (1 - fx) + gy[i0 + 1] * fx) * (1 - fy) + (gy[i1] * (1 - fx) + gy[i1 + 1] * fx) * fy;

            // Care / Don't-care at run time. A feature over a Don't-care pixel is either removed
            // from the score (numerator *and* denominator) or kept as clutter, which is the
            // difference between DoNotScoreFeatures and ScoreFeaturesAsClutter.
            if (mask != null)
            {
                int mx = (int)px, my = (int)py;
                bool care = mx >= 0 && my >= 0 && mx < maskW && my < maskH && mask[my * maskW + mx] != 0;
                if (!care && maskScoring == MaskScoringMethod.DoNotScoreFeatures) continue;
                visible += om.W[f];
                visibleN++;
            }

            double signed = gxb * om.Nx[f] + gyb * om.Ny[f];
            double d = om.Agnostic ? Math.Abs(signed) : signed;
            if (d >= minResponse)
            {
                matched++;
                wsum += om.W[f];
                // Whether the image edge points the same way as the model normal. A real instance
                // is coherent (all positive, or all negative when the contrast is inverted); a
                // pattern that merely shares edge density lands on both signs about equally.
                if (signed >= 0) posWeight += om.W[f]; else negWeight += om.W[f];
                contrast += d;
                double rel = d * invRef;
                acc += om.W[f] * (rel < 1.0 ? rel : 1.0);
            }
        }

        matchedCount = matched;
        matchedWeight = wsum;
        contrastSum = contrast;
        polarityCoherence = wsum > 0 ? Math.Max(posWeight, negWeight) / wsum : 0;
        visibleWeight = mask != null ? visible : om.TotalWeight;
        visibleCount = mask != null ? visibleN : om.Count;
        return acc / Math.Max(1e-9, visibleWeight);
    }

    /// <summary>
    /// Unclamped correlation score. Unlike the clamped variant this keeps discriminating
    /// once every feature already matches, which an angular refinement needs - the clamped
    /// version saturates and produces ties at the window edge.
    /// </summary>
    private static double ScoreSumBilinear(EdgeMap map, OrientedModel om, double cx, double cy, float invRef,
        double minResponse, out double signedAgreement)
    {
        var gx = map.Gx; var gy = map.Gy;
        int w = map.Width, h = map.Height;
        double acc = 0, energy = 0;
        double signedAcc = 0, absAcc = 0;
        int inBounds = 0;
        int n = om.Count;

        for (int f = 0; f < n; f++)
        {
            double px = cx + om.Ox[f];
            double py = cy + om.Oy[f];
            int x0 = (int)px, y0 = (int)py;
            if (x0 < 0 || y0 < 0 || x0 >= w - 1 || y0 >= h - 1) continue;
            double fx = px - x0, fy = py - y0;
            int i0 = y0 * w + x0, i1 = i0 + w;

            double gxb = (gx[i0] * (1 - fx) + gx[i0 + 1] * fx) * (1 - fy) + (gx[i1] * (1 - fx) + gx[i1 + 1] * fx) * fy;
            double gyb = (gy[i0] * (1 - fx) + gy[i0 + 1] * fx) * (1 - fy) + (gy[i1] * (1 - fx) + gy[i1 + 1] * fx) * fy;

            double signed = gxb * om.Nx[f] + gyb * om.Ny[f];
            // Ranking signal for polarity-agnostic models: the signed accumulator agrees only
            // when the image edge points the way the model normal does. A pose that merely shares
            // edge density cancels itself out here, which is what keeps a shrunken copy of the
            // pattern from outranking the real instance.
            double sPos = signed - minResponse;
            if (sPos > 0) signedAcc += om.W[f] * sPos;
            double sAbs = Math.Abs(signed) - minResponse;
            if (sAbs > 0) absAcc += om.W[f] * sAbs;

            double d = om.Agnostic ? Math.Abs(signed) : signed;
            d -= minResponse;
            if (d > 0) acc += om.W[f] * d;

            energy += Math.Sqrt(gxb * gxb + gyb * gyb);
            inBounds++;
        }

        // The refinement stays on the absolute (gain-sensitive) score on purpose: normalising
        // here makes a slightly mis-scaled model score as well as the right one, because the
        // leftover energy of the wrong footprint is divided away. Normalisation belongs to the
        // coarse candidate stage, which only has to decide *where* to look.
        _ = energy; _ = inBounds;
        signedAgreement = absAcc > 1e-9 ? signedAcc / absAcc : 0.0;
        return acc * invRef / om.TotalWeight;
    }

    /// <summary>
    /// Range of origin positions worth testing. Normally the model's own footprint, rounded
    /// outwards so a position sitting exactly on the boundary is not lost; if the model does
    /// not fit inside the image at all (a part clipped by the frame, or a model larger than
    /// the image) the whole image is scanned and the scorer simply skips the features that
    /// fall outside - which is what partial visibility means.
    /// </summary>
    private static void OriginRange(OrientedModel om, int width, int height,
        out int loX, out int hiX, out int loY, out int hiY)
    {
        loX = (int)Math.Floor(-om.MinX);
        hiX = (int)Math.Ceiling(width - 1 - om.MaxX);
        loY = (int)Math.Floor(-om.MinY);
        hiY = (int)Math.Ceiling(height - 1 - om.MaxY);

        if (loX > hiX) { loX = 1; hiX = Math.Max(1, width - 2); }
        else { loX = Math.Max(1, loX); hiX = Math.Min(width - 2, hiX); }

        if (loY > hiY) { loY = 1; hiY = Math.Max(1, height - 2); }
        else { loY = Math.Max(1, loY); hiY = Math.Min(height - 2, hiY); }
    }

    /// <summary>Local position search with parabolic sub-pixel interpolation.</summary>
    private static void SearchWindow(EdgeMap map, OrientedModel om, double x0, double y0, int win,
        float invRef, double minResp, out double bestX, out double bestY, out double bestScore)
    {
        bestX = x0; bestY = y0; bestScore = 0;

        OriginRange(om, map.Width, map.Height, out int loX, out int hiX, out int loY, out int hiY);
        if (loX > hiX || loY > hiY) return;

        int cx = (int)Math.Round(x0), cy = (int)Math.Round(y0);
        int xa = Math.Max(loX, cx - win), xb = Math.Min(hiX, cx + win);
        int ya = Math.Max(loY, cy - win), yb = Math.Min(hiY, cy + win);

        double best = double.MinValue;
        int bestIx = xa, bestIy = ya;

        for (int y = ya; y <= yb; y++)
            for (int x = xa; x <= xb; x++)
            {
                double s = ScoreSumBilinear(map, om, x, y, invRef, minResp, out _);
                if (s > best) { best = s; bestIx = x; bestIy = y; }
            }

        double dx = 0, dy = 0;
        if (bestIx > xa && bestIx < xb)
        {
            double v0 = ScoreSumBilinear(map, om, bestIx - 1, bestIy, invRef, minResp, out _);
            double v2 = ScoreSumBilinear(map, om, bestIx + 1, bestIy, invRef, minResp, out _);
            double denom = v0 - 2 * best + v2;
            if (Math.Abs(denom) > 1e-12) dx = Math.Clamp(0.5 * (v0 - v2) / denom, -0.5, 0.5);
        }
        if (bestIy > ya && bestIy < yb)
        {
            double v0 = ScoreSumBilinear(map, om, bestIx, bestIy - 1, invRef, minResp, out _);
            double v2 = ScoreSumBilinear(map, om, bestIx, bestIy + 1, invRef, minResp, out _);
            double denom = v0 - 2 * best + v2;
            if (Math.Abs(denom) > 1e-12) dy = Math.Clamp(0.5 * (v0 - v2) / denom, -0.5, 0.5);
        }

        bestX = bestIx + dx;
        bestY = bestIy + dy;
        bestScore = best;
    }

    // ------------------------------------------------------------------
    //  Coarse stage
    // ------------------------------------------------------------------
    private struct Hypothesis
    {
        public double X, Y, Angle, Scale, Score;
    }

    /// <summary>
    /// Total order for hypotheses. Sorting on score alone leaves ties in whatever order the
    /// parallel workers happened to finish, and the merge/prune steps downstream then pick a
    /// different survivor on every run: the same image could score 0.895 or 0.562 depending on
    /// thread timing. The tie-breakers make a run reproducible.
    /// </summary>
    private static int CompareHypotheses(Hypothesis a, Hypothesis b)
    {
        int c = b.Score.CompareTo(a.Score);
        if (c != 0) return c;
        // Prefer the smaller rotation on a tie: the search prior is "near nominal", and
        // ordering by signed angle instead used to pick the most negative branch.
        c = Math.Abs(a.Angle).CompareTo(Math.Abs(b.Angle));
        if (c != 0) return c;
        c = a.Angle.CompareTo(b.Angle);
        if (c != 0) return c;
        c = a.X.CompareTo(b.X);
        if (c != 0) return c;
        c = a.Y.CompareTo(b.Y);
        if (c != 0) return c;
        return a.Scale.CompareTo(b.Scale);
    }

    private List<Hypothesis> CoarseStage(PatternModel model, SearchPyramid pyramid, int level,
        List<double> angles, List<double> scales, SearchOptions opt, SearchDiagnostics diag)
    {
        var map = pyramid[level];
        var stats = pyramid.Stats[level];
        double pixelScale = pyramid.Scales[level];
        float invRef = 1f / Math.Max(1f, stats.MagnitudeReference);
        int stride = Math.Max(1, opt.CoarseStride);

        var candidates = new System.Collections.Concurrent.ConcurrentBag<Hypothesis>();
        int combos = angles.Count * scales.Count;
        int scored = 0;

        Parallel.For(0, combos, combo =>
        {
            if (Expired) return;
            int ai = combo / scales.Count;
            int si = combo % scales.Count;
            double angle = angles[ai];
            double scale = scales[si];

            var om = BuildOrientedModel(model, opt.CoarseFeatureCount, angle, scale, pixelScale);

            OriginRange(om, map.Width, map.Height, out int loX, out int hiX, out int loY, out int hiY);
            if (loX > hiX || loY > hiY) return;

            int sw = (hiX - loX) / stride + 1;
            int sh = (hiY - loY) / stride + 1;
            var surface = new double[sw * sh];

            ScoreSurfaceFast(map, om, stride, loX, hiX, loY, hiY, invRef, opt.NormalizedCorrelation, surface, sw, out double maxValue);
            System.Threading.Interlocked.Add(ref scored, sw * sh);
            if (maxValue <= 1e-6) return;

            double minValue = Math.Max(0.10, maxValue * 0.30);
            int suppression = Math.Max(2, (int)Math.Ceiling(model.ExtentRadius / pixelScale / 2.5));
            var peaks = PeakDetector.FindPeaks2D(surface, sw, sh, suppression, 4, minValue);

            foreach (var p in peaks)
            {
                var (dx, dy) = PeakDetector.SubCellOffset(surface, sw, (int)p.X, (int)p.Y);
                candidates.Add(new Hypothesis
                {
                    X = loX + ((int)p.X + dx) * stride,
                    Y = loY + ((int)p.Y + dy) * stride,
                    Angle = angle,
                    Scale = scale,
                    Score = p.Value
                });
            }
        });

        diag.ScoredPositions = scored;
        var raw = candidates.ToList();

        // Score each raw peak properly (coverage over the coarse feature subset) and gate it
        // with CoarseAcceptThreshold. Ranking by the correlation peak alone mixes scales and
        // angles; coverage is on the same 0..1 scale as the final score.
        var list = new List<Hypothesis>(raw.Count);
        double coarseMin = Math.Clamp(opt.CoarseAcceptThreshold, 0.0, 1.0);
        // half the final contrast floor: the coarse level is blurred and its features are a
        // subsample, so demanding the full floor here would reject real parts
        float coarseFloor = stats.MagnitudeReference * (float)opt.ContrastThreshold * 0.5f;
        foreach (var cand in raw)
        {
            var om = BuildOrientedModel(model, opt.CoarseFeatureCount, cand.Angle, cand.Scale, pixelScale);
            double coverage = ScoreAtBilinear(map, om, cand.X, cand.Y, invRef, coarseFloor,
                                              opt.SearchMask, opt.SearchMaskWidth, opt.SearchMaskHeight,
                                              opt.MaskScoring,
                                              out _, out _, out _, out double coherence,
                                              out double coarseVisible, out _);
            if (coverage < coarseMin) continue;
            // A position where the mask hides most of the model would score high on very little
            // evidence, so require the model to be substantially visible before it can compete.
            if (opt.SearchMask != null && coarseVisible < 0.35 * om.TotalWeight) continue;
            var scoredHyp = cand;
            // Polarity-agnostic ranking. Coverage alone cannot tell a real instance from a pose
            // that only shares edge density, and both score ~1.0, so the wrong one used to win the
            // candidate slot and the correct pose never reached refinement. Coherence separates
            // them cleanly (real ~1.0, wrong ~0.5); 2c-1 maps that onto 0..1, so a 0.5 pose keeps
            // nothing of its coverage score while a real instance keeps all of it.
            scoredHyp.Score = model.PolarityAgnostic
                ? coverage * Math.Clamp(2.0 * coherence - 1.0, 0.0, 1.0)
                : coverage;
            list.Add(scoredHyp);
        }
        if (list.Count == 0) list.AddRange(raw.Take(Math.Min(4, raw.Count)));

        list.Sort(CompareHypotheses);

        // Two passes, because either rule alone fails on real data:
        //  * a pure score cut can hand every slot to one wrong angle branch, since a coarse level
        //    sees a blurry, nearly rotationally symmetric shape;
        //  * a pure per-bucket quota can silently drop the strongest candidate once its bucket is
        //    full, which is how a well-centred instance used to be lost.
        // So the strongest half always goes through, and bucket diversity fills the rest.
        int budget = Math.Max(1, opt.MaxCandidates);
        int guaranteed = Math.Max(1, budget / 2);

        var selected = new List<Hypothesis>(budget);
        var seen = new HashSet<int>();
        for (int i = 0; i < list.Count && selected.Count < guaranteed; i++)
        {
            selected.Add(list[i]);
            seen.Add(i);
        }

        var perBucket = new Dictionary<int, int>();
        int buckets = 24;
        for (int i = 0; i < list.Count && selected.Count < budget; i++)
        {
            if (seen.Contains(i)) continue;
            var h = list[i];
            int bucket = (int)Math.Round((h.Angle * 180 / Math.PI + 360) % 360 / (360.0 / buckets)) % buckets;
            perBucket.TryGetValue(bucket, out int n);
            if (n >= 2) continue;
            perBucket[bucket] = n + 1;
            selected.Add(h);
        }

        // still short of budget: fill from the score order
        for (int i = 0; i < list.Count && selected.Count < budget; i++)
            if (!seen.Contains(i)) selected.Add(list[i]);

        selected.Sort(CompareHypotheses);
        diag.CoarseCandidates = selected.Count;
        return selected;
    }

    // ------------------------------------------------------------------
    //  Refinement (position -> angle -> position at each level)
    // ------------------------------------------------------------------
    private List<Hypothesis> RefineLevel(PatternModel model, SearchPyramid pyramid, int level,
        List<Hypothesis> input, SearchOptions opt, bool finestLevel)
    {
        var map = pyramid[level];
        var stats = pyramid.Stats[level];
        double pixelScale = pyramid.Scales[level];
        float invRef = 1f / Math.Max(1f, stats.MagnitudeReference);

        double minResp = Math.Max(0.5, stats.MagnitudeReference * opt.ContrastThreshold);
        int featureCount = finestLevel ? model.Count : opt.MidFeatureCount;
        double angleStep = (finestLevel ? opt.FineAngleStepDeg : opt.MidAngleStepDeg) * Math.PI / 180.0;
        int angleSamples = finestLevel ? 4 : 3;

        double ratio = level + 1 < pyramid.Scales.Length
            ? (double)pyramid.Scales[level + 1] / pixelScale
            : 2.0;
        int win = Math.Max(2, (int)Math.Ceiling(opt.CoarseStride * ratio * 0.75)) + 1;

        var outp = new List<Hypothesis>(input.Count);
        object gate = new();

        Parallel.For(0, input.Count, i =>
        {
            if (Expired) return;
            var h = input[i];
            double x0 = h.X * ratio, y0 = h.Y * ratio;

            // ---- A: position at the incoming angle ----
            var om = BuildOrientedModel(model, featureCount, h.Angle, h.Scale, pixelScale);
            SearchWindow(map, om, x0, y0, win, invRef, minResp, out double bx, out double by, out double bestPos);

            // ---- B: angle at the best position (with parabolic polish) ----
            int samples = 2 * angleSamples + 1;
            var profile = new double[samples];
            double bestAng = h.Angle, bestScore = double.MinValue;
            for (int a = 0; a < samples; a++)
            {
                double ang = h.Angle + (a - angleSamples) * angleStep;
                var omA = a == angleSamples ? om : BuildOrientedModel(model, featureCount, ang, h.Scale, pixelScale);
                double s = ScoreSumBilinear(map, omA, bx, by, invRef, minResp, out _);
                profile[a] = s;
                if (s > bestScore) { bestScore = s; bestAng = ang; }
            }

            int peak = 0;
            for (int a = 1; a < samples; a++) if (profile[a] > profile[peak]) peak = a;
            if (peak > 0 && peak < samples - 1)
            {
                double v0 = profile[peak - 1], v1 = profile[peak], v2 = profile[peak + 1];
                double denom = v0 - 2 * v1 + v2;
                if (Math.Abs(denom) > 1e-12)
                {
                    double frac = Math.Clamp(0.5 * (v0 - v2) / denom, -0.5, 0.5);
                    bestAng = h.Angle + (peak - angleSamples + frac) * angleStep;
                }
            }

            // ---- C: position again at the refined angle ----
            var omB = BuildOrientedModel(model, featureCount, bestAng, h.Scale, pixelScale);
            SearchWindow(map, omB, bx, by, Math.Max(1, win / 2), invRef, minResp, out bx, out by, out double bestPos2);

            lock (gate)
                outp.Add(new Hypothesis
                {
                    X = bx, Y = by,
                    Angle = bestAng,
                    Scale = h.Scale,
                    Score = Math.Max(bestScore, Math.Max(bestPos, bestPos2))
                });
        });

        outp.Sort(CompareHypotheses);

        // Spatially distinct hypotheses must survive: without this, several instances of
        // the same pattern collapse into the single best peak and only one is reported.
        // Two hypotheses at the same place but a different angle are *different solutions*
        // (a symmetric or partially occluded part has several), so both must be kept until
        // the full-feature score can decide between them.
        double minSep = Math.Max(2.0, model.ExtentRadius / pixelScale * 0.6);
        double angleSep = 12.0 * Math.PI / 180.0;
        int maxKeep = finestLevel
            ? Math.Clamp(opt.MaxResults * 2, 6, 12)
            : Math.Max(1, opt.MaxCandidates);
        outp = Dedupe(outp, minSep, angleSep, maxKeep);
        return outp;
    }

    /// <summary>Keep the strongest hypothesis per (position, angle) cluster.</summary>
    private static List<Hypothesis> Dedupe(List<Hypothesis> input, double minSep, double angleSep, int maxKeep)
    {
        var kept = new List<Hypothesis>(Math.Min(maxKeep, input.Count));
        double sepSq = minSep * minSep;
        foreach (var h in input)
        {
            bool duplicate = false;
            foreach (var k in kept)
            {
                double dx = k.X - h.X, dy = k.Y - h.Y;
                if (dx * dx + dy * dy >= sepSq) continue;
                if (Math.Abs(NormalizeAngle(k.Angle - h.Angle)) >= angleSep) continue;
                duplicate = true;
                break;
            }
            if (duplicate) continue;
            kept.Add(h);
            if (kept.Count >= maxKeep) break;
        }
        return kept;
    }

    // ------------------------------------------------------------------
    //  Verification + sub-pixel pose
    // ------------------------------------------------------------------
    private MatchResult Verify(PatternModel model, EdgeMap map, LevelStats stats, Hypothesis h,
        double pixelScale, SearchOptions opt)
    {
        float invRef = 1f / Math.Max(1f, stats.MagnitudeReference);

        double x = h.X, y = h.Y, angle = h.Angle, scale = h.Scale;
        double fitError = 0;

        if (opt.SubpixelRefinement)
        {
            var pose = RefinePose(model, map, stats, x, y, angle, scale, opt);
            if (pose.HasValue)
            {
                var p = pose.Value;
                double dAng = NormalizeAngle(p.Angle - angle);
                double dScale = p.Scale / Math.Max(1e-6, scale);
                if (Math.Abs(dAng) < 0.25 && dScale > 0.75 && dScale < 1.35)
                {
                    x = p.X; y = p.Y; angle = p.Angle; scale = p.Scale;
                    fitError = p.Rms;
                }
            }
        }

        var omFinal = BuildOrientedModel(model, int.MaxValue, angle, scale, pixelScale);
        double minResp = Math.Max(0.5, stats.MagnitudeReference * opt.ContrastThreshold);
        ScoreAtBilinear(map, omFinal, x, y, invRef, minResp,
            opt.SearchMask, opt.SearchMaskWidth, opt.SearchMaskHeight, opt.MaskScoring,
            out int matched, out double matchedWeight, out double contrastSum, out double polarityCoherence,
            out double visibleWeight, out int visibleCount);

        double totalWeight = visibleWeight > 0 ? visibleWeight : omFinal.TotalWeight;
        int totalCount = visibleCount > 0 ? visibleCount : omFinal.Count;
        double coverage = matchedWeight / Math.Max(1e-6, totalWeight);
        int bad = Math.Max(0, totalCount - matched);

        // A position where the mask hides most of the model would score highly on very little
        // evidence, so it is not allowed to compete.
        bool tooLittleVisible = opt.SearchMask != null && visibleWeight < 0.35 * omFinal.TotalWeight;
        double clutterFraction = opt.UseClutter && totalCount > 0
            ? Math.Clamp((double)bad / totalCount * 0.5, 0, 1)
            : 0.0;

        double exponent = 1.0 / Math.Max(1.0, model.N);
        var breakdown = Scorer.Score(matchedWeight, totalWeight, matched, totalCount,
            clutterFraction, 1.0, exponent);
        double score = breakdown.Score;
        coverage = breakdown.Coverage;

        double contrast = matched > 0 ? contrastSum / matched * invRef : 0;

        return new MatchResult
        {
            Score = score,
            Contrast = contrast,
            Coverage = coverage,
            X = x,
            Y = y,
            Angle = NormalizeAngle(angle),
            Scale = scale,
            MatchedFeatures = matched,
            BadFeatures = bad,
            Accepted = !tooLittleVisible
                    && score >= opt.AcceptThreshold
                    && contrast >= opt.ContrastThreshold
                    && (opt.MinPolarityCoherence <= 0 || polarityCoherence >= opt.MinPolarityCoherence)
                    && InAngleWindow(NormalizeAngle(angle) * 180.0 / Math.PI, opt.AngleMin, opt.AngleMax),
            FitError = fitError,
            PolarityCoherence = polarityCoherence,
            Clutter = clutterFraction
        };
    }

    private readonly record struct PoseEstimate(double X, double Y, double Angle, double Scale, double Rms, int Inliers);

    /// <summary>
    /// Edge-normal correspondence plus robust similarity fit. This is the step that turns a
    /// discrete hypothesis into a sub-pixel pose: each model feature is pulled onto the real
    /// edge along its own normal, and the fit uses those measured points (not the predicted
    /// ones, which would make the fit the identity).
    /// </summary>
    private PoseEstimate? RefinePose(PatternModel model, EdgeMap map, LevelStats stats,
        double x, double y, double angle, double scale, SearchOptions opt)
    {
        if (model.Count < 6) return null;

        bool agnostic = model.PolarityAgnostic;

        // In polarity-agnostic mode the response magnitude is what matters, so the sign test
        // becomes an absolute value; otherwise the feature polarity selects the direction.
        double Respond(double value, double polarity) => agnostic ? Math.Abs(value) : polarity * value;

        double radius = Math.Max(0.5, opt.NormalSearchRadius);
        double step = Math.Clamp(opt.NormalSearchStep, 0.1, 1.0);
        int taps = (int)Math.Round(2 * radius / step) + 1;
        var offsets = new double[taps];
        for (int t = 0; t < taps; t++) offsets[t] = -radius + t * step;

        float invRef = 1f / Math.Max(1f, stats.MagnitudeReference);
        double minResponse = Math.Max(1.0, stats.MagnitudeReference * opt.ContrastThreshold * 0.5);

        // cap the number of features used by the fit: the estimate is flat past ~120 points
        // and this loop is the memory-bound hot spot of the whole search
        int stride = Math.Max(1, (int)Math.Ceiling(model.Count / (double)Math.Max(24, opt.PoseFitMaxPoints)));

        var modelPts = new List<LeastSquaresFitter.Point2D>(model.Count / stride + 1);
        var measuredPts = new List<LeastSquaresFitter.Point2D>(model.Count / stride + 1);
        var fitter = new LeastSquaresFitter();

        double cx = x, cy = y, cang = angle, cscale = scale;
        PoseEstimate? result = null;

        for (int iter = 0; iter < 3; iter++)
        {
            double cos = Math.Cos(cang), sin = Math.Sin(cang);
            modelPts.Clear();
            measuredPts.Clear();

            for (int i = 0; i < model.Count; i += stride)
            {
                double rx = model.Px[i] - model.OriginX;
                double ry = model.Py[i] - model.OriginY;

                double nx = cos * model.Nx[i] - sin * model.Ny[i];
                double ny = sin * model.Nx[i] + cos * model.Ny[i];
                double pol = model.Polarity[i];

                double px = cx + cscale * (cos * rx - sin * ry);
                double py = cy + cscale * (sin * rx + cos * ry);
                if (px < 1 || py < 1 || px > map.Width - 2 || py > map.Height - 2) continue;

                double bestVal = double.MinValue, bestOff = 0;
                for (int t = 0; t < taps; t++)
                {
                    double o = offsets[t];
                    double v = Respond(SampleDirectional(map, px + o * nx, py + o * ny, nx, ny), pol);
                    if (v > bestVal) { bestVal = v; bestOff = o; }
                }
                if (bestVal < minResponse) continue;   // no edge found along the normal

                // An edge far from the prediction is not this feature's edge - rejecting it
                // keeps occlusion boundaries and nearby clutter from biasing the pose.
                if (Math.Abs(bestOff) > radius * 0.85) continue;

                double off = bestOff;
                int ti = (int)Math.Round((bestOff + radius) / step);
                if (ti > 0 && ti < taps - 1)
                {
                    double v0 = Respond(SampleDirectional(map, px + offsets[ti - 1] * nx, py + offsets[ti - 1] * ny, nx, ny), pol);
                    double v1 = bestVal;
                    double v2 = Respond(SampleDirectional(map, px + offsets[ti + 1] * nx, py + offsets[ti + 1] * ny, nx, ny), pol);
                    double denom = v0 - 2 * v1 + v2;
                    if (Math.Abs(denom) > 1e-9) off += 0.5 * (v0 - v2) / denom * step;
                }

                modelPts.Add(new LeastSquaresFitter.Point2D(rx, ry));
                measuredPts.Add(new LeastSquaresFitter.Point2D(px + off * nx, py + off * ny));
            }

            if (modelPts.Count < 6) return result;

            var pose = fitter.FitSimilarity(modelPts, measuredPts);
            if (double.IsNaN(pose.Angle) || double.IsNaN(pose.Scale) || pose.Scale <= 0) return result;

            result = new PoseEstimate(pose.X, pose.Y, pose.Angle, pose.Scale, pose.RmsError, pose.Inliers);
            cx = pose.X; cy = pose.Y; cang = pose.Angle; cscale = pose.Scale;
        }
        return result;
    }

    /// <summary>Bilinear signed directional derivative of the edge field along unit (nx, ny).</summary>
    private static double SampleDirectional(EdgeMap map, double px, double py, double nx, double ny)
    {
        int w = map.Width, h = map.Height;
        if (px < 0 || py < 0 || px > w - 1.001 || py > h - 1.001) return 0;

        int x0 = (int)px, y0 = (int)py;
        int x1 = x0 + 1 < w ? x0 + 1 : x0;
        int y1 = y0 + 1 < h ? y0 + 1 : y0;
        double fx = px - x0, fy = py - y0;

        int i00 = y0 * w + x0, i10 = y0 * w + x1, i01 = y1 * w + x0, i11 = y1 * w + x1;
        double gx = (map.Gx[i00] * (1 - fx) + map.Gx[i10] * fx) * (1 - fy) +
                    (map.Gx[i01] * (1 - fx) + map.Gx[i11] * fx) * fy;
        double gy = (map.Gy[i00] * (1 - fx) + map.Gy[i10] * fx) * (1 - fy) +
                    (map.Gy[i01] * (1 - fx) + map.Gy[i11] * fx) * fy;

        return gx * nx + gy * ny;
    }

    // ------------------------------------------------------------------
    //  Post-processing
    // ------------------------------------------------------------------
    private static List<MatchResult> SuppressOverlaps(List<MatchResult> input, PatternModel model, SearchOptions opt)
    {
        input.Sort((a, b) =>
        {
            int c = b.Score.CompareTo(a.Score);
            if (c != 0) return c;
            c = a.X.CompareTo(b.X);
            return c != 0 ? c : a.Y.CompareTo(b.Y);
        });
        var kept = new List<MatchResult>(input.Count);
        double minDist = opt.MinSeparationPixels > 0
            ? opt.MinSeparationPixels
            : Math.Max(4.0, model.Diameter * Math.Clamp(opt.OverlapSuppression, 0.0, 2.0));

        foreach (var r in input)
        {
            if (!r.Accepted) continue;
            if (!InAngleWindow(r.Angle * 180.0 / Math.PI, opt.AngleMin, opt.AngleMax)) continue;
            if (opt.MinPolarityCoherence > 0 && r.PolarityCoherence < opt.MinPolarityCoherence) continue;
            bool overlaps = false;
            foreach (var k in kept)
            {
                double dx = k.X - r.X, dy = k.Y - r.Y;
                if (dx * dx + dy * dy < minDist * minDist) { overlaps = true; break; }
            }
            if (!overlaps) kept.Add(r);
        }
        return kept;
    }

    /// <summary>
    /// Whether a reported angle lies inside the configured search window. Refinement is free to
    /// walk the angle away from the coarse grid, and it used to walk straight out of the window:
    /// a +-10 deg search still returned a part at -36 deg. Reported poses are now restricted to
    /// what the caller asked to look for.
    /// </summary>
    private static bool InAngleWindow(double degrees, double min, double max)
    {
        double lo = Math.Min(min, max), hi = Math.Max(min, max);
        if (hi - lo >= 359.999) return true;
        for (int k = -1; k <= 1; k++)
        {
            double a = degrees + 360.0 * k;
            if (a >= lo - 1e-6 && a <= hi + 1e-6) return true;
        }
        return false;
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}


