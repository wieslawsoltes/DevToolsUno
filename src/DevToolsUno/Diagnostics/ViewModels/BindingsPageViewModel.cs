using System.Collections.ObjectModel;
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

internal sealed class BindingsPageViewModel : ViewModelBase
{
    private readonly FrameworkElement _root;
    private readonly FilterViewModel _filter;
    private BindingInspectionSnapshot? _snapshot;
    private BindingDescriptorViewModel? _selectedBinding;
    private BindingFactViewModel? _selectedDetailItem;
    private BindingObjectDetailsViewModel? _details;
    private DependencyObject? _inspectionTarget;
    private bool _sortDescending;
    private bool _includeClrProperties;
    private string _selectedSortOption = "Property";
    private string? _pendingBindingRestoreId;
    private string? _pendingDetailRestoreName;
    private int _bindingCount;

    public BindingsPageViewModel(FrameworkElement root, bool includeClrProperties)
    {
        _root = root;
        _includeClrProperties = includeClrProperties;
        _filter = new FilterViewModel();
        _filter.RefreshFilter += (_, _) => RefreshBindings();

        RefreshCommand = new RelayCommand(Refresh);
        CopySelectedPathCommand = new RelayCommand(async () =>
        {
            if (SelectedBinding is not null)
            {
                await PropertyInspector.CopyTextAsync(SelectedBinding.PathText);
            }
        }, () => SelectedBinding is not null);
        CopySelectedValueCommand = new RelayCommand(async () =>
        {
            if (SelectedBinding is not null)
            {
                await PropertyInspector.CopyTextAsync(SelectedBinding.ValueText);
            }
        }, () => SelectedBinding is not null);

        var source = new FlatTreeDataGridSource<BindingDescriptorViewModel>(Array.Empty<BindingDescriptorViewModel>());
        source.Columns.Add(new TextColumn<BindingDescriptorViewModel, string>("Property", x => x.PropertyName, new AGridLength(2, AGridUnitType.Star)));
        source.Columns.Add(new TextColumn<BindingDescriptorViewModel, string>("Kind", x => x.Kind, new AGridLength(1, AGridUnitType.Star)));
        source.Columns.Add(new TextColumn<BindingDescriptorViewModel, string>("Path", x => x.PathText, new AGridLength(2, AGridUnitType.Star)));
        source.Columns.Add(new TextColumn<BindingDescriptorViewModel, string>("Source", x => x.SourceText, new AGridLength(2, AGridUnitType.Star)));
        source.Columns.Add(new TextColumn<BindingDescriptorViewModel, string>("Value", x => x.ValueText, new AGridLength(1.5, AGridUnitType.Star)));
        BindingSource = source;
        BindingSelection = new TreeDataGridRowSelectionModel<BindingDescriptorViewModel>(BindingSource)
        {
            SingleSelect = true,
        };
        BindingSelection.SelectionChanged += (_, _) => SelectedBinding = BindingSelection.SelectedItem;
        BindingSource.Selection = BindingSelection;

        Refresh();
    }

    public FlatTreeDataGridSource<BindingDescriptorViewModel> BindingSource { get; }

    public TreeDataGridRowSelectionModel<BindingDescriptorViewModel> BindingSelection { get; }

    public FilterViewModel Filter => _filter;

    public ObservableCollection<BindingFactViewModel> DetailItems { get; } = [];

    public IReadOnlyList<string> SortOptions { get; } = ["Property", "Kind", "Path", "Source"];

    public RelayCommand RefreshCommand { get; }

    public RelayCommand CopySelectedPathCommand { get; }

    public RelayCommand CopySelectedValueCommand { get; }

    public bool SortDescending
    {
        get => _sortDescending;
        set
        {
            if (RaiseAndSetIfChanged(ref _sortDescending, value))
            {
                RefreshBindings();
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
                RefreshBindings();
            }
        }
    }

    public string InspectionTargetSummary => BindingInspector.FormatInspectionTargetSummary(_snapshot, _root);

    public string SelectedBindingName => SelectedBinding?.PropertyName ?? "No binding selected";

    public string SelectedBindingSummary => SelectedBinding?.Summary ?? "Select a binding to inspect its runtime source and binding object.";

    public string SelectedBindingPath => SelectedBinding?.PathText ?? string.Empty;

