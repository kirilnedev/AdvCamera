# AdvCamera user guide

## Choosing a camera

Pick the camera from the list at the top right of the window. If you plug a camera in while AdvCamera is
running, click the refresh button next to the list.

If you see **"Could not open the camera"**, another program (the Windows Camera app, Teams, a browser tab, a
second copy of AdvCamera…) is probably using it. Close that program and select the camera again.

AdvCamera remembers the last camera, resolution and all adjustments the next time it starts.

## The adjustments panel

Click **Adjust** (or press `P`) to show or hide the panel on the right. Drag its left edge to make it wider or
narrower. Each section can be collapsed by clicking its title.

### Orientation

- **Rotation**: 0°, 90°, 180° or 270° clockwise. The toolbar's ↺ / ↻ buttons and `R` / `Shift+R` rotate in 90° steps.
- **Mirror horizontally (flip X)** and **Mirror vertically (flip Y)** mirror the image along the screen's axes,
  after rotation. Mirroring both ways is the same as rotating 180°.

### Image adjustments

These are applied by AdvCamera to every frame, so they work with any camera. They also apply to snapshots and
recordings.

| Setting | What it does |
| --- | --- |
| Brightness | Makes the whole image lighter or darker |
| Contrast | Increases or decreases the difference between light and dark |
| Gamma | Above 1 brightens the shadows, below 1 darkens them, without clipping highlights |
| Saturation | 0 % is gray, 100 % is unchanged, 200 % is very vivid |
| Sharpness | Makes edges crisper (unsharp mask) |
| Temperature | Corrects a blue or yellow color cast |
| Tint | Corrects a green or magenta color cast |
| Noise reduction | Averages consecutive frames. Very effective on a still subject; moving objects leave trails |
| Grayscale / Invert | Black-and-white image / negative image |

Click the small ↶ icon next to a slider, or double-click the slider, to reset it. **Reset all adjustments**
resets the whole section.

### Overlay

A crosshair and/or a grid drawn over the live view, in a color of your choice. Overlays are only shown on
screen; they are not included in snapshots or videos. The crosshair always marks the center of the view.

### Camera

- **Resolution / format** lists every mode the camera offers. **MJPG** modes usually give the highest frame rate
  at high resolutions; **YUY2** is uncompressed but often slower. *Camera default* lets the driver decide.
  The line below the list shows what the camera actually delivers.
- **Driver settings…** opens the camera manufacturer's settings dialog.
- **Hardware controls** are settings the camera applies itself (exposure, focus, gain, white balance, …).
  Which controls appear, and their ranges, depends on the camera. Tick **Auto** to let the camera adjust a
  value automatically; untick it to set it by hand. **Restore camera defaults** resets all of them.

Tip for microscopes: hardware exposure and gain give a cleaner image than raising software brightness, because
software brightness also amplifies noise.

### Snapshots & recording

- **Save folder**: where snapshots and videos go (default: `Pictures\AdvCamera`).
- **Snapshot format**: PNG (lossless, larger) or JPEG (smaller).
- Videos are saved as Motion-JPEG `.avi` files, which play in VLC, the Windows Media Player and most editors.

## Toolbar

| Button | Action |
| --- | --- |
| ↺ ↻ | Rotate counter-clockwise / clockwise |
| Flip X / Flip Y | Mirror horizontally / vertically |
| Snapshot | Save the current image |
| Record / Stop | Start or stop video recording. A red **REC** badge shows the elapsed time |
| Freeze | Hold the current image. Snapshots and copy use the frozen image |
| Copy | Copy the current image to the clipboard |
| ⌖ / grid | Toggle crosshair / grid |
| Fit | Reset zoom |
| Full screen | Hide everything except the image. `Esc` returns |

## Zoom and pan

- Mouse wheel over the image zooms in and out around the mouse pointer.
- When zoomed in, drag with the left mouse button to move around.
- Double-click the image, or press `0`, to fit it to the window again.
- At high zoom, pixels are shown as sharp squares instead of being smoothed.

## Keyboard shortcuts

| Key | Action |
| --- | --- |
| `R` / `Shift+R` | Rotate 90° clockwise / counter-clockwise |
| `H` | Mirror X |
| `V` | Mirror Y |
| `S` or `F12` | Save snapshot |
| `Ctrl+C` | Copy image to clipboard |
| `Ctrl+R` | Start / stop recording |
| `Space` | Freeze / resume |
| `C` | Crosshair on / off |
| `G` | Grid on / off |
| `+` / `-` / `0` | Zoom in / out / fit |
| `P` | Show / hide the adjustments panel |
| `F11` | Full screen; `Esc` to leave |

## Where settings are stored

`%APPDATA%\AdvCamera\settings.json`. Delete this file to return to the defaults.
