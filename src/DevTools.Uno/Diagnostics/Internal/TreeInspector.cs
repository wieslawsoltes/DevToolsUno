using System.Linq;
using System.Runtime.CompilerServices;
using DevTools.Uno.Diagnostics.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace DevTools.Uno.Diagnostics.Internal;

internal static class TreeInspector
{
    public static IReadOnlyList<InspectableNode> BuildVisualTree(FrameworkElement root)
    {
        var top = new InspectableNode(root, null, isVisualTree: true)
        {
            IsExpanded = true,
        };

        var attachedPopups = new HashSet<DependencyObject>();
        PopulateVisualChildren(top, root, attachedPopups);

        foreach (var popup in GetOpenPopups(root.XamlRoot))
        {
            if (popup.Child is not DependencyObject popupChild || !attachedPopups.Add(popupChild))
            {
                continue;
            }

            var node = CreateVisualPopupNode(popup, popupChild, top);
            top.Children.Add(node);
            PopulateVisualChildren(node, popupChild, attachedPopups);
        }

        return [top];
    }

    public static IReadOnlyList<InspectableNode> BuildLogicalTree(FrameworkElement root)
    {
        var top = new InspectableNode(root, null, isVisualTree: false)
        {
            IsExpanded = true,
        };

        var visited = new HashSet<DependencyObject>(System.Collections.Generic.ReferenceEqualityComparer.Instance)
        {
            root,
        };
        PopulateLogicalChildren(top, root, visited, isRoot: true);
        return [top];
    }

    public static int GetVisualTreeSignature(FrameworkElement root)
    {
        var hash = new HashCode();
        var attachedPopups = new HashSet<DependencyObject>(System.Collections.Generic.ReferenceEqualityComparer.Instance);

        AccumulateVisualSignature(ref hash, root, attachedPopups);

        foreach (var popupChild in GetOpenPopupChildren(root.XamlRoot))
        {
            if (attachedPopups.Add(popupChild))
            {
                AccumulateVisualSignature(ref hash, popupChild, attachedPopups);
            }
        }

        return hash.ToHashCode();
    }

    public static int GetLogicalTreeSignature(FrameworkElement root)
    {
        var hash = new HashCode();
        var visited = new HashSet<DependencyObject>(System.Collections.Generic.ReferenceEqualityComparer.Instance);
        AccumulateLogicalSignature(ref hash, root, isRoot: true, visited);
        return hash.ToHashCode();
    }

