using PatMaxSharp.Core;
using PatMaxSharp.Edge;
using PatMaxSharp.Fit;
using PatMaxSharp.Matching;
using PatMaxSharp.Projection;

namespace PatMaxSharp;

/// <summary>
/// PatMax (PMAlign) implementation reconstructed from the Cognex binary.
///
/// Pipeline (confirmed by diagnostic strings, appendix B.4):
///   Train: Gabor edge -> dipoles -> run-length chains -> featurelets -> W/N
///   Run:   Gabor edge -> projection -> peak detect -> feature matching -> score -> pose fit
/// </summary>
public sealed class PatMaxTool
{
    public GaborEdgeDetector EdgeDetector { get; }
    public ProjectionMethod Projector { get; } = new ProjectionMethod();
    public PeakDetector PeakDetector { get; } = new PeakDetector();
    public Scorer Scorer { get; } = new Scorer();
    public LeastSquaresFitter Fitter { get; } = new LeastSquaresFitter();

    public PatternModel Pattern { get; private set; }
    public double AcceptThreshold { get; set; } = 0.5;
    public double ContrastThreshold { get; set; } = 0.1;

    /// <summary>Voting accumulator step in pixels (smaller = higher precision, slower).</summary>
    public int PrecisionStep { get; set; } = 2;

    /// <summary>Enable sub-pixel refinement of the voted origin.</summary>
    public bool SubpixelRefinement { get; set; } = true;

    /// <summary>Cluster radius in pixels for merging nearby candidate origins.</summary>
    public double ClusterRadius { get; set; } = 8.0;

    /// <summary>Debug: peak of the voting accumulator (last run).</summary>
    public (double x, double y, double votes) LastVotePeak { get; private set; }

    /// <summary>Enable clustering of nearby candidates (robustness against duplicates).</summary>
    public bool EnableClustering { get; set; } = true;

    /// <summary>Gaussian pre-smooth sigma (noise robustness). 0 disables.</summary>
    public double PreSmoothSigma { get; set; } = 0.5;

    public PatMaxTool()
    {
        EdgeDetector = new GaborEdgeDetector();
    }

    // ------------------------------------------------------------------
    //  Dipole extraction
    // ------------------------------------------------------------------
    /// <summary>
    /// Convert an edge map to dipoles.
    ///   x = ix + 0.5 + scale*cos(theta)
    ///   y = iy + 0.5 + scale*sin(theta)      (pixel-center convention)
    /// </summary>
    public List<Dipole> ExtractDipoles(GaborEdgeDetector.EdgeMap edges, double scale = 0.5)
    {
        var dipoles = new List<Dipole>();
        for (int y = 0; y < edges.Height; y++)
        {
            for (int x = 0; x < edges.Width; x++)
            {
                if (!edges.IsEdgePixel(x, y)) continue;
                float mag = edges.GetMagnitude(x, y);
                float ang = edges.GetAngle(x, y);
                dipoles.Add(new Dipole
                {
                    X = x + 0.5f,
                    Y = y + 0.5f,
                    Angle = ang,
                    Strength = mag,
                    Weight = 1.0f
                });
            }
        }
        return dipoles;
    }

    // ------------------------------------------------------------------
    //  Chain building (run-length grouping by connectivity)
    // ------------------------------------------------------------------
    public List<List<Dipole>> BuildChains(List<Dipole> dipoles, double spacing = 2.0)
    {
        var chains = new List<List<Dipole>>();
        if (dipoles.Count == 0) return chains;

        var remaining = new List<Dipole>(dipoles);
        while (remaining.Count > 0)
        {
            var chain = new List<Dipole> { remaining[0] };
            remaining.RemoveAt(0);
            bool grew = true;
            while (grew)
            {
                grew = false;
                var tail = chain[chain.Count - 1];
                for (int i = remaining.Count - 1; i >= 0; i--)
                {
                    double dx = remaining[i].X - tail.X, dy = remaining[i].Y - tail.Y;
                    if (dx * dx + dy * dy <= spacing * spacing)
                    {
                        chain.Add(remaining[i]);
                        remaining.RemoveAt(i);
                        grew = true;
                        break;
                    }
                }
            }
            chains.Add(chain);
        }
        chains.RemoveAll(c => c.Count < 2);
        return chains;
    }

