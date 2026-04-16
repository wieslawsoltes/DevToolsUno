using Uno.Controls;
using Uno.Controls.Models.TreeDataGrid;
using Uno.Controls.Selection;
using DevToolsUno.Diagnostics.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using IndexPath = Uno.Controls.IndexPath;
using AGridLength = Avalonia.GridLength;
using AGridUnitType = Avalonia.GridUnitType;

namespace DevToolsUno.Diagnostics.ViewModels;

internal sealed class EventsPageViewModel : ViewModelBase, IDisposable
{
    private readonly MainViewModel _mainView;
    private readonly FrameworkElement _root;
    private readonly List<EventListenerNode> _listenerRoots = [];
    private readonly List<EventListenerNode> _listenerLeaves = [];
    private readonly Dictionary<string, ActiveListenerState> _activeStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<RoutedEventArgs, PendingEventCapture>> _pendingCaptures = new(StringComparer.Ordinal);
    private readonly List<EventLogEntry> _entries = [];
    private EventListenerNode? _selectedListener;
    private EventLogEntry? _selectedEntry;
    private EventRouteEntryViewModel? _selectedRouteEntry;
    private bool _isCapturing = true;
    private int _enabledListenerCount;
    private long _nextSequenceId;

    public EventsPageViewModel(MainViewModel mainView, FrameworkElement root)
    {
        _mainView = mainView;
        _root = root;
        EnableDefaultsCommand = new RelayCommand(EnableDefaults);
        EnableAllCommand = new RelayCommand(EnableAll);
        DisableAllCommand = new RelayCommand(DisableAll);
        ClearCommand = new RelayCommand(Clear);
        ToggleCaptureCommand = new RelayCommand(ToggleCapture);

        var listenerSource = new HierarchicalTreeDataGridSource<EventListenerNode>(Array.Empty<EventListenerNode>());
        listenerSource.Columns.Add(
            new HierarchicalExpanderColumn<EventListenerNode>(
                new TemplateColumn<EventListenerNode>("Listener", "EventListenerCellTemplate", width: new AGridLength(1, AGridUnitType.Star)),
                x => x.Children,
                x => x.Children.Count > 0,
                x => x.IsExpanded));
        ListenerTreeSource = listenerSource;
        ListenerSelection = new TreeDataGridRowSelectionModel<EventListenerNode>(ListenerTreeSource)
        {
            SingleSelect = true,
        };
        ListenerSelection.SelectionChanged += (_, _) => SelectedListener = ListenerSelection.SelectedItem;
        ListenerTreeSource.Selection = ListenerSelection;

        var logSource = new FlatTreeDataGridSource<EventLogEntry>(Array.Empty<EventLogEntry>());
        logSource.Columns.Add(new TextColumn<EventLogEntry, string>("Time", x => x.Time, new AGridLength(1, AGridUnitType.Auto)));
        logSource.Columns.Add(new TextColumn<EventLogEntry, string>("Event", x => x.EventText, new AGridLength(1.75, AGridUnitType.Star)));
        logSource.Columns.Add(new TextColumn<EventLogEntry, string>("Strategy", x => x.Strategy, new AGridLength(0.9, AGridUnitType.Star)));
        logSource.Columns.Add(new TextColumn<EventLogEntry, string>("Source", x => x.SourceDisplayName, new AGridLength(1.5, AGridUnitType.Star)));
        logSource.Columns.Add(new TextColumn<EventLogEntry, string>("Handled", x => x.HandledText, new AGridLength(0.9, AGridUnitType.Star)));
        logSource.Columns.Add(new TextColumn<EventLogEntry, string>("Route", x => x.RouteCountText, new AGridLength(0.9, AGridUnitType.Star)));
        LogSource = logSource;
        LogSelection = new TreeDataGridRowSelectionModel<EventLogEntry>(LogSource)
        {
            SingleSelect = true,
        };
        LogSelection.SelectionChanged += (_, _) => SelectedEntry = LogSelection.SelectedItem;
        LogSource.Selection = LogSelection;

        var routeSource = new FlatTreeDataGridSource<EventRouteEntryViewModel>(Array.Empty<EventRouteEntryViewModel>());
        routeSource.Columns.Add(new TextColumn<EventRouteEntryViewModel, string>("Step", x => x.StepText, new AGridLength(0.9, AGridUnitType.Star)));
        routeSource.Columns.Add(new TextColumn<EventRouteEntryViewModel, string>("Element", x => x.ElementDisplayName, new AGridLength(1.35, AGridUnitType.Star)));
        routeSource.Columns.Add(new TextColumn<EventRouteEntryViewModel, string>("Flags", x => x.FlagsText, new AGridLength(1.1, AGridUnitType.Star)));
        routeSource.Columns.Add(new TextColumn<EventRouteEntryViewModel, string>("Selector", x => x.Selector, new AGridLength(2.2, AGridUnitType.Star)));
        RouteSource = routeSource;
        RouteSelection = new TreeDataGridRowSelectionModel<EventRouteEntryViewModel>(RouteSource)
        {
            SingleSelect = true,
        };
        RouteSelection.SelectionChanged += (_, _) => SelectedRouteEntry = RouteSelection.SelectedItem;
        RouteSource.Selection = RouteSelection;

        InspectSelectedSourceCommand = new RelayCommand(InspectSelectedSource, () => SelectedEntry?.SourceElement is not null);
        InspectSelectedRouteElementCommand = new RelayCommand(InspectSelectedRouteElement, () => SelectedRouteEntry?.CanInspect == true);

        _root.LayoutUpdated += OnRootLayoutUpdated;

        BuildListenerTree();
        ListenerTreeSource.Items = _listenerRoots.ToArray();
        UpdateEnabledListenerCount();
        Refresh();
    }