    public string BindingCountText => _bindingCount == 1 ? "1 binding" : $"{_bindingCount} bindings";

    public BindingObjectDetailsViewModel? Details
    {
        get => _details;
        private set => RaiseAndSetIfChanged(ref _details, value);
    }

    public BindingDescriptorViewModel? SelectedBinding
    {
        get => _selectedBinding;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedBinding, value))
            {
                RaisePropertyChanged(nameof(SelectedBindingName));
                RaisePropertyChanged(nameof(SelectedBindingSummary));
                RaisePropertyChanged(nameof(SelectedBindingPath));
                RefreshSelectedDetails();
                CopySelectedPathCommand.RaiseCanExecuteChanged();
                CopySelectedValueCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public BindingFactViewModel? SelectedDetailItem
    {
        get => _selectedDetailItem;
        set
        {
            if (RaiseAndSetIfChanged(ref _selectedDetailItem, value))
            {
                UpdateDetails();
            }
        }
    }

    public void Refresh()
    {
        _pendingBindingRestoreId = SelectedBinding?.BindingId;
        _pendingDetailRestoreName = SelectedDetailItem?.Name;
        _snapshot = BindingInspector.BuildSnapshot(_root, _inspectionTarget);
        RaisePropertyChanged(nameof(InspectionTargetSummary));
        RefreshBindings();
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

    private void RefreshBindings()
    {
        var restoreId = _pendingBindingRestoreId ?? SelectedBinding?.BindingId;
        var bindings = _snapshot is not null
            ? BindingInspector.ApplyFilterAndSort(_snapshot.Bindings, _filter, SelectedSortOption, SortDescending).ToArray()
            : Array.Empty<BindingDescriptorViewModel>();

        BindingSource.Items = bindings;
        if (RaiseAndSetIfChanged(ref _bindingCount, bindings.Length, nameof(BindingCountText)))
        {
            RaisePropertyChanged(nameof(BindingCountText));
        }

        _pendingBindingRestoreId = null;

        if (restoreId is not null && TryFindBinding(bindings, restoreId, out var index, out var binding))
        {
            BindingSelection.SelectedIndex = new IndexPath(index);
            SelectedBinding = binding;
            return;
        }

        BindingSelection.SelectedIndex = bindings.Length > 0 ? new IndexPath(0) : default;
        SelectedBinding = bindings.Length > 0 ? bindings[0] : null;
    }

    private void RefreshSelectedDetails()
    {
        var restoreName = _pendingDetailRestoreName ?? SelectedDetailItem?.Name;
        _pendingDetailRestoreName = null;

        DetailItems.Clear();
        foreach (var item in SelectedBinding is not null ? BindingInspector.BuildFacts(SelectedBinding) : Array.Empty<BindingFactViewModel>())
        {
            DetailItems.Add(item);
        }

        if (restoreName is not null)
        {
            SelectedDetailItem = DetailItems.FirstOrDefault(x => string.Equals(x.Name, restoreName, StringComparison.Ordinal));
        }

        SelectedDetailItem ??= DetailItems.FirstOrDefault();
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (SelectedBinding is null)
        {
            Details = null;
            return;
        }

        if (SelectedDetailItem?.InspectionObject is { } detailObject)
        {
            Details = new BindingObjectDetailsViewModel(
                SelectedDetailItem.Name,
                SelectedDetailItem.Value,
                SelectedDetailItem.Detail,
                SelectedBinding.Kind,
                SelectedBinding.PropertyName,
                SelectedBinding.BindingId,
                detailObject,
                _includeClrProperties);
            return;
        }

        Details = new BindingObjectDetailsViewModel(
            SelectedBinding.PropertyName,
            SelectedBinding.Kind,
            SelectedBinding.Summary,
            SelectedBinding.Kind,
            SelectedBinding.PropertyName,
            SelectedBinding.BindingId,
            SelectedBinding.BindingExpression,
            _includeClrProperties);
    }

    private static bool TryFindBinding(IReadOnlyList<BindingDescriptorViewModel> bindings, string bindingId, out int index, out BindingDescriptorViewModel? binding)
    {
        for (var i = 0; i < bindings.Count; i++)
        {
            if (string.Equals(bindings[i].BindingId, bindingId, StringComparison.Ordinal))
            {
                index = i;
                binding = bindings[i];
                return true;
            }
        }

        index = -1;
        binding = null;
        return false;
    }
}
