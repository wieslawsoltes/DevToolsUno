using DevTools.Uno.Diagnostics.Internal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;

namespace DevTools.Uno.Diagnostics.ViewModels;

internal sealed class MainViewModel : ViewModelBase, IDisposable
{
    private readonly FrameworkElement _root;
    private readonly OverlayService _overlay;
    private readonly DevToolsOptions _options;
    private readonly HotKeyPageViewModel _hotKeys;
    private readonly EventsPageViewModel _events;
    private readonly TreePageViewModel _logicalTree;
    private readonly TreePageViewModel _visualTree;
    private readonly ResourcesPageViewModel _resources;
    private readonly AssetsPageViewModel _assets;
    private readonly StylesPageViewModel _styles;
    private readonly BindingsPageViewModel _bindings;
    private readonly MemoryPageViewModel _memory;
    private int _selectedTab;
    private string? _focusedControl;
    private string? _pointerOverElement;
    private string? _treeHoverElement;
    private string? _inspectionTarget;
    private bool _showMarginPadding;
    private bool _showFocusHighlighter;
    private bool _showFps;
    private bool _freezePopups;
    private bool _showClrProperties = true;
    private int _knownPopupCount = -1;
    private bool _isSynchronizingTreeSelection;

    public MainViewModel(FrameworkElement root, OverlayService overlay, DevToolsOptions options, Action closeRequested)
    {
        _root = root;
        _overlay = overlay;
        _options = options;
        var pinned = new HashSet<string>(StringComparer.Ordinal);
        _logicalTree = new TreePageViewModel(this, root, isVisualTree: false, pinned);
        _visualTree = new TreePageViewModel(this, root, isVisualTree: true, pinned);
        _events = new EventsPageViewModel(this, root);
        _hotKeys = new HotKeyPageViewModel();
        _resources = new ResourcesPageViewModel(root, _showClrProperties);
        _assets = new AssetsPageViewModel();
        _styles = new StylesPageViewModel(root, _showClrProperties);
        _bindings = new BindingsPageViewModel(root, _showClrProperties);
        _memory = new MemoryPageViewModel(root, _showClrProperties);
        CloseCommand = new RelayCommand(closeRequested);
        RefreshCommand = new RelayCommand(RefreshAll);
        ToggleMarginPaddingCommand = new RelayCommand(() => ShowMarginPadding = !ShowMarginPadding);
        ToggleFocusHighlightCommand = new RelayCommand(() => ShowFocusHighlighter = !ShowFocusHighlighter);
        ToggleFpsCommand = new RelayCommand(() => ShowFps = !ShowFps);
        ToggleFreezePopupsCommand = new RelayCommand(() => FreezePopups = !FreezePopups);
        ToggleClrPropertiesCommand = new RelayCommand(() =>
        {
            ShowClrProperties = !ShowClrProperties;
            _logicalTree.UpdateIncludeClrProperties(ShowClrProperties);
            _visualTree.UpdateIncludeClrProperties(ShowClrProperties);
            _resources.UpdateIncludeClrProperties(ShowClrProperties);
            _styles.UpdateIncludeClrProperties(ShowClrProperties);
            _bindings.UpdateIncludeClrProperties(ShowClrProperties);
            _memory.UpdateIncludeClrProperties(ShowClrProperties);
        });
        _root.LayoutUpdated += OnRootLayoutUpdated;
        _hotKeys.Refresh(root, options);
        RefreshPopupState();
        SelectedTab = (int)options.LaunchView;
    }

    public TreePageViewModel LogicalTree => _logicalTree;

    public TreePageViewModel VisualTree => _visualTree;

    public EventsPageViewModel Events => _events;

    public HotKeyPageViewModel HotKeys => _hotKeys;

    public ResourcesPageViewModel Resources => _resources;

    public AssetsPageViewModel Assets => _assets;

    public StylesPageViewModel Styles => _styles;

    public BindingsPageViewModel Bindings => _bindings;

    public MemoryPageViewModel Memory => _memory;

    public RelayCommand CloseCommand { get; }

    public RelayCommand RefreshCommand { get; }

    public RelayCommand ToggleMarginPaddingCommand { get; }

    public RelayCommand ToggleFocusHighlightCommand { get; }

    public RelayCommand ToggleFpsCommand { get; }

    public RelayCommand ToggleFreezePopupsCommand { get; }

    public RelayCommand ToggleClrPropertiesCommand { get; }

    public string? FocusedControl
    {
        get => _focusedControl;
        private set => RaiseAndSetIfChanged(ref _focusedControl, value);
    }

