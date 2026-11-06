using System.Windows;
using System.Windows.Controls;
using PatMaxSharp.UI.Controls;
using PatMaxSharp.UI.ViewModels;

namespace PatMaxSharp.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;

        rbTrain.Checked += (_, _) => viewer.SelectMode = RegionKind.Train;
        rbSearch.Checked += (_, _) => viewer.SelectMode = RegionKind.Search;

        viewer.RegionChanged += _ => _vm.RefreshRegions();
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ProcessingTimeMs))
                timeText.Text = $"{_vm.ProcessingTimeMs:F1}";
            if (e.PropertyName == nameof(MainViewModel.ThroughputMpxs))
                tputText.Text = $"{_vm.ThroughputMpxs:F2}";
        };
    }
}
