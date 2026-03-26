using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using DevTools.Uno.Diagnostics.Internal;
using Microsoft.UI.Xaml;

namespace DevTools.Uno.Diagnostics.ViewModels;

internal sealed class MemoryObjectDetailsViewModel : ViewModelBase
{
    private readonly object? _value;
    private readonly ISet<string> _pinnedProperties = new HashSet<string>(StringComparer.Ordinal);
    private bool _includeClrProperties;
    private PropertyGridNode? _selectedProperty;

    public MemoryObjectDetailsViewModel(
        string title,
        string subtitle,
        string summary,
        string kind,
        string owner,
        string path,
        object? value,
        bool includeClrProperties)
    {
        Title = title;
        Subtitle = subtitle;
        Summary = summary;
        Kind = kind;
        Owner = owner;
        Path = path;
        _value = value;
        _includeClrProperties = includeClrProperties;

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

    public string Title { get; }

    public string Subtitle { get; }

    public string Summary { get; }

    public string Kind { get; }

    public string Owner { get; }

    public string Path { get; }

    public string ValueText => PropertyInspector.FormatValue(_value);

    public string ValueType => _value?.GetType().FullName ?? "(null)";

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
        var hadPropertyState = PropertySource.Items.Any();
        var expandedFullNames = hadPropertyState
            ? PropertyGridSourceBuilder.CaptureExpandedFullNames(PropertySource.Items)
            : new HashSet<string>(StringComparer.Ordinal);
        PropertySource.Items = BuildPropertyNodes().ToArray();
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

        RaisePropertyChanged(nameof(ValueText));
        RaisePropertyChanged(nameof(ValueType));
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
        if (_value is null || IsSimpleType(_value.GetType()))
        {
            yield return CreateScalarGroup();
            yield break;
        }

        var includeClrProperties = _includeClrProperties || _value is not DependencyObject;
        foreach (var node in PropertyInspector.BuildPropertyTree(_value, _pinnedProperties, includeClrProperties, Filter))
        {
            AttachNotify(node);
            yield return node;
        }
    }

    private PropertyGridNode CreateScalarGroup()
    {
        var group = new PropertyGridNode
        {
            Name = "Value",
            ValueText = string.Empty,
            TypeText = string.Empty,
            PriorityText = string.Empty,
            SourceText = string.Empty,
            IsGroup = true,
            IsEditable = false,
            FullName = $"MEMORY-DETAILS:{Title}:{Path}",
            Editor = PropertyEditorMetadata.ReadOnly,
        };

        group.Children.Add(new PropertyGridNode
        {
            Name = Title,
            ValueText = ValueText,
            TypeText = _value?.GetType().Name ?? "(null)",
            PriorityText = Kind,
            SourceText = Owner,
            IsGroup = false,
            IsEditable = false,
            FullName = $"MEMORY-DETAILS-VALUE:{Title}:{Path}",
            Editor = PropertyEditorMetadata.ReadOnly,
            GetSources = () =>
            [
                new PropertyValueSourceViewModel
                {
                    Name = Kind,
                    Value = ValueText,
                    Detail = Summary,
                    IsActive = true,
                },
            ],
            GetRawValue = () => _value,
        });

        return group;
    }

    private void AttachNotify(PropertyGridNode node)
    {
        node.NotifyValueChanged = Refresh;
        foreach (var child in node.Children)
        {
            AttachNotify(child);
        }
    }

    private void ReloadSelectedSources()
    {
        ValueSourceGrid.Items = (SelectedProperty?.LoadSources() ?? []).ToArray();
    }

    private void OnSelectedPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PropertyGridNode.ValueText) or nameof(PropertyGridNode.PriorityText))
        {
            Refresh();
            ReloadSelectedSources();
        }
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