    public string? PointerOverElement
    {
        get => _pointerOverElement;
        private set => RaiseAndSetIfChanged(ref _pointerOverElement, value);
    }

    public string? InspectionTarget
    {
        get => _inspectionTarget;
        private set => RaiseAndSetIfChanged(ref _inspectionTarget, value);
    }

    public string? TreeHoverElement
    {
        get => _treeHoverElement;
        private set => RaiseAndSetIfChanged(ref _treeHoverElement, value);
    }

    public bool ShowMarginPadding
    {
        get => _showMarginPadding;
        set
        {
            if (RaiseAndSetIfChanged(ref _showMarginPadding, value))
            {
                _overlay.ShowMarginPadding = value;
            }
        }
    }

    public bool ShowFocusHighlighter
    {
        get => _showFocusHighlighter;
        set
        {
            if (RaiseAndSetIfChanged(ref _showFocusHighlighter, value))
            {
                _overlay.ShowFocus = value;
            }
        }
    }

    public bool ShowFps
    {
        get => _showFps;
        set
        {
            if (RaiseAndSetIfChanged(ref _showFps, value))
            {
                _overlay.ShowFps = value;
            }
        }
    }

    public bool FreezePopups
    {
        get => _freezePopups;
        set
        {
            if (RaiseAndSetIfChanged(ref _freezePopups, value))
            {
                ApplyPopupFreeze(value);
            }
        }
    }

    public bool ShowClrProperties
    {
        get => _showClrProperties;
        private set => RaiseAndSetIfChanged(ref _showClrProperties, value);
    }

    public int SelectedTab
    {
        get => _selectedTab;
        set => RaiseAndSetIfChanged(ref _selectedTab, value);
    }

    public async Task CaptureSelectionAsync()
    {
        var element = CurrentTree?.SelectedNode?.Element as FrameworkElement;
        if (element is null)
        {
            return;
        }

        try
        {
            var folder = _options.ScreenshotFolder ?? ApplicationData.Current.TemporaryFolder;
            var file = await folder.CreateFileAsync($"devtools-{DateTime.Now:yyyyMMdd-HHmmssfff}.png", CreationCollisionOption.GenerateUniqueName);
            var renderer = new Microsoft.UI.Xaml.Media.Imaging.RenderTargetBitmap();
            await renderer.RenderAsync(element);
            if (renderer.PixelWidth <= 0 || renderer.PixelHeight <= 0)
            {
                return;
            }

            var pixels = await renderer.GetPixelsAsync();

            using var stream = await file.OpenAsync(FileAccessMode.ReadWrite);
            var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(Windows.Graphics.Imaging.SoftwareBitmap.CreateCopyFromBuffer(
                pixels,
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                renderer.PixelWidth,
                renderer.PixelHeight));
            await encoder.FlushAsync();
        }
        catch
        {
        }
    }

    public void UpdatePointerOver(DependencyObject? element)
    {
        PointerOverElement = FormatElementSummary(element);
    }

    public void UpdateFocused(DependencyObject? element)
    {
        FocusedControl = FormatElementSummary(element);
        _overlay.SetFocusedElement(element);
    }

    public void UpdateInspectionTarget(DependencyObject? element, bool preferVisualTree, bool activatePreferredTree)
    {
        var resolved = ResolveInspectableElement(element);
        InspectionTarget = resolved is not null ? InspectableNode.BuildSelector(resolved) : null;
        _overlay.SetInspectionTarget(resolved);

        if (resolved is not null)
        {
            SelectElement(resolved, preferVisualTree, activatePreferredTree);
        }
    }

    public void ClearInspectionTarget()
    {
        InspectionTarget = null;
        _overlay.SetInspectionTarget(null);
    }

    public void UpdateTreeHover(TreePageViewModel source, DependencyObject? element)
    {
        var resolved = ResolveInspectableElement(element);
        TreeHoverElement = resolved is null
            ? null
            : $"{(source.IsVisualTree ? "Visual" : "Logical")} Tree: {FormatElementSummary(resolved)}";
        _overlay.SetTreeHoverTarget(
            resolved,
            resolved is null
                ? null
                : $"{(source.IsVisualTree ? "Visual" : "Logical")} Tree: {InspectableNode.BuildSelector(resolved)}");
    }

    public void ClearTreeHover()
    {
        TreeHoverElement = null;
        _overlay.SetTreeHoverTarget(null, null);
    }

