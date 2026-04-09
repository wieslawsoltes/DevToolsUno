# DevToolsUno

[![NuGet](https://img.shields.io/nuget/v/DevToolsUno?logo=nuget)](https://www.nuget.org/packages/DevToolsUno/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/DevToolsUno?logo=nuget)](https://www.nuget.org/packages/DevToolsUno/)
[![CI](https://github.com/wieslawsoltes/DevToolsUno/actions/workflows/ci.yml/badge.svg)](https://github.com/wieslawsoltes/DevToolsUno/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/wieslawsoltes/DevToolsUno?display_name=tag&logo=github)](https://github.com/wieslawsoltes/DevToolsUno/releases)

`DevToolsUno` is an in-app diagnostics package for Uno Platform applications. It attaches directly to your application, window, or root `FrameworkElement` and gives you a dedicated diagnostics surface for inspecting the live UI tree, bindings, resources, styles, events, assets, and memory state without leaving the running app.

<img width="3596" height="1994" alt="image" src="https://github.com/user-attachments/assets/eb8ef241-79fb-4b1f-81f2-cdd3b0694b97" />

## Features

- Logical tree and visual tree inspection with shared property details.
- Runtime property inspection with copy, pin, filtering, and value-source views.
- Binding diagnostics for `Binding`, `ElementName`, `TemplateBinding`, and `x:Bind` scenarios.
- Event listener and event route inspection.
- Resource dictionary and style exploration.
- Asset discovery and preview.
- Memory snapshots and tracked object inspection.
- Built-in screenshots with pluggable save handlers.
- Keyboard-driven inspection flow with configurable launch and action hotkeys.

## Package

Install from NuGet:

```bash
dotnet add package DevToolsUno
```

The package targets `net9.0` and is intended for [Uno Platform](https://github.com/unoplatform) applications built on `Uno.WinUI` 6.5.x.

## Quick Start

Attach DevTools from your app startup and keep the returned `IDisposable` for cleanup:

```csharp
using DevToolsUno;
using DevToolsUno.Diagnostics;
using Microsoft.UI.Xaml;

namespace MyApp;

public sealed partial class App : Application
{
    private IDisposable? _devTools;
    private Window? _window;

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new Window();
        _window.Content = new MainPage();

        _devTools = _window.AttachDevTools(new DevToolsOptions
        {
            LaunchView = DevToolsViewKind.VisualTree,
            ShowAsChildWindow = false,
        });

        _window.Closed += (_, _) =>
        {
            _devTools?.Dispose();
            _devTools = null;
        };

        _window.Activate();
    }
}
```

You can also attach DevTools to:

- `Application`
- `Window`
- `FrameworkElement`

## Configuration

`DevToolsOptions` lets you tune how the diagnostics UI is launched and how it behaves:

```csharp
using DevToolsUno.Diagnostics;
using DevToolsUno.Diagnostics.Screenshots;
using Windows.System;

var options = new DevToolsOptions
{
    Gesture = VirtualKey.F12,
    GestureModifiers = VirtualKeyModifiers.None,
    LaunchView = DevToolsViewKind.VisualTree,
    ShowAsChildWindow = false,
    EnablePointerInspection = true,
    EnableFocusTracking = true,
    ScreenshotHandler = new FileSavePickerScreenshotHandler(),
    HotKeys = new DevToolsHotKeyConfiguration
    {
        InspectHoveredControl = DevToolsHotKeyGesture.ModifiersOnly(
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Shift),
        TogglePopupFreeze = new DevToolsHotKeyGesture(
            VirtualKey.F,
            VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu),
        ScreenshotSelectedControl = new DevToolsHotKeyGesture(
            VirtualKey.F8,
            VirtualKeyModifiers.None),
    },
};
```

If you want screenshots to go directly to a known folder, use `FolderScreenshotHandler`:

```csharp
var screenshotOptions = new DevToolsOptions
{
    ScreenshotHandler = new FolderScreenshotHandler(folder),
};
```

## Views

The diagnostics shell exposes the following primary views:

- `LogicalTree`
- `VisualTree`
- `Events`
- `HotKeys`
- `Resources`
- `Assets`
- `Styles`
- `Bindings`
- `Memory`

## Default Hotkeys

- `F12`: open DevTools.
- `Ctrl+Shift`: inspect the hovered control.
- `Ctrl+Alt+F`: freeze or unfreeze popup inspection.
- `F8`: capture a screenshot of the selected control.

## Sample

A runnable sample host is included at [samples/DevToolsUno.Sample](https://github.com/wieslawsoltes/DevToolsUno/tree/main/samples/DevToolsUno.Sample). It demonstrates:

- Window-level attachment.
- Opening directly to the visual tree.
- Data binding scenarios for the binding inspector.
- Asset inspection content under `Assets/Diagnostics`.

## Development

This repository uses a Git submodule for the Uno TreeDataGrid port. Clone with submodules or initialize them after cloning:

```bash
git clone --recurse-submodules https://github.com/wieslawsoltes/DevToolsUno.git
cd DevToolsUno
git submodule update --init --recursive
```

Build locally:

```bash
dotnet restore DevToolsUno.sln
dotnet build DevToolsUno.sln -c Release
dotnet pack src/DevToolsUno/DevToolsUno.csproj -c Release -o artifacts/packages
```

The sample project is intentionally marked as non-packable. Only `DevToolsUno` is produced as a NuGet package.

## CI And Release

GitHub Actions are configured for:

- `CI`: restore, build, and pack validation on pushes and pull requests.
- `Release`: build, pack, publish the NuGet package, and create a GitHub release from a version tag.

To publish a package:

1. Add the `NUGET_API_KEY` secret in GitHub repository settings.
2. Push a semantic version tag such as `v0.1.0`.
3. Let the `Release` workflow publish the package and attach the artifacts to the GitHub release.

## License

This project is licensed under the [MIT License](https://github.com/wieslawsoltes/DevToolsUno/blob/main/LICENSE).
