using DevToolsUno.Diagnostics.Internal;
using DevToolsUno.Diagnostics.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.ComponentModel;

namespace DevToolsUno.Diagnostics.Views;

public sealed partial class ResourcesPageView : UserControl
{
    private readonly DeferredLayoutRefresh _layoutRefresh;
    private readonly PaneSplitterController _paneSplitter;
    private readonly TreeDataGridSelectionBringIntoViewController _providerSelectionBringIntoView;
    private readonly TreeDataGridSelectionBringIntoViewController _resourceSelectionBringIntoView;
    private ResourcesPageViewModel? _viewModel;

    public ResourcesPageView()
    {
        InitializeComponent();
        _layoutRefresh = new DeferredLayoutRefresh(this, 6, ProviderTree, ContentTabs);
        _paneSplitter = new PaneSplitterController(LayoutRoot, PaneSplitter, ProviderColumn, ContentColumn, () => _layoutRefresh.Request());
        _providerSelectionBringIntoView = new TreeDataGridSelectionBringIntoViewController(
            this,
            ProviderTree,
            dataContext => (dataContext as ViewModels.ResourcesPageViewModel)?.ProviderSelection);
        _resourceSelectionBringIntoView = new TreeDataGridSelectionBringIntoViewController(
            this,
            ResourcesGrid,
            dataContext => (dataContext as ViewModels.ResourcesPageViewModel)?.ResourceSelection);
        DataContextChanged += OnDataContextChanged;
    }

    internal void RequestLayoutRecovery()
    {
        _paneSplitter.RefreshLayout();
        _layoutRefresh.Request();
        _providerSelectionBringIntoView.RequestBringIntoView();
        _resourceSelectionBringIntoView.RequestBringIntoView();
        DetailsView.RequestLayoutRecovery();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as ResourcesPageViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateSelectedTab();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResourcesPageViewModel.SelectedResource))
        {
            UpdateSelectedTab();
        }
    }

    private void UpdateSelectedTab()
    {
        ContentTabs.SelectedIndex = _viewModel?.SelectedResource is not null ? 1 : 0;
        _layoutRefresh.Request();
    }
}
