# AdvCamera

[![Build](https://github.com/kirilnedev/AdvCamera/actions/workflows/build.yml/badge.svg)](https://github.com/kirilnedev/AdvCamera/actions/workflows/build.yml)
[![Latest release](https://img.shields.io/github/v/release/kirilnedev/AdvCamera)](https://github.com/kirilnedev/AdvCamera/releases/latest)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

A camera viewer for Windows, similar to the built-in Camera app but with the controls you need for
**USB microscopes**, document cameras and inspection setups: mirroring, rotation, live image adjustments,
camera hardware controls, zoom, crosshair/grid overlays, snapshots and video recording.

Built with .NET 10 and WPF. Camera capture and image processing use [OpenCvSharp](https://github.com/shimat/opencvsharp).

<!-- Add a screenshot here once you have one, e.g.: ![AdvCamera](docs/images/screenshot.png) -->

## Features

- **Orientation**: mirror horizontally (X) and/or vertically (Y), rotate 0° / 90° / 180° / 270°
- **Image adjustments** (side panel that can be shown or hidden): brightness, contrast, gamma, saturation,
  sharpness, color temperature, tint, noise reduction (frame averaging), grayscale, invert
- **Camera hardware controls**: exposure, focus, gain, white balance, zoom and others, with *Auto* toggles
  where supported, plus the driver's own settings dialog
- **Resolution / format picker**: lists every resolution, pixel format (MJPG, YUY2, …) and frame rate the camera offers
- **Zoom and pan**: mouse wheel to zoom (up to 40×), drag to pan, double-click to fit; pixels stay sharp at high zoom
- **Overlays**: crosshair and grid in several colors
- **Snapshots** (PNG or JPEG), **copy to clipboard**, **video recording** (Motion-JPEG `.avi`)
- **Freeze** the live image to examine or save it
- Full-screen mode, keyboard shortcuts, settings remembered between sessions

## Download

Get the latest `AdvCamera-win-x64.zip` from the [Releases](https://github.com/kirilnedev/AdvCamera/releases/latest) page, unzip it anywhere and run
`AdvCamera.exe`. Nothing needs to be installed: the release includes the .NET runtime.

Requirements: Windows 10 (version 1809 or later) or Windows 11, 64-bit.

## Usage

See the [User guide](docs/USER_GUIDE.md) for details. Main keyboard shortcuts:

| Key | Action |
| --- | --- |
| `R` / `Shift+R` | Rotate 90° clockwise / counter-clockwise |
| `H` / `V` | Mirror X / mirror Y |
| `S` or `F12` | Save snapshot |
| `Ctrl+C` | Copy image to clipboard |
| `Ctrl+R` | Start / stop recording |
| `Space` | Freeze / resume |
| `C` / `G` | Crosshair / grid |
| `+` / `-` / `0` | Zoom in / out / fit |
| `P` | Show / hide the adjustments panel |
| `F11` / `Esc` | Full screen / leave full screen |

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). Visual Studio 2026,
VS Code with C# Dev Kit, or JetBrains Rider all work.

```powershell
git clone https://github.com/kirilnedev/AdvCamera.git
cd AdvCamera
dotnet run --project src/AdvCamera
```

To build a standalone copy that runs without installing .NET:

```powershell
dotnet publish src/AdvCamera -c Release -r win-x64 --self-contained -o publish
```

## Project layout

```
src/AdvCamera/
  App.xaml, MainWindow.xaml(.cs)   UI: toolbar, live view, side panel, status bar
  AppSettings.cs                   All user settings (saved to %APPDATA%\AdvCamera\settings.json)
  Camera/CaptureEngine.cs          Background capture thread (OpenCV, DirectShow backend), recording
  Camera/DsCamera.cs               DirectShow: camera list, formats, hardware controls, driver dialog
  Camera/DirectShowInterop.cs      COM interface definitions
  Processing/FrameProcessor.cs     Rotation, mirroring and image adjustments per frame
  Controls/LabeledSlider.xaml      Slider with label, value and reset button
docs/                              User guide and maintainer notes
```

## Contributing

Bug reports, ideas and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT](LICENSE). Third-party components: OpenCvSharp (Apache-2.0) and OpenCV (Apache-2.0).
