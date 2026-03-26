using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using IndexPath = Avalonia.Controls.IndexPath;
using AGridLength = Avalonia.GridLength;
using AGridUnitType = Avalonia.GridUnitType;
using DevTools.Uno.Diagnostics.Internal;

namespace DevTools.Uno.Diagnostics.ViewModels;

internal sealed class AssetsPageViewModel : ViewModelBase
{
    private readonly FilterViewModel _filter;
    private AssetFolderNode? _rootNode;
    private AssetFolderNode? _selectedFolder;
    private AssetEntryViewModel? _selectedAsset;
    private bool _recursive = true;
    private bool _sortDescending;
    private bool _isLoading;
    private string _statusText = "Loading assets…";
    private string _selectedSortOption = "Name";
    private string? _pendingAssetRestorePath;
    private int _assetCount;

    public AssetsPageViewModel()
    {
        _filter = new FilterViewModel();
        _filter.RefreshFilter += (_, _) => RefreshAssets();
        Preview = new AssetPreviewViewModel();

        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        ExpandAllCommand = new RelayCommand(() => SetExpanded(true));
        CollapseAllCommand = new RelayCommand(() => SetExpanded(false));
        CopySelectedUriCommand = new RelayCommand(async () =>
        {
            if (SelectedAsset is not null)
            {
                await PropertyInspector.CopyTextAsync(SelectedAsset.AssetUri.ToString());
            }
        }, () => SelectedAsset is not null);
        CopySelectedPathCommand = new RelayCommand(async () =>
        {
            if (SelectedAsset is not null)
            {
                await PropertyInspector.CopyTextAsync(SelectedAsset.RelativePath);
            }
        }, () => SelectedAsset is not null);
        ExportSelectedCommand = new RelayCommand(async () => await Preview.ExportAsync(), () => SelectedAsset is not null);

        var folders = new HierarchicalTreeDataGridSource<AssetFolderNode>(Array.Empty<AssetFolderNode>());
        folders.Columns.Add(
            new HierarchicalExpanderColumn<AssetFolderNode>(
                new TextColumn<AssetFolderNode, string>("Folder", x => x.Name, new AGridLength(2, AGridUnitType.Star)),
                x => x.Children,
                x => x.Children.Count > 0,
                x => x.IsExpanded));
        folders.Columns.Add(new TextColumn<AssetFolderNode, string>("Summary", x => x.Summary, new AGridLength(2, AGridUnitType.Star)));
        FolderTreeSource = folders;
        FolderSelection = new TreeDataGridRowSelectionModel<AssetFolderNode>(FolderTreeSource)
        {
            SingleSelect = true,
        };
        FolderSelection.SelectionChanged += (_, _) => SelectedFolder = FolderSelection.SelectedItem;
        FolderTreeSource.Selection = FolderSelection;

        var assets = new FlatTreeDataGridSource<AssetEntryViewModel>(Array.Empty<AssetEntryViewModel>());
        assets.Columns.Add(new TextColumn<AssetEntryViewModel, string>("Name", x => x.Name, new AGridLength(2, AGridUnitType.Star)));
        assets.Columns.Add(new TextColumn<AssetEntryViewModel, string>("Type", x => x.Type, new AGridLength(1, AGridUnitType.Star)));
        assets.Columns.Add(new TextColumn<AssetEntryViewModel, string>("Size", x => x.SizeText, new AGridLength(1, AGridUnitType.Star)));
        assets.Columns.Add(new TextColumn<AssetEntryViewModel, string>("Path", x => x.RelativePath, new AGridLength(2, AGridUnitType.Star)));
        AssetSource = assets;
        AssetSelection = new TreeDataGridRowSelectionModel<AssetEntryViewModel>(AssetSource)
        {
            SingleSelect = true,
        };
        AssetSelection.SelectionChanged += (_, _) => SelectedAsset = AssetSelection.SelectedItem;
        AssetSource.Selection = AssetSelection;

        _ = RefreshAsync();
    }

    public HierarchicalTreeDataGridSource<AssetFolderNode> FolderTreeSource { get; }

    public TreeDataGridRowSelectionModel<AssetFolderNode> FolderSelection { get; }

    public FlatTreeDataGridSource<AssetEntryViewModel> AssetSource { get; }

    public TreeDataGridRowSelectionModel<AssetEntryViewModel> AssetSelection { get; }

    public FilterViewModel Filter => _filter;

    public AssetPreviewViewModel Preview { get; }

    public IReadOnlyList<string> SortOptions { get; } = ["Name", "Type", "Size"];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ExpandAllCommand { get; }

    public RelayCommand CollapseAllCommand { get; }

    public RelayCommand CopySelectedUriCommand { get; }

    public RelayCommand CopySelectedPathCommand { get; }

    public RelayCommand ExportSelectedCommand { get; }

    public bool Recursive
    {
        get => _recursive;
        set
        {
            if (RaiseAndSetIfChanged(ref _recursive, value))
            {
                RefreshAssets();
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
                RefreshAssets();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            if (RaiseAndSetIfChanged(ref _selectedSortOption, value))
            {
                RefreshAssets();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        private set => RaiseAndSetIfChanged(ref _statusText, value);
    }

    public string SelectedFolderName => SelectedFolder?.Name ?? "No folder selected";

    public string SelectedFolderSummary => SelectedFolder?.Summary ?? "Select a folder to inspect packaged assets.";

    public string AssetCountText => _assetCount == 1 ? "1 asset" : $"{_assetCount} assets";

    public AssetFolderNode? SelectedFolder
    {
        get => _selectedFolder;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedFolder, value))
            {
                RaisePropertyChanged(nameof(SelectedFolderName));
                RaisePropertyChanged(nameof(SelectedFolderSummary));
                RefreshAssets();
            }
        }
    }

