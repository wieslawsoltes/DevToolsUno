using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;

namespace DevToolsUno.Diagnostics.Views;

public sealed partial class DevToolsRoot : UserControl
{
    public DevToolsRoot()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => RequestSelectedTabLayoutRecovery();

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => RequestSelectedTabLayoutRecovery();

    private void OnTabSelectionChanged(object sender, SelectionChangedEventArgs e) => RequestSelectedTabLayoutRecovery();

    private void RequestSelectedTabLayoutRecovery()
    {
        if (Tabs.SelectedItem is not TabViewItem item)
        {
            return;
        }

        if (item.Content is TreePageView treePage)
        {
            treePage.RequestLayoutRecovery();
            return;
        }

        if (item.Content is EventsPageView eventsPage)
        {
            eventsPage.RequestLayoutRecovery();
            return;
        }

        if (item.Content is ResourcesPageView resourcesPage)
        {
            resourcesPage.RequestLayoutRecovery();
            return;
        }

        if (item.Content is AssetsPageView assetsPage)
        {
            assetsPage.RequestLayoutRecovery();
            return;
        }

        if (item.Content is StylesPageView stylesPage)
        {
            stylesPage.RequestLayoutRecovery();
            return;
        }

        if (item.Content is BindingsPageView bindingsPage)
        {
            bindingsPage.RequestLayoutRecovery();
            return;
        }

        if (item.Content is MemoryPageView memoryPage)
        {
            memoryPage.RequestLayoutRecovery();
        }
    }
}
