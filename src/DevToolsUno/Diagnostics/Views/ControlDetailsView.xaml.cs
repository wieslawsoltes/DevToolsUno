using Microsoft.UI.Xaml.Controls;
using DevToolsUno.Diagnostics.Internal;

namespace DevToolsUno.Diagnostics.Views;

public sealed partial class ControlDetailsView : UserControl
{
    private readonly DeferredLayoutRefresh _layoutRefresh;

    public ControlDetailsView()
    {
        InitializeComponent();
        _layoutRefresh = new DeferredLayoutRefresh(this, 6, InspectorTabs);
    }

    internal void RequestLayoutRecovery()
    {
        _layoutRefresh.Request();
    }
}
