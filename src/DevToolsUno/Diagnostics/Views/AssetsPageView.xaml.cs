using DevToolsUno.Diagnostics.Internal;
using DevToolsUno.Diagnostics.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.ComponentModel;

namespace DevToolsUno.Diagnostics.Views;

public sealed partial class AssetsPageView : UserControl
{
    private readonly DeferredLayoutRefresh _layoutRefresh;
    private readonly PaneSplitterController _paneSplitter;
    private AssetsPageViewModel? _viewModel;

    public AssetsPageView()
    {
        InitializeComponent();
        _layoutRefresh = new DeferredLayoutRefresh(this, 6, FolderTree, ContentTabs);
        _paneSplitter = new PaneSplitterController(LayoutRoot, PaneSplitter, FolderColumn, ContentColumn, () => _layoutRefresh.Request());
        DataContextChanged += OnDataContextChanged;
    }

    internal void RequestLayoutRecovery()
    {
        _paneSplitter.RefreshLayout();
        _layoutRefresh.Request();
        PreviewView.RequestLayoutRecovery();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as AssetsPageViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateSelectedTab();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AssetsPageViewModel.SelectedAsset))
        {
            UpdateSelectedTab();
        }
    }

    private void UpdateSelectedTab()
    {
        ContentTabs.SelectedIndex = _viewModel?.SelectedAsset is not null ? 1 : 0;
        _layoutRefresh.Request();
    }
}