    public HierarchicalTreeDataGridSource<EventListenerNode> ListenerTreeSource { get; }

    public TreeDataGridRowSelectionModel<EventListenerNode> ListenerSelection { get; }

    public FlatTreeDataGridSource<EventLogEntry> LogSource { get; }

    public TreeDataGridRowSelectionModel<EventLogEntry> LogSelection { get; }

    public FlatTreeDataGridSource<EventRouteEntryViewModel> RouteSource { get; }

    public TreeDataGridRowSelectionModel<EventRouteEntryViewModel> RouteSelection { get; }

    public RelayCommand EnableDefaultsCommand { get; }

    public RelayCommand EnableAllCommand { get; }

    public RelayCommand DisableAllCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand ToggleCaptureCommand { get; }

    public RelayCommand InspectSelectedSourceCommand { get; }

    public RelayCommand InspectSelectedRouteElementCommand { get; }

    public bool IsCapturing
    {
        get => _isCapturing;
        private set
        {
            if (RaiseAndSetIfChanged(ref _isCapturing, value))
            {
                RaisePropertyChanged(nameof(ToggleCaptureText));
            }
        }
    }

    public string ToggleCaptureText => IsCapturing ? "Pause Capture" : "Resume Capture";

    public string EnabledListenerCountText => _enabledListenerCount == 1
        ? "1 listener enabled"
        : $"{_enabledListenerCount} listeners enabled";

    public string EventCountText => _entries.Count == 1 ? "1 raised event" : $"{_entries.Count} raised events";

    public string SelectedListenerTitle => _selectedListener?.Name ?? "Event listeners";

    public string SelectedListenerSummary => _selectedListener?.Description ?? "Enable or disable listeners to capture routed and control events.";

    public string SelectedEventTitle => SelectedEntry?.EventText ?? "No raised event selected";

    public string SelectedEventSummary => SelectedEntry?.Summary ?? "Select a raised event to inspect its observed route through the Uno visual tree.";

    public string SelectedEventSourceSelector => SelectedEntry?.SourceSelector ?? string.Empty;