    public void OnTreeSelectionChanged(TreePageViewModel source, DependencyObject? element)
    {
        _overlay.SetSelection(element);
        _resources.UpdateInspectionTarget(element);
        _styles.UpdateInspectionTarget(element);
        _bindings.UpdateInspectionTarget(element);
        _memory.UpdateInspectionTarget(element);

        if (_isSynchronizingTreeSelection || element is null)
        {
            return;
        }

        var counterpart = ReferenceEquals(source, _visualTree) ? _logicalTree : _visualTree;
        _isSynchronizingTreeSelection = true;
        try
        {
            counterpart.SelectElement(element, activateTab: false, notifyMainView: false, refreshIfMissing: true);
        }
        finally
        {
            _isSynchronizingTreeSelection = false;
        }
    }

    public bool SelectElement(DependencyObject? element, bool preferVisualTree, bool activatePreferredTree = true)
    {
        var resolved = ResolveInspectableElement(element);
        if (resolved is null)
        {
            return false;
        }

        TreePageViewModel? primarySource = null;
        var selectedAny = false;
        _isSynchronizingTreeSelection = true;
        try
        {
            if (preferVisualTree)
            {
                var visualSelected = _visualTree.SelectElement(resolved, activateTab: activatePreferredTree, notifyMainView: false, refreshIfMissing: true);
                var logicalSelected = _logicalTree.SelectElement(resolved, activateTab: false, notifyMainView: false, refreshIfMissing: true);
                primarySource = visualSelected || !logicalSelected ? _visualTree : _logicalTree;
                selectedAny = visualSelected || logicalSelected;
            }
            else
            {
                var logicalSelected = _logicalTree.SelectElement(resolved, activateTab: activatePreferredTree, notifyMainView: false, refreshIfMissing: true);
                var visualSelected = _visualTree.SelectElement(resolved, activateTab: false, notifyMainView: false, refreshIfMissing: true);
                primarySource = logicalSelected || !visualSelected ? _logicalTree : _visualTree;
                selectedAny = logicalSelected || visualSelected;
            }
        }
        finally
        {
            _isSynchronizingTreeSelection = false;
        }

        if (selectedAny && primarySource is not null)
        {
            OnTreeSelectionChanged(primarySource, resolved);
        }

        return selectedAny;
    }

    public void RefreshAll()
    {
        _logicalTree.Refresh();
        _visualTree.Refresh();
        _events.Refresh();
        _hotKeys.Refresh(_root, _options);
        _resources.Refresh();
        _assets.Refresh();
        _styles.Refresh();
        _bindings.Refresh();
        _memory.Refresh();
        RefreshCurrentDetails();
        _styles.RefreshDetails();
        _bindings.RefreshDetails();
        _memory.RefreshDetails();
    }

    public void Dispose()
    {
        _root.LayoutUpdated -= OnRootLayoutUpdated;
        _logicalTree.Dispose();
        _visualTree.Dispose();
        _events.Dispose();
        _memory.Dispose();
    }

    private TreePageViewModel? CurrentTree => SelectedTab switch
    {
        1 => _visualTree,
        0 => _logicalTree,
        _ => null,
    };

    private void RefreshCurrentDetails() => CurrentTree?.Details?.Refresh();

    private void OnRootLayoutUpdated(object? sender, object e)
    {
        RefreshPopupState();
        _logicalTree.RequestAutoRefresh();
        _visualTree.RequestAutoRefresh();
    }

    private void RefreshPopupState()
    {
        var popupCount = _root.XamlRoot is { } xamlRoot
            ? VisualTreeHelper.GetOpenPopupsForXamlRoot(xamlRoot).Count
            : 0;

        if (popupCount == _knownPopupCount)
        {
            return;
        }

        _knownPopupCount = popupCount;
        if (FreezePopups)
        {
            ApplyPopupFreeze(true);
        }

        _hotKeys.Refresh(_root, _options);
    }

    private void ApplyPopupFreeze(bool freeze)
    {
        if (_root.XamlRoot is null)
        {
            return;
        }

        foreach (var popup in Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(_root.XamlRoot))
        {
            popup.IsLightDismissEnabled = !freeze;
        }
    }

    private static string? FormatElementSummary(DependencyObject? element)
    {
        if (element is null)
        {
            return null;
        }

        if (element is FrameworkElement frameworkElement)
        {
            return string.IsNullOrWhiteSpace(frameworkElement.Name)
                ? element.GetType().Name
                : $"{element.GetType().Name} #{frameworkElement.Name}";
        }

        return element.GetType().Name;
    }

    private DependencyObject? ResolveInspectableElement(DependencyObject? element)
    {
        for (var current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (_visualTree.ContainsElement(current) || _logicalTree.ContainsElement(current))
            {
                return current;
            }
        }

        return null;
    }
}