    // ------------------------------------------------------------------
    //  Featurelet sampling
    // ------------------------------------------------------------------
    private List<Featurelet> SampleFeaturelets(List<List<Dipole>> chains, int maxPerChain = 12)
    {
        var fts = new List<Featurelet>();
        for (int c = 0; c < chains.Count; c++)
        {
            var chain = chains[c];
            double arc = 0;
            int step = Math.Max(1, chain.Count / Math.Max(1, maxPerChain));
            for (int i = 0; i < chain.Count; i += step)
            {
                var d = chain[i];
                if (i > 0)
                {
                    var p = chain[i - 1];
                    arc += Math.Sqrt(Math.Pow(d.X - p.X, 2) + Math.Pow(d.Y - p.Y, 2));
                }
                fts.Add(new Featurelet
                {
                    X = d.X, Y = d.Y,
                    Angle = d.Angle,
                    Angle8 = d.Angle8,
                    Magnitude = d.Strength,
                    ChainIndex = c,
                    ChainPosition = (float)arc,
                    Weight = 1.0 + d.Strength / 255.0
                });
            }
        }
        return fts;
    }

    // ------------------------------------------------------------------
    //  Train
    // ------------------------------------------------------------------
    public PatternModel Train(GrayImage image, double originX = 0, double originY = 0, double grain = 1.0)
    {
        var edges = EdgeDetector.Detect(image);
        var dipoles = ExtractDipoles(edges);
        var chains = BuildChains(dipoles);
        var featurelets = SampleFeaturelets(chains);

        // Capture the edge-magnitude template for the robust NCC matcher.
        var tmpl = new float[image.Width * image.Height];
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                tmpl[y * image.Width + x] = edges.GetMagnitude(x, y);

        var pattern = new PatternModel
        {
            OriginX = originX,
            OriginY = originY,
            Grain = grain,
            Template = tmpl,
            TemplateWidth = image.Width,
            TemplateHeight = image.Height
        };
        foreach (var f in featurelets) pattern.AddFeaturelet(f);
        pattern.N = 2.0;   // exponent denominator (tuned for discriminative scoring)

        Pattern = pattern;
        return pattern;
    }

    // ------------------------------------------------------------------
    //  Run
    // ------------------------------------------------------------------
    public List<MatchResult> Run(GrayImage image, int maxResults = 10)
    {
        if (Pattern == null || !Pattern.Trained)
            throw new InvalidOperationException("pattern not trained");

        // Optional Gaussian pre-smooth for noise robustness
        GrayImage smoothed = image;
        if (PreSmoothSigma > 0) smoothed = GaussianSmooth(image, PreSmoothSigma);

        var edges = EdgeDetector.Detect(smoothed);

        // Prefer the robust grid matcher (coverage based).
        if (Pattern != null && Pattern.FeatureCount > 0)
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var tmpl = MatchByTemplate(edges);
            if (tmpl.Count > 0)
            {
                return tmpl;
            }
        }

        var results = new List<MatchResult>();

        // ------------------------------------------------------------------
        // Feature voting (geometric Hough): each edge votes for the object
        // origin implied by every featurelet whose angle it matches.
        // ------------------------------------------------------------------
        int step = Math.Max(1, PrecisionStep);
        int accW = Math.Max(1, image.Width / step), accH = Math.Max(1, image.Height / step);
        var accumulator = new double[accW, accH];

        // index featurelets by 8-bit angle for fast lookup
        var byAngle = new Dictionary<byte, List<Featurelet>>();
        foreach (var ft in Pattern.Featurelets)
        {
            if (!byAngle.TryGetValue(ft.Angle8, out var list)) { list = new List<Featurelet>(); byAngle[ft.Angle8] = list; }
            list.Add(ft);
        }

        for (int y = 0; y < edges.Height; y++)
        {
            for (int x = 0; x < edges.Width; x++)
            {
                if (!edges.IsEdgePixel(x, y)) continue;
                float ang = edges.GetAngle(x, y);
                byte a8 = (byte)((int)(ang * 40.74366543152521) & 0xFF);

                for (int d = -2; d <= 2; d++)
                {
                    var key = (byte)((a8 + d) & 0xFF);
                    if (!byAngle.TryGetValue(key, out var angleMatches)) continue;
                    foreach (var ft in angleMatches)
                    {
                        double ox = x - (ft.X - Pattern.OriginX);
                        double oy = y - (ft.Y - Pattern.OriginY);
                        int ax = (int)(ox / step), ay = (int)(oy / step);
                        if (ax >= 0 && ay >= 0 && ax < accW && ay < accH)
                            accumulator[ax, ay] += ft.Weight;
                    }
                }
            }
        }

