using DevToolsUno.Diagnostics.Internal;
using DevToolsUno.Diagnostics.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using System.ComponentModel;

namespace DevToolsUno.Diagnostics.Views;

public sealed partial class StylesPageView : UserControl
{
    private readonly DeferredLayoutRefresh _layoutRefresh;
    private readonly PaneSplitterController _paneSplitter;
    private readonly TreeDataGridSelectionBringIntoViewController _scopeSelectionBringIntoView;
    private readonly TreeDataGridSelectionBringIntoViewController _entrySelectionBringIntoView;
    private StylesPageViewModel? _viewModel;

    public StylesPageView()
    {
        InitializeComponent();
        _layoutRefresh = new DeferredLayoutRefresh(this, 6, ScopeTree, ContentTabs);
        _paneSplitter = new PaneSplitterController(LayoutRoot, PaneSplitter, ScopeColumn, ContentColumn, () => _layoutRefresh.Request());
        _scopeSelectionBringIntoView = new TreeDataGridSelectionBringIntoViewController(
            this,
            ScopeTree,
            dataContext => (dataContext as ViewModels.StylesPageViewModel)?.ScopeSelection);
        _entrySelectionBringIntoView = new TreeDataGridSelectionBringIntoViewController(
            this,
            EntriesGrid,
            dataContext => (dataContext as ViewModels.StylesPageViewModel)?.EntrySelection);
        DataContextChanged += OnDataContextChanged;
    }

    internal void RequestLayoutRecovery()
    {
        _paneSplitter.RefreshLayout();
        _layoutRefresh.Request();
        _scopeSelectionBringIntoView.RequestBringIntoView();
        _entrySelectionBringIntoView.RequestBringIntoView();
        DetailsView.RequestLayoutRecovery();
    }

    private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as StylesPageViewModel;

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        UpdateSelectedTab();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StylesPageViewModel.SelectedEntry))
        {
            UpdateSelectedTab();
        }
    }

    private void UpdateSelectedTab()
    {
        ContentTabs.SelectedIndex = _viewModel?.SelectedEntry is not null ? 1 : 0;
        _layoutRefresh.Request();
    }
}
