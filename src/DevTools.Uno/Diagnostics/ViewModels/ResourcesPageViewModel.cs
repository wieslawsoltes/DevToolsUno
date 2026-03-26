using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using IndexPath = Avalonia.Controls.IndexPath;
using AGridLength = Avalonia.GridLength;
using AGridUnitType = Avalonia.GridUnitType;
using DevTools.Uno.Diagnostics.Internal;
using Microsoft.UI.Xaml;

namespace DevTools.Uno.Diagnostics.ViewModels;

internal sealed class ResourcesPageViewModel : ViewModelBase
{
    private readonly FrameworkElement _root;
    private readonly FilterViewModel _filter;
    private ResourceProviderNode? _selectedProvider;
    private ResourceEntryViewModel? _selectedResource;
    private ResourceValueDetailsViewModel? _details;
    private DependencyObject? _inspectionTarget;
    private bool _includeNested;
    private bool _sortDescending;
    private bool _includeClrProperties;
    private string _selectedSortOption = "Key";
    private string? _pendingResourceRestoreId;
    private int _resourceCount;

    public ResourcesPageViewModel(FrameworkElement root, bool includeClrProperties)
    {
        _root = root;
        _includeClrProperties = includeClrProperties;
        _filter = new FilterViewModel();
        _filter.RefreshFilter += (_, _) => RefreshResources();

        RefreshCommand = new RelayCommand(Refresh);
        ExpandAllCommand = new RelayCommand(() => SetExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetExpanded(false));
        CopySelectedKeyCommand = new RelayCommand(async () =>
        {
            if (SelectedResource is not null)
            {
                await PropertyInspector.CopyTextAsync(SelectedResource.KeyText);
            }
        }, () => SelectedResource is not null);
        CopySelectedValueCommand = new RelayCommand(async () =>
        {
            if (SelectedResource is not null)
            {
                await PropertyInspector.CopyTextAsync(SelectedResource.ValueText);
            }
        }, () => SelectedResource is not null);

        var providers = new HierarchicalTreeDataGridSource<ResourceProviderNode>(Array.Empty<ResourceProviderNode>());
        providers.Columns.Add(
            new HierarchicalExpanderColumn<ResourceProviderNode>(
                new TextColumn<ResourceProviderNode, string>("Provider", x => x.Name, new AGridLength(2, AGridUnitType.Star)),
                x => x.Children,
                x => x.Children.Count > 0,
                x => x.IsExpanded));
        providers.Columns.Add(new TextColumn<ResourceProviderNode, string>("Kind", x => x.Kind, new AGridLength(1, AGridUnitType.Star)));
        providers.Columns.Add(new TextColumn<ResourceProviderNode, string>("Summary", x => x.Summary, new AGridLength(2, AGridUnitType.Star)));
        ProviderTreeSource = providers;
        ProviderSelection = new TreeDataGridRowSelectionModel<ResourceProviderNode>(ProviderTreeSource)
        {
            SingleSelect = true,
        };
        ProviderSelection.SelectionChanged += (_, _) => SelectedProvider = ProviderSelection.SelectedItem;
        ProviderTreeSource.Selection = ProviderSelection;

        var resources = new FlatTreeDataGridSource<ResourceEntryViewModel>(Array.Empty<ResourceEntryViewModel>());
        resources.Columns.Add(new TextColumn<ResourceEntryViewModel, string>("Key", x => x.KeyText, new AGridLength(2, AGridUnitType.Star)));
        resources.Columns.Add(new TextColumn<ResourceEntryViewModel, string>("Value", x => x.ValueText, (row, value) => { row.ApplyValue(value); }, new AGridLength(2, AGridUnitType.Star)));
        resources.Columns.Add(new TextColumn<ResourceEntryViewModel, string>("Type", x => x.TypeText, new AGridLength(1, AGridUnitType.Star)));
        resources.Columns.Add(new TextColumn<ResourceEntryViewModel, string>("Provider", x => x.ProviderName, new AGridLength(1, AGridUnitType.Star)));
        ResourceSource = resources;
        ResourceSelection = new TreeDataGridRowSelectionModel<ResourceEntryViewModel>(ResourceSource)
        {
            SingleSelect = true,
        };
        ResourceSelection.SelectionChanged += (_, _) => SelectedResource = ResourceSelection.SelectedItem;
        ResourceSource.Selection = ResourceSelection;

        Refresh();
    }

