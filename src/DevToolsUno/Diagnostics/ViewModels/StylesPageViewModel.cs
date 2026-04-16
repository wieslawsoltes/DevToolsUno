using Avalonia;
using Uno.Controls;
using Uno.Controls.Models.TreeDataGrid;
using Uno.Controls.Selection;
using DevToolsUno.Diagnostics.Internal;
using Microsoft.UI.Xaml;
using IndexPath = Uno.Controls.IndexPath;
using AGridLength = Avalonia.GridLength;
using AGridUnitType = Avalonia.GridUnitType;

namespace DevToolsUno.Diagnostics.ViewModels;

internal sealed class StylesPageViewModel : ViewModelBase
{
    private readonly FrameworkElement _root;
    private readonly FilterViewModel _filter;
    private StyleInspectionSnapshot? _snapshot;
    private StyleScopeNode? _selectedScope;
    private StyleEntryViewModel? _selectedEntry;
    private StyleValueDetailsViewModel? _details;
    private DependencyObject? _inspectionTarget;
    private bool _sortDescending;
    private bool _includeClrProperties;
    private string _selectedSortOption = "Name";
    private string? _pendingEntryRestoreId;
    private int _entryCount;

    public StylesPageViewModel(FrameworkElement root, bool includeClrProperties)
    {
        _root = root;
        _includeClrProperties = includeClrProperties;
        _filter = new FilterViewModel();
        _filter.RefreshFilter += (_, _) => RefreshEntries();

        RefreshCommand = new RelayCommand(Refresh);
        ExpandAllCommand = new RelayCommand(() => SetExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetExpanded(false));
        CopySelectedNameCommand = new RelayCommand(async () =>
        {
            if (SelectedEntry is not null)
            {
                await PropertyInspector.CopyTextAsync(SelectedEntry.Name);
            }
        }, () => SelectedEntry is not null);
        CopySelectedValueCommand = new RelayCommand(async () =>
        {
            if (SelectedEntry is not null)
            {
                await PropertyInspector.CopyTextAsync(SelectedEntry.ValueText);
            }
        }, () => SelectedEntry is not null);

        var scopes = new HierarchicalTreeDataGridSource<StyleScopeNode>(Array.Empty<StyleScopeNode>());
        scopes.Columns.Add(
            new HierarchicalExpanderColumn<StyleScopeNode>(
                new TextColumn<StyleScopeNode, string>("Scope", x => x.Name, new AGridLength(2, AGridUnitType.Star)),
                x => x.Children,
                x => x.Children.Count > 0,
                x => x.IsExpanded));
        scopes.Columns.Add(new TextColumn<StyleScopeNode, string>("Kind", x => x.Kind, new AGridLength(1, AGridUnitType.Star)));
        scopes.Columns.Add(new TextColumn<StyleScopeNode, string>("Summary", x => x.Summary, new AGridLength(2, AGridUnitType.Star)));
        ScopeTreeSource = scopes;
        ScopeSelection = new TreeDataGridRowSelectionModel<StyleScopeNode>(ScopeTreeSource)
        {
            SingleSelect = true,
        };
        ScopeSelection.SelectionChanged += (_, _) => SelectedScope = ScopeSelection.SelectedItem;
        ScopeTreeSource.Selection = ScopeSelection;

        var entries = new FlatTreeDataGridSource<StyleEntryViewModel>(Array.Empty<StyleEntryViewModel>());
        entries.Columns.Add(new TextColumn<StyleEntryViewModel, string>("Name", x => x.Name, new AGridLength(2, AGridUnitType.Star)));
        entries.Columns.Add(new TextColumn<StyleEntryViewModel, string>("Value", x => x.ValueText, new AGridLength(2, AGridUnitType.Star)));
        entries.Columns.Add(new TextColumn<StyleEntryViewModel, string>("Type", x => x.TypeText, new AGridLength(1, AGridUnitType.Star)));
        entries.Columns.Add(new TextColumn<StyleEntryViewModel, string>("Origin", x => x.OriginText, new AGridLength(1.5, AGridUnitType.Star)));
        entries.Columns.Add(new TextColumn<StyleEntryViewModel, string>("Kind", x => x.Kind, new AGridLength(1, AGridUnitType.Star)));
        EntrySource = entries;
        EntrySelection = new TreeDataGridRowSelectionModel<StyleEntryViewModel>(EntrySource)
        {
            SingleSelect = true,
        };
        EntrySelection.SelectionChanged += (_, _) => SelectedEntry = EntrySelection.SelectedItem;
        EntrySource.Selection = EntrySelection;

        Refresh();
    }

