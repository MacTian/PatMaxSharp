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
using PatMaxSharp;
using PatMaxSharp.Core;
using PatMaxSharp.UI.Controls;

namespace PatMaxSharp.UI.ViewModels;

/// <summary>
/// Main view model — binds the whole PatMax UI.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly PatMaxTool _tool = new();
    private BitmapSource _image;
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
    public ICommand SavePresetCommand { get; }
    public ICommand NewPresetCommand { get; }
    public ICommand DeletePresetCommand { get; }
    #endregion

    #region Presets
    public ObservableCollection<ParameterPreset> Presets { get; } = new();
    public ParameterPreset SelectedPreset { get; set; }

    private string PresetDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PatMaxSharp", "Presets");

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
    public ImageSource Image { get => _image; private set => Set(ref _image, (BitmapSource)value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public Rect? TrainRegion { get => _trainRegion; set { if (Set(ref _trainRegion, value)) OnPropertyChanged(nameof(CanTrain)); } }
    public Rect? SearchRegion { get => _searchRegion; set => Set(ref _searchRegion, value); }
    public bool Trained { get => _trained; private set => Set(ref _trained, value); }
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
        set => Set(ref _RotationAngleMin, value);
    }
    private double _RotationAngleMax = 180;
    public double RotationAngleMax
    {
        get => _RotationAngleMax;
        set => Set(ref _RotationAngleMax, value);
    }
    private double _ScaleMin = 0.8;
    public double ScaleMin
    {
        get => _ScaleMin;
        set => Set(ref _ScaleMin, value);
    }
    private double _ScaleMax = 1.2;
    public double ScaleMax
    {
        get => _ScaleMax;
        set => Set(ref _ScaleMax, value);
    }
    private double _AcceptThreshold = 0.5;
    public double AcceptThreshold
    {
        get => _AcceptThreshold;
        set => Set(ref _AcceptThreshold, value);
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
        set => Set(ref _MaxResults, value);
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
    public double TimeoutMs
    {
        get => _TimeoutMs;
        set => Set(ref _TimeoutMs, value);
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
        set => Set(ref _ScoreUsingClutter, value);
    }

    public ObservableCollection<MatchResult> Results { get; } = new();

    /// <summary>Markers drawn on the image (position + angle + score).</summary>
    public List<MatchMarker> Markers { get; private set; } = new();

    /// <summary>Model outline (featurelet positions relative to the pattern origin).</summary>
    public List<Point> PatternOutline { get; private set; } = new();

    /// <summary>Last run processing time (ms).</summary>
    public double ProcessingTimeMs { get; private set; }

    /// <summary>Last run throughput (Mpx/s).</summary>
    public double ThroughputMpxs { get; private set; }

    /// <summary>Currently selected result (highlights the marker on the image).</summary>
    public MatchResult SelectedResult { get; set; }
    #endregion

    private void OpenImage()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择图片",
            Filter = "图片文件|*.bmp;*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.gif|BMP|*.bmp|PNG|*.png|JPEG|*.jpg;*.jpeg|TIFF|*.tif;*.tiff|所有文件|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(dlg.FileName);
            bmp.EndInit();
            bmp.Freeze();
            Image = bmp;
            ImagePixelWidth = bmp.PixelWidth;
            ImagePixelHeight = bmp.PixelHeight;
            Status = $"已加载: {System.IO.Path.GetFileName(dlg.FileName)} ({bmp.PixelWidth}×{bmp.PixelHeight})";
            // Keep the trained model and parameters so the user can run directly
            // on the new image. Results from the previous image are cleared.
            Results.Clear();
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
            var originX = crop.Width / 2.0;
            var originY = crop.Height / 2.0;

            _tool.EdgeDetector.UseSimd = UseSimd;
            _tool.EdgeDetector.UseParallel = UseParallel;
            var pattern = _tool.Train(crop, originX, originY, Grain);
            Trained = true;
            Status = $"训练完成: {pattern.FeatureCount} 特征, W={pattern.W:F2}, grain={Grain}";
            Results.Clear();
        }
        catch (Exception ex)
        {
            Status = $"训练失败: {ex.Message}";
        }
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
            _tool.ContrastThreshold = ContrastThreshold;

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

            // model outline: featurelet positions relative to the pattern origin
            if (_tool.Pattern != null)
            {
                PatternOutline = _tool.Pattern.Featurelets
                    .Select(f => new Point(f.X - _tool.Pattern.OriginX, f.Y - _tool.Pattern.OriginY))
                    .ToList();
                OnPropertyChanged(nameof(PatternOutline));
            }
            ProcessingTimeMs = sw.Elapsed.TotalMilliseconds;
            ThroughputMpxs = (gray.Width * gray.Height / 1_000_000.0) / (sw.Elapsed.TotalSeconds > 0 ? sw.Elapsed.TotalSeconds : 1e-9);

            OnPropertyChanged(nameof(Markers));
            OnPropertyChanged(nameof(ProcessingTimeMs));
            OnPropertyChanged(nameof(ThroughputMpxs));

            Status = $"找到 {Results.Count} 个结果, 耗时 {sw.ElapsedMilliseconds} ms";
        }
        catch (Exception ex)
        {
            Status = $"运行失败: {ex.Message}";
        }
    }

    /// <summary>Result of the calibration verification test.</summary>
    public string CalibrationReport { get; private set; } = "";

    public ICommand CalibrationTestCommand => new RelayCommand(_ => RunCalibrationTest());

    private void RunCalibrationTest()
    {
        try
        {
            string dir = @"D:\FTP\calibration";
            if (!Directory.Exists(dir)) { Status = "标定目录不存在"; return; }

            var names = new[] { "1.png", "2.png", "3.png", "4.png", "5.png", "6.png", "7.png", "8.png", "9.png", "r1.png", "r2.png", "r3.png" };
            var sb = new System.Text.StringBuilder();

            // train on 1.png (load it if the current image is empty)
            var tmpl = Image == null ? LoadPng(Path.Combine(dir, "1.png")) : ToGray((BitmapSource)Image);
            var tb = FindObject(tmpl);
            var tool = new PatMaxTool();
            var pat = tool.Train(tmpl, tb.x + tb.w / 2.0, tb.y + tb.h / 2.0, Grain);
            sb.AppendLine($"模板 1.png: 特征数={pat.FeatureCount}, 目标({tb.x},{tb.y}) {tb.w}x{tb.h}");

            foreach (var name in names)
            {
                var fp = Path.Combine(dir, name);
                if (!File.Exists(fp)) continue;
                var img = LoadPng(fp);
                var ob = FindObject(img);
                int ex = ob.x + ob.w / 2, ey = ob.y + ob.h / 2;
                var r = tool.Run(img, 1);
                var s0 = r.Count > 0 ? r[0] : null;
                sb.AppendLine($"{name,-8} 期望=({ex,4},{ey,4})  " +
                              (s0 != null
                                  ? $"结果=({s0.X,4:F0},{s0.Y,4:F0}) 得分={s0.Score:F3} 覆盖={s0.Coverage:P0} 角度={s0.Angle * 180 / Math.PI:F1}°"
                                  : "无结果"));
            }
            CalibrationReport = sb.ToString();
            OnPropertyChanged(nameof(CalibrationReport));
            Status = "标定测试完成";
        }
        catch (Exception ex) { Status = "标定测试失败: " + ex.Message; }
    }

    private static (int x, int y, int w, int h) FindObject(GrayImage img)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int y = 0; y < img.Height; y++)
            for (int x = 0; x < img.Width; x++)
                if (img[x, y] > 40)
                {
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return (0, 0, img.Width, img.Height);
        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private static GrayImage LoadPng(string path)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.UriSource = new Uri(path);
        b.EndInit(); b.Freeze();
        int w = b.PixelWidth, h = b.PixelHeight;
        var px = new byte[w * h * 4];
        b.CopyPixels(px, w * 4, 0);
        var g = new byte[w * h];
        for (int i = 0; i < w * h; i++)
            g[i] = (byte)(px[i * 4] * 0.114 + px[i * 4 + 1] * 0.587 + px[i * 4 + 2] * 0.299);
        return new GrayImage(w, h, g);
    }

    /// <summary>Refresh region bindings after a region is drawn on the canvas.</summary>
    public void RefreshRegions()
    {
        OnPropertyChanged(nameof(TrainRegion));
        OnPropertyChanged(nameof(SearchRegion));
        OnPropertyChanged(nameof(CanTrain));
    }

    private void Clear()
    {
        Results.Clear();
        Trained = false;
        TrainRegion = null;
        SearchRegion = null;
        Status = "已清空";
    }

    // ---- Image helpers ----
    internal static GrayImage ToGray(BitmapSource src)
    {
        int w = src.PixelWidth, h = src.PixelHeight;
        var bytes = new byte[w * h * 4];
        src.CopyPixels(bytes, w * 4, 0);
        var gray = new byte[w * h];
        bool hasAlpha = src.Format == PixelFormats.Bgra32 || src.Format == PixelFormats.Pbgra32;
        for (int i = 0; i < w * h; i++)
        {
            byte b = bytes[i * 4], g = bytes[i * 4 + 1], r = bytes[i * 4 + 2];
            byte a = hasAlpha ? bytes[i * 4 + 3] : (byte)255;
            gray[i] = (byte)((r * 0.299 + g * 0.587 + b * 0.114) * (a / 255.0));
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
