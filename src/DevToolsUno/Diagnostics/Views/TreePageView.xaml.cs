using System;
using Uno.Controls;
using Uno.Controls.Primitives;
using DevToolsUno.Diagnostics.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DevToolsUno.Diagnostics.Views;

public sealed partial class TreePageView : UserControl
{
    private readonly DeferredLayoutRefresh _layoutRefresh;
    private readonly TreeDataGridSelectionBringIntoViewController _selectionBringIntoView;
    private readonly PointerEventHandler _treePointerMovedHandler;
    private readonly PointerEventHandler _treePointerExitedHandler;
    private bool _treeHandlersAttached;
    private bool _isResizing;
    private double _lastSplitterX;

    public TreePageView()
    {
        InitializeComponent();
        _layoutRefresh = new DeferredLayoutRefresh(this, 6, TreeGrid, DetailsView);
        _selectionBringIntoView = new TreeDataGridSelectionBringIntoViewController(
            this,
            TreeGrid,
            dataContext => (dataContext as ViewModels.TreePageViewModel)?.Selection);
        _treePointerMovedHandler = OnTreePointerMoved;
        _treePointerExitedHandler = OnTreePointerExited;
        SplitterChrome.ApplyHorizontal(PaneSplitter);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    internal void RequestLayoutRecovery()
    {
        _layoutRefresh.Request();
        _selectionBringIntoView.RequestBringIntoView();
    }

    private void OnSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isResizing = true;
        _lastSplitterX = e.GetCurrentPoint(LayoutRoot).Position.X;
        PaneSplitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isResizing)
        {
            return;
        }

        var currentX = e.GetCurrentPoint(LayoutRoot).Position.X;
        var delta = currentX - _lastSplitterX;
        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        ResizeColumns(delta);
        _lastSplitterX = currentX;
        e.Handled = true;
    }

    private void OnSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndResize();
        e.Handled = true;
    }

    private void OnSplitterPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndResize();
    }

    private void ResizeColumns(double delta)
    {
        var availableWidth = LayoutRoot.ActualWidth - PaneSplitter.Width;
        if (availableWidth <= 0)
        {
            return;
        }

        var treeMinWidth = TreeColumn.MinWidth;
        var detailsMinWidth = DetailsColumn.MinWidth;
        var newTreeWidth = Math.Max(treeMinWidth, TreeColumn.ActualWidth + delta);
        var maxTreeWidth = Math.Max(treeMinWidth, availableWidth - detailsMinWidth);
        newTreeWidth = Math.Min(newTreeWidth, maxTreeWidth);
        var newDetailsWidth = Math.Max(detailsMinWidth, availableWidth - newTreeWidth);

        TreeColumn.Width = new GridLength(newTreeWidth);
        DetailsColumn.Width = new GridLength(newDetailsWidth);
        _layoutRefresh.Request();
    }

    private void EndResize()
    {
        _isResizing = false;
        PaneSplitter.ReleasePointerCaptures();
    }

    private void OnTreePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is not ViewModels.TreePageViewModel viewModel)
        {
            return;
        }

        viewModel.UpdateHoveredNode(ResolveHoveredNode(e.OriginalSource as DependencyObject));
    }

    private void OnTreePointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is ViewModels.TreePageViewModel viewModel)
        {
            viewModel.ClearHoveredNode();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_treeHandlersAttached)
        {
            return;
        }

        TreeGrid.AddHandler(UIElement.PointerMovedEvent, _treePointerMovedHandler, true);
        TreeGrid.AddHandler(UIElement.PointerExitedEvent, _treePointerExitedHandler, true);
        _treeHandlersAttached = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_treeHandlersAttached)
        {
            TreeGrid.RemoveHandler(UIElement.PointerMovedEvent, _treePointerMovedHandler);
            TreeGrid.RemoveHandler(UIElement.PointerExitedEvent, _treePointerExitedHandler);
            _treeHandlersAttached = false;
        }

        if (DataContext is ViewModels.TreePageViewModel viewModel)
        {
            viewModel.ClearHoveredNode();
        }
    }

    private static ViewModels.InspectableNode? ResolveHoveredNode(DependencyObject? source)
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TreeDataGrid)
            {
                break;
            }

            if (current is TreeDataGridRow row && row.Model is ViewModels.InspectableNode rowNode)
            {
                return rowNode;
            }

            if (current is FrameworkElement element && element.DataContext is ViewModels.InspectableNode node)
            {
                return node;
            }
        }

        return null;
    }
}
