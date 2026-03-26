using DevTools.Uno.Diagnostics.Internal;
using DevTools.Uno.Diagnostics.ViewModels;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Uno.UI;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI.Core;
using Windows.System;

namespace DevTools.Uno.Diagnostics;

internal static class DevTools
{
    private static readonly TimeSpan ApplicationAttachScanInterval = TimeSpan.FromMilliseconds(250);
    private static readonly System.Reflection.MethodInfo? MoveInZOrderAtTopMethod =
        typeof(AppWindow).GetMethod("MoveInZOrderAtTop", Type.EmptyTypes);
    private static readonly Dictionary<FrameworkElement, SessionEntry> Sessions = new();

    public static IDisposable Attach(FrameworkElement root, DevToolsOptions options)
    {
        DevToolsThemeManager.EnsureResources();

        if (!Sessions.TryGetValue(root, out var entry))
        {
            entry = new SessionEntry(new DevToolsSession(root, options, () => InvalidateSession(root)));
            Sessions[root] = entry;
        }

        entry.ReferenceCount++;
        return new DisposableAction(() => Release(root, entry));
    }

    public static IDisposable Attach(Window window, DevToolsOptions options)
    {
        if (window.Content is not FrameworkElement root)
        {
            throw new InvalidOperationException("The target window must have a FrameworkElement content root.");
        }

        return Attach(root, options);
    }

    public static IDisposable Attach(Application application, DevToolsOptions options)
    {
        _ = application;
        return new ApplicationAttachment(options);
    }

    private static HashSet<FrameworkElement> GetCurrentRoots()
    {
        var roots = new HashSet<FrameworkElement>(ReferenceEqualityComparer.Instance);

        foreach (var window in ApplicationHelper.Windows)
        {
            if (window.Content is FrameworkElement root)
            {
                roots.Add(root);
            }
        }

        if (Window.Current?.Content is FrameworkElement currentRoot)
        {
            roots.Add(currentRoot);
        }

        return roots;
    }

    private sealed class DevToolsSession : IDisposable
    {
        private readonly FrameworkElement _root;
        private readonly DevToolsOptions _options;
        private readonly OverlayService _overlay;
        private readonly MainViewModel _viewModel;
        private readonly Views.DevToolsRoot _view;
        private readonly Action _invalidated;
        private readonly KeyEventHandler _previewKeyDownHandler;
        private readonly KeyEventHandler _keyUpHandler;
        private readonly PointerEventHandler _pointerMovedHandler;
        private readonly PointerEventHandler _pointerExitedHandler;
        private readonly TypedEventHandler<FrameworkElement, object> _actualThemeChangedHandler;
        private readonly KeyboardAccelerator? _launchAccelerator;
        private readonly HashSet<VirtualKey> _pressedKeys = [];
        private Popup? _popup;
        private Grid? _popupHost;
        private Border? _popupShell;
        private Window? _toolWindow;
        private Border? _toolWindowChrome;
        private XamlRoot? _attachedXamlRoot;
        private VirtualKeyModifiers _activeModifiers;
        private bool _launchGesturePressed;
        private bool _disposed;

        public DevToolsSession(FrameworkElement root, DevToolsOptions options, Action invalidated)
        {
            _root = root;
            _options = options;
            _invalidated = invalidated;
            _overlay = new OverlayService(root, options);
            _viewModel = new MainViewModel(root, _overlay, options, Close);
            _view = new Views.DevToolsRoot
            {
                DataContext = _viewModel,
            };
            _previewKeyDownHandler = OnPreviewKeyDown;
            _keyUpHandler = OnKeyUp;
            _pointerMovedHandler = OnPointerMoved;
            _pointerExitedHandler = OnPointerExited;
            _actualThemeChangedHandler = OnRootActualThemeChanged;

            try
            {
                _launchAccelerator = new KeyboardAccelerator
                {
                    Key = _options.Gesture,
                    Modifiers = _options.GestureModifiers,
                    ScopeOwner = root,
                };
                _launchAccelerator.Invoked += OnLaunchAcceleratorInvoked;
                root.KeyboardAccelerators.Add(_launchAccelerator);
            }
            catch
            {
                _launchAccelerator = null;
            }

            root.AddHandler(UIElement.PreviewKeyDownEvent, _previewKeyDownHandler, true);
            root.AddHandler(UIElement.KeyUpEvent, _keyUpHandler, true);
            root.AddHandler(UIElement.PointerMovedEvent, _pointerMovedHandler, true);
            root.AddHandler(UIElement.PointerExitedEvent, _pointerExitedHandler, true);
            root.ActualThemeChanged += _actualThemeChangedHandler;

            if (options.EnableFocusTracking)
            {
                root.GotFocus += OnGotFocus;
                root.LostFocus += OnLostFocus;
            }

            root.SizeChanged += OnRootSizeChanged;
            root.Unloaded += OnRootUnloaded;

            if (_options.ShowAsChildWindow)
            {
                EnsurePopupHost();
                UpdatePopupLayout();
            }

            SyncXamlRoot();
            ApplyHostTheme();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_popup is not null)
            {
                _popup.IsOpen = false;
            }