        // ------------------------------------------------------------------
        // Find peaks in the voting accumulator (candidate origins)
        // ------------------------------------------------------------------
        var candidates = new List<(double x, double y, double votes)>();
        double maxVote = 0;
        for (int ay = 0; ay < accH; ay++)
            for (int ax = 0; ax < accW; ax++)
                maxVote = Math.Max(maxVote, accumulator[ax, ay]);

        LastVotePeak = FindPeak(accumulator, accW, accH, step);
        double voteThreshold = maxVote * PeakDetector.MaxAcceptablePeakFraction;
        var taken = new bool[accW, accH];
        for (int ay = 0; ay < accH; ay++)
        {
            for (int ax = 0; ax < accW; ax++)
            {
                double v = accumulator[ax, ay];
                if (v < voteThreshold) continue;

                // non-max suppression in a PeakSeparationThreshold window (in accumulator cells)
                int win = Math.Max(1, PeakDetector.PeakSeparationThreshold / step);
                bool isMax = true;
                for (int dy = -win; dy <= win && isMax; dy++)
                    for (int dx = -win; dx <= win && isMax; dx++)
                    {
                        int nx = ax + dx, ny = ay + dy;
                        if (nx < 0 || ny < 0 || nx >= accW || ny >= accH) continue;
                        if (accumulator[nx, ny] > v) { isMax = false; break; }
                    }
                if (!isMax) continue;

                // sub-pixel refinement by parabolic interpolation of the accumulator
                double subX = 0.0, subY = 0.0;
                if (SubpixelRefinement && ax > 0 && ax < accW - 1 && ay > 0 && ay < accH - 1)
                {
                    double vL = accumulator[ax - 1, ay], vR = accumulator[ax + 1, ay];
                    double dx1 = vL - 2.0 * v + vR;
                    if (Math.Abs(dx1) > 1e-9) subX = 0.5 * (vL - vR) / dx1;
                    double vD = accumulator[ax, ay - 1], vU = accumulator[ax, ay + 1];
                    double dy1 = vD - 2.0 * v + vU;
                    if (Math.Abs(dy1) > 1e-9) subY = 0.5 * (vD - vU) / dy1;
                }
                candidates.Add((ax * step + subX * step, ay * step + subY * step, v));
            }
        }
        candidates.Sort((a, b) => b.votes.CompareTo(a.votes));

