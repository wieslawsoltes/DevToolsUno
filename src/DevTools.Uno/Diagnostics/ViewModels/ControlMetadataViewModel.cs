using DevTools.Uno.Diagnostics.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace DevTools.Uno.Diagnostics.ViewModels;

internal sealed class ControlMetadataViewModel : ViewModelBase
{
    private string _selector = string.Empty;
    private string _state = string.Empty;
    private string _dataContext = string.Empty;
    private string _automationName = string.Empty;
    private string _automationId = string.Empty;
    private string _toolTip = string.Empty;
    private string _interaction = string.Empty;
    private string _xamlRoot = string.Empty;
    private string _popupSurfaces = string.Empty;
    private string _popupState = string.Empty;

    public string Selector
    {
        get => _selector;
        private set => RaiseAndSetIfChanged(ref _selector, value);
    }

    public string State
    {
        get => _state;
        private set => RaiseAndSetIfChanged(ref _state, value);
    }

    public string DataContext
    {
        get => _dataContext;
        private set => RaiseAndSetIfChanged(ref _dataContext, value);
    }

    public string AutomationName
    {
        get => _automationName;
        private set => RaiseAndSetIfChanged(ref _automationName, value);
    }

    public string AutomationId
    {
        get => _automationId;
        private set => RaiseAndSetIfChanged(ref _automationId, value);
    }

    public string ToolTip
    {
        get => _toolTip;
        private set => RaiseAndSetIfChanged(ref _toolTip, value);
    }

    public string Interaction
    {
        get => _interaction;
        private set => RaiseAndSetIfChanged(ref _interaction, value);
    }

    public string XamlRoot
    {
        get => _xamlRoot;
        private set => RaiseAndSetIfChanged(ref _xamlRoot, value);
    }

    public string PopupSurfaces
    {
        get => _popupSurfaces;
        private set => RaiseAndSetIfChanged(ref _popupSurfaces, value);
    }

    public string PopupState
    {
        get => _popupState;
        private set => RaiseAndSetIfChanged(ref _popupState, value);
    }

    public void Update(DependencyObject? element)
    {
        if (element is null)
        {
            Selector = string.Empty;
            State = string.Empty;
            DataContext = string.Empty;
            AutomationName = string.Empty;
            AutomationId = string.Empty;
            ToolTip = string.Empty;
            Interaction = string.Empty;
            XamlRoot = string.Empty;
            PopupSurfaces = string.Empty;
            PopupState = string.Empty;
            return;
        }

        Selector = InspectableNode.BuildSelector(element);
        State = InspectableNode.BuildStateSummary(element);
        PopupSurfaces = PopupSurfaceInspector.BuildSurfaceSummary(element);
        PopupState = FindOwningPopup(element) is { } popup ? BuildPopupState(popup) : "(none)";

        if (element is FrameworkElement fe)
        {
            DataContext = DescribeValue(fe.DataContext);
            AutomationName = DescribeString(AutomationProperties.GetName(fe));
            AutomationId = DescribeString(AutomationProperties.GetAutomationId(fe));
            ToolTip = DescribeToolTip(ToolTipService.GetToolTip(fe));
            XamlRoot = fe.XamlRoot is { } xamlRoot
                ? $"{xamlRoot.Size.Width:0.#} x {xamlRoot.Size.Height:0.#} @ {xamlRoot.RasterizationScale:0.##}x"
                : "(none)";
            Interaction = BuildInteraction(fe);
        }
        else
        {
            DataContext = "(n/a)";
            AutomationName = "(n/a)";
            AutomationId = "(n/a)";
            ToolTip = "(n/a)";
            XamlRoot = "(n/a)";
            Interaction = "(n/a)";
        }
    }

    private static string BuildInteraction(FrameworkElement element)
    {
        var parts = new List<string>();

        if (element is UIElement ui)
        {
            parts.Add(ui.IsHitTestVisible ? "HitTest" : "NoHitTest");
        }

        if (element is Control control)
        {
            parts.Add(control.IsEnabled ? "Enabled" : "Disabled");
            parts.Add(control.IsTabStop ? "TabStop" : "NoTabStop");
            parts.Add($"Focus:{control.FocusState}");
        }

        return parts.Count > 0 ? string.Join(" | ", parts) : "(none)";
    }

    private static string BuildPopupState(Popup popup)
    {
        var child = popup.Child is null ? "(none)" : popup.Child.GetType().Name;
        return $"Open:{popup.IsOpen} | Offset:{popup.HorizontalOffset:0.#},{popup.VerticalOffset:0.#} | LightDismiss:{popup.IsLightDismissEnabled} | Child:{child}";
    }

    private static Popup? FindOwningPopup(DependencyObject element)
    {
        if (element is Popup popup)
        {
            return popup;
        }

        var xamlRoot = element switch
        {
            FrameworkElement fe => fe.XamlRoot,
            UIElement ui => ui.XamlRoot,
            _ => null,
        };

        if (xamlRoot is null)
        {
            return null;
        }

        foreach (var openPopup in VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot))
        {
            if (openPopup.Child is not null && Contains(openPopup.Child, element))
            {
                return openPopup;
            }
        }

        return null;
    }

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

    private static string DescribeToolTip(object? value)
    {
        return value switch
        {
            null => "(none)",
            ToolTip toolTip when toolTip.Content is not null => DescribeValue(toolTip.Content),
            ToolTip _ => "ToolTip",
            _ => DescribeValue(value),
        };
    }

    private static string DescribeValue(object? value)
    {
        return value switch
        {
            null => "(null)",
            string text when string.IsNullOrEmpty(text) => "\"\"",
            string text => text,
            DependencyObject dependencyObject => dependencyObject.GetType().FullName ?? dependencyObject.GetType().Name,
            _ when string.Equals(value.ToString(), value.GetType().FullName, StringComparison.Ordinal) => value.GetType().FullName ?? value.GetType().Name,
            _ => $"{value.GetType().FullName}: {value}",
        };
    }

    private static string DescribeString(string? value)
        => string.IsNullOrWhiteSpace(value) ? "(none)" : value;
}