            if (_toolWindow is not null)
            {
                _toolWindow.Closed -= OnToolWindowClosed;
                _toolWindow.Close();
                _toolWindow = null;
            }

            if (_launchAccelerator is not null)
            {
                _launchAccelerator.Invoked -= OnLaunchAcceleratorInvoked;
                _root.KeyboardAccelerators.Remove(_launchAccelerator);
            }

            _root.RemoveHandler(UIElement.PreviewKeyDownEvent, _previewKeyDownHandler);
            _root.RemoveHandler(UIElement.KeyUpEvent, _keyUpHandler);
            _root.RemoveHandler(UIElement.PointerMovedEvent, _pointerMovedHandler);
            _root.RemoveHandler(UIElement.PointerExitedEvent, _pointerExitedHandler);
            _root.ActualThemeChanged -= _actualThemeChangedHandler;

            if (_options.EnableFocusTracking)
            {
                _root.GotFocus -= OnGotFocus;
                _root.LostFocus -= OnLostFocus;
            }

            _root.SizeChanged -= OnRootSizeChanged;
            _root.Unloaded -= OnRootUnloaded;

            if (_attachedXamlRoot is not null)
            {
                _attachedXamlRoot.Changed -= OnXamlRootChanged;
                _attachedXamlRoot = null;
            }

            _viewModel.Dispose();
            _overlay.Dispose();
            _invalidated();
        }

        private void OnRootUnloaded(object sender, RoutedEventArgs e)
        {
            Dispose();
        }

        private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
        {
            SyncXamlRoot();
            if (_options.ShowAsChildWindow)
            {
                UpdatePopupLayout();
            }
        }

        private void OnXamlRootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
        {
            if (_options.ShowAsChildWindow)
            {
                UpdatePopupLayout();
            }
        }

        private void OnRootActualThemeChanged(FrameworkElement sender, object args)
        {
            ApplyHostTheme();
        }

        private void OnGotFocus(object sender, RoutedEventArgs e)
        {
            if (_root.XamlRoot is { } xamlRoot)
            {
                _viewModel.UpdateFocused(FocusManager.GetFocusedElement(xamlRoot) as DependencyObject);
            }
        }

        private void OnLostFocus(object sender, RoutedEventArgs e)
        {
            if (_root.XamlRoot is { } xamlRoot)
            {
                _viewModel.UpdateFocused(FocusManager.GetFocusedElement(xamlRoot) as DependencyObject);
            }
        }

        private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            _activeModifiers |= GetModifier(e.Key);
            var isFirstPress = _pressedKeys.Add(e.Key);

            if (isFirstPress && MatchesLaunchGesture(e.Key) && TryBeginLaunchGesture())
            {
                Toggle();
                e.Handled = true;
                return;
            }

