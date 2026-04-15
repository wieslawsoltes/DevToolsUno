using DevToolsUno.Diagnostics.Internal;
using Microsoft.UI.Xaml.Controls;

namespace DevToolsUno.Diagnostics.Views;

public sealed partial class MemoryPageView : UserControl
{
    private readonly DeferredLayoutRefresh _layoutRefresh;
    private readonly TreeDataGridSelectionBringIntoViewController _sampleSelectionBringIntoView;
    private readonly TreeDataGridSelectionBringIntoViewController _trackedSelectionBringIntoView;

    public MemoryPageView()
    {
        InitializeComponent();
        _layoutRefresh = new DeferredLayoutRefresh(this, 6, SamplesGrid, ContentTabs);
        _sampleSelectionBringIntoView = new TreeDataGridSelectionBringIntoViewController(
            this,
            SamplesGrid,
            dataContext => (dataContext as ViewModels.MemoryPageViewModel)?.SampleSelection);
        _trackedSelectionBringIntoView = new TreeDataGridSelectionBringIntoViewController(
            this,
            TrackedGrid,
            dataContext => (dataContext as ViewModels.MemoryPageViewModel)?.TrackedSelection);
    }

    internal void RequestLayoutRecovery()
    {
        _layoutRefresh.Request();
        _sampleSelectionBringIntoView.RequestBringIntoView();
        _trackedSelectionBringIntoView.RequestBringIntoView();
        DetailsView.RequestLayoutRecovery();
    }
}
