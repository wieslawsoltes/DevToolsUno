using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using IndexPath = Avalonia.Controls.IndexPath;
using AGridLength = Avalonia.GridLength;
using AGridUnitType = Avalonia.GridUnitType;
using DevTools.Uno.Diagnostics.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DevTools.Uno.Diagnostics.ViewModels;

internal sealed class TreePageViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan AutoRefreshDelay = TimeSpan.FromMilliseconds(250);

    private readonly FrameworkElement _root;
    private readonly MainViewModel _mainView;
    private readonly bool _isVisualTree;
    private readonly DispatcherTimer _autoRefreshTimer;
    private InspectableNode? _hoveredNode;
    private InspectableNode? _selectedNode;
    private ControlDetailsViewModel? _details;
    private bool _suppressMainViewNotification;
    private int _lastTreeSignature;

    public TreePageViewModel(MainViewModel mainView, FrameworkElement root, bool isVisualTree, ISet<string> pinnedProperties)
    {
        _mainView = mainView;
        _root = root;
        _isVisualTree = isVisualTree;
        PinnedProperties = pinnedProperties;
        _autoRefreshTimer = new DispatcherTimer
        {
            Interval = AutoRefreshDelay,
        };
        _autoRefreshTimer.Tick += OnAutoRefreshTick;

        SelectRootCommand = new RelayCommand(Refresh);
        CopySelectorCommand = new RelayCommand(async () =>
        {
            if (SelectedNode is not null)
            {
                await PropertyInspector.CopyTextAsync(SelectedNode.Selector);
            }
        }, () => SelectedNode is not null);
        ExpandRecursivelyCommand = new RelayCommand(ExpandRecursively, () => SelectedNode is not null);
        CollapseChildrenCommand = new RelayCommand(CollapseChildren, () => SelectedNode is not null);
        BringIntoViewCommand = new RelayCommand(BringIntoView, () => SelectedNode?.Element is FrameworkElement);
        FocusCommand = new RelayCommand(FocusSelected, () => SelectedNode?.Element is Control);
        ScreenshotCommand = new RelayCommand(async () => await _mainView.CaptureSelectionAsync(), () => SelectedNode?.Element is FrameworkElement);

        var source = new HierarchicalTreeDataGridSource<InspectableNode>(Array.Empty<InspectableNode>());
        source.Columns.Add(
            new HierarchicalExpanderColumn<InspectableNode>(
                new TemplateColumn<InspectableNode>("Element", "TreeElementCellTemplate", width: new AGridLength(1, AGridUnitType.Star)),
                x => x.Children,
                x => x.Children.Count > 0,
                x => x.IsExpanded));

        TreeSource = source;
        Selection = new TreeDataGridRowSelectionModel<InspectableNode>(TreeSource)
        {
            SingleSelect = true,
        };
        Selection.SelectionChanged += (_, _) => SelectedNode = Selection.SelectedItem;
        TreeSource.Selection = Selection;

        Refresh();
    }

    public ISet<string> PinnedProperties { get; }

    public bool IsVisualTree => _isVisualTree;

    public string PageTitle => _isVisualTree ? "Visual Tree" : "Logical Tree";

    public string PageSummary => _isVisualTree
        ? "Inspect the rendered object hierarchy and keep the shared property inspector synchronized with hover and selection."
        : "Inspect the logical object hierarchy and keep the shared property inspector synchronized with hover and selection.";

    public HierarchicalTreeDataGridSource<InspectableNode> TreeSource { get; }

    public TreeDataGridRowSelectionModel<InspectableNode> Selection { get; }

    public RelayCommand SelectRootCommand { get; }

    public RelayCommand CopySelectorCommand { get; }

    public RelayCommand ExpandRecursivelyCommand { get; }

    public RelayCommand CollapseChildrenCommand { get; }

    public RelayCommand BringIntoViewCommand { get; }

    public RelayCommand FocusCommand { get; }

    public RelayCommand ScreenshotCommand { get; }

    public ControlDetailsViewModel? Details
    {
        get => _details;
        private set => RaiseAndSetIfChanged(ref _details, value);
    }

    public InspectableNode? SelectedNode
    {
        get => _selectedNode;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedNode, value))
            {
                Details = value is not null ? new ControlDetailsViewModel(value.Element, PinnedProperties, _mainView.ShowClrProperties) : null;
                if (!_suppressMainViewNotification)
                {
                    _mainView.OnTreeSelectionChanged(this, value?.Element);
                }

                CopySelectorCommand.RaiseCanExecuteChanged();
                ExpandRecursivelyCommand.RaiseCanExecuteChanged();
                CollapseChildrenCommand.RaiseCanExecuteChanged();
                BringIntoViewCommand.RaiseCanExecuteChanged();
                FocusCommand.RaiseCanExecuteChanged();
                ScreenshotCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Refresh()
    {
        var state = CaptureRefreshState();
        TreeSource.Items = (_isVisualTree ? TreeInspector.BuildVisualTree(_root) : TreeInspector.BuildLogicalTree(_root)).ToArray();
        if (state.HasExpansionState)
        {
            RestoreExpandedState(state.ExpandedElements);
        }
        RestoreHoveredState(state.HoveredElement);
        RestoreSelectionState(state.SelectedLineage);
        _lastTreeSignature = GetTreeSignature();
    }

    public void RequestAutoRefresh()
    {
        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Start();
    }

    public void UpdateIncludeClrProperties(bool includeClrProperties)
    {
        Details?.UpdateIncludeClrProperties(includeClrProperties);
    }

    public void UpdateHoveredNode(InspectableNode? node)
    {
        if (ReferenceEquals(_hoveredNode, node))
        {
            return;
        }

        _hoveredNode = node;
        _mainView.UpdateTreeHover(this, node?.Element);
    }

    public void ClearHoveredNode()
    {
        if (_hoveredNode is null)
        {
            return;
        }

        _hoveredNode = null;
        _mainView.ClearTreeHover();
    }

    public bool ContainsElement(DependencyObject element)
        => TryFind(TreeSource.Items, element, out _, out _);

    public bool SelectElement(DependencyObject? element, bool activateTab = true, bool notifyMainView = true, bool refreshIfMissing = false)
    {
        if (TrySelectElementCore(element, activateTab, notifyMainView))
        {
            return true;
        }

        if (!refreshIfMissing || element is null)
        {
            return false;
        }

        Refresh();
        return TrySelectElementCore(element, activateTab, notifyMainView);
    }

    public void Dispose()
    {
        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Tick -= OnAutoRefreshTick;
        ClearHoveredNode();
    }

    private bool TrySelectElementCore(DependencyObject? element, bool activateTab, bool notifyMainView)
    {
        if (element is null)
        {
            return false;
        }

        if (TryFind(TreeSource.Items, element, out var path, out var node))
        {
            ExpandAncestors(node);
            _suppressMainViewNotification = !notifyMainView;
            try
            {
                Selection.SelectedIndex = path;
                SelectedNode = node;
            }
            finally
            {
                _suppressMainViewNotification = false;
            }

            if (activateTab)
            {
                _mainView.SelectedTab = _isVisualTree ? 1 : 0;
            }

            return true;
        }

        return false;
    }

    private TreeRefreshState CaptureRefreshState()
        => new(
            TreeSource.Items.Any(),
            HierarchyExpansionState.CaptureExpandedKeys<InspectableNode, DependencyObject>(
                TreeSource.Items,
                x => x.Children,
                x => x.Element,
                x => x.IsExpanded,
                (IEqualityComparer<DependencyObject>)System.Collections.Generic.ReferenceEqualityComparer.Instance),
            CaptureSelectionLineage(),
            _hoveredNode?.Element);

    private List<DependencyObject> CaptureSelectionLineage()
    {
        var lineage = new List<DependencyObject>();
        for (var current = SelectedNode; current is not null; current = current.Parent)
        {
            lineage.Add(current.Element);
        }

        return lineage;
    }

    private void RestoreExpandedState(ISet<DependencyObject> expandedElements)
        => HierarchyExpansionState.RestoreExpandedKeys(
            TreeSource.Items,
            x => x.Children,
            x => x.Element,
            (node, isExpanded) => node.IsExpanded = isExpanded,
            expandedElements);

    private void RestoreHoveredState(DependencyObject? hoveredElement)
    {
        if (hoveredElement is not null &&
            TryFind(TreeSource.Items, hoveredElement, out _, out var hoveredNode))
        {
            UpdateHoveredNode(hoveredNode);
            return;
        }

        ClearHoveredNode();
    }

    private void RestoreSelectionState(IReadOnlyList<DependencyObject> selectedLineage)
    {
        if (selectedLineage.Count == 0)
        {
            return;
        }

        if (TrySelectElementCore(selectedLineage[0], activateTab: false, notifyMainView: false))
        {
            return;
        }

        for (var index = 1; index < selectedLineage.Count; index++)
        {
            if (TrySelectElementCore(selectedLineage[index], activateTab: false, notifyMainView: true))
            {
                return;
            }
        }

        Selection.SelectedIndex = default;
        SelectedNode = null;
        _mainView.OnTreeSelectionChanged(this, null);
    }

    private void OnAutoRefreshTick(object? sender, object e)
    {
        _autoRefreshTimer.Stop();

        var signature = GetTreeSignature();
        if (signature != _lastTreeSignature)
        {
            Refresh();
        }
    }

    private int GetTreeSignature()
        => _isVisualTree
            ? TreeInspector.GetVisualTreeSignature(_root)
            : TreeInspector.GetLogicalTreeSignature(_root);

    private void ExpandRecursively()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var stack = new Stack<InspectableNode>();
        stack.Push(SelectedNode);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            current.IsExpanded = true;
            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }
    }

    private void CollapseChildren()
    {
        if (SelectedNode is null)
        {
            return;
        }

        var stack = new Stack<InspectableNode>();
        stack.Push(SelectedNode);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            current.IsExpanded = false;
            foreach (var child in current.Children)
            {
                stack.Push(child);
            }
        }
    }

    private void BringIntoView()
    {
        if (SelectedNode?.Element is FrameworkElement element)
        {
            element.StartBringIntoView();
        }
    }

    private void FocusSelected()
    {
        if (SelectedNode?.Element is Control control)
        {
            control.Focus(FocusState.Programmatic);
        }
    }

    private static void ExpandAncestors(InspectableNode? node)
    {
        while (node is not null)
        {
            node.IsExpanded = true;
            node = node.Parent;
        }
    }

    private static bool TryFind(IEnumerable<InspectableNode> roots, DependencyObject target, out IndexPath path, out InspectableNode? node)
    {
        var index = 0;
        foreach (var root in roots)
        {
            if (TryFind(root, target, new IndexPath(index), out path, out node))
            {
                return true;
            }

            index++;
        }

        path = default;
        node = null;
        return false;
    }

    private static bool TryFind(InspectableNode current, DependencyObject target, IndexPath currentPath, out IndexPath path, out InspectableNode? node)
    {
        if (ReferenceEquals(current.Element, target))
        {
            path = currentPath;
            node = current;
            return true;
        }

        for (var index = 0; index < current.Children.Count; index++)
        {
            if (TryFind(current.Children[index], target, currentPath.Append(index), out path, out node))
            {
                return true;
            }
        }

        path = default;
        node = null;
        return false;
    }

    private sealed record TreeRefreshState(
        bool HasExpansionState,
        HashSet<DependencyObject> ExpandedElements,
        List<DependencyObject> SelectedLineage,
        DependencyObject? HoveredElement);
}
