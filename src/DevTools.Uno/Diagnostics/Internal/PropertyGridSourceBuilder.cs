using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using DevTools.Uno.Diagnostics.ViewModels;
using IndexPath = Avalonia.Controls.IndexPath;
using AGridLength = Avalonia.GridLength;
using AGridUnitType = Avalonia.GridUnitType;

namespace DevTools.Uno.Diagnostics.Internal;

internal static class PropertyGridSourceBuilder
{
    public const string ValueCellTemplateKey = "PropertyValueCellTemplate";
    public const string ValueEditorCellTemplateKey = "PropertyValueEditorCellTemplate";

    public static HierarchicalTreeDataGridSource<PropertyGridNode> CreateSource()
    {
        var source = new HierarchicalTreeDataGridSource<PropertyGridNode>(Array.Empty<PropertyGridNode>());

        source.Columns.Add(
            new HierarchicalExpanderColumn<PropertyGridNode>(
                new TextColumn<PropertyGridNode, string>("Property", x => x.Name, new AGridLength(2, AGridUnitType.Star)),
                x => x.Children,
                x => x.Children.Count > 0,
                x => x.IsExpanded));

        source.Columns.Add(
            new TemplateColumn<PropertyGridNode>(
                "Value",
                ValueCellTemplateKey,
                ValueEditorCellTemplateKey,
                width: new AGridLength(2, AGridUnitType.Star),
                options: new TemplateColumnOptions<PropertyGridNode>
                {
                    BeginEditGestures = BeginEditGestures.Default | BeginEditGestures.WhenSelected,
                    IsTextSearchEnabled = true,
                    TextSearchValueSelector = x => x.ValueText,
                }));

        source.Columns.Add(new TextColumn<PropertyGridNode, string>("Type", x => x.TypeText, new AGridLength(1, AGridUnitType.Star)));
        source.Columns.Add(new TextColumn<PropertyGridNode, string>("Priority", x => x.PriorityText, new AGridLength(1, AGridUnitType.Star)));
        source.Columns.Add(new TextColumn<PropertyGridNode, string>("Source", x => x.SourceText, new AGridLength(1, AGridUnitType.Star)));

        return source;
    }

    public static TreeDataGridRowSelectionModel<PropertyGridNode> CreateSelection(
        HierarchicalTreeDataGridSource<PropertyGridNode> source,
        Action<PropertyGridNode?> onSelectionChanged)
    {
        var selection = new TreeDataGridRowSelectionModel<PropertyGridNode>(source)
        {
            SingleSelect = true,
        };

        selection.SelectionChanged += (_, _) => onSelectionChanged(selection.SelectedItem);
        source.Selection = selection;
        return selection;
    }

    public static bool TryFindByFullName(
        IEnumerable<PropertyGridNode> roots,
        string fullName,
        out IndexPath path,
        out PropertyGridNode? node)
    {
        var index = 0;
        foreach (var root in roots)
        {
            if (TryFind(root, fullName, new IndexPath(index), out path, out node))
            {
                return true;
            }

            index++;
        }

        path = default;
        node = null;
        return false;
    }

    private static bool TryFind(PropertyGridNode current, string fullName, IndexPath currentPath, out IndexPath path, out PropertyGridNode? node)
    {
        if (string.Equals(current.FullName, fullName, StringComparison.Ordinal))
        {
            path = currentPath;
            node = current;
            return true;
        }

        for (var index = 0; index < current.Children.Count; index++)
        {
            if (TryFind(current.Children[index], fullName, currentPath.Append(index), out path, out node))
            {
                return true;
            }
        }

        path = default;
        node = null;
        return false;
    }
}