        // Cluster nearby candidates (robustness against duplicate detections)
        if (EnableClustering && candidates.Count > 1)
        {
            var clustered = new List<(double x, double y, double votes)>();
            var used = new bool[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
            {
                if (used[i]) continue;
                double sx = 0, sy = 0, sw = 0;
                for (int j = i; j < candidates.Count; j++)
                {
                    if (used[j]) continue;
                    double dx = candidates[j].x - candidates[i].x;
                    double dy = candidates[j].y - candidates[i].y;
                    if (dx * dx + dy * dy > ClusterRadius * ClusterRadius) continue;
                    used[j] = true;
                    sx += candidates[j].x * candidates[j].votes;
                    sy += candidates[j].y * candidates[j].votes;
                    sw += candidates[j].votes;
                }
                if (sw > 0) clustered.Add((sx / sw, sy / sw, sw));
            }
            clustered.Sort((a, b) => b.votes.CompareTo(a.votes));
            candidates = clustered;
        }

        // ------------------------------------------------------------------
        // Verify each candidate: match featurelets against the local edges
        // ------------------------------------------------------------------
        double norm = 1.0;   // S (magScale normalization) normalized to 1 for this demo
        foreach (var cand in candidates)
        {
            if (results.Count >= maxResults) break;

            double originX = cand.x, originY = cand.y;
            var srcPts = new List<LeastSquaresFitter.Point2D>();
            var dstPts = new List<LeastSquaresFitter.Point2D>();
            int matched = 0, bad = 0;

            foreach (var ft in Pattern.Featurelets)
            {
                double ex = originX + (ft.X - Pattern.OriginX);
                double ey = originY + (ft.Y - Pattern.OriginY);
                int ix = (int)Math.Round(ex), iy = (int)Math.Round(ey);
                if (ix < 0 || iy < 0 || ix >= edges.Width || iy >= edges.Height) { bad++; continue; }

                float mag = edges.GetMagnitude(ix, iy);
                if (mag <= 0) { bad++; continue; }

                double diff = Math.Abs(NormalizeAngle(edges.GetAngle(ix, iy) - ft.Angle));
                if (diff < Math.PI / 6)
                {
                    matched++;
                    srcPts.Add(new LeastSquaresFitter.Point2D(ft.X - Pattern.OriginX, ft.Y - Pattern.OriginY));
                    dstPts.Add(new LeastSquaresFitter.Point2D(ex - originX, ey - originY));
                }
                else bad++;
            }

            double score = Scorer.Score(Pattern, matched, norm, bad, 0.5);

            double poseX = originX, poseY = originY, poseAngle = 0, poseScale = 1.0;
            if (srcPts.Count >= 3)
            {
                try
                {
                    var xf = Fitter.Fit(srcPts, dstPts, LeastSquaresFitter.FitType.Affine);
                    poseX = originX + xf.Tx;
                    poseY = originY + xf.Ty;
                    poseScale = Math.Sqrt(Math.Abs(xf.Det));
                    poseAngle = Math.Atan2(xf.M10, xf.M00);
                }
                catch { /* keep hypothesis */ }
            }

            bool accepted = score >= AcceptThreshold;
            results.Add(new MatchResult
            {
                Score = score,
                Accepted = accepted,
                X = poseX,
                Y = poseY,
                Angle = poseAngle,
                Scale = poseScale,
                Coverage = Pattern.FeatureCount > 0 ? (double)matched / Pattern.FeatureCount : 0,
                MatchedFeatures = matched,
                BadFeatures = bad,
                PeakValue = cand.votes
            });
        }

        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results;
    }

    /// <summary>
    /// Run all models in a multi-pattern set (cfMultiRun -> ccPMMultiModel::run).
    /// </summary>
    public List<MatchResult> RunMulti(GrayImage image, PatternMulti multi, int maxResults = 10)
    {
        var all = new List<MatchResult>();
        foreach (var model in multi.SelectModels(_ => 0.0))
        {
            Pattern = model;
            all.AddRange(Run(image, maxResults));
        }
        all.Sort((a, b) => b.Score.CompareTo(a.Score));
        return all.Take(maxResults).ToList();
    }

