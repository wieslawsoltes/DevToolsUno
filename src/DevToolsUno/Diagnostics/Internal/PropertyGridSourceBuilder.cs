using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using DevToolsUno.Diagnostics.ViewModels;
using IndexPath = Avalonia.Controls.IndexPath;
using AGridLength = Avalonia.GridLength;
using AGridUnitType = Avalonia.GridUnitType;

namespace DevToolsUno.Diagnostics.Internal;

internal static class PropertyGridSourceBuilder
{
    public const string ValueCellTemplateKey = "PropertyValueCellTemplate";
    public const string ValueEditorCellTemplateKey = "PropertyValueEditorCellTemplate";

    public static HierarchicalTreeDataGridSource<PropertyGridNode> CreateSource()
    {
        var source = new HierarchicalTreeDataGridSource<PropertyGridNode>(Array.Empty<PropertyGridNode>());

        source.Columns.Add(
            new HierarchicalExpanderColumn<PropertyGridNode>(
                new TextColumn<PropertyGridNode, string>("Property", x => x.Name, new AGridLength(1.7, AGridUnitType.Star)),
                x => x.Children,
                x => x.Children.Count > 0,
                x => x.IsExpanded));

        source.Columns.Add(
            new TemplateColumn<PropertyGridNode>(
                "Value",
                ValueCellTemplateKey,
                ValueEditorCellTemplateKey,
                width: new AGridLength(2.3, AGridUnitType.Star),
                options: new TemplateColumnOptions<PropertyGridNode>
                {
                    BeginEditGestures = BeginEditGestures.Default | BeginEditGestures.WhenSelected,
                    IsTextSearchEnabled = true,
                    TextSearchValueSelector = x => x.ValueText,
                }));

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

    public static HashSet<string> CaptureExpandedFullNames(IEnumerable<PropertyGridNode> roots)
        => HierarchyExpansionState.CaptureExpandedKeys(
            roots,
            x => x.Children,
            x => string.IsNullOrWhiteSpace(x.FullName) ? null : x.FullName,
            x => x.IsExpanded,
            StringComparer.Ordinal);

    public static void RestoreExpandedFullNames(IEnumerable<PropertyGridNode> roots, ISet<string> expandedFullNames)
        => HierarchyExpansionState.RestoreExpandedKeys(
            roots,
            x => x.Children,
            x => string.IsNullOrWhiteSpace(x.FullName) ? null : x.FullName,
            (node, isExpanded) => node.IsExpanded = isExpanded,
            expandedFullNames);

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
