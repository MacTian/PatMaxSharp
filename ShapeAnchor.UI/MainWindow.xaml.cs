using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using ShapeAnchor.UI.Controls;
using ShapeAnchor.UI.ViewModels;

namespace ShapeAnchor.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        // Command line:
        //   ShapeAnchor.UI.exe <image>             start with that image loaded
        //   ShapeAnchor.UI.exe --batch <folder>    run the folder check on startup
        //   ShapeAnchor.UI.exe --cycle <folder>    walk the folder so the overlays can be watched
        var args = System.Environment.GetCommandLineArgs();
        if (args.Length > 2 && args[1].Equals("--batch", System.StringComparison.OrdinalIgnoreCase)
            && System.IO.Directory.Exists(args[2]))
            _vm.RunBatch(args[2]);
        else if (args.Length > 2 && args[1].Equals("--cycle", System.StringComparison.OrdinalIgnoreCase)
            && System.IO.Directory.Exists(args[2]))
            _ = _vm.CycleFolderAsync(args[2], 1200);
        else if (args.Length > 3 && args[1].Equals("--record", System.StringComparison.OrdinalIgnoreCase)
            && System.IO.Directory.Exists(args[2]))
            StartRecording(args[2], args[3]);
        else if (args.Length > 6 && args[1].Equals("--show-region", System.StringComparison.OrdinalIgnoreCase)
            && System.IO.File.Exists(args[2]))
        {
            // deterministic check that image coordinates land where they should: draw a region
            // given in image pixels and see whether it sits on the part.
            _vm.LoadImageFile(args[2]);
            _vm.TrainRegion = new Rect(double.Parse(args[3]), double.Parse(args[4]),
                                       double.Parse(args[5]), double.Parse(args[6]));
        }
        else if (args.Length > 1 && System.IO.File.Exists(args[1]))
            _vm.LoadImageFile(args[1]);

        timeText.Text = "-";
        tputText.Text = "-";

        rbTrain.Checked += (_, _) => viewer.SelectMode = RegionKind.Train;
        rbSearch.Checked += (_, _) => viewer.SelectMode = RegionKind.Search;

        viewer.RegionChanged += _ => _vm.RefreshRegions();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ProcessingTimeMs))
                timeText.Text = $"{_vm.ProcessingTimeMs:F1}";
            if (e.PropertyName == nameof(MainViewModel.ThroughputMpxs))
                tputText.Text = $"{_vm.ThroughputMpxs:F2}";
            if (e.PropertyName == nameof(MainViewModel.SelectedResult))
            {
                // In review mode the canvas shows exactly one marker (the selected row), so the
                // highlighted marker is index 0 rather than the row index.
                int idx = _vm.Results.IndexOf(_vm.SelectedResult);
                viewer.SelectedIndex = (_vm.Markers?.Count ?? 0) == 1 ? 0 : idx;
                resultsGrid.ScrollIntoView(_vm.SelectedResult);
            }
        };
    }

    /// <summary>
    /// Walk a folder and render this window to PNG frames after every match, so the review clip
    /// shows exactly what the canvas draws regardless of other windows on the desktop.
    /// </summary>
    private async void StartRecording(string folder, string outDir)
    {
        Directory.CreateDirectory(outDir);
        int frame = 0;
        const int framesPerImage = 12;   // at 10 fps this is ~1.2 s per image

        await _vm.CycleFolderAsync(folder, 120, _ =>
        {
            for (int i = 0; i < framesPerImage; i++)
            {
                SaveFrame(outDir, frame++);
                System.Threading.Thread.Sleep(20);
            }
        });

        // two seconds of the final state
        for (int i = 0; i < 20; i++) { SaveFrame(outDir, frame++); System.Threading.Thread.Sleep(20); }
        _vm.SetStatus($"录制完成: {frame} 帧 → {outDir}");
    }

    /// <summary>Render the window content to one PNG frame.</summary>
    private void SaveFrame(string dir, int index)
    {
        UpdateLayout();
        var width = (int)System.Math.Ceiling(ActualWidth);
        var height = (int)System.Math.Ceiling(ActualHeight);
        if (width <= 0 || height <= 0) return;

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(Path.Combine(dir, $"frame_{index:D5}.png"));
        encoder.Save(stream);
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        _vm.ApplyPreset(_vm.SelectedPreset);
        // refresh all parameter bindings after a preset is applied
        DataContext = null;
        DataContext = _vm;
    }
}
