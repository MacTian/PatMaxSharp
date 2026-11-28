using System;
using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ShapeAnchor;
using ShapeAnchor.Core;
using ShapeAnchor.Training;
using ShapeAnchor.Controls;

namespace ShapeAnchor.UI.ViewModels;

/// <summary>
/// Main view model — binds the whole PatMax UI.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly ShapeAnchorTool _tool = new();
    private BitmapSource _image;
    private GrayImage _imageGray;      // decoded once per image: cursor readout and runs reuse it
    private string _status = "就绪";
    private Rect? _trainRegion;
    private Rect? _searchRegion;
    private bool _trained;

    public MainViewModel()
    {
        OpenImageCommand = new RelayCommand(_ => OpenImage());
        TrainCommand = new RelayCommand(_ => Train(), _ => _image != null && TrainRegion.HasValue);
        RunCommand = new RelayCommand(_ => Run(), _ => _trained && _image != null);
        ClearCommand = new RelayCommand(_ => Clear(), _ => _image != null);
        BatchValidateCommand = new RelayCommand(_ => BatchValidate());
        CenterOriginCommand = new RelayCommand(_ => OriginFollowsCenter = true);
        ClearMaskCommand = new RelayCommand(_ => ClearMask());
        SavePresetCommand = new RelayCommand(_ => SavePreset());
        NewPresetCommand = new RelayCommand(_ => NewPreset());
        DeletePresetCommand = new RelayCommand(_ => DeletePreset(), _ => SelectedPreset != null && !IsBuiltIn(SelectedPreset));

        Presets = new ObservableCollection<ParameterPreset>(ParameterPreset.BuiltIn());
        LoadUserPresets();
        SelectedPreset = Presets.FirstOrDefault();
        ApplyPreset(SelectedPreset);
    }

    #region Commands
    public ICommand OpenImageCommand { get; }
    public ICommand TrainCommand { get; }
    public ICommand RunCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand BatchValidateCommand { get; }
    public ICommand SavePresetCommand { get; }
    public ICommand NewPresetCommand { get; }
    public ICommand DeletePresetCommand { get; }
    #endregion

    #region Presets
    public ObservableCollection<ParameterPreset> Presets { get; } = new();
    public ParameterPreset SelectedPreset { get; set; }

    private string PresetDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShapeAnchor", "Presets");

    private static bool IsBuiltIn(ParameterPreset p) =>
        ParameterPreset.BuiltIn().Any(b => b.Name == p.Name);

    private void LoadUserPresets()
    {
        try
        {
            Directory.CreateDirectory(PresetDir);
            foreach (var file in Directory.GetFiles(PresetDir, "*.json"))
            {
                try { Presets.Add(ParameterPreset.FromJson(File.ReadAllText(file))); }
                catch { /* skip corrupt preset */ }
            }
        }
        catch { /* ignore */ }
    }

    /// <summary>Apply a preset to the live parameters.</summary>
    public void ApplyPreset(ParameterPreset p)
    {
        if (p == null) return;
        RotationAngleMin = p.RotationAngleMin;
        RotationAngleMax = p.RotationAngleMax;
        ScaleMin = p.ScaleMin;
        ScaleMax = p.ScaleMax;
        ApproximateNumberToFind = p.ApproximateNumberToFind;
        XYOverlap = p.XYOverlap;
        TimeoutMs = p.TimeoutMs;
        AcceptThreshold = p.AcceptThreshold;
        ContrastThreshold = p.ContrastThreshold;
        MinPolarityCoherence = p.MinPolarityCoherence;
        Grain = p.Grain;
        CoarseGrainLimit = p.CoarseGrainLimit;
        FineGrainLimit = p.FineGrainLimit;
        ScoreUsingClutter = p.ScoreUsingClutter;
        MaxResults = p.MaxResults;
        PrecisionStep = p.PrecisionStep;
        SubpixelRefinement = p.SubpixelRefinement;
        EnableClustering = p.EnableClustering;
        ClusterRadius = p.ClusterRadius;
        PreSmoothSigma = p.PreSmoothSigma;
        UseSimd = p.UseSimd;
        UseParallel = p.UseParallel;
        OnPropertyChanged(string.Empty);
    }

    private ParameterPreset CaptureCurrent(string name, string description = "")
    {
        return new ParameterPreset
        {
            Name = name, Description = description,
            RotationAngleMin = RotationAngleMin, RotationAngleMax = RotationAngleMax,
            ScaleMin = ScaleMin, ScaleMax = ScaleMax,
            ApproximateNumberToFind = ApproximateNumberToFind, XYOverlap = XYOverlap, TimeoutMs = TimeoutMs,
            AcceptThreshold = AcceptThreshold, ContrastThreshold = ContrastThreshold,
            Grain = Grain, CoarseGrainLimit = CoarseGrainLimit, FineGrainLimit = FineGrainLimit,
            ScoreUsingClutter = ScoreUsingClutter, MaxResults = MaxResults,
            PrecisionStep = PrecisionStep, SubpixelRefinement = SubpixelRefinement,
            EnableClustering = EnableClustering, ClusterRadius = ClusterRadius,
            PreSmoothSigma = PreSmoothSigma, UseSimd = UseSimd, UseParallel = UseParallel
        };
    }

    private void NewPreset()
    {
        var p = CaptureCurrent("新建预设", "");
        Presets.Add(p);
        SelectedPreset = p;
        Status = $"已创建预设: {p.Name}";
    }

    private void SavePreset()
    {
        try
        {
            var p = SelectedPreset ?? CaptureCurrent("默认");
            p = CaptureCurrent(p.Name, p.Description);
            Directory.CreateDirectory(PresetDir);
            File.WriteAllText(Path.Combine(PresetDir, $"{p.Name}.json"), p.ToJson());

            // replace in the list
            int idx = Presets.IndexOf(SelectedPreset);
            if (idx >= 0) Presets[idx] = p; else Presets.Add(p);
            SelectedPreset = p;
            Status = $"预设已保存: {p.Name}";
        }
        catch (Exception ex) { Status = $"保存失败: {ex.Message}"; }
    }

    private void DeletePreset()
    {
        try
        {
            if (SelectedPreset == null) return;
            var file = Path.Combine(PresetDir, $"{SelectedPreset.Name}.json");
            if (File.Exists(file)) File.Delete(file);
            Presets.Remove(SelectedPreset);
            SelectedPreset = Presets.FirstOrDefault();
            Status = "预设已删除";
        }
        catch (Exception ex) { Status = $"删除失败: {ex.Message}"; }
    }
    #endregion

    #region Properties
    public ImageSource Image
    {
        get => _image;
        private set
        {
            if (Set(ref _image, (BitmapSource)value))
            {
                OnPropertyChanged(nameof(HasImage));
                OnPropertyChanged(nameof(ShowCanvasHint));
            }
        }
    }

    /// <summary>True once an image is loaded (drives the canvas placeholder and button states).</summary>
    public bool HasImage => _image != null;

    /// <summary>Show the workflow hint over the empty canvas.</summary>
    public bool ShowCanvasHint => _image == null;

    /// <summary>Compact summary of the settings that decide a match, shown in the status bar.</summary>
    public string ParamsSummary =>
        $"一致性 {MinPolarityCoherence:F2}   接受阈值 {AcceptThreshold:F2}   角度 {RotationAngleMin:F0}…{RotationAngleMax:F0}°   " +
        $"缩放 {ScaleMin:F2}…{ScaleMax:F2}   最大结果 {MaxResults}   " +
        $"{ (ScoreUsingClutter ? "杂波计分开" : "杂波计分关") }";

    /// <summary>Training region as readable text.</summary>
    public string TrainRegionText => TrainRegion is Rect r
        ? $"X {r.X:F0}   Y {r.Y:F0}   W {r.Width:F0}   H {r.Height:F0}"
        : "未框选";

    public string SearchRegionText => SearchRegion is Rect s
        ? $"X {s.X:F0}   Y {s.Y:F0}   W {s.Width:F0}   H {s.Height:F0}"
        : "整幅图";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public Rect? TrainRegion
    {
        get => _trainRegion;
        set
        {
            if (Set(ref _trainRegion, value))
            {
                OnPropertyChanged(nameof(CanTrain));
                OnPropertyChanged(nameof(TrainRegionText));
                OnPropertyChanged(nameof(OriginPoint));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                if (value.HasValue && OriginFollowsCenter) SnapOriginToCenter();
                // drawing a new region brings the green overlay back
                if (value.HasValue) ShowTrainRegion = true;
            }
        }
    }

    // ---- training parameters (the panel that was missing) ----
    private bool _IgnorePolarity;
    public bool IgnorePolarity { get => _IgnorePolarity; set => Set(ref _IgnorePolarity, value); }

    private int _TrainMaxFeatures = 120;
    public int TrainMaxFeatures { get => _TrainMaxFeatures; set => Set(ref _TrainMaxFeatures, value); }

    private double _TrainMinSpacing = 2.0;
    public double TrainMinSpacing { get => _TrainMinSpacing; set => Set(ref _TrainMinSpacing, value); }

    private double _EdgeThreshold = 8.0;
    public double EdgeThreshold
    {
        get => _EdgeThreshold;
        set { if (Set(ref _EdgeThreshold, value)) _tool.EdgeDetector.NoiseFloor = value; }
    }

    private bool _AutoEdgeThreshold = true;
    public bool AutoEdgeThreshold
    {
        get => _AutoEdgeThreshold;
        set { if (Set(ref _AutoEdgeThreshold, value)) _tool.EdgeDetector.AutoNoiseFloor = value; }
    }

    // ---- training origin (Cognex 训练区域与原点) ----
    private double _originX;
    public double OriginX
    {
        get => _originX;
        set
        {
            if (!Set(ref _originX, value)) return;
            _originFollowsCenter = false;
            OnPropertyChanged(nameof(OriginFollowsCenter));
            OnPropertyChanged(nameof(OriginIsFollowedByCenter));
            OnPropertyChanged(nameof(OriginText));
            OnPropertyChanged(nameof(OriginPoint));
        }
    }

    private double _originY;
    public double OriginY
    {
        get => _originY;
        set
        {
            if (!Set(ref _originY, value)) return;
            _originFollowsCenter = false;
            OnPropertyChanged(nameof(OriginFollowsCenter));
            OnPropertyChanged(nameof(OriginIsFollowedByCenter));
            OnPropertyChanged(nameof(OriginText));
            OnPropertyChanged(nameof(OriginPoint));
        }
    }

    private bool _originFollowsCenter = true;

    /// <summary>While true, a new training region snaps the origin to its centre.</summary>
    public bool OriginFollowsCenter
    {
        get => _originFollowsCenter;
        set
        {
            if (!Set(ref _originFollowsCenter, value)) return;
            if (value) SnapOriginToCenter();
            OnPropertyChanged(nameof(OriginIsFollowedByCenter));
        }
    }

    public bool OriginIsFollowedByCenter => _originFollowsCenter;

    public string OriginText => $"({OriginX:F1}, {OriginY:F1})";

    /// <summary>
    /// Where the origin marker is drawn.
    ///
    /// This is the model origin *in the image currently shown*: before a run that is the point
    /// picked on the training region, after a run it is the matched pose - the same point, moved
    /// with the part. It used to be bound to the training coordinates only, which is why the
    /// marker stayed behind after a match.
    /// </summary>
    public System.Windows.Point? OriginPoint =>
        Markers.Count > 0 ? new System.Windows.Point(Markers[0].X, Markers[0].Y)
        : TrainRegion.HasValue ? new System.Windows.Point(OriginX, OriginY)
        : null;

    public ICommand CenterOriginCommand { get; }

    private void SnapOriginToCenter()
    {
        if (TrainRegion is not Rect r) return;
        _originX = r.X + r.Width / 2.0;
        _originY = r.Y + r.Height / 2.0;
        OnPropertyChanged(nameof(OriginX));
        OnPropertyChanged(nameof(OriginY));
        OnPropertyChanged(nameof(OriginText));
        OnPropertyChanged(nameof(OriginPoint));
    }

    private bool _showTrainRegion = true;

    /// <summary>
    /// Whether the green training rectangle is drawn. Turned off after a run so the overlay
    /// does not sit on top of the match result, turned back on when a region is (re)drawn.
    /// </summary>
    public bool ShowTrainRegion
    {
        get => _showTrainRegion;
        private set => Set(ref _showTrainRegion, value);
    }
    public Rect? SearchRegion
    {
        get => _searchRegion;
        set { if (Set(ref _searchRegion, value)) OnPropertyChanged(nameof(SearchRegionText)); }
    }
    public bool Trained
    {
        get => _trained;
        private set
        {
            if (Set(ref _trained, value)) System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool CanTrain => _image != null && TrainRegion.HasValue;

    /// <summary>Displayed image size in pixels.</summary>
    public int ImagePixelWidth { get; private set; }
    public int ImagePixelHeight { get; private set; }

    // ---- Parameters (bound to the UI) ----
    private int _PrecisionStep = 1;
    public int PrecisionStep
    {
        get => _PrecisionStep;
        set => Set(ref _PrecisionStep, value);
    }
    private bool _SubpixelRefinement = true;
    public bool SubpixelRefinement
    {
        get => _SubpixelRefinement;
        set => Set(ref _SubpixelRefinement, value);
    }
    private bool _EnableClustering = true;
    public bool EnableClustering
    {
        get => _EnableClustering;
        set => Set(ref _EnableClustering, value);
    }
    private double _ClusterRadius = 8.0;
    public double ClusterRadius
    {
        get => _ClusterRadius;
        set => Set(ref _ClusterRadius, value);
    }
    private double _PreSmoothSigma = 0.5;
    public double PreSmoothSigma
    {
        get => _PreSmoothSigma;
        set => Set(ref _PreSmoothSigma, value);
    }
    private bool _UseSimd = true;
    public bool UseSimd
    {
        get => _UseSimd;
        set => Set(ref _UseSimd, value);
    }
    private bool _UseParallel = true;
    public bool UseParallel
    {
        get => _UseParallel;
        set => Set(ref _UseParallel, value);
    }

    // ---- Search parameters ----
    private double _RotationAngleMin = -180;
    public double RotationAngleMin
    {
        get => _RotationAngleMin;
        set { if (Set(ref _RotationAngleMin, value)) OnPropertyChanged(nameof(ParamsSummary)); }
    }
    private double _RotationAngleMax = 180;
    public double RotationAngleMax
    {
        get => _RotationAngleMax;
        set { if (Set(ref _RotationAngleMax, value)) OnPropertyChanged(nameof(ParamsSummary)); }
    }
    private double _ScaleMin = 1.0;
    public double ScaleMin
    {
        get => _ScaleMin;
        set { if (Set(ref _ScaleMin, value)) OnPropertyChanged(nameof(ParamsSummary)); }
    }
    private double _ScaleMax = 1.0;
    public double ScaleMax
    {
        get => _ScaleMax;
        set { if (Set(ref _ScaleMax, value)) OnPropertyChanged(nameof(ParamsSummary)); }
    }
    private double _AcceptThreshold = 0.7;
    public double AcceptThreshold
    {
        get => _AcceptThreshold;
        set { if (Set(ref _AcceptThreshold, value)) OnPropertyChanged(nameof(ParamsSummary)); }
    }
    // ---- Care / Don''t-care mask ----
    private byte[] _mask;
    /// <summary>Mask over the loaded image: 0 = Don''t care, 1 = Care. Null until an image is loaded.</summary>
    public byte[] Mask
    {
        get => _mask;
        set
        {
            _mask = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasMask));
            OnPropertyChanged(nameof(MaskSummary));
        }
    }

    public bool HasMask => _mask != null;

    /// <summary>Live readout: how many pixels the mask currently excludes.</summary>
    public string MaskSummary
    {
        get
        {
            if (_mask == null) return "无掩膜";
            int care = 0;
            foreach (var b in _mask) if (b != 0) care++;
            return $"掩膜 {ImagePixelWidth}×{ImagePixelHeight}：排除 {_mask.Length - care} px，保留 {care} px";
        }
    }

    private string _mouseInfo = "";
    /// <summary>Live cursor readout: image coordinates and the grey value under the pointer.</summary>
    public string MouseInfoText
    {
        get => _mouseInfo;
        private set => Set(ref _mouseInfo, value);
    }

    /// <summary>Called by the canvas on every mouse move.</summary>
    public void UpdateCursor(double x, double y)
    {
        if (_imageGray == null) { MouseInfoText = ""; return; }
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y);
        if (ix < 0 || iy < 0 || ix >= _imageGray.Width || iy >= _imageGray.Height)
        {
            MouseInfoText = $"X {x,6:F0}  Y {y,6:F0}   （图像外）";
            return;
        }
        byte grey = _imageGray.Data[iy * _imageGray.Width + ix];
        MouseInfoText = $"X {ix,4}  Y {iy,4}   灰度 {grey,3}";
    }

    /// <summary>Recompute the readout after the canvas painted a stroke.</summary>
    public void RefreshMaskSummary() => OnPropertyChanged(nameof(MaskSummary));

    private bool _useMask;
    /// <summary>Apply the painted mask to training and to the run.</summary>
    public bool UseMask
    {
        get => _useMask;
        set { if (Set(ref _useMask, value)) OnPropertyChanged(nameof(MaskEditing)); }
    }

    private bool _maskMode;
    /// <summary>Left-drag on the canvas paints the mask instead of selecting a region.</summary>
    public bool MaskMode
    {
        get => _maskMode;
        set { if (Set(ref _maskMode, value)) OnPropertyChanged(nameof(MaskEditing)); }
    }

    public bool MaskEditing => _maskMode && _mask != null;

    private bool _maskErase = true;
    /// <summary>Brush marks Don''t care (true) or restores Care (false).</summary>
    public bool MaskErase { get => _maskErase; set => Set(ref _maskErase, value); }

    private double _brushRadius = 14;
    public double BrushRadius { get => _brushRadius; set => Set(ref _brushRadius, value); }

    private int _maskScoringIndex;
    /// <summary>0 = DoNotScoreFeatures, 1 = ScoreFeaturesAsClutter.</summary>
    public int MaskScoringIndex { get => _maskScoringIndex; set => Set(ref _maskScoringIndex, value); }

    public ICommand ClearMaskCommand { get; }

    /// <summary>
    /// Crop the full-image mask down to the training region. The trainer indexes the mask with the
    /// image it is given, which is the crop, so passing the full-image array directly would read
    /// the wrong rows and quietly throw away most of the model.
    /// </summary>
    private byte[] CropMaskForTrain()
    {
        if (!UseMask || Mask == null || TrainRegion is not Rect r) return null;
        int x0 = Math.Clamp((int)r.X, 0, ImagePixelWidth - 1);
        int y0 = Math.Clamp((int)r.Y, 0, ImagePixelHeight - 1);
        int w = Math.Clamp((int)r.Width, 1, ImagePixelWidth - x0);
        int h = Math.Clamp((int)r.Height, 1, ImagePixelHeight - y0);
        var crop = new byte[w * h];
        for (int y = 0; y < h; y++)
            Array.Copy(Mask, (y0 + y) * ImagePixelWidth + x0, crop, y * w, w);
        return crop;
    }
    private void ResetMask()
    {
        if (ImagePixelWidth <= 0 || ImagePixelHeight <= 0) { Mask = null; return; }
        var m = new byte[ImagePixelWidth * ImagePixelHeight];
        Array.Fill(m, (byte)1);
        Mask = m;
    }

    private void ClearMask()
    {
        ResetMask();
        Status = "掩膜已清空（全部 Care）";
    }
    // ---- previously engine-only knobs, now exposed (高级) ----
    private int _MaxCandidates = 20;
    /// <summary>Hypotheses carried from the coarse stage into refinement.</summary>
    public int MaxCandidates { get => _MaxCandidates; set => Set(ref _MaxCandidates, value); }

    private double _CoarseAcceptThreshold = 0.33;
    /// <summary>Coarse-stage gate; lower costs time, higher risks missing marginal parts.</summary>
    public double CoarseAcceptThreshold
    {
        get => _CoarseAcceptThreshold;
        set => Set(ref _CoarseAcceptThreshold, value);
    }

    private double _ScoreExponentN = 2.0;
    /// <summary>score = coverage^(1/N). Smaller N is stricter about partial matches.</summary>
    public double ScoreExponentN { get => _ScoreExponentN; set => Set(ref _ScoreExponentN, value); }

    private bool _TrainMinSpacingAuto = true;
    /// <summary>Let the grain decide the feature spacing (PatMax has no spacing parameter).</summary>
    public bool TrainMinSpacingAuto
    {
        get => _TrainMinSpacingAuto;
        set { if (Set(ref _TrainMinSpacingAuto, value)) OnPropertyChanged(nameof(SpacingSummary)); }
    }

    /// <summary>What the trainer will actually use, shown next to the switch.</summary>
    public string SpacingSummary => TrainMinSpacingAuto
        ? $"自动：{Math.Clamp(2.0 * Grain, 1.0, 6.0):F1} px（= 2 × 粒度）"
        : $"手动：{TrainMinSpacing:F1} px";

    private double _TrainFeatureFloor = 0.08;
    /// <summary>Training edge floor as a fraction of the strongest edge in the region.</summary>
    public double TrainFeatureFloor { get => _TrainFeatureFloor; set => Set(ref _TrainFeatureFloor, value); }
    private double _MinPolarityCoherence = 0.75;
    /// <summary>
    /// Polarity coherence gate (0..1): share of matched features whose edge points the way the
    /// model normal does. A real instance reads ~1.0, a pose that only shares edge density ~0.5.
    /// </summary>
    public double MinPolarityCoherence
    {
        get => _MinPolarityCoherence;
        set { if (Set(ref _MinPolarityCoherence, value)) OnPropertyChanged(nameof(ParamsSummary)); }
    }

    private double _ContrastThreshold = 0.1;
    public double ContrastThreshold
    {
        get => _ContrastThreshold;
        set => Set(ref _ContrastThreshold, value);
    }
    private double _Grain = 1.0;
    public double Grain
    {
        get => _Grain;
        set => Set(ref _Grain, value);
    }
    private int _MaxResults = 10;
    public int MaxResults
    {
        get => _MaxResults;
        set { if (Set(ref _MaxResults, value)) OnPropertyChanged(nameof(ParamsSummary)); }
    }
    private int _CoarseGrainLimit = 10;
    public int CoarseGrainLimit
    {
        get => _CoarseGrainLimit;
        set => Set(ref _CoarseGrainLimit, value);
    }
    private int _FineGrainLimit = 5;
    public int FineGrainLimit
    {
        get => _FineGrainLimit;
        set => Set(ref _FineGrainLimit, value);
    }
    private double _XYOverlap = 0.5;
    public double XYOverlap
    {
        get => _XYOverlap;
        set => Set(ref _XYOverlap, value);
    }
    private double _TimeoutMs = 5000;

    /// <summary>"不限" reads better than "0 ms" for the unlimited case.</summary>
    public string TimeoutText => TimeoutMs <= 0 ? "超时：不限" : $"超时：{TimeoutMs:F0} ms";
    public double TimeoutMs
    {
        get => _TimeoutMs;
        set { if (Set(ref _TimeoutMs, value)) OnPropertyChanged(nameof(TimeoutText)); }
    }
    private int _ApproximateNumberToFind = 1;
    public int ApproximateNumberToFind
    {
        get => _ApproximateNumberToFind;
        set => Set(ref _ApproximateNumberToFind, value);
    }
    private bool _ScoreUsingClutter = true;
    public bool ScoreUsingClutter
    {
        get => _ScoreUsingClutter;
        set { if (Set(ref _ScoreUsingClutter, value)) OnPropertyChanged(nameof(ParamsSummary)); }
    }

    public ObservableCollection<MatchResult> Results { get; } = new();

    /// <summary>Markers drawn on the image (position + angle + score).</summary>
    private List<MatchMarker> _markers = new();
    public List<MatchMarker> Markers
    {
        get => _markers;
        private set
        {
            _markers = value ?? new List<MatchMarker>();
            OnPropertyChanged();
            OnPropertyChanged(nameof(OriginPoint));
        }
    }

    /// <summary>
    /// Model overlay: feature position relative to the pattern origin plus its unit edge
    /// normal, so the viewer can rotate and scale it onto each matched pose.
    /// </summary>
    public List<OutlineFeature> PatternOutline { get; private set; } = new();

    /// <summary>CSV written by the last batch run (empty when none has run).</summary>
    public string LastBatchCsv { get; private set; } = "";

    /// <summary>Last run processing time (ms).</summary>
    public double ProcessingTimeMs { get; private set; }

    /// <summary>Last run throughput (Mpx/s).</summary>
    public double ThroughputMpxs { get; private set; }

    /// <summary>Currently selected result (highlights the marker on the image).</summary>
    private MatchResult _selectedResult;

    /// <summary>
    /// Row selected in the results grid. When the row came from a batch run it also loads that
    /// image and shows its pose, so the list doubles as a review tool.
    /// </summary>
    public MatchResult SelectedResult
    {
        get => _selectedResult;
        set
        {
            if (!Set(ref _selectedResult, value)) return;
            InspectSelectedResult();
        }
    }

    /// <summary>Folder of the last batch run (used to resolve a row back to its image file).</summary>
    public string LastBatchFolder { get; private set; } = "";

    private void InspectSelectedResult()
    {
        var r = _selectedResult;
        if (r == null || LastBatchFolder.Length == 0 || string.IsNullOrEmpty(r.ModelId)) return;

        string path = System.IO.Path.Combine(LastBatchFolder, r.ModelId);
        if (!System.IO.File.Exists(path)) return;

        ShowImageWithoutClearing(path);
        Markers = r.Score < 0.001
            ? new List<MatchMarker>()
            : new List<MatchMarker> { ToMarker(r) };
        OnPropertyChanged(nameof(Markers));
        ShowTrainRegion = false;

        Status = r.Score < 0.001
            ? $"{r.ModelId}  —  无结果"
            : $"{r.ModelId}  得分 {r.Score:F3}  覆盖率 {r.Coverage:P0}  " +
              $"位置 ({r.X:F1},{r.Y:F1})  角度 {r.AngleDegrees:F2}°  缩放 {r.Scale:F3}";
    }

    /// <summary>Swap the displayed image without touching the results list.</summary>
    private void ShowImageWithoutClearing(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path);
        bmp.EndInit();
        bmp.Freeze();
        Image = bmp;
        ImagePixelWidth = bmp.PixelWidth;
        ImagePixelHeight = bmp.PixelHeight;
        ResetMask();          // a mask belongs to one image: start from "all Care"
        MaskMode = false;
        ResetMask();
    }

    /// <summary>Result -> marker on the canvas.</summary>
    private static MatchMarker ToMarker(MatchResult r) => new()
    {
        X = r.X, Y = r.Y,
        AngleDeg = r.AngleDegrees,
        Scale = r.Scale,
        Score = r.Score,
        Accepted = r.Accepted,
        Size = Math.Max(12, Math.Sqrt(Math.Max(1, r.MatchedFeatures)) * 1.5)
    };
    #endregion

    /// <summary>Set the status line from outside the view model (recording progress).</summary>
    public void SetStatus(string text) => Status = text;

    private void OpenImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择图片",
            Filter = "图片文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.gif|BMP|*.bmp|PNG|*.png|JPEG|*.jpg;*.jpeg|TIFF|*.tif;*.tiff|所有文件|*.*"
        };
        if (dlg.ShowDialog() != true) return;
        LoadImageFile(dlg.FileName);
    }

    /// <summary>Load an image by path. Also used for a path given on the command line.</summary>
    /// <summary>
    /// Regression check for "loading a new image must clear the previous overlay": loads the first
    /// image, plants a marker/result as a run would, loads the second, and reports what is left.
    /// </summary>
    public string VerifyLoadClearsOverlay(string first, string second)
    {
        LoadImageFile(first);
        Markers = new List<MatchMarker> { new() { X = 1, Y = 1, Accepted = true } };
        Results.Add(new MatchResult { ModelId = "planted", Score = 1.0, Accepted = true });
        int beforeMarkers = Markers.Count, beforeResults = Results.Count;

        LoadImageFile(second);
        return $"before: markers={beforeMarkers} results={beforeResults} | " +
               $"after: markers={Markers.Count} results={Results.Count} " +
               $"selection={(SelectedResult == null ? "null" : "set")} time={ProcessingTimeMs:F1}ms";
    }

    public void LoadImageFile(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            Image = bmp;
            ImagePixelWidth = bmp.PixelWidth;
            ImagePixelHeight = bmp.PixelHeight;
            _imageGray = ToGray(bmp);
            ResetMask();          // a mask belongs to one image: start from "all Care"
            MaskMode = false;
            Status = $"已加载: {System.IO.Path.GetFileName(path)} ({bmp.PixelWidth}×{bmp.PixelHeight})";
            // Keep the trained model and parameters so the user can run directly on the new image,
            // but drop everything that belongs to the previous image: its result rows, the overlay
            // that was drawn from them, the timing and the selection.
            Results.Clear();
            Markers = new List<MatchMarker>();
            SelectedResult = null;
            ShowTrainRegion = true;
            ProcessingTimeMs = 0;
            ThroughputMpxs = 0;
            OnPropertyChanged(nameof(ProcessingTimeMs));
            OnPropertyChanged(nameof(ThroughputMpxs));
            OnPropertyChanged(nameof(Trained));
        }
        catch (Exception ex)
        {
            Status = $"加载失败: {ex.Message}";
        }
    }

    private void Train()
    {
        if (Image == null || !TrainRegion.HasValue) return;
        try
        {
            var gray = ToGray((BitmapSource)Image);
            var r = TrainRegion.Value;
            var crop = Crop(gray, (int)r.X, (int)r.Y, (int)r.Width, (int)r.Height);

            // The origin the user picked, expressed inside the crop. Training and matching are
            // both relative to it, so results report the coordinates of *that* point.
            if (OriginFollowsCenter) SnapOriginToCenter();
            double originX = Math.Clamp(OriginX - r.X, 0, crop.Width - 1);
            double originY = Math.Clamp(OriginY - r.Y, 0, crop.Height - 1);

            var pattern = TrainWithPanel(crop, new RectI(0, 0, crop.Width, crop.Height), originX, originY);

            // A region over plain background yields no features. Say so instead of leaving a
            // model that later fails with the bare "pattern not trained" from the engine.
            if (pattern.FeatureCount < 8)
            {
                Trained = false;
                Status = $"训练失败: 框内只有 {pattern.FeatureCount} 个边缘特征 —— 请把训练框拖到目标上" +
                         $"（区域 {r.X:F0},{r.Y:F0} {r.Width:F0}×{r.Height:F0}，" +
                         $"实际裁剪 {crop.Width}×{crop.Height}，梯度门限 {_tool.EdgeDetector.LastEffectiveFloor:F2}）";
                Results.Clear();
                return;
            }

            Trained = true;
            Status = $"训练完成: {pattern.FeatureCount} 特征, W={pattern.W:F2}, " +
                     $"原点 {OriginText}, 间距={_tool.LastEffectiveSpacing:F1}px, " +
            $"边缘阈值={_tool.EdgeDetector.LastEffectiveFloor:F1}" +
                     $"{(_tool.Pattern.PolarityAgnostic ? ", 无极性" : "")}";
            Results.Clear();
        }
        catch (Exception ex)
        {
            Status = $"训练失败: {ex.Message}";
        }
    }

    /// <summary>
    /// Train on the first image of a folder and run every image in it, one row per image in the
    /// results grid. This is how a whole capture set is judged: the numbers that drift between
    /// frames are the ones worth arguing about.
    /// </summary>
    private void BatchValidate()
    {
        var dlg = new OpenFolderDialog { Title = "选择图片文件夹（第一张用作训练基准）" };
        if (dlg.ShowDialog() != true) return;
        RunBatch(dlg.FolderName);
    }

    /// <summary>Run the batch over a folder (also callable directly, e.g. for a scripted check).</summary>
    public void RunBatch(string directory)
    {
        try
        {
            var files = ImageLoader.Enumerate(directory);
            if (files.Count == 0)
            {
                Status = "该文件夹里没有 PNG / BMP 图片";
                return;
            }

            var baseImage = ImageLoader.Load(files[0]);
            var roi = PatternTrainer.ContentBounds(baseImage);

            var pattern = TrainWithPanel(baseImage, roi,
                roi.X + (roi.Width - 1) / 2.0, roi.Y + (roi.Height - 1) / 2.0);
            // batch/review use the region centre as origin by definition
            TrainRegion = new Rect(roi.X, roi.Y, roi.Width, roi.Height);
            OriginFollowsCenter = true;

            if (pattern.FeatureCount < 8)
            {
                Trained = false;
                Status = $"批量失败: 基准图 {System.IO.Path.GetFileName(files[0])} 只训练出 {pattern.FeatureCount} 个特征";
                return;
            }
            Trained = true;

            ApplyParameters();
            LastBatchFolder = directory;
            LoadImageFile(files[0]);          // show the base image and its model
            Results.Clear();

            var watch = System.Diagnostics.Stopwatch.StartNew();
            int found = 0;
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("image,score,x,y,angle_deg,scale,coverage,matched,bad,accepted,time_ms,instances");

            foreach (var file in files)
            {
                var gray = ImageLoader.Load(file);
                var one = System.Diagnostics.Stopwatch.StartNew();
                int want = Math.Max(1, Math.Max(MaxResults, ApproximateNumberToFind));
                var r = _tool.Run(gray, want);
                one.Stop();
                string name = System.IO.Path.GetFileName(file);

                if (r.Count > 0)
                {
                    var m = r[0];
                    m.ModelId = name;
                    Results.Add(m);
                    found++;
                    csv.AppendLine($"{name},{m.Score:F3},{m.X:F2},{m.Y:F2},{m.AngleDegrees:F2}," +
                                   $"{m.Scale:F3},{m.Coverage:P1},{m.MatchedFeatures},{m.BadFeatures}," +
                                   $"{m.Accepted},{one.Elapsed.TotalMilliseconds:F1},{r.Count}");
                }
                else
                {
                    Results.Add(new MatchResult { ModelId = name + "  —  无结果", Score = 0, Accepted = false });
                    csv.AppendLine($"{name},0,,,,,,,,{one.Elapsed.TotalMilliseconds:F1},0");
                }
            }
            watch.Stop();

            // The grid only shows a handful of rows, so the whole run also lands in a file.
            string csvPath = System.IO.Path.Combine(directory, "patmax_batch_results.csv");
            System.IO.File.WriteAllText(csvPath, csv.ToString(), new System.Text.UTF8Encoding(false));
            LastBatchCsv = csvPath;

            OnPropertyChanged(nameof(Results));
            ProcessingTimeMs = watch.Elapsed.TotalMilliseconds;
            Status = $"批量完成: {found}/{files.Count} 张命中, 基准 {System.IO.Path.GetFileName(files[0])} " +
                     $"({pattern.FeatureCount} 特征), 总计 {watch.ElapsedMilliseconds} ms → 已导出 {System.IO.Path.GetFileName(csvPath)}";
        }
        catch (Exception ex)
        {
            Status = $"批量失败: {ex.Message}";
        }
    }

    /// <summary>
    /// Train on the first image of a folder, then walk the whole folder: load, match, hold for
    /// <paramref name="dwellMs"/> so the overlay can be watched (or recorded), next. This is the
    /// review loop - every frame shows the model features projected onto the part.
    /// </summary>
    public async System.Threading.Tasks.Task CycleFolderAsync(string directory, int dwellMs = 900,
        Action<string> onImageDone = null)
    {
        try
        {
            var files = ImageLoader.Enumerate(directory);
            if (files.Count == 0) { Status = "该文件夹里没有 PNG / BMP 图片"; return; }

            var baseImage = ImageLoader.Load(files[0]);
            var roi = PatternTrainer.ContentBounds(baseImage);
            var pattern = TrainWithPanel(baseImage, roi,
                roi.X + (roi.Width - 1) / 2.0, roi.Y + (roi.Height - 1) / 2.0);
            // batch/review use the region centre as origin by definition
            TrainRegion = new Rect(roi.X, roi.Y, roi.Width, roi.Height);
            OriginFollowsCenter = true;
            if (pattern.FeatureCount < 8)
            {
                Trained = false;
                Status = $"循环失败: 基准图 {System.IO.Path.GetFileName(files[0])} 只训练出 {pattern.FeatureCount} 个特征";
                return;
            }
            Trained = true;
            ApplyParameters();

            int index = 0;
            foreach (var file in files)
            {
                index++;
                var gray = ImageLoader.Load(file);
                LoadImageFile(file);
                await System.Threading.Tasks.Task.Delay(150);

                var watch = System.Diagnostics.Stopwatch.StartNew();
                var results = _tool.Run(gray, Math.Max(1, MaxResults));
                watch.Stop();

                Results.Clear();
                var markers = new List<MatchMarker>();
                foreach (var r in results)
                {
                    Results.Add(r);
                    markers.Add(new MatchMarker
                    {
                        X = r.X, Y = r.Y, AngleDeg = r.AngleDegrees, Scale = r.Scale,
                        Score = r.Score, Accepted = r.Accepted,
                        Size = Math.Max(12, Math.Sqrt(Math.Max(1, r.MatchedFeatures)) * 1.5)
                    });
                }
                Markers = markers;
                if (_tool.Pattern != null)
                {
                    PatternOutline = _tool.Pattern.Featurelets
                        .Select(f => new OutlineFeature
                        {
                            X = f.X - _tool.Pattern.OriginX, Y = f.Y - _tool.Pattern.OriginY,
                            Nx = f.Nx, Ny = f.Ny
                        })
                        .ToList();
                    OnPropertyChanged(nameof(PatternOutline));
                }
                ShowTrainRegion = false;
                ProcessingTimeMs = watch.Elapsed.TotalMilliseconds;
                OnPropertyChanged(nameof(Markers));
                OnPropertyChanged(nameof(ProcessingTimeMs));
                Status = $"[{index}/{files.Count}] {System.IO.Path.GetFileName(file)}  —  " +
                         $"找到 {results.Count} 个结果, 耗时 {watch.ElapsedMilliseconds} ms";
                onImageDone?.Invoke(file);
                await System.Threading.Tasks.Task.Delay(Math.Max(200, dwellMs));
            }

            Status = $"循环结束: 共 {files.Count} 张（基准 {System.IO.Path.GetFileName(files[0])}, {pattern.FeatureCount} 特征）";
        }
        catch (Exception ex)
        {
            Status = $"循环失败: {ex.Message}";
        }
    }
    /// <summary>Map the current parameter panel onto the tool (shared by Run and RunBatch).</summary>
    private void ApplyParameters()
    {
        _tool.PrecisionStep = Math.Max(1, PrecisionStep);
        _tool.SubpixelRefinement = SubpixelRefinement;
        _tool.EnableClustering = EnableClustering;
        _tool.ClusterRadius = ClusterRadius;
        _tool.PreSmoothSigma = PreSmoothSigma;
        _tool.AcceptThreshold = AcceptThreshold;
        _tool.MinPolarityCoherence = MinPolarityCoherence;
        _tool.SearchMask = UseMask ? Mask : null;
        _tool.SearchMaskWidth = UseMask ? ImagePixelWidth : 0;
        _tool.SearchMaskHeight = UseMask ? ImagePixelHeight : 0;
        _tool.MaskScoring = MaskScoringIndex == 1
            ? ShapeAnchor.Matching.MaskScoringMethod.ScoreFeaturesAsClutter
            : ShapeAnchor.Matching.MaskScoringMethod.DoNotScoreFeatures;
        _tool.ContrastThreshold = ContrastThreshold;
        _tool.RotationAngleMin = RotationAngleMin;
        _tool.RotationAngleMax = RotationAngleMax;
        _tool.ScaleMin = ScaleMin;
        _tool.ScaleMax = ScaleMax;
        _tool.MaxCandidates = Math.Clamp(MaxCandidates, 4, 64);
        _tool.CoarseAcceptThreshold = CoarseAcceptThreshold;
        _tool.ScoreExponentN = ScoreExponentN;
        _tool.UseClutter = ScoreUsingClutter;
        _tool.TimeoutMs = TimeoutMs;
        _tool.XYOverlap = XYOverlap;
    }

    /// <summary>
    /// Train applying every training-parameter control. Single-image training, the batch check
    /// and the review walk all go through here, so the panel means the same thing in each.
    /// </summary>
    private PatternModel TrainWithPanel(GrayImage image, RectI roi, double originX, double originY)
    {
        _tool.EdgeDetector.UseSimd = UseSimd;
        _tool.EdgeDetector.UseParallel = UseParallel;
        _tool.EdgeDetector.NoiseFloor = EdgeThreshold;
        _tool.EdgeDetector.AutoNoiseFloor = AutoEdgeThreshold;
        _tool.IgnorePolarity = IgnorePolarity;
        _tool.TrainMaxFeatures = Math.Max(20, TrainMaxFeatures);
        _tool.TrainMinSpacing = Math.Max(1.0, TrainMinSpacing);
        _tool.TrainFeatureFloor = Math.Clamp(TrainFeatureFloor, 0.0, 0.9);
        _tool.TrainMinSpacingAuto = TrainMinSpacingAuto;
        _tool.TrainMask = CropMaskForTrain();
        return _tool.Train(image, roi, originX, originY, Grain);
    }

    private void Run()
    {
        if (Image == null || !Trained) return;
        try
        {
            var gray = ToGray((BitmapSource)Image);
            _tool.PrecisionStep = Math.Max(1, PrecisionStep);
            _tool.SubpixelRefinement = SubpixelRefinement;
            _tool.EnableClustering = EnableClustering;
            _tool.ClusterRadius = ClusterRadius;
            _tool.PreSmoothSigma = PreSmoothSigma;
            _tool.AcceptThreshold = AcceptThreshold;
        _tool.MinPolarityCoherence = MinPolarityCoherence;
        _tool.SearchMask = UseMask ? Mask : null;
        _tool.SearchMaskWidth = UseMask ? ImagePixelWidth : 0;
        _tool.SearchMaskHeight = UseMask ? ImagePixelHeight : 0;
        _tool.MaskScoring = MaskScoringIndex == 1
            ? ShapeAnchor.Matching.MaskScoringMethod.ScoreFeaturesAsClutter
            : ShapeAnchor.Matching.MaskScoringMethod.DoNotScoreFeatures;
            _tool.ContrastThreshold = ContrastThreshold;
            _tool.RotationAngleMin = RotationAngleMin;
            _tool.RotationAngleMax = RotationAngleMax;
            _tool.ScaleMin = ScaleMin;
            _tool.ScaleMax = ScaleMax;
            _tool.MaxCandidates = Math.Clamp(MaxCandidates, 4, 64);
        _tool.CoarseAcceptThreshold = CoarseAcceptThreshold;
        _tool.ScoreExponentN = ScoreExponentN;
            _tool.UseClutter = ScoreUsingClutter;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var results = _tool.Run(gray, MaxResults);
            sw.Stop();

            Results.Clear();
            var markers = new List<MatchMarker>();
            foreach (var r in results)
            {
                Results.Add(r);
                markers.Add(new MatchMarker
                {
                    X = r.X, Y = r.Y,
                    AngleDeg = r.Angle * 180 / Math.PI,
                    Scale = r.Scale,
                    Score = r.Score,
                    Accepted = r.Accepted,
                    Size = Math.Max(12, Math.Sqrt(Math.Max(1, r.MatchedFeatures)) * 1.5)
                });
            }
            Markers = markers;

            // Model overlay: position relative to the origin plus the edge normal, so the
            // viewer can rotate/scale it onto the found pose (a bare point list could not be).
            if (_tool.Pattern != null)
            {
                PatternOutline = _tool.Pattern.Featurelets
                    .Select(f => new OutlineFeature
                    {
                        X = f.X - _tool.Pattern.OriginX,
                        Y = f.Y - _tool.Pattern.OriginY,
                        Nx = f.Nx,
                        Ny = f.Ny
                    })
                    .ToList();
                OnPropertyChanged(nameof(PatternOutline));
            }

            // the green training rectangle has done its job once the match is on screen
            ShowTrainRegion = false;
            ProcessingTimeMs = sw.Elapsed.TotalMilliseconds;
            ThroughputMpxs = (gray.Width * gray.Height / 1_000_000.0) / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 1e-9);

            OnPropertyChanged(nameof(Markers));
            OnPropertyChanged(nameof(ProcessingTimeMs));
            OnPropertyChanged(nameof(ThroughputMpxs));

            Status = $"找到 {Results.Count} 个结果, 耗时 {sw.ElapsedMilliseconds} ms";
        }
        catch (Exception ex)
        {
            Status = $"运行失败: {ex.Message}（先把训练框拖到目标上并点“训练模型”）";
        }
    }

    /// <summary>Refresh region bindings after a region is drawn on the canvas.</summary>
    public void RefreshRegions()
    {
        OnPropertyChanged(nameof(TrainRegion));
        OnPropertyChanged(nameof(SearchRegion));
        OnPropertyChanged(nameof(CanTrain));
        OnPropertyChanged(nameof(TrainRegionText));
        OnPropertyChanged(nameof(SearchRegionText));
    }

    private void Clear()
    {
        // Everything the canvas draws has to be reset here: result rows, markers, the model
        // overlay and the regions. Clearing only the regions left the match overlay behind.
        Results.Clear();
        Markers = new List<MatchMarker>();
        PatternOutline = new List<OutlineFeature>();
        OnPropertyChanged(nameof(PatternOutline));

        _tool.ClearPattern();
        Trained = false;
        ResetMask();

        TrainRegion = null;
        SearchRegion = null;

        ProcessingTimeMs = 0;
        ThroughputMpxs = 0;
        OnPropertyChanged(nameof(ProcessingTimeMs));
        OnPropertyChanged(nameof(ThroughputMpxs));
        Status = "已清空（图像保留，可重新框选）";
    }

    // ---- Image helpers ----
    /// <summary>
    /// Convert any WPF bitmap to 8-bit grey.
    ///
    /// The conversion goes through a FormatConvertedBitmap on purpose: the previous version
    /// copied pixels straight out of the source assuming 4 bytes per pixel, which is only true
    /// for Bgra32/Pbgra32. An 8-bit indexed or 24-bit image (every BMP this tool loads) came
    /// back shifted or black, so a box dragged over the part contained no edges at all.
    /// </summary>
    internal static GrayImage ToGray(BitmapSource src)
    {
        int w = src.PixelWidth, h = src.PixelHeight;
        BitmapSource bgra = src;
        if (src.Format != PixelFormats.Bgra32 && src.Format != PixelFormats.Pbgra32)
            bgra = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

        var bytes = new byte[w * h * 4];
        bgra.CopyPixels(bytes, w * 4, 0);

        var gray = new byte[w * h];
        for (int i = 0; i < w * h; i++)
        {
            byte b = bytes[i * 4], g = bytes[i * 4 + 1], r = bytes[i * 4 + 2], a = bytes[i * 4 + 3];
            int lum = (r * 299 + g * 587 + b * 114) / 1000;
            gray[i] = (byte)(a == 255 || a == 0 ? lum : lum * a / 255);
        }
        return new GrayImage(w, h, gray);
    }

    private static GrayImage Crop(GrayImage src, int x, int y, int w, int h)
    {
        x = Math.Clamp(x, 0, src.Width - 1);
        y = Math.Clamp(y, 0, src.Height - 1);
        w = Math.Clamp(w, 1, src.Width - x);
        h = Math.Clamp(h, 1, src.Height - y);
        var data = new byte[w * h];
        for (int j = 0; j < h; j++)
            Array.Copy(src.Data, (y + j) * src.Width + x, data, j * w, w);
        return new GrayImage(w, h, data);
    }

    #region INotifyPropertyChanged
    public event PropertyChangedEventHandler PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; OnPropertyChanged(name); return true;
    }
    #endregion
}

public sealed class RelayCommand : ICommand
{
    private readonly Action<object> _execute;
    private readonly Predicate<object> _canExecute;
    public RelayCommand(Action<object> execute, Predicate<object> canExecute = null) { _execute = execute; _canExecute = canExecute; }
    public bool CanExecute(object p) => _canExecute == null || _canExecute(p);
    public void Execute(object p) => _execute(p);
    public event EventHandler CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }
}