            if (isFirstPress && TryHandleInternalHotKey(e.Key))
            {
                e.Handled = true;
            }
        }

        private void OnKeyUp(object sender, KeyRoutedEventArgs e)
        {
            _activeModifiers &= ~GetModifier(e.Key);
            _pressedKeys.Remove(e.Key);
            if (e.Key == _options.Gesture)
            {
                _launchGesturePressed = false;
            }

            if (!IsInspectionGestureActive())
            {
                _viewModel.ClearInspectionTarget();
            }
        }

        private void OnLaunchAcceleratorInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            if (TryBeginLaunchGesture())
            {
                Toggle();
                args.Handled = true;
            }
        }

        private bool MatchesLaunchGesture(VirtualKey key)
            => key == _options.Gesture && GetCurrentModifiers() == _options.GestureModifiers;

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            var candidate = e.OriginalSource as DependencyObject;
            if (candidate is null || _overlay.IsOwnElement(candidate))
            {
                _viewModel.UpdatePointerOver(null);
                if (IsInspectionGestureActive())
                {
                    _viewModel.ClearInspectionTarget();
                }

                return;
            }

            _viewModel.UpdatePointerOver(candidate);

            if (!IsInspectionGestureActive())
            {
                return;
            }

            var wasOpen = IsOpen();
            if (!wasOpen)
            {
                Open();
            }

            _viewModel.UpdateInspectionTarget(candidate, preferVisualTree: true, activatePreferredTree: !wasOpen);
        }

        private void OnPointerExited(object sender, PointerRoutedEventArgs e)
        {
            _viewModel.UpdatePointerOver(null);
            _viewModel.ClearInspectionTarget();
        }

        private void Toggle()
        {
            if (IsOpen())
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        private void Open()
        {
            SyncXamlRoot();
            _viewModel.RefreshAll();
            if (_options.EnableFocusTracking && _root.XamlRoot is { } xamlRoot)
            {
                _viewModel.UpdateFocused(FocusManager.GetFocusedElement(xamlRoot) as DependencyObject);
            }

            ApplyHostTheme();

            if (_options.ShowAsChildWindow || !TryOpenDetachedWindow())
            {
                EnsurePopupHost();
                UpdatePopupLayout();
                _popup!.IsOpen = true;
            }

            _view.InvalidateMeasure();
            _view.InvalidateArrange();
            _view.UpdateLayout();
            _view.Focus(FocusState.Programmatic);
        }

        private void Close()
        {
            if (_popup is not null)
            {
                _popup.IsOpen = false;
            }

            if (_toolWindow is not null)
            {
                _toolWindow.Close();
            }
        }

        private bool IsOpen()
            => (_popup?.IsOpen ?? false) || (_toolWindow?.Visible ?? false);

        private bool IsInspectionGestureActive()
        {
            if (!_options.EnablePointerInspection)
            {
                return false;
            }

            var gesture = _options.HotKeys.InspectHoveredControl;
            if (GetCurrentModifiers() != gesture.Modifiers)
            {
                return false;
            }

            return gesture.Key == VirtualKey.None || _pressedKeys.Contains(gesture.Key);
        }

        private bool TryBeginLaunchGesture()
        {
            if (_launchGesturePressed)
            {
                return false;
            }

            _launchGesturePressed = true;
            return true;
        }

        private bool TryHandleInternalHotKey(VirtualKey key)
        {
            if (MatchesHotKey(key, _options.HotKeys.TogglePopupFreeze))
            {
                _viewModel.TogglePopupFreeze();
                return true;
            }

            if (MatchesHotKey(key, _options.HotKeys.ScreenshotSelectedControl))
            {
                _ = _viewModel.CaptureSelectionAsync();
                return true;
            }

            return false;
        }

        private bool MatchesHotKey(VirtualKey key, DevToolsHotKeyGesture gesture)
            => gesture.Key != VirtualKey.None &&
               key == gesture.Key &&
               GetCurrentModifiers() == gesture.Modifiers;

        private VirtualKeyModifiers GetCurrentModifiers()
        {
            var modifiers = _activeModifiers;
            if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down))
            {
                modifiers |= VirtualKeyModifiers.Control;
            }

            if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down))
            {
                modifiers |= VirtualKeyModifiers.Shift;
            }

            if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu).HasFlag(CoreVirtualKeyStates.Down))
            {
                modifiers |= VirtualKeyModifiers.Menu;
            }

            if (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.LeftWindows).HasFlag(CoreVirtualKeyStates.Down) ||
                InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.RightWindows).HasFlag(CoreVirtualKeyStates.Down))
            {
                modifiers |= VirtualKeyModifiers.Windows;
            }

            return modifiers;
        }

        private void EnsurePopupHost()
        {
            if (_popup is not null)
            {
                _popup.XamlRoot = _attachedXamlRoot;
                ApplyHostTheme();
                return;
            }

            (_popupHost, _popupShell) = CreatePopupChrome(_view);
            _popup = new Popup
            {
                XamlRoot = _attachedXamlRoot,
                IsLightDismissEnabled = false,
                Child = _popupHost,
            };
            ApplyHostTheme();
        }

        private bool TryOpenDetachedWindow()
        {
            if (_toolWindow is null)
            {
                try
                {
                    _toolWindowChrome = CreateDetachedWindowChrome(_view);
                    var window = new Window
                    {
                        Content = _toolWindowChrome,
                        Title = "DevTools.Uno",
                    };

                    window.Closed += OnToolWindowClosed;
                    var width = Math.Max(480, (int)Math.Ceiling(_options.Size.Width));
                    var height = Math.Max(360, (int)Math.Ceiling(_options.Size.Height));
                    window.AppWindow.Resize(new SizeInt32
                    {
                        Width = width,
                        Height = height,
                    });
                    ConfigureDetachedAppWindow(window.AppWindow);
                    _toolWindow = window;
                }
                catch
                {
                    _toolWindow = null;
                    return false;
                }
            }

            ApplyHostTheme();
            _toolWindow.Activate();
            PromoteDetachedAppWindow(_toolWindow.AppWindow);
            return true;
        }

        private void OnToolWindowClosed(object sender, WindowEventArgs e)
        {
            if (_toolWindow is not null)
            {
                _toolWindow.Closed -= OnToolWindowClosed;
                _toolWindow = null;
            }

            _toolWindowChrome = null;
        }

        private void UpdatePopupLayout()
        {
            SyncXamlRoot();
            if (_popupHost is null || _popupShell is null)
            {
                return;
            }

            var viewportWidth = _root.XamlRoot?.Size.Width ?? _root.ActualWidth;
            var viewportHeight = _root.XamlRoot?.Size.Height ?? _root.ActualHeight;
            if (viewportWidth <= 0 || viewportHeight <= 0)
            {
                return;
            }

            const double chromeMargin = 24;
            var availableWidth = Math.Max(0, viewportWidth - (chromeMargin * 2));
            var availableHeight = Math.Max(0, viewportHeight - (chromeMargin * 2));

            _popupHost.Width = viewportWidth;
            _popupHost.Height = viewportHeight;
            _popupShell.MaxWidth = availableWidth;
            _popupShell.MaxHeight = availableHeight;
            _popupShell.Width = Math.Min(_options.Size.Width, availableWidth);
            _popupShell.Height = Math.Min(_options.Size.Height, availableHeight);

            if (_popup?.IsOpen == true)
            {
                _popupHost.InvalidateMeasure();
                _popupHost.InvalidateArrange();
                _popupHost.UpdateLayout();
            }
        }

        private void SyncXamlRoot()
        {
            if (ReferenceEquals(_attachedXamlRoot, _root.XamlRoot))
            {
                return;
            }

            if (_attachedXamlRoot is not null)
            {
                _attachedXamlRoot.Changed -= OnXamlRootChanged;
            }

            _attachedXamlRoot = _root.XamlRoot;
            if (_attachedXamlRoot is not null)
            {
                _attachedXamlRoot.Changed += OnXamlRootChanged;
            }

            if (_popup is not null)
            {
                _popup.XamlRoot = _attachedXamlRoot;
            }
        }

        private void ApplyHostTheme()
        {
            var theme = _root.ActualTheme;

            _view.RequestedTheme = theme;

            if (_popup is not null)
            {
                _popup.RequestedTheme = theme;
            }

            if (_popupHost is not null)
            {
                _popupHost.RequestedTheme = theme;
                _popupHost.Background = DevToolsThemeManager.CreateBackdropBrush(theme);
            }

            if (_popupShell is not null)
            {
                _popupShell.RequestedTheme = theme;
                _popupShell.Background = DevToolsThemeManager.CreateSurfaceBrush(theme);
                _popupShell.BorderBrush = DevToolsThemeManager.CreateBorderBrush(theme);
            }

            if (_toolWindowChrome is not null)
            {
                _toolWindowChrome.RequestedTheme = theme;
                _toolWindowChrome.Background = DevToolsThemeManager.CreateSurfaceBrush(theme);
                _toolWindowChrome.BorderBrush = DevToolsThemeManager.CreateBorderBrush(theme);
            }

            if (_toolWindow?.Content is FrameworkElement content)
            {
                content.RequestedTheme = theme;
            }
        }

        private static (Grid Host, Border Shell) CreatePopupChrome(FrameworkElement content)
        {
            var host = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            var shell = new Border
            {
                MaxWidth = 1600,
                MaxHeight = 1000,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Child = content,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };

            host.Children.Add(shell);
            return (host, shell);
        }

        private static Border CreateDetachedWindowChrome(FrameworkElement content)
        {
            return new Border
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Child = content,
            };
        }

        private static void ConfigureDetachedAppWindow(AppWindow appWindow)
        {
            try
            {
                if (appWindow.Presenter is OverlappedPresenter overlappedPresenter)
                {
                    overlappedPresenter.IsAlwaysOnTop = true;
                }
                else
                {
                    var presenter = OverlappedPresenter.CreateForToolWindow();
                    presenter.IsAlwaysOnTop = true;
                    appWindow.SetPresenter(presenter);
                }

                TryMoveDetachedWindowToTop(appWindow);
            }
            catch
            {
            }
        }

        private static void PromoteDetachedAppWindow(AppWindow appWindow)
        {
            try
            {
                if (appWindow.Presenter is OverlappedPresenter overlappedPresenter)
                {
                    overlappedPresenter.IsAlwaysOnTop = true;
                }

                TryMoveDetachedWindowToTop(appWindow);
            }
            catch
            {
            }
        }

        private static void TryMoveDetachedWindowToTop(AppWindow appWindow)
        {
            try
            {
                MoveInZOrderAtTopMethod?.Invoke(appWindow, null);
            }
            catch
            {
            }
        }

        private static VirtualKeyModifiers GetModifier(VirtualKey key)
        {
            return key switch
            {
                VirtualKey.Control or VirtualKey.LeftControl or VirtualKey.RightControl => VirtualKeyModifiers.Control,
                VirtualKey.Shift or VirtualKey.LeftShift or VirtualKey.RightShift => VirtualKeyModifiers.Shift,
                VirtualKey.Menu or VirtualKey.LeftMenu or VirtualKey.RightMenu => VirtualKeyModifiers.Menu,
                VirtualKey.LeftWindows or VirtualKey.RightWindows => VirtualKeyModifiers.Windows,
                _ => VirtualKeyModifiers.None,
            };
        }
    }

    private sealed class ApplicationAttachment : IDisposable
    {
        private readonly DevToolsOptions _options;
        private readonly DispatcherTimer _timer;
        private readonly Dictionary<FrameworkElement, IDisposable> _attachments = new(ReferenceEqualityComparer.Instance);
        private bool _disposed;

        public ApplicationAttachment(DevToolsOptions options)
        {
            _options = options;
            _timer = new DispatcherTimer
            {
                Interval = ApplicationAttachScanInterval,
            };
            _timer.Tick += OnTick;

            SyncRoots();
            _timer.Start();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Stop();
            _timer.Tick -= OnTick;

            foreach (var attachment in _attachments.Values.ToArray())
            {
                attachment.Dispose();
            }

            _attachments.Clear();
        }

        private void OnTick(object? sender, object e)
        {
            SyncRoots();
        }

        private void SyncRoots()
        {
            var liveRoots = GetCurrentRoots();

            foreach (var root in liveRoots)
            {
                if (!_attachments.ContainsKey(root))
                {
                    _attachments[root] = Attach(root, _options);
                }
            }

            foreach (var root in _attachments.Keys.ToArray())
            {
                if (liveRoots.Contains(root))
                {
                    continue;
                }

                _attachments[root].Dispose();
                _attachments.Remove(root);
            }
        }
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<FrameworkElement>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public bool Equals(FrameworkElement? x, FrameworkElement? y)
            => ReferenceEquals(x, y);

        public int GetHashCode(FrameworkElement obj)
            => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }

    private sealed class SessionEntry(DevToolsSession session)
    {
        public DevToolsSession Session { get; } = session;

        public int ReferenceCount { get; set; }

        public bool Invalidated { get; set; }
    }

    private static void Release(FrameworkElement root, SessionEntry entry)
    {
        if (entry.Invalidated)
        {
            return;
        }

        entry.ReferenceCount = Math.Max(0, entry.ReferenceCount - 1);
        if (entry.ReferenceCount == 0)
        {
            Sessions.Remove(root);
            entry.Invalidated = true;
            entry.Session.Dispose();
        }
    }

    private static void InvalidateSession(FrameworkElement root)
    {
        if (Sessions.Remove(root, out var entry))
        {
            entry.Invalidated = true;
        }
    }
}