    /// <summary>
    /// Robust normalized cross-correlation of the trained edge template over the
    /// search image. Returns candidate origins with correlation scores.
    /// </summary>
    private List<MatchResult> MatchByTemplate(GaborEdgeDetector.EdgeMap edges)
    {
        var results = new List<MatchResult>();
        if (!Pattern.HasTemplate) return results;

        int tw = Pattern.TemplateWidth, th = Pattern.TemplateHeight;
        int w = edges.Width, h = edges.Height;
        if (tw > w || th > h) return results;

        // template statistics (zero-mean)
        double tMean = 0;
        for (int i = 0; i < Pattern.Template.Length; i++) tMean += Pattern.Template[i];
        tMean /= Pattern.Template.Length;
        double tEnergy = 0;
        var tNorm = new float[Pattern.Template.Length];
        for (int i = 0; i < tNorm.Length; i++) { tNorm[i] = (float)(Pattern.Template[i] - tMean); tEnergy += tNorm[i] * tNorm[i]; }
        if (tEnergy <= 1e-9) return results;
        tEnergy = Math.Sqrt(tEnergy);

        int step = Math.Max(1, PrecisionStep);
        int maxX = w - tw, maxY = h - th;
        var corr = new double[maxX + 1, maxY + 1];
        double best = -1;

        // search image magnitude
        var mag = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                mag[y * w + x] = edges.GetMagnitude(x, y);

        for (int oy = 0; oy <= maxY; oy += step)
        {
            for (int ox = 0; ox <= maxX; ox += step)
            {
                double dot = 0, iEnergy = 0;
                for (int ty = 0; ty < th; ty++)
                {
                    int row = (oy + ty) * w + ox;
                    int tRow = ty * tw;
                    for (int tx = 0; tx < tw; tx++)
                    {
                        float iv = mag[row + tx];
                        dot += tNorm[tRow + tx] * iv;
                        iEnergy += iv * iv;
                    }
                }
                double denom = tEnergy * Math.Sqrt(iEnergy);
                double c = denom > 1e-9 ? dot / denom : 0;
                corr[ox, oy] = c;
                if (c > best) best = c;
            }
        }

        // DEBUG: NCC at the expected origin
        if (System.Environment.GetEnvironmentVariable("PMA_NCC") == "1")
        {
            int eox = (int)Math.Round(Pattern.OriginX - 0); // top-left that puts origin at origin
            int eoy = 0;
            // find the correlation value closest to the expected origin
            Console.WriteLine($"[NCC] best={best:F4} tEnergy={tEnergy:F1}");
            for (int oy = 0; oy <= maxY; oy += step)
                for (int ox = 0; ox <= maxX; ox += step)
                {
                    double c = corr[ox, oy];
                    if (c > 0.5)
                        Console.WriteLine($"[NCC] ox={ox} oy={oy} c={c:F4} origin=({ox + Pattern.OriginX:F0},{oy + Pattern.OriginY:F0})");
                }
        }
        // peaks above threshold
        double threshold = best * PeakDetector.MaxAcceptablePeakFraction;
        var taken = new bool[maxX + 1, maxY + 1];
        int win = Math.Max(1, PeakDetector.PeakSeparationThreshold / step);
        for (int oy = 0; oy <= maxY; oy++)
        {
            for (int ox = 0; ox <= maxX; ox++)
            {
                double c = corr[ox, oy];
                if (c < threshold) continue;
                bool isMax = true;
                for (int dy = -win; dy <= win && isMax; dy++)
                    for (int dx = -win; dx <= win && isMax; dx++)
                    {
                        int nx = ox + dx, ny = oy + dy;
                        if (nx < 0 || ny < 0 || nx <= maxX && ny <= maxY) continue;
                        if (nx < 0 || ny < 0 || nx > maxX || ny > maxY) continue;
                        if (corr[nx, ny] > c) { isMax = false; break; }
                    }
                if (!isMax) continue;

                // origin = top-left of the match + the pattern origin offset
                double originX = ox + Pattern.OriginX;
                double originY = oy + Pattern.OriginY;
                int matched = 0;
                foreach (var ft in Pattern.Featurelets)
                {
                    int ix = (int)Math.Round(originX + ft.X - Pattern.OriginX);
                    int iy = (int)Math.Round(originY + ft.Y - Pattern.OriginY);
                    if (ix >= 0 && iy >= 0 && ix < w && iy < h && edges.GetMagnitude(ix, iy) > 0) matched++;
                }
                double coverage = Pattern.FeatureCount > 0 ? (double)matched / Pattern.FeatureCount : 0;
                results.Add(new MatchResult
                {
                    Score = Math.Max(0, Math.Min(1, coverage)),
                    X = originX, Y = originY,
                    Angle = 0, Scale = 1.0,
                    Coverage = Pattern.FeatureCount > 0 ? (double)matched / Pattern.FeatureCount : 0,
                    MatchedFeatures = matched,
                    BadFeatures = Pattern.FeatureCount - matched,
                    Accepted = c >= AcceptThreshold
                });
            }
        }
        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results;
    }

