using System;
using Uno.Controls;
using Uno.Controls.Primitives;
using DevToolsUno.Diagnostics.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace DevToolsUno.Diagnostics.Views;

public sealed partial class EventsPageView : UserControl
{
    private readonly DeferredLayoutRefresh _layoutRefresh;
    private readonly PointerEventHandler _eventLogPointerMovedHandler;
    private readonly PointerEventHandler _eventLogPointerExitedHandler;
    private readonly PointerEventHandler _routePointerMovedHandler;
    private readonly PointerEventHandler _routePointerExitedHandler;
    private readonly object _eventLogHoverOwner = new();
    private readonly object _routeHoverOwner = new();
    private bool _hoverHandlersAttached;
    private bool _isPaneResizing;
    private double _lastPaneX;

    public EventsPageView()
    {
        InitializeComponent();
        _layoutRefresh = new DeferredLayoutRefresh(this, 6, ListenersGrid, ResultsTabs);
        _eventLogPointerMovedHandler = OnEventLogPointerMoved;
        _eventLogPointerExitedHandler = OnEventLogPointerExited;
        _routePointerMovedHandler = OnRoutePointerMoved;
        _routePointerExitedHandler = OnRoutePointerExited;
        SplitterChrome.ApplyHorizontal(PaneSplitter);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnPaneSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isPaneResizing = true;
        _lastPaneX = e.GetCurrentPoint(LayoutRoot).Position.X;
        PaneSplitter.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void OnPaneSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isPaneResizing)
        {
            return;
        }

        var currentX = e.GetCurrentPoint(LayoutRoot).Position.X;
        var delta = currentX - _lastPaneX;
        if (Math.Abs(delta) < double.Epsilon)
        {
            return;
        }

        ResizeColumns(delta);
        _lastPaneX = currentX;
        e.Handled = true;
    }

    private void OnPaneSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndPaneResize();
        e.Handled = true;
    }

    private void OnPaneSplitterPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        EndPaneResize();
    }

    private void ResizeColumns(double delta)
    {
        var availableWidth = LayoutRoot.ActualWidth - PaneSplitter.Width;
        if (availableWidth <= 0)
        {
            return;
        }

        var leftMinWidth = ListenerColumn.MinWidth;
        var rightMinWidth = ResultsColumn.MinWidth;
        var newLeftWidth = Math.Max(leftMinWidth, ListenerColumn.ActualWidth + delta);
        var maxLeftWidth = Math.Max(leftMinWidth, availableWidth - rightMinWidth);
        newLeftWidth = Math.Min(newLeftWidth, maxLeftWidth);
        var newRightWidth = Math.Max(rightMinWidth, availableWidth - newLeftWidth);

        ListenerColumn.Width = new GridLength(newLeftWidth);
        ResultsColumn.Width = new GridLength(newRightWidth);
        _layoutRefresh.Request();
    }

    private void EndPaneResize()
    {
        _isPaneResizing = false;
        PaneSplitter.ReleasePointerCaptures();
    }

    private void OnEventLogPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is ViewModels.EventsPageViewModel viewModel)
        {
            viewModel.UpdateHoveredSource(ResolveHoveredModel<ViewModels.EventLogEntry>(e.OriginalSource as DependencyObject), _eventLogHoverOwner);
        }
    }

    private void OnEventLogPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is ViewModels.EventsPageViewModel viewModel)
        {
            viewModel.ClearHoveredOverlay(_eventLogHoverOwner);
        }
    }

    private void OnRoutePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is ViewModels.EventsPageViewModel viewModel)
        {
            viewModel.UpdateHoveredRouteElement(ResolveHoveredModel<ViewModels.EventRouteEntryViewModel>(e.OriginalSource as DependencyObject), _routeHoverOwner);
        }
    }

    private void OnRoutePointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (DataContext is ViewModels.EventsPageViewModel viewModel)
        {
            viewModel.ClearHoveredOverlay(_routeHoverOwner);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_hoverHandlersAttached)
        {
            return;
        }

        EventLogGrid.AddHandler(UIElement.PointerMovedEvent, _eventLogPointerMovedHandler, true);
        EventLogGrid.AddHandler(UIElement.PointerExitedEvent, _eventLogPointerExitedHandler, true);
        RoutesGrid.AddHandler(UIElement.PointerMovedEvent, _routePointerMovedHandler, true);
        RoutesGrid.AddHandler(UIElement.PointerExitedEvent, _routePointerExitedHandler, true);
        _hoverHandlersAttached = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_hoverHandlersAttached)
        {
            EventLogGrid.RemoveHandler(UIElement.PointerMovedEvent, _eventLogPointerMovedHandler);
            EventLogGrid.RemoveHandler(UIElement.PointerExitedEvent, _eventLogPointerExitedHandler);
            RoutesGrid.RemoveHandler(UIElement.PointerMovedEvent, _routePointerMovedHandler);
            RoutesGrid.RemoveHandler(UIElement.PointerExitedEvent, _routePointerExitedHandler);
            _hoverHandlersAttached = false;
        }

        if (DataContext is ViewModels.EventsPageViewModel viewModel)
        {
            viewModel.ClearHoveredOverlay(_eventLogHoverOwner);
            viewModel.ClearHoveredOverlay(_routeHoverOwner);
        }
    }

    internal void RequestLayoutRecovery()
        => _layoutRefresh.Request();

    private static TModel? ResolveHoveredModel<TModel>(DependencyObject? source)
        where TModel : class
    {
        for (var current = source; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is TreeDataGrid)
            {
                break;
            }

            if (current is TreeDataGridRow row && row.Model is TModel rowModel)
            {
                return rowModel;
            }

            if (current is FrameworkElement element && element.DataContext is TModel model)
            {
                return model;
            }
        }

        return null;
    }
}