    public HierarchicalTreeDataGridSource<ResourceProviderNode> ProviderTreeSource { get; }

    public TreeDataGridRowSelectionModel<ResourceProviderNode> ProviderSelection { get; }

    public FlatTreeDataGridSource<ResourceEntryViewModel> ResourceSource { get; }

    public TreeDataGridRowSelectionModel<ResourceEntryViewModel> ResourceSelection { get; }

    public FilterViewModel Filter => _filter;

    public IReadOnlyList<string> SortOptions { get; } = ["Key", "Type"];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ExpandAllCommand { get; }

    public RelayCommand CollapseAllCommand { get; }

    public RelayCommand CopySelectedKeyCommand { get; }

    public RelayCommand CopySelectedValueCommand { get; }

    public bool IncludeNested
    {
        get => _includeNested;
        set
        {
            if (RaiseAndSetIfChanged(ref _includeNested, value))
            {
                RefreshResources();
            }
        }
    }

    public bool SortDescending
    {
        get => _sortDescending;
        set
        {
            if (RaiseAndSetIfChanged(ref _sortDescending, value))
            {
                RefreshResources();
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
                RefreshResources();
            }
        }
    }

    public string InspectionTargetSummary => _inspectionTarget is FrameworkElement element
        ? $"Selection: {ResourceInspector.FormatElementName(element)}"
        : "Selection: (none)";

    public string SelectedProviderName => SelectedProvider?.Name ?? "No provider selected";

    public string SelectedProviderSummary => SelectedProvider?.Summary ?? "Select a provider to inspect its resources.";

    public string SelectedProviderPath => SelectedProvider?.Path ?? string.Empty;

    public string ResourceCountText => _resourceCount == 1 ? "1 resource" : $"{_resourceCount} resources";

    public ResourceValueDetailsViewModel? Details
    {
        get => _details;
        private set => RaiseAndSetIfChanged(ref _details, value);
    }