    public EventListenerNode? SelectedListener
    {
        get => _selectedListener;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedListener, value))
            {
                RaisePropertyChanged(nameof(SelectedListenerTitle));
                RaisePropertyChanged(nameof(SelectedListenerSummary));
            }
        }
    }

    public EventLogEntry? SelectedEntry
    {
        get => _selectedEntry;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedEntry, value))
            {
                RaisePropertyChanged(nameof(SelectedEventTitle));
                RaisePropertyChanged(nameof(SelectedEventSummary));
                RaisePropertyChanged(nameof(SelectedEventSourceSelector));
                InspectSelectedSourceCommand.RaiseCanExecuteChanged();
                UpdateRouteSource();
            }
        }
    }

    public EventRouteEntryViewModel? SelectedRouteEntry
    {
        get => _selectedRouteEntry;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedRouteEntry, value))
            {
                InspectSelectedRouteElementCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Refresh()
    {
        RebuildSubscriptions();
    }

    public void Dispose()
    {
        _root.LayoutUpdated -= OnRootLayoutUpdated;
        DisposeSubscriptions();
    }

    public void UpdateHoveredSource(EventLogEntry? entry, object owner)
    {
        if (entry?.SourceElement is { } sourceElement)
        {
            _mainView.UpdateEventHover(owner, sourceElement, "Event Source");
        }
        else
        {
            _mainView.ClearEventHover(owner);
        }
    }

    public void UpdateHoveredRouteElement(EventRouteEntryViewModel? entry, object owner)
    {
        if (entry?.Element is { } routeElement)
        {
            _mainView.UpdateEventHover(owner, routeElement, "Event Route");
        }
        else
        {
            _mainView.ClearEventHover(owner);
        }
    }

    public void ClearHoveredOverlay(object owner)
        => _mainView.ClearEventHover(owner);

    private void ToggleCapture()
    {
        IsCapturing = !IsCapturing;
        if (IsCapturing)
        {
            RebuildSubscriptions();
        }
        else
        {
            DisposeSubscriptions();
        }
    }

    private void EnableDefaults()
    {
        foreach (var leaf in _listenerLeaves)
        {
            leaf.SetCheckedFromOwner(leaf.Definition!.IsDefaultEnabled);
        }

        RefreshListenerGroups();
        OnListenerConfigurationChanged();
    }

    private void EnableAll()
    {
        foreach (var leaf in _listenerLeaves)
        {
            leaf.SetCheckedFromOwner(true);
        }

        RefreshListenerGroups();
        OnListenerConfigurationChanged();
    }

    private void DisableAll()
    {
        foreach (var leaf in _listenerLeaves)
        {
            leaf.SetCheckedFromOwner(false);
        }

        RefreshListenerGroups();
        OnListenerConfigurationChanged();
    }

    private void Clear()
    {
        _entries.Clear();
        RefreshLogSource();
        SelectedEntry = null;
        SelectedRouteEntry = null;
        RaisePropertyChanged(nameof(EventCountText));
    }

    private void OnRootLayoutUpdated(object? sender, object e)
    {
        if (IsCapturing)
        {
            WireActiveDefinitions();
        }
    }

    private void InspectSelectedSource()
    {
        if (SelectedEntry?.SourceElement is { } source)
        {
            _mainView.SelectElement(source, preferVisualTree: true);
        }
    }

    private void InspectSelectedRouteElement()
    {
        if (SelectedRouteEntry?.Element is { } element)
        {
            _mainView.SelectElement(element, preferVisualTree: true);
        }
    }

    private void BuildListenerTree()
    {
        var groups = new Dictionary<string, EventListenerNode>(StringComparer.Ordinal);

        foreach (var definition in CreateDefinitions())
        {
            definition.IsEnabled = definition.IsDefaultEnabled;

            if (!groups.TryGetValue(definition.GroupName, out var groupNode))
            {
                groupNode = new EventListenerNode(definition.GroupName, null, null, OnListenerConfigurationChanged);
                groups.Add(definition.GroupName, groupNode);
                _listenerRoots.Add(groupNode);
            }

            var listenerNode = new EventListenerNode(definition.DisplayName, definition, groupNode, OnListenerConfigurationChanged);
            groupNode.AddChild(listenerNode);
            _listenerLeaves.Add(listenerNode);
        }

        RefreshListenerGroups();
    }

    private void RefreshListenerGroups()
    {
        foreach (var root in _listenerRoots)
        {
            root.RefreshFromChildrenRecursive();
        }
    }

    private void OnListenerConfigurationChanged()
    {
        UpdateEnabledListenerCount();
        if (IsCapturing)
        {
            RebuildSubscriptions();
        }
    }

    private void UpdateEnabledListenerCount()
    {
        RaiseAndSetIfChanged(ref _enabledListenerCount, _listenerLeaves.Count(x => x.Definition!.IsEnabled), nameof(EnabledListenerCountText));
        RaisePropertyChanged(nameof(EnabledListenerCountText));
    }

    private void RebuildSubscriptions()
    {
        DisposeSubscriptions();

        if (!IsCapturing)
        {
            return;
        }

        foreach (var definition in _listenerLeaves.Select(x => x.Definition!).Where(x => x.IsEnabled))
        {
            _activeStates.Add(definition.Id, new ActiveListenerState(definition));
            _pendingCaptures[definition.Id] = new Dictionary<RoutedEventArgs, PendingEventCapture>(ReferenceEqualityComparer.Instance);
        }

        WireActiveDefinitions();
    }

    private void DisposeSubscriptions()
    {
        foreach (var state in _activeStates.Values)
        {
            foreach (var subscription in state.Subscriptions)
            {
                subscription.Dispose();
            }
        }

        _activeStates.Clear();
        _pendingCaptures.Clear();
    }

    private void WireActiveDefinitions()
    {
        if (_activeStates.Count == 0)
        {
            return;
        }

        foreach (var element in EnumerateInspectableElements())
        {
            foreach (var state in _activeStates.Values)
            {
                if (!state.KnownElements.Add(element))
                {
                    continue;
                }

                var definition = state.Definition;
                var subscription = definition.TryAttach(element, (senderElement, args) => OnEventRaised(definition, senderElement, args));
                if (subscription is not null)
                {
                    state.Subscriptions.Add(subscription);
                }
            }
        }
    }

    private IEnumerable<UIElement> EnumerateInspectableElements()
    {
        var pending = new Stack<DependencyObject>();
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);

        pending.Push(_root);
        foreach (var popupChild in TreeInspector.GetOpenPopupChildren(_root.XamlRoot))
        {
            pending.Push(popupChild);
        }

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!seen.Add(current))
            {
                continue;
            }

            if (current is UIElement element)
            {
                yield return element;
            }

            var childCount = VisualTreeHelper.GetChildrenCount(current);
            for (var index = childCount - 1; index >= 0; index--)
            {
                if (VisualTreeHelper.GetChild(current, index) is DependencyObject child)
                {
                    pending.Push(child);
                }
            }
        }
    }

    private void OnEventRaised(EventListenerDefinition definition, UIElement senderElement, RoutedEventArgs args)
    {
        if (!IsCapturing)
        {
            return;
        }

        if (!_pendingCaptures.TryGetValue(definition.Id, out var captures))
        {
            return;
        }

        if (!captures.TryGetValue(args, out var pending))
        {
            pending = new PendingEventCapture(definition, args, definition.FormatDetail(args), DateTime.Now);
            captures.Add(args, pending);
            _root.DispatcherQueue.TryEnqueue(() => FinalizePendingCapture(definition.Id, args));
        }

        pending.AddRouteEntry(senderElement, TryGetHandled(args));
    }

    private void FinalizePendingCapture(string definitionId, RoutedEventArgs args)
    {
        if (!_pendingCaptures.TryGetValue(definitionId, out var captures) || !captures.Remove(args, out var pending))
        {
            return;
        }

        if (pending.RouteEntries.Count == 0)
        {
            return;
        }

        var sourceElement = pending.ResolveSourceElement();
        var sourceDisplayName = FormatElementDisplayName(sourceElement ?? pending.RouteEntries[0].Element);
        var sourceSelector = BuildSelector(sourceElement ?? pending.RouteEntries[0].Element);
        var handled = TryGetHandled(args);
        var handledText = handled ? "Handled" : "Open";
        var routeEntries = BuildRouteEntries(pending.RouteEntries);
        var routeCountText = routeEntries.Count == 1 ? "1 hop" : $"{routeEntries.Count} hops";
        var eventText = string.IsNullOrWhiteSpace(pending.DetailText)
            ? pending.Definition.DisplayName
            : $"{pending.Definition.DisplayName} ({pending.DetailText})";
        var summary = $"{sourceDisplayName} • {pending.Definition.Strategy} • {routeCountText} • {handledText}";

        _entries.Insert(0, EventLogEntry.Create(
            ++_nextSequenceId,
            pending.Timestamp,
            pending.Definition.DisplayName,
            eventText,
            pending.Definition.Strategy,
            sourceElement,
            sourceDisplayName,
            sourceSelector,
            handledText,
            routeCountText,
            summary,
            routeEntries));

        while (_entries.Count > 500)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }

        RefreshLogSource();
        RaisePropertyChanged(nameof(EventCountText));
    }

    private void RefreshLogSource()
    {
        var selectedSequenceId = SelectedEntry?.SequenceId;
        var items = _entries.ToArray();
        LogSource.Items = items;

        if (items.Length == 0)
        {
            SelectedEntry = null;
            return;
        }

        if (selectedSequenceId is not null)
        {
            for (var index = 0; index < items.Length; index++)
            {
                if (items[index].SequenceId == selectedSequenceId.Value)
                {
                    LogSelection.SelectedIndex = new IndexPath(index);
                    SelectedEntry = items[index];
                    return;
                }
            }
        }

        if (SelectedEntry is null)
        {
            LogSelection.SelectedIndex = new IndexPath(0);
            SelectedEntry = items[0];
        }
    }

    private void UpdateRouteSource()
    {
        var routeEntries = SelectedEntry?.RouteEntries.ToArray() ?? Array.Empty<EventRouteEntryViewModel>();
        RouteSource.Items = routeEntries;

        if (routeEntries.Length == 0)
        {
            SelectedRouteEntry = null;
            return;
        }

        RouteSelection.SelectedIndex = new IndexPath(0);
        SelectedRouteEntry = routeEntries[0];
    }

    private static IReadOnlyList<EventRouteEntryViewModel> BuildRouteEntries(IReadOnlyList<PendingRouteEntry> routeEntries)
    {
        var result = new List<EventRouteEntryViewModel>(routeEntries.Count);

        for (var index = 0; index < routeEntries.Count; index++)
        {
            var routeEntry = routeEntries[index];
            var flags = new List<string>();

            if (routeEntry.IsOriginalSource)
            {
                flags.Add("Source");
            }

            if (routeEntry.HandledHere)
            {
                flags.Add("Handled here");
            }
            else if (routeEntry.HandledObserved)
            {
                flags.Add("Handled");
            }

            result.Add(EventRouteEntryViewModel.Create(
                index + 1,
                routeEntry.Element,
                routeEntry.IsOriginalSource ? "Source" : $"Step {index + 1}",
                FormatElementDisplayName(routeEntry.Element),
                BuildSelector(routeEntry.Element),
                flags.Count > 0 ? string.Join(" • ", flags) : "Route"));
        }

        return result;
    }

    private static bool TryGetHandled(RoutedEventArgs args)
    {
        return args switch
        {
            PointerRoutedEventArgs pointerArgs => pointerArgs.Handled,
            KeyRoutedEventArgs keyArgs => keyArgs.Handled,
            TappedRoutedEventArgs tappedArgs => tappedArgs.Handled,
            DoubleTappedRoutedEventArgs doubleTappedArgs => doubleTappedArgs.Handled,
            RightTappedRoutedEventArgs rightTappedArgs => rightTappedArgs.Handled,
            HoldingRoutedEventArgs holdingArgs => holdingArgs.Handled,
            GettingFocusEventArgs gettingFocusArgs => gettingFocusArgs.Handled,
            LosingFocusEventArgs losingFocusArgs => losingFocusArgs.Handled,
            _ => TryGetHandledFromReflection(args),
        };
    }

    private static bool TryGetHandledFromReflection(RoutedEventArgs args)
    {
        if (args.GetType().GetProperty("Handled")?.GetValue(args) is bool handled)
        {
            return handled;
        }

        return false;
    }

    private static string FormatElementDisplayName(DependencyObject? element)
    {
        return element switch
        {
            FrameworkElement fe when !string.IsNullOrWhiteSpace(fe.Name) => $"{element.GetType().Name}  #{fe.Name}",
            DependencyObject d => d.GetType().Name,
            _ => "(unknown)",
        };
    }

    private static string BuildSelector(DependencyObject? element)
        => element is not null ? InspectableNode.BuildSelector(element) : "(unknown)";

    private static IEnumerable<EventListenerDefinition> CreateDefinitions()
    {
        yield return CreateRoutedEvent(
            "pointer.pointerpressed",
            "Pointer",
            "PointerPressed",
            "Bubble",
            isDefaultEnabled: true,
            "Raised when a pointer button is pressed.",
            "Bubble routed • default",
            UIElement.PointerPressedEvent,
            capture => (PointerEventHandler)((sender, args) => capture((sender as UIElement) ?? throw new InvalidOperationException("Pointer sender was not a UIElement."), args)));

        yield return CreateRoutedEvent(
            "pointer.pointerreleased",
            "Pointer",
            "PointerReleased",
            "Bubble",
            isDefaultEnabled: true,
            "Raised when a pointer button is released.",
            "Bubble routed • default",
            UIElement.PointerReleasedEvent,
            capture => (PointerEventHandler)((sender, args) => capture((sender as UIElement) ?? throw new InvalidOperationException("Pointer sender was not a UIElement."), args)));

        yield return CreateRoutedEvent(
            "pointer.pointerentered",
            "Pointer",
            "PointerEntered",
            "Bubble",
            isDefaultEnabled: false,
            "Raised when a pointer enters an element.",
            "Bubble routed",
            UIElement.PointerEnteredEvent,
            capture => (PointerEventHandler)((sender, args) => capture((sender as UIElement) ?? throw new InvalidOperationException("Pointer sender was not a UIElement."), args)));

        yield return CreateRoutedEvent(
            "pointer.pointerexited",
            "Pointer",
            "PointerExited",
            "Bubble",
            isDefaultEnabled: false,
            "Raised when a pointer leaves an element.",
            "Bubble routed",
            UIElement.PointerExitedEvent,
            capture => (PointerEventHandler)((sender, args) => capture((sender as UIElement) ?? throw new InvalidOperationException("Pointer sender was not a UIElement."), args)));

        yield return CreateRoutedEvent(
            "keyboard.keydown",
            "Keyboard",
            "KeyDown",
            "Bubble",
            isDefaultEnabled: true,
            "Raised when a key is pressed while an element has focus.",
            "Bubble routed • default",
            UIElement.KeyDownEvent,
            capture => (KeyEventHandler)((sender, args) => capture((sender as UIElement) ?? throw new InvalidOperationException("Key sender was not a UIElement."), args)),
            args => args is KeyRoutedEventArgs keyArgs ? keyArgs.Key.ToString() : null);

        yield return CreateRoutedEvent(
            "keyboard.keyup",
            "Keyboard",
            "KeyUp",
            "Bubble",
            isDefaultEnabled: true,
            "Raised when a key is released while an element has focus.",
            "Bubble routed • default",
            UIElement.KeyUpEvent,
            capture => (KeyEventHandler)((sender, args) => capture((sender as UIElement) ?? throw new InvalidOperationException("Key sender was not a UIElement."), args)),
            args => args is KeyRoutedEventArgs keyArgs ? keyArgs.Key.ToString() : null);

        yield return CreateRoutedEvent(
            "gesture.tapped",
            "Gesture",
            "Tapped",
            "Bubble",
            isDefaultEnabled: false,
            "Raised when an element receives a tap gesture.",
            "Bubble routed",
            UIElement.TappedEvent,
            capture => (TappedEventHandler)((sender, args) => capture((sender as UIElement) ?? throw new InvalidOperationException("Tapped sender was not a UIElement."), args)));

        yield return CreateRoutedEvent(
            "gesture.doubletapped",
            "Gesture",
            "DoubleTapped",
            "Bubble",
            isDefaultEnabled: false,
            "Raised when an element receives a double-tap gesture.",
            "Bubble routed",
            UIElement.DoubleTappedEvent,
            capture => (DoubleTappedEventHandler)((sender, args) => capture((sender as UIElement) ?? throw new InvalidOperationException("DoubleTapped sender was not a UIElement."), args)));

        yield return CreateRoutedEvent(
            "gesture.righttapped",
            "Gesture",
            "RightTapped",
            "Bubble",
            isDefaultEnabled: false,
            "Raised when an element receives a right-tap gesture.",
            "Bubble routed",
            UIElement.RightTappedEvent,
            capture => (RightTappedEventHandler)((sender, args) => capture((sender as UIElement) ?? throw new InvalidOperationException("RightTapped sender was not a UIElement."), args)));

        yield return CreateRoutedEvent(
            "gesture.holding",
            "Gesture",
            "Holding",
            "Bubble",
            isDefaultEnabled: false,
            "Raised when a holding gesture is recognized.",
            "Bubble routed",
            UIElement.HoldingEvent,
            capture => (HoldingEventHandler)((sender, args) => capture((sender as UIElement) ?? throw new InvalidOperationException("Holding sender was not a UIElement."), args)),
            args => args is HoldingRoutedEventArgs holdingArgs ? holdingArgs.HoldingState.ToString() : null);

        yield return CreateDirectEvent<UIElement, RoutedEventHandler>(
            "focus.gotfocus",
            "Focus",
            "GotFocus",
            "Bubble",
            isDefaultEnabled: false,
            "Raised when an element receives focus.",
            "Bubble routed",
            capture => (sender, args) => capture((UIElement)sender, args),
            (element, handler) => element.GotFocus += handler,
            (element, handler) => element.GotFocus -= handler);

        yield return CreateDirectEvent<UIElement, RoutedEventHandler>(
            "focus.lostfocus",
            "Focus",
            "LostFocus",
            "Bubble",
            isDefaultEnabled: false,
            "Raised when an element loses focus.",
            "Bubble routed",
            capture => (sender, args) => capture((UIElement)sender, args),
            (element, handler) => element.LostFocus += handler,
            (element, handler) => element.LostFocus -= handler);

        yield return CreateDirectEvent<ButtonBase, RoutedEventHandler>(
            "controls.button.click",
            "Controls",
            "Button.Click",
            "Direct",
            isDefaultEnabled: true,
            "Raised when a button invokes its click action.",
            "Direct control event • default",
            capture => (sender, args) => capture((ButtonBase)sender, args),
            (element, handler) => element.Click += handler,
            (element, handler) => element.Click -= handler);

        yield return CreateDirectEvent<ToggleButton, RoutedEventHandler>(
            "controls.toggle.checked",
            "Controls",
            "ToggleButton.Checked",
            "Direct",
            isDefaultEnabled: false,
            "Raised when a toggle button becomes checked.",
            "Direct control event",
            capture => (sender, args) => capture((ToggleButton)sender, args),
            (element, handler) => element.Checked += handler,
            (element, handler) => element.Checked -= handler);

        yield return CreateDirectEvent<ToggleButton, RoutedEventHandler>(
            "controls.toggle.unchecked",
            "Controls",
            "ToggleButton.Unchecked",
            "Direct",
            isDefaultEnabled: false,
            "Raised when a toggle button becomes unchecked.",
            "Direct control event",
            capture => (sender, args) => capture((ToggleButton)sender, args),
            (element, handler) => element.Unchecked += handler,
            (element, handler) => element.Unchecked -= handler);

        yield return CreateDirectEvent<ToggleButton, RoutedEventHandler>(
            "controls.toggle.indeterminate",
            "Controls",
            "ToggleButton.Indeterminate",
            "Direct",
            isDefaultEnabled: false,
            "Raised when a toggle button becomes indeterminate.",
            "Direct control event",
            capture => (sender, args) => capture((ToggleButton)sender, args),
            (element, handler) => element.Indeterminate += handler,
            (element, handler) => element.Indeterminate -= handler);

        yield return CreateDirectEvent<Selector, SelectionChangedEventHandler>(
            "controls.selector.selectionchanged",
            "Controls",
            "Selector.SelectionChanged",
            "Direct",
            isDefaultEnabled: false,
            "Raised when selector selection changes.",
            "Direct control event",
            capture => (sender, args) => capture((Selector)sender, args),
            (element, handler) => element.SelectionChanged += handler,
            (element, handler) => element.SelectionChanged -= handler,
            args => args is SelectionChangedEventArgs selectionArgs ? $"+{selectionArgs.AddedItems.Count} / -{selectionArgs.RemovedItems.Count}" : null);

        yield return CreateDirectEvent<TextBox, TextChangedEventHandler>(
            "controls.textbox.textchanged",
            "Controls",
            "TextBox.TextChanged",
            "Direct",
            isDefaultEnabled: true,
            "Raised when text changes in a text box. This is the Uno replacement for Avalonia's default TextInput listener lane.",
            "Direct control event • default",
            capture => (sender, args) => capture((TextBox)sender, args),
            (element, handler) => element.TextChanged += handler,
            (element, handler) => element.TextChanged -= handler);

        yield return CreateDirectEvent<PasswordBox, RoutedEventHandler>(
            "controls.passwordbox.passwordchanged",
            "Controls",
            "PasswordBox.PasswordChanged",
            "Direct",
            isDefaultEnabled: false,
            "Raised when the password box value changes.",
            "Direct control event",
            capture => (sender, args) => capture((PasswordBox)sender, args),
            (element, handler) => element.PasswordChanged += handler,
            (element, handler) => element.PasswordChanged -= handler);

        yield return CreateDirectEvent<RangeBase, RangeBaseValueChangedEventHandler>(
            "controls.rangebase.valuechanged",
            "Controls",
            "RangeBase.ValueChanged",
            "Direct",
            isDefaultEnabled: false,
            "Raised when a range-based control changes value.",
            "Direct control event",
            capture => (sender, args) => capture((RangeBase)sender, args),
            (element, handler) => element.ValueChanged += handler,
            (element, handler) => element.ValueChanged -= handler,
            args => args is RangeBaseValueChangedEventArgs rangeArgs ? $"{rangeArgs.OldValue:0.###} -> {rangeArgs.NewValue:0.###}" : null);
    }

    private static EventListenerDefinition CreateRoutedEvent(
        string id,
        string groupName,
        string displayName,
        string strategy,
        bool isDefaultEnabled,
        string description,
        string summaryText,
        RoutedEvent routedEvent,
        Func<Action<UIElement, RoutedEventArgs>, object> createHandler,
        Func<RoutedEventArgs, string?>? detailFormatter = null)
        => new()
        {
            Id = id,
            GroupName = groupName,
            DisplayName = displayName,
            Strategy = strategy,
            Description = description,
            SummaryText = summaryText,
            IsDefaultEnabled = isDefaultEnabled,
            TryAttach = (element, capture) =>
            {
                var handler = createHandler(capture);
                element.AddHandler(routedEvent, handler, handledEventsToo: true);
                return new DisposableAction(() => element.RemoveHandler(routedEvent, handler));
            },
            FormatDetail = detailFormatter ?? (_ => null),
        };

    private static EventListenerDefinition CreateDirectEvent<TElement, THandler>(
        string id,
        string groupName,
        string displayName,
        string strategy,
        bool isDefaultEnabled,
        string description,
        string summaryText,
        Func<Action<UIElement, RoutedEventArgs>, THandler> createHandler,
        Action<TElement, THandler> subscribe,
        Action<TElement, THandler> unsubscribe,
        Func<RoutedEventArgs, string?>? detailFormatter = null)
        where TElement : UIElement
        where THandler : Delegate
        => new()
        {
            Id = id,
            GroupName = groupName,
            DisplayName = displayName,
            Strategy = strategy,
            Description = description,
            SummaryText = summaryText,
            IsDefaultEnabled = isDefaultEnabled,
            TryAttach = (element, capture) =>
            {
                if (element is not TElement target)
                {
                    return null;
                }

                var handler = createHandler(capture);
                subscribe(target, handler);
                return new DisposableAction(() => unsubscribe(target, handler));
            },
            FormatDetail = detailFormatter ?? (_ => null),
        };

    private sealed class ActiveListenerState
    {
        public ActiveListenerState(EventListenerDefinition definition)
        {
            Definition = definition;
        }

        public EventListenerDefinition Definition { get; }

        public HashSet<UIElement> KnownElements { get; } = new(ReferenceEqualityComparer.Instance);

        public List<IDisposable> Subscriptions { get; } = [];
    }

    private sealed class PendingEventCapture
    {
        private readonly HashSet<DependencyObject> _seenRouteElements = new(ReferenceEqualityComparer.Instance);
        private bool _handledObserved;

        public PendingEventCapture(EventListenerDefinition definition, RoutedEventArgs args, string? detailText, DateTime timestamp)
        {
            Definition = definition;
            Args = args;
            DetailText = detailText;
            Timestamp = timestamp;
        }

        public EventListenerDefinition Definition { get; }

        public RoutedEventArgs Args { get; }

        public string? DetailText { get; }

        public DateTime Timestamp { get; }

        public List<PendingRouteEntry> RouteEntries { get; } = [];

        public void AddRouteEntry(DependencyObject element, bool handledObserved)
        {
            if (!_seenRouteElements.Add(element))
            {
                return;
            }

            var isOriginalSource = ReferenceEquals(Args.OriginalSource, element);
            var handledHere = handledObserved && !_handledObserved;

            RouteEntries.Add(new PendingRouteEntry
            {
                Element = element,
                IsOriginalSource = isOriginalSource,
                HandledObserved = handledObserved,
                HandledHere = handledHere,
            });

            _handledObserved |= handledObserved;
        }

        public DependencyObject? ResolveSourceElement()
        {
            if (Args.OriginalSource is DependencyObject originalSource)
            {
                return originalSource;
            }

            var originalRouteEntry = RouteEntries.FirstOrDefault(x => x.IsOriginalSource);
            if (originalRouteEntry is not null)
            {
                return originalRouteEntry.Element;
            }

            return RouteEntries.FirstOrDefault()?.Element;
        }
    }

    private sealed class PendingRouteEntry
    {
        public required DependencyObject Element { get; init; }

        public required bool IsOriginalSource { get; init; }

        public required bool HandledObserved { get; init; }

        public required bool HandledHere { get; init; }
    }
}