    public static IReadOnlyList<Popup> GetOpenPopups(XamlRoot? xamlRoot)
    {
        if (xamlRoot is null)
        {
            return [];
        }

        return VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).ToArray();
    }

    public static IReadOnlyList<DependencyObject> GetOpenPopupChildren(XamlRoot? xamlRoot)
        => GetOpenPopups(xamlRoot)
            .Select(x => x.Child)
            .Where(x => x is not null)
            .Cast<DependencyObject>()
            .ToArray();

    internal static IReadOnlyList<DependencyObject> GetLogicalChildren(DependencyObject element, bool isRoot)
    {
        var result = new List<DependencyObject>();
        var seen = new HashSet<DependencyObject>(System.Collections.Generic.ReferenceEqualityComparer.Instance);

        void Add(object? value)
        {
            switch (value)
            {
                case null:
                case string:
                    return;
                case DependencyObject dependencyObject when seen.Add(dependencyObject):
                    result.Add(dependencyObject);
                    break;
                case System.Collections.IEnumerable enumerable:
                    foreach (var item in enumerable)
                    {
                        Add(item);
                    }

                    break;
            }
        }

        switch (element)
        {
            case Panel panel:
                Add(panel.Children);
                break;
            case Border border:
                Add(border.Child);
                break;
            case ContentControl contentControl:
                Add(contentControl.Content);
                break;
            case ContentPresenter presenter:
                Add(presenter.Content);
                break;
            case ItemsControl itemsControl:
                Add(itemsControl.ItemsPanelRoot);
                break;
            case Popup popup:
                Add(popup.Child);
                break;
        }

        if (element is FrameworkElement fe)
        {
            Add(GetPropertyValue(fe, "Header"));
            Add(GetPropertyValue(fe, "Footer"));
        }

        foreach (var surface in PopupSurfaceInspector.GetAttachedSurfaces(element))
        {
            Add(surface.Surface);
        }

        if (result.Count == 0)
        {
            var visualCount = VisualTreeHelper.GetChildrenCount(element);
            for (var index = 0; index < visualCount; index++)
            {
                Add(VisualTreeHelper.GetChild(element, index));
            }
        }

        return result;
    }

    private static void PopulateVisualChildren(InspectableNode parent, DependencyObject element, ISet<DependencyObject> attachedPopups)
    {
        var count = VisualTreeHelper.GetChildrenCount(element);

        for (var index = 0; index < count; index++)
        {
            if (VisualTreeHelper.GetChild(element, index) is not DependencyObject child)
            {
                continue;
            }

            var node = new InspectableNode(child, parent, isVisualTree: true);
            parent.Children.Add(node);
            PopulateVisualChildren(node, child, attachedPopups);
        }

        if (element is FrameworkElement fe && fe.XamlRoot is not null)
        {
            foreach (var popup in GetOpenPopups(fe.XamlRoot))
            {
                if (popup.Child is not DependencyObject popupChild ||
                    (!ReferenceEquals(popup.Parent, element) && !ReferenceEquals(popup.PlacementTarget, element)) ||
                    !attachedPopups.Add(popupChild))
                {
                    continue;
                }

                var node = CreateVisualPopupNode(popup, popupChild, parent);
                parent.Children.Add(node);
                PopulateVisualChildren(node, popupChild, attachedPopups);
            }
        }
    }

    private static void PopulateLogicalChildren(InspectableNode parent, DependencyObject element, ISet<DependencyObject> visited, bool isRoot = false)
    {
        foreach (var child in GetLogicalChildren(element, isRoot))
        {
            if (!visited.Add(child))
            {
                continue;
            }

            var node = CreateLogicalNode(element, child, parent);
            parent.Children.Add(node);
            PopulateLogicalChildren(node, child, visited);
        }
    }

    private static void AccumulateVisualSignature(ref HashCode hash, DependencyObject element, ISet<DependencyObject> attachedPopups)
    {
        hash.Add(RuntimeHelpers.GetHashCode(element));

        var count = VisualTreeHelper.GetChildrenCount(element);
        hash.Add(count);

        for (var index = 0; index < count; index++)
        {
            if (VisualTreeHelper.GetChild(element, index) is DependencyObject child)
            {
                AccumulateVisualSignature(ref hash, child, attachedPopups);
            }
        }

        if (element is FrameworkElement fe && fe.XamlRoot is not null)
        {
            foreach (var popup in GetOpenPopups(fe.XamlRoot))
            {
                if (popup.Child is null ||
                    (!ReferenceEquals(popup.Parent, element) && !ReferenceEquals(popup.PlacementTarget, element)) ||
                    !attachedPopups.Add(popup.Child))
                {
                    continue;
                }

                AccumulateVisualSignature(ref hash, popup.Child, attachedPopups);
            }
        }
    }

    private static void AccumulateLogicalSignature(ref HashCode hash, DependencyObject element, bool isRoot, ISet<DependencyObject> visited)
    {
        if (!visited.Add(element))
        {
            hash.Add(RuntimeHelpers.GetHashCode(element));
            return;
        }

        hash.Add(RuntimeHelpers.GetHashCode(element));

        var children = GetLogicalChildren(element, isRoot);
        hash.Add(children.Count);
        foreach (var surface in PopupSurfaceInspector.GetAttachedSurfaces(element))
        {
            hash.Add(surface.Label);
            hash.Add(RuntimeHelpers.GetHashCode(surface.Surface));
            hash.Add(surface.IsOpen);
        }

        foreach (var child in children)
        {
            AccumulateLogicalSignature(ref hash, child, isRoot: false, visited);
        }
    }

    private static object? GetPropertyValue(object instance, string name)
        => instance.GetType().GetProperty(name)?.GetValue(instance);

    private static InspectableNode CreateLogicalNode(DependencyObject owner, DependencyObject child, InspectableNode parent)
    {
        PopupSurfaceInspector.TryDescribeAttachedSurface(owner, child, out var displayTypeName, out var displayQualifier);
        return new InspectableNode(child, parent, isVisualTree: false, displayTypeName, displayQualifier);
    }

    private static InspectableNode CreateVisualPopupNode(Popup popup, DependencyObject child, InspectableNode parent)
    {
        PopupSurfaceInspector.TryDescribePopupChild(popup, child, out var displayTypeName, out var displayQualifier);
        return new InspectableNode(child, parent, isVisualTree: true, displayTypeName, displayQualifier);
    }
}
