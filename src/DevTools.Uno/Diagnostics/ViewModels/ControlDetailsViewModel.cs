using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using DevTools.Uno.Diagnostics.Internal;
using Microsoft.UI.Xaml;

namespace DevTools.Uno.Diagnostics.ViewModels;

internal sealed class ControlDetailsViewModel : ViewModelBase
{
    private readonly ISet<string> _pinnedProperties;
    private readonly string _selectedElementName;
    private readonly string _selectedElementType;
    private bool _includeClrProperties;
    private PropertyGridNode? _selectedProperty;

    public ControlDetailsViewModel(
        DependencyObject element,
        ISet<string> pinnedProperties,
        bool includeClrProperties,
        string? selectedElementName = null,
        string? selectedElementType = null)
    {
        Element = element;
        _pinnedProperties = pinnedProperties;
        _includeClrProperties = includeClrProperties;
        _selectedElementName = string.IsNullOrWhiteSpace(selectedElementName)
            ? (element is FrameworkElement fe && !string.IsNullOrWhiteSpace(fe.Name) ? fe.Name : element.GetType().Name)
            : selectedElementName;
        _selectedElementType = string.IsNullOrWhiteSpace(selectedElementType)
            ? element.GetType().FullName ?? element.GetType().Name
            : selectedElementType;
        Layout = new ControlLayoutViewModel(Refresh);
        Metadata = new ControlMetadataViewModel();
        Filter = new FilterViewModel();
        Filter.RefreshFilter += (_, _) => Refresh();

        TogglePinCommand = new RelayCommand(TogglePinnedSelection, () => SelectedProperty is { IsGroup: false });
        CopyValueCommand = new RelayCommand(async () =>
        {
            if (SelectedProperty is not null)
            {
                await PropertyInspector.CopyTextAsync(SelectedProperty.ValueText);
            }
        }, () => SelectedProperty is not null);

        PropertySource = PropertyGridSourceBuilder.CreateSource();
        Selection = PropertyGridSourceBuilder.CreateSelection(PropertySource, property => SelectedProperty = property);
        ValueSourceGrid = PropertyValueSourceGridBuilder.Create();

        Refresh();
    }

    public DependencyObject Element { get; }

    public string SelectedElementName => _selectedElementName;

    public string SelectedElementType => _selectedElementType;

    public FilterViewModel Filter { get; }

    public ControlLayoutViewModel Layout { get; }

    public ControlMetadataViewModel Metadata { get; }

    public HierarchicalTreeDataGridSource<PropertyGridNode> PropertySource { get; }

    public TreeDataGridRowSelectionModel<PropertyGridNode> Selection { get; }

    public FlatTreeDataGridSource<PropertyValueSourceViewModel> ValueSourceGrid { get; }

    public RelayCommand TogglePinCommand { get; }

    public RelayCommand CopyValueCommand { get; }

    public PropertyGridNode? SelectedProperty
    {
        get => _selectedProperty;
        private set
        {
            if (ReferenceEquals(_selectedProperty, value))
            {
                return;
            }

            if (_selectedProperty is not null)
            {
                _selectedProperty.PropertyChanged -= OnSelectedPropertyChanged;
            }

            if (RaiseAndSetIfChanged(ref _selectedProperty, value))
            {
                if (_selectedProperty is not null)
                {
                    _selectedProperty.PropertyChanged += OnSelectedPropertyChanged;
                }

                ReloadSelectedSources();

                TogglePinCommand.RaiseCanExecuteChanged();
                CopyValueCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Refresh()
    {
        var selectedFullName = SelectedProperty?.FullName;
        var hadPropertyState = PropertySource.Items.Any();
        var expandedFullNames = hadPropertyState
            ? PropertyGridSourceBuilder.CaptureExpandedFullNames(PropertySource.Items)
            : new HashSet<string>(StringComparer.Ordinal);
        Layout.Update(Element);
        Metadata.Update(Element);
        PropertySource.Items = PropertyInspector.BuildPropertyTree(Element, _pinnedProperties, _includeClrProperties, Filter).ToArray();
        if (hadPropertyState)
        {
            PropertyGridSourceBuilder.RestoreExpandedFullNames(PropertySource.Items, expandedFullNames);
        }

        if (selectedFullName is not null &&
            PropertyGridSourceBuilder.TryFindByFullName(PropertySource.Items, selectedFullName, out var path, out var node))
        {
            Selection.SelectedIndex = path;
            SelectedProperty = node;
        }
        else
        {
            SelectedProperty = Selection.SelectedItem;
        }
    }

    public void UpdateIncludeClrProperties(bool includeClrProperties)
    {
        if (_includeClrProperties == includeClrProperties)
        {
            return;
        }

        _includeClrProperties = includeClrProperties;
        Refresh();
    }

    private void TogglePinnedSelection()
    {
        if (SelectedProperty is not { IsGroup: false })
        {
            return;
        }

        if (!_pinnedProperties.Add(SelectedProperty.FullName))
        {
            _pinnedProperties.Remove(SelectedProperty.FullName);
        }

        Refresh();
    }

    private void ReloadSelectedSources()
    {
        ValueSourceGrid.Items = (SelectedProperty?.LoadSources() ?? []).ToArray();
    }

    private void OnSelectedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PropertyGridNode.ValueText) or nameof(PropertyGridNode.PriorityText))
        {
            Layout.Update(Element);
            Metadata.Update(Element);
            ReloadSelectedSources();
        }
    }
}
