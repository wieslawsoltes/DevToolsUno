using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace DevTools.Uno.Diagnostics.Internal;

internal readonly record struct PopupSurfaceInfo(string Label, DependencyObject Surface, string RuntimeTypeName, bool IsOpen);

internal static class PopupSurfaceInspector
{
    private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy;
    private static readonly PropertyInfo? PopupAssociatedFlyoutProperty =
        typeof(Popup).GetProperty("AssociatedFlyout", InstanceFlags);

    public static IReadOnlyList<PopupSurfaceInfo> GetAttachedSurfaces(DependencyObject element)
    {
        var result = new List<PopupSurfaceInfo>();
        var seen = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);

        void Add(string label, object? value)
        {
            if (value is null or string)
            {
                return;
            }

            if (value is DependencyObject dependencyObject && seen.Add(dependencyObject))
            {
                result.Add(CreateInfo(label, dependencyObject));
            }
        }

        if (element is FrameworkElement frameworkElement)
        {
            Add("ToolTip", ToolTipService.GetToolTip(frameworkElement));
            Add("AttachedFlyout", FlyoutBase.GetAttachedFlyout(frameworkElement));
        }

        AddLocalPropertySurface(element, "ContextFlyout", "ContextFlyout", Add);
        AddLocalPropertySurface(element, "Flyout", "Flyout", Add);
        AddLocalPropertySurface(element, "SelectionFlyout", "SelectionFlyout", Add);
        AddLocalPropertySurface(element, "ProofingMenuFlyout", "ProofingMenuFlyout", Add);

        return result;
    }

    public static string BuildSurfaceSummary(DependencyObject element)
    {
        var surfaces = GetAttachedSurfaces(element);
        if (surfaces.Count == 0)
        {
            return "(none)";
        }

        return string.Join(
            " | ",
            surfaces.Select(surface =>
            {
                var qualifier = GetDisplayQualifier(surface.Label, surface.RuntimeTypeName);
                return qualifier is null
                    ? $"{surface.Label} ({(surface.IsOpen ? "Open" : "Closed")})"
                    : $"{surface.Label} ({qualifier}, {(surface.IsOpen ? "Open" : "Closed")})";
            }));
    }

    public static bool TryDescribeAttachedSurface(
        DependencyObject owner,
        DependencyObject surface,
        out string? displayTypeName,
        out string? displayQualifier)
    {
        foreach (var candidate in GetAttachedSurfaces(owner))
        {
            if (ReferenceEquals(candidate.Surface, surface))
            {
                displayTypeName = candidate.Label;
                displayQualifier = GetDisplayQualifier(candidate.Label, candidate.RuntimeTypeName);
                return true;
            }
        }

        displayTypeName = null;
        displayQualifier = null;
        return false;
    }

    public static bool TryDescribePopupChild(
        Popup popup,
        DependencyObject child,
        out string? displayTypeName,
        out string? displayQualifier)
    {
        var host = popup.PlacementTarget ?? popup.Parent as DependencyObject;
        if (host is not null && TryMatchPopupSurface(popup, host, out var surface))
        {
            displayTypeName = surface.Label;
            displayQualifier = GetDisplayQualifier(surface.Label, child.GetType().Name);
            return true;
        }

        if (child is ToolTip)
        {
            displayTypeName = "ToolTip";
            displayQualifier = null;
            return true;
        }

        displayTypeName = null;
        displayQualifier = null;
        return false;
    }

    private static bool TryMatchPopupSurface(Popup popup, DependencyObject host, out PopupSurfaceInfo surface)
    {
        if (PopupAssociatedFlyoutProperty?.GetValue(popup) is DependencyObject associatedSurface)
        {
            foreach (var candidate in GetAttachedSurfaces(host))
            {
                if (ReferenceEquals(candidate.Surface, associatedSurface))
                {
                    surface = candidate;
                    return true;
                }
            }
        }

        if (popup.Child is { } child)
        {
            foreach (var candidate in GetAttachedSurfaces(host))
            {
                if (candidate.Surface is ToolTip toolTip &&
                    toolTip.IsOpen &&
                    (ReferenceEquals(toolTip, child) || Contains(toolTip, child)))
                {
                    surface = candidate;
                    return true;
                }
            }
        }

        surface = default;
        return false;
    }

    private static PopupSurfaceInfo CreateInfo(string label, DependencyObject surface)
        => new(label, surface, surface.GetType().Name, GetIsOpen(surface));

    private static bool GetIsOpen(DependencyObject surface)
        => surface switch
        {
            FlyoutBase flyout => flyout.IsOpen,
            ToolTip toolTip => toolTip.IsOpen,
            Popup popup => popup.IsOpen,
            _ => false,
        };

    private static void AddLocalPropertySurface(
        DependencyObject element,
        string propertyName,
        string label,
        Action<string, object?> add)
    {
        var dependencyProperty = FindDependencyProperty(element.GetType(), propertyName);
        if (dependencyProperty is null ||
            element.ReadLocalValue(dependencyProperty) == DependencyProperty.UnsetValue)
        {
            return;
        }

        var property = FindInstanceProperty(element.GetType(), propertyName);
        add(label, property?.GetValue(element));
    }

    private static DependencyProperty? FindDependencyProperty(Type type, string propertyName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetProperty($"{propertyName}Property", StaticFlags)?.GetValue(null) is DependencyProperty property)
            {
                return property;
            }

            if (current.GetField($"{propertyName}Property", StaticFlags)?.GetValue(null) is DependencyProperty field)
            {
                return field;
            }
        }

        return null;
    }

    private static PropertyInfo? FindInstanceProperty(Type type, string propertyName)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetProperty(propertyName, InstanceFlags) is { } property)
            {
                return property;
            }
        }

        return null;
    }

    private static string? GetDisplayQualifier(string label, string runtimeTypeName)
        => string.Equals(label, runtimeTypeName, StringComparison.Ordinal) ? null : runtimeTypeName;

    private static bool Contains(DependencyObject root, DependencyObject target)
    {
        if (ReferenceEquals(root, target))
        {
            return true;
        }

        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            if (VisualTreeHelper.GetChild(root, index) is { } child && Contains(child, target))
            {
                return true;
            }
        }

        return false;
    }
}
