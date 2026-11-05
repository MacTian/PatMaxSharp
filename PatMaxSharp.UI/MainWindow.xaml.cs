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

        // Notify the view model that the region properties changed (binding refresh)
        viewer.RegionChanged += _ =>
        {
            _vm.NotifyRegionChanged();
        };
    }
}

/// <summary>Helper extension so the view model can refresh region bindings.</summary>
public static class VmExtensions
{
    public static void NotifyRegionChanged(this MainViewModel vm)
    {
        vm.RefreshRegions();
    }
}
