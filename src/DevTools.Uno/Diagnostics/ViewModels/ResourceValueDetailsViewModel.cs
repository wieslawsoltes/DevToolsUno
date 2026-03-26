using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using DevTools.Uno.Diagnostics.Internal;
using Microsoft.UI.Xaml;

namespace DevTools.Uno.Diagnostics.ViewModels;

internal sealed class ResourceValueDetailsViewModel : ViewModelBase
{
    private readonly ResourceEntryViewModel _entry;
    private readonly ISet<string> _pinnedProperties = new HashSet<string>(StringComparer.Ordinal);
    private bool _includeClrProperties;
    private PropertyGridNode? _selectedProperty;

    public ResourceValueDetailsViewModel(ResourceEntryViewModel entry, bool includeClrProperties)
    {
        _entry = entry;
        _includeClrProperties = includeClrProperties;
        _entry.PropertyChanged += OnEntryPropertyChanged;
        Filter = new FilterViewModel();
        Filter.RefreshFilter += (_, _) => Refresh();

        CopySelectedPropertyValueCommand = new RelayCommand(async () =>
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

    public string SelectedResourceKey => _entry.KeyText;

    public string SelectedResourceType => _entry.TypeText;

    public string SelectedResourceValue => _entry.ValueText;

    public string ProviderName => _entry.ProviderName;

    public string ProviderKind => _entry.ProviderKind;

    public string ProviderPath => _entry.ProviderPath;

    public string DictionarySource => _entry.DictionarySource;

    public FilterViewModel Filter { get; }

    public HierarchicalTreeDataGridSource<PropertyGridNode> PropertySource { get; }

    public TreeDataGridRowSelectionModel<PropertyGridNode> Selection { get; }

    public FlatTreeDataGridSource<PropertyValueSourceViewModel> ValueSourceGrid { get; }

    public RelayCommand CopySelectedPropertyValueCommand { get; }

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
                CopySelectedPropertyValueCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public void Refresh()
    {
        var selectedFullName = SelectedProperty?.FullName;
        PropertySource.Items = BuildPropertyNodes().ToArray();

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

        RaiseHeaderPropertiesChanged();
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

    private IEnumerable<PropertyGridNode> BuildPropertyNodes()
    {
        var value = _entry.GetValue();
        if (value is null || IsSimpleType(value.GetType()))
        {
            yield return CreateScalarGroup();
            yield break;
        }

        var includeClrProperties = _includeClrProperties || value is not DependencyObject;
        foreach (var node in PropertyInspector.BuildPropertyTree(value, _pinnedProperties, includeClrProperties, Filter))
        {
            AttachNotify(node);
            yield return node;
        }
    }

    private PropertyGridNode CreateScalarGroup()
    {
        var group = new PropertyGridNode
        {
            Name = "Resource Value",
            ValueText = string.Empty,
            TypeText = string.Empty,
            PriorityText = string.Empty,
            SourceText = string.Empty,
            IsGroup = true,
            IsEditable = false,
            FullName = $"RESOURCE-GROUP:{_entry.ResourceId}",
            Editor = PropertyEditorMetadata.ReadOnly,
        };

        group.Children.Add(new PropertyGridNode
        {
            Name = "Value",
            ValueText = _entry.ValueText,
            TypeText = _entry.TypeText,
            PriorityText = "Resource",
            SourceText = _entry.ProviderKind,
            IsGroup = false,
            IsEditable = _entry.CanEditInline,
            FullName = $"RESOURCE:{_entry.ResourceId}",
            Editor = PropertyInspector.GetEditorMetadata(_entry.ValueType, _entry.GetValue(), _entry.CanEditInline),
            TrySetValue = value => _entry.ApplyValue(value),
            GetSources = () =>
            [
                new PropertyValueSourceViewModel
                {
                    Name = "Resource",
                    Value = _entry.ValueText,
                    Detail = $"{_entry.ProviderPath} · {_entry.DictionarySource}",
                    IsActive = true,
                },
            ],
            GetRawValue = () => _entry.GetValue(),
            NotifyValueChanged = OnPropertyValueChanged,
        });

        return group;
    }

    private void AttachNotify(PropertyGridNode node)
    {
        node.NotifyValueChanged = OnPropertyValueChanged;
        foreach (var child in node.Children)
        {
            AttachNotify(child);
        }
    }

    private void OnPropertyValueChanged()
        => _entry.RefreshFromOwner();

    private void ReloadSelectedSources()
    {
        ValueSourceGrid.Items = (SelectedProperty?.LoadSources() ?? []).ToArray();
    }

    private void OnSelectedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PropertyGridNode.ValueText) or nameof(PropertyGridNode.PriorityText))
        {
            _entry.RefreshFromOwner();
        }
    }

    private void OnEntryPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ResourceEntryViewModel.RawValue))
        {
            Refresh();
        }
    }

    private void RaiseHeaderPropertiesChanged()
    {
        RaisePropertyChanged(nameof(SelectedResourceType));
        RaisePropertyChanged(nameof(SelectedResourceValue));
    }

    private static bool IsSimpleType(Type type)
    {
        var actualType = Nullable.GetUnderlyingType(type) ?? type;
        return actualType.IsPrimitive ||
               actualType.IsEnum ||
               actualType == typeof(string) ||
               actualType == typeof(decimal) ||
               actualType == typeof(DateTime) ||
               actualType == typeof(DateTimeOffset) ||
               actualType == typeof(TimeSpan) ||
               actualType == typeof(Guid);
    }

}