    public HierarchicalTreeDataGridSource<StyleScopeNode> ScopeTreeSource { get; }

    public TreeDataGridRowSelectionModel<StyleScopeNode> ScopeSelection { get; }

    public FlatTreeDataGridSource<StyleEntryViewModel> EntrySource { get; }

    public TreeDataGridRowSelectionModel<StyleEntryViewModel> EntrySelection { get; }

    public FilterViewModel Filter => _filter;

    public IReadOnlyList<string> SortOptions { get; } = ["Name", "Origin", "Type"];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ExpandAllCommand { get; }

    public RelayCommand CollapseAllCommand { get; }

    public RelayCommand CopySelectedNameCommand { get; }

    public RelayCommand CopySelectedValueCommand { get; }

    public bool SortDescending
    {
        get => _sortDescending;
        set
        {
            if (RaiseAndSetIfChanged(ref _sortDescending, value))
            {
                RefreshEntries();
            }
        }
    }

    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (RaiseAndSetIfChanged(ref _selectedSortOption, value))
            {
                RefreshEntries();
            }
        }
    }

    public string InspectionTargetSummary => _snapshot?.TargetElement is { } element
        ? _snapshot.IsFallbackTarget
            ? $"Selection: (none, using host root {ResourceInspector.FormatElementName(element)})"
            : $"Selection: {ResourceInspector.FormatElementName(element)}"
        : "Selection: (none)";

    public string SelectedScopeName => SelectedScope?.Name ?? "No style scope selected";

    public string SelectedScopeSummary => SelectedScope?.Summary ?? "Select a scope to inspect styles, templates, and visual states.";

    public string SelectedScopePath => SelectedScope?.Path ?? string.Empty;

    public string EntryCountText => _entryCount == 1 ? "1 entry" : $"{_entryCount} entries";

    public StyleValueDetailsViewModel? Details
    {
        get => _details;
        private set => RaiseAndSetIfChanged(ref _details, value);
    }

    public StyleScopeNode? SelectedScope
    {
        get => _selectedScope;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedScope, value))
            {
                RaisePropertyChanged(nameof(SelectedScopeName));
                RaisePropertyChanged(nameof(SelectedScopeSummary));
                RaisePropertyChanged(nameof(SelectedScopePath));
                RefreshEntries();
            }
        }
    }

    public StyleEntryViewModel? SelectedEntry
    {
        get => _selectedEntry;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedEntry, value))
            {
                UpdateDetails();
                CopySelectedNameCommand.RaiseCanExecuteChanged();
                CopySelectedValueCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Refresh()
    {
        var previousScopePath = SelectedScope?.Path;
        var hadScopeState = ScopeTreeSource.Items.Any();
        var expandedScopePaths = hadScopeState
            ? HierarchyExpansionState.CaptureExpandedKeys(
                ScopeTreeSource.Items,
                x => x.Children,
                x => string.IsNullOrWhiteSpace(x.Path) ? null : x.Path,
                x => x.IsExpanded,
                StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        _pendingEntryRestoreId = SelectedEntry?.EntryId;
        _snapshot = StyleInspector.BuildSnapshot(_root, _inspectionTarget);
        ScopeTreeSource.Items = _snapshot.Nodes.ToArray();
        if (hadScopeState)
        {
            HierarchyExpansionState.RestoreExpandedKeys(
                ScopeTreeSource.Items,
                x => x.Children,
                x => string.IsNullOrWhiteSpace(x.Path) ? null : x.Path,
                (node, isExpanded) => node.IsExpanded = isExpanded,
                expandedScopePaths);
        }

        RaisePropertyChanged(nameof(InspectionTargetSummary));

        if (previousScopePath is not null &&
            TryFindScope(ScopeTreeSource.Items, previousScopePath, out var restoredPath, out var restoredNode))
        {
            if (restoredNode is not null)
            {
                ExpandAncestors(restoredNode);
                ScopeSelection.SelectedIndex = restoredPath;
                SelectedScope = restoredNode;
                return;
            }
        }

        if (_snapshot.Nodes.Count > 0)
        {
            ScopeSelection.SelectedIndex = new IndexPath(0);
            SelectedScope = _snapshot.Nodes[0];
            return;
        }

        ScopeSelection.SelectedIndex = default;
        SelectedScope = null;
    }

    public void RefreshDetails() => Details?.Refresh();

    public void UpdateInspectionTarget(DependencyObject? element)
    {
        if (ReferenceEquals(_inspectionTarget, element))
        {
            return;
        }

        _inspectionTarget = element;
        Refresh();
    }

    public void UpdateIncludeClrProperties(bool includeClrProperties)
    {
        if (_includeClrProperties == includeClrProperties)
        {
            return;
        }

        _includeClrProperties = includeClrProperties;
        Details?.UpdateIncludeClrProperties(includeClrProperties);
    }

    private void RefreshEntries()
    {
        var restoreId = _pendingEntryRestoreId ?? SelectedEntry?.EntryId;
        _pendingEntryRestoreId = null;

        var entries = _snapshot is not null && SelectedScope is not null
            ? StyleInspector.BuildEntries(_snapshot, SelectedScope, _filter, SelectedSortOption, SortDescending).ToArray()
            : Array.Empty<StyleEntryViewModel>();

        EntrySource.Items = entries;
        if (RaiseAndSetIfChanged(ref _entryCount, entries.Length, nameof(EntryCountText)))
        {
            RaisePropertyChanged(nameof(EntryCountText));
        }

        if (restoreId is not null && TryFindEntry(entries, restoreId, out var index, out var entry))
        {
            EntrySelection.SelectedIndex = new IndexPath(index);
            SelectedEntry = entry;
            return;
        }

        EntrySelection.SelectedIndex = default;
        SelectedEntry = null;
    }

    private void UpdateDetails()
    {
        if (SelectedEntry?.GetInspectionTarget() is { } entryTarget)
        {
            Details = new StyleValueDetailsViewModel(
                SelectedEntry.Name,
                SelectedEntry.TypeText,
                SelectedEntry.Summary,
                SelectedEntry.Kind,
                SelectedEntry.OriginText,
                SelectedEntry.EntryId,
                entryTarget,
                _includeClrProperties);
            return;
        }

        Details = null;
    }

    private void SetExpanded(bool expanded)
    {
        foreach (var node in ScopeTreeSource.Items)
        {
            SetExpanded(node, expanded);
        }
    }

    private void SetExpanded(StyleScopeNode node, bool expanded)
    {
        node.IsExpanded = expanded;
        foreach (var child in node.Children)
        {
            SetExpanded(child, expanded);
        }
    }

    private static bool TryFindEntry(IReadOnlyList<StyleEntryViewModel> entries, string entryId, out int index, out StyleEntryViewModel? entry)
    {
        for (var i = 0; i < entries.Count; i++)
        {
            if (string.Equals(entries[i].EntryId, entryId, StringComparison.Ordinal))
            {
                index = i;
                entry = entries[i];
                return true;
            }
        }

        index = -1;
        entry = null;
        return false;
    }

    private static bool TryFindScope(IEnumerable<StyleScopeNode> roots, string path, out IndexPath indexPath, out StyleScopeNode? node)
    {
        var index = 0;
        foreach (var root in roots)
        {
            if (TryFindScope(root, path, new IndexPath(index), out indexPath, out node))
            {
                return true;
            }

            index++;
        }

        indexPath = default;
        node = null;
        return false;
    }

    private static bool TryFindScope(StyleScopeNode current, string path, IndexPath currentPath, out IndexPath indexPath, out StyleScopeNode? node)
    {
        if (string.Equals(current.Path, path, StringComparison.Ordinal))
        {
            indexPath = currentPath;
            node = current;
            return true;
        }

        for (var index = 0; index < current.Children.Count; index++)
        {
            if (TryFindScope(current.Children[index], path, currentPath.Append(index), out indexPath, out node))
            {
                return true;
            }
        }

        indexPath = default;
        node = null;
        return false;
    }

    private static void ExpandAncestors(StyleScopeNode node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
        {
            current.IsExpanded = true;
        }
    }
}