    public ResourceProviderNode? SelectedProvider
    {
        get => _selectedProvider;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedProvider, value))
            {
                RaisePropertyChanged(nameof(SelectedProviderName));
                RaisePropertyChanged(nameof(SelectedProviderSummary));
                RaisePropertyChanged(nameof(SelectedProviderPath));
                RefreshResources();
            }
        }
    }

    public ResourceEntryViewModel? SelectedResource
    {
        get => _selectedResource;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedResource, value))
            {
                Details = value is not null ? new ResourceValueDetailsViewModel(value, _includeClrProperties) : null;
                CopySelectedKeyCommand.RaiseCanExecuteChanged();
                CopySelectedValueCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Refresh()
    {
        var previousProviderPath = SelectedProvider?.Path;
        var hadProviderState = ProviderTreeSource.Items.Any();
        var expandedProviderPaths = hadProviderState
            ? HierarchyExpansionState.CaptureExpandedKeys(
                ProviderTreeSource.Items,
                x => x.Children,
                x => string.IsNullOrWhiteSpace(x.Path) ? null : x.Path,
                x => x.IsExpanded,
                StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        _pendingResourceRestoreId = SelectedResource?.ResourceId;
        ProviderTreeSource.Items = ResourceInspector.BuildProviderTree(_root, _inspectionTarget).ToArray();
        if (hadProviderState)
        {
            HierarchyExpansionState.RestoreExpandedKeys(
                ProviderTreeSource.Items,
                x => x.Children,
                x => string.IsNullOrWhiteSpace(x.Path) ? null : x.Path,
                (node, isExpanded) => node.IsExpanded = isExpanded,
                expandedProviderPaths);
        }

        if (previousProviderPath is not null &&
            TryFindProvider(ProviderTreeSource.Items, previousProviderPath, out var restoredPath, out var restoredNode))
        {
            ExpandAncestors(restoredNode);
            ProviderSelection.SelectedIndex = restoredPath;
            SelectedProvider = restoredNode;
            return;
        }

        if (TrySelectDefaultProvider())
        {
            return;
        }

        ProviderSelection.SelectedIndex = default;
        SelectedProvider = null;
    }

    public void RefreshDetails() => Details?.Refresh();

    public void UpdateInspectionTarget(DependencyObject? element)
    {
        if (ReferenceEquals(_inspectionTarget, element))
        {
            return;
        }

        _inspectionTarget = element;
        RaisePropertyChanged(nameof(InspectionTargetSummary));
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

    private void RefreshResources()
    {
        var restoreResourceId = _pendingResourceRestoreId ?? SelectedResource?.ResourceId;
        _pendingResourceRestoreId = null;

        var resources = SelectedProvider is not null
            ? ResourceInspector.BuildResources(
                SelectedProvider,
                _filter,
                IncludeNested,
                SelectedSortOption,
                SortDescending,
                OnResourceValueReplaced).ToArray()
            : Array.Empty<ResourceEntryViewModel>();

        ResourceSource.Items = resources;
        if (RaiseAndSetIfChanged(ref _resourceCount, resources.Length, nameof(ResourceCountText)))
        {
            RaisePropertyChanged(nameof(ResourceCountText));
        }

        if (restoreResourceId is not null && TryFindResource(resources, restoreResourceId, out var index, out var resource))
        {
            ResourceSelection.SelectedIndex = new IndexPath(index);
            SelectedResource = resource;
            return;
        }

        ResourceSelection.SelectedIndex = default;
        SelectedResource = null;
    }

    private void OnResourceValueReplaced(ResourceEntryViewModel entry)
    {
        _pendingResourceRestoreId = entry.ResourceId;
        Refresh();
    }

    private void SetExpanded(bool isExpanded)
    {
        foreach (var root in ProviderTreeSource.Items)
        {
            SetExpanded(root, isExpanded);
        }
    }

    private static void SetExpanded(ResourceProviderNode node, bool isExpanded)
    {
        node.IsExpanded = isExpanded;
        foreach (var child in node.Children)
        {
            SetExpanded(child, isExpanded);
        }
    }

    private bool TrySelectDefaultProvider()
    {
        var roots = ProviderTreeSource.Items.ToArray();
        foreach (var root in roots)
        {
            if (string.Equals(root.Kind, "Host Root", StringComparison.Ordinal))
            {
                ProviderSelection.SelectedIndex = new IndexPath(FindRootIndex(roots, root));
                SelectedProvider = root;
                return true;
            }
        }

        if (roots.Length == 0)
        {
            return false;
        }

        ProviderSelection.SelectedIndex = new IndexPath(0);
        SelectedProvider = ProviderSelection.SelectedItem;
        return SelectedProvider is not null;
    }

    private static int FindRootIndex(IReadOnlyList<ResourceProviderNode> roots, ResourceProviderNode target)
    {
        for (var index = 0; index < roots.Count; index++)
        {
            if (ReferenceEquals(roots[index], target))
            {
                return index;
            }
        }

        return 0;
    }

    private static void ExpandAncestors(ResourceProviderNode? node)
    {
        while (node is not null)
        {
            node.IsExpanded = true;
            node = node.Parent;
        }
    }

    private static bool TryFindProvider(IEnumerable<ResourceProviderNode> roots, string path, out IndexPath indexPath, out ResourceProviderNode? node)
    {
        var index = 0;
        foreach (var root in roots)
        {
            if (TryFindProvider(root, path, new IndexPath(index), out indexPath, out node))
            {
                return true;
            }

            index++;
        }

        indexPath = default;
        node = null;
        return false;
    }

    private static bool TryFindProvider(ResourceProviderNode current, string path, IndexPath currentPath, out IndexPath indexPath, out ResourceProviderNode? node)
    {
        if (string.Equals(current.Path, path, StringComparison.Ordinal))
        {
            indexPath = currentPath;
            node = current;
            return true;
        }

        for (var index = 0; index < current.Children.Count; index++)
        {
            if (TryFindProvider(current.Children[index], path, currentPath.Append(index), out indexPath, out node))
            {
                return true;
            }
        }

        indexPath = default;
        node = null;
        return false;
    }

    private static bool TryFindResource(IReadOnlyList<ResourceEntryViewModel> resources, string resourceId, out int index, out ResourceEntryViewModel? resource)
    {
        for (var i = 0; i < resources.Count; i++)
        {
            if (!string.Equals(resources[i].ResourceId, resourceId, StringComparison.Ordinal))
            {
                continue;
            }

            index = i;
            resource = resources[i];
            return true;
        }

        index = -1;
        resource = null;
        return false;
    }
}