    public AssetEntryViewModel? SelectedAsset
    {
        get => _selectedAsset;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedAsset, value))
            {
                _ = Preview.LoadAsync(value);
                CopySelectedUriCommand.RaiseCanExecuteChanged();
                CopySelectedPathCommand.RaiseCanExecuteChanged();
                ExportSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Refresh() => _ = RefreshAsync();

    private async Task RefreshAsync()
    {
        IsLoading = true;
        StatusText = "Loading assets…";

        var previousFolder = SelectedFolder?.RelativePath;
        var hadFolderState = FolderTreeSource.Items.Any();
        var expandedFolderPaths = hadFolderState
            ? HierarchyExpansionState.CaptureExpandedKeys(
                FolderTreeSource.Items,
                x => x.Children,
                x => string.IsNullOrWhiteSpace(x.RelativePath) ? null : x.RelativePath,
                x => x.IsExpanded,
                StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        _pendingAssetRestorePath = SelectedAsset?.RelativePath;

        try
        {
            _rootNode = await AssetInspector.BuildFolderTreeAsync();
            FolderTreeSource.Items = [_rootNode];
            if (hadFolderState)
            {
                HierarchyExpansionState.RestoreExpandedKeys(
                    FolderTreeSource.Items,
                    x => x.Children,
                    x => string.IsNullOrWhiteSpace(x.RelativePath) ? null : x.RelativePath,
                    (node, isExpanded) => node.IsExpanded = isExpanded,
                    expandedFolderPaths);
            }

            if (previousFolder is not null &&
                TryFindFolder(FolderTreeSource.Items, previousFolder, out var folderIndex, out var folder))
            {
                ExpandAncestors(folder);
                FolderSelection.SelectedIndex = folderIndex;
                SelectedFolder = folder;
            }
            else
            {
                FolderSelection.SelectedIndex = new IndexPath(0);
                SelectedFolder = _rootNode;
            }

            StatusText = _rootNode.TotalAssetCount > 0
                ? $"{_rootNode.TotalAssetCount} packaged assets discovered."
                : "No packaged assets matched the current asset filter.";
        }
        catch (Exception exception)
        {
            FolderTreeSource.Items = Array.Empty<AssetFolderNode>();
            AssetSource.Items = Array.Empty<AssetEntryViewModel>();
            SelectedFolder = null;
            SelectedAsset = null;
            StatusText = exception.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RefreshAssets()
    {
        var restorePath = _pendingAssetRestorePath ?? SelectedAsset?.RelativePath;
        _pendingAssetRestorePath = null;

        var assets = SelectedFolder is not null
            ? AssetInspector.GetAssets(SelectedFolder, _filter, Recursive, SelectedSortOption, SortDescending).ToArray()
            : Array.Empty<AssetEntryViewModel>();

        AssetSource.Items = assets;
        if (RaiseAndSetIfChanged(ref _assetCount, assets.Length, nameof(AssetCountText)))
        {
            RaisePropertyChanged(nameof(AssetCountText));
        }

        if (restorePath is not null && TryFindAsset(assets, restorePath, out var index, out var asset))
        {
            AssetSelection.SelectedIndex = new IndexPath(index);
            SelectedAsset = asset;
            return;
        }

        AssetSelection.SelectedIndex = assets.Length > 0 ? new IndexPath(0) : default;
        SelectedAsset = AssetSelection.SelectedItem;
    }

    private void SetExpanded(bool value)
    {
        if (_rootNode is null)
        {
            return;
        }

        SetExpanded(_rootNode, value);
    }

    private static void SetExpanded(AssetFolderNode node, bool value)
    {
        node.IsExpanded = value;
        foreach (var child in node.Children)
        {
            SetExpanded(child, value);
        }
    }

    private static void ExpandAncestors(AssetFolderNode? node)
    {
        while (node is not null)
        {
            node.IsExpanded = true;
            node = node.Parent;
        }
    }

    private static bool TryFindFolder(IEnumerable<AssetFolderNode> roots, string relativePath, out IndexPath indexPath, out AssetFolderNode? node)
    {
        var index = 0;
        foreach (var root in roots)
        {
            if (TryFindFolder(root, relativePath, new IndexPath(index), out indexPath, out node))
            {
                return true;
            }

            index++;
        }

        indexPath = default;
        node = null;
        return false;
    }

    private static bool TryFindFolder(AssetFolderNode current, string relativePath, IndexPath currentPath, out IndexPath indexPath, out AssetFolderNode? node)
    {
        if (string.Equals(current.RelativePath, relativePath, StringComparison.Ordinal))
        {
            indexPath = currentPath;
            node = current;
            return true;
        }

        for (var index = 0; index < current.Children.Count; index++)
        {
            if (TryFindFolder(current.Children[index], relativePath, currentPath.Append(index), out indexPath, out node))
            {
                return true;
            }
        }

        indexPath = default;
        node = null;
        return false;
    }

    private static bool TryFindAsset(IReadOnlyList<AssetEntryViewModel> assets, string relativePath, out int index, out AssetEntryViewModel? asset)
    {
        for (var i = 0; i < assets.Count; i++)
        {
            if (!string.Equals(assets[i].RelativePath, relativePath, StringComparison.Ordinal))
            {
                continue;
            }

            index = i;
            asset = assets[i];
            return true;
        }

        index = -1;
        asset = null;
        return false;
    }
}
