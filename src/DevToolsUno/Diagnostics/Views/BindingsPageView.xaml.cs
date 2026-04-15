using DevToolsUno.Diagnostics.Internal;
using Microsoft.UI.Xaml.Controls;

namespace DevToolsUno.Diagnostics.Views;

public sealed partial class BindingsPageView : UserControl
{
    private readonly DeferredLayoutRefresh _layoutRefresh;
    private readonly TreeDataGridSelectionBringIntoViewController _bindingSelectionBringIntoView;

    public BindingsPageView()
    {
        InitializeComponent();
        _layoutRefresh = new DeferredLayoutRefresh(this, 6, BindingsGrid, ContentTabs);
        _bindingSelectionBringIntoView = new TreeDataGridSelectionBringIntoViewController(
            this,
            BindingsGrid,
            dataContext => (dataContext as ViewModels.BindingsPageViewModel)?.BindingSelection);
    }

    internal void RequestLayoutRecovery()
    {
        _layoutRefresh.Request();
        _bindingSelectionBringIntoView.RequestBringIntoView();
        DetailsView.RequestLayoutRecovery();
    }
}