    /// <summary>
    /// Robust grid search: find the origin where the most featurelets land on
    /// an edge. Independent of angle conventions, so it is reliable.
    /// </summary>
    private List<MatchResult> MatchByGrid(GaborEdgeDetector.EdgeMap edges)
    {
        var results = new List<MatchResult>();
        if (Pattern == null || Pattern.FeatureCount == 0) return results;

        int w = edges.Width, h = edges.Height;
        int step = Math.Max(1, PrecisionStep);
        int margin = 8;
        int bestCount = -1;
        var peaks = new List<(double x, double y, int count)>();

        for (int oy = margin; oy < h - margin; oy += step)
        {
            for (int ox = margin; ox < w - margin; ox += step)
            {
                int matched = 0;
                for (int f = 0; f < Pattern.Featurelets.Count; f++)
                {
                    var ft = Pattern.Featurelets[f];
                    int ix = ox + (int)Math.Round(ft.X - Pattern.OriginX);
                    int iy = oy + (int)Math.Round(ft.Y - Pattern.OriginY);
                    if (ix >= 0 && iy >= 0 && ix < w && iy < h && edges.GetMagnitude(ix, iy) > 0) matched++;
                }
                if (matched > bestCount) { bestCount = matched; peaks.Clear(); peaks.Add((ox, oy, matched)); }
                else if (matched == bestCount && peaks.Count < 50) peaks.Add((ox, oy, matched));
            }
        }

        if (bestCount <= 0) return results;

        // refine around the best peak (sub-pixel via centroid of matched featurelets)
        foreach (var p in peaks)
        {
            double sumX = 0, sumY = 0; int n = 0;
            foreach (var ft in Pattern.Featurelets)
            {
                int ix = (int)Math.Round(p.x + ft.X - Pattern.OriginX);
                int iy = (int)Math.Round(p.y + ft.Y - Pattern.OriginY);
                if (ix >= 0 && iy >= 0 && ix < w && iy < h && edges.GetMagnitude(ix, iy) > 0)
                {
                    sumX += (p.x + ft.X - Pattern.OriginX) - ix;
                    sumY += (p.y + ft.Y - Pattern.OriginY) - iy;
                    n++;
                }
            }
            double rx = p.x + (n > 0 ? sumX / n : 0);
            double ry = p.y + (n > 0 ? sumY / n : 0);
            double coverage = (double)p.count / Pattern.FeatureCount;
            results.Add(new MatchResult
            {
                Score = coverage,
                X = rx, Y = ry,
                Angle = 0, Scale = 1.0,
                Coverage = coverage,
                MatchedFeatures = p.count,
                BadFeatures = Pattern.FeatureCount - p.count,
                Accepted = coverage >= AcceptThreshold
            });
        }
        results.Sort((a, b) => b.Score.CompareTo(a.Score));
        return results;
    }

    private static (double, double, double) FindPeak(double[,] acc, int w, int h, int step)
    {
        double max = 0; int mx = 0, my = 0;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                if (acc[x, y] > max) { max = acc[x, y]; mx = x; my = y; }
        return (mx * step, my * step, max);
    }

    /// <summary>Separable Gaussian smoothing (noise robustness).</summary>
    private static GrayImage GaussianSmooth(GrayImage src, double sigma)
    {
        int w = src.Width, h = src.Height;
        int r = Math.Max(1, (int)Math.Ceiling(3 * sigma));
        var kernel = new double[2 * r + 1];
        double sum = 0;
        for (int i = -r; i <= r; i++) { kernel[i + r] = Math.Exp(-(i * i) / (2 * sigma * sigma)); sum += kernel[i + r]; }
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;

        var tmp = new GrayImage(w, h);
        var dst = new GrayImage(w, h);

        // horizontal
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double acc = 0;
                for (int k = -r; k <= r; k++)
                {
                    int sx = Math.Clamp(x + k, 0, w - 1);
                    acc += src[sx, y] * kernel[k + r];
                }
                tmp[x, y] = (byte)Math.Clamp(acc, 0, 255);
            }
        // vertical
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double acc = 0;
                for (int k = -r; k <= r; k++)
                {
                    int sy = Math.Clamp(y + k, 0, h - 1);
                    acc += tmp[x, sy] * kernel[k + r];
                }
                dst[x, y] = (byte)Math.Clamp(acc, 0, 255);
            }
        return dst;
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    /// <summary>Fit a pose from matched feature correspondences (affine, type 6).</summary>
    public LeastSquaresFitter.LinearXform FitPose(
        List<LeastSquaresFitter.Point2D> source,
        List<LeastSquaresFitter.Point2D> target)
    {
        return Fitter.Fit(source, target, LeastSquaresFitter.FitType.Affine);
    }
}

/// <summary>Match result (mirrors ccPMResult layout).</summary>
public sealed class MatchResult
{
    public double Score { get; set; }          // +0x48
    public double Contrast { get; set; }       // +0x4c
    public double FitError { get; set; }       // +0x50
    public double Coverage { get; set; }       // +0x54
    public bool Accepted { get; set; }         // +0x5c
    public double X { get; set; }
    public double Y { get; set; }
    public double Angle { get; set; }
    public double Scale { get; set; }
    public int MatchedFeatures { get; set; }
    public int BadFeatures { get; set; }
    public double PeakValue { get; set; }

    public override string ToString() =>
        $"Result(Score={Score:F3}, Pos=({X:F1},{Y:F1}), Coverage={Coverage:P0}, " +
        $"Matched={MatchedFeatures}, Bad={BadFeatures}, Accepted={Accepted})";
}
