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
            if (e.PropertyName == nameof(MainViewModel.SelectedResult))
            {
                int idx = _vm.Results.IndexOf(_vm.SelectedResult);
                viewer.SelectedIndex = idx;
                resultsGrid.ScrollIntoView(_vm.SelectedResult);
            }
        };
    }

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        _vm.ApplyPreset(_vm.SelectedPreset);
        // refresh all parameter bindings after a preset is applied
        DataContext = null;
        DataContext = _vm;
    }
}
