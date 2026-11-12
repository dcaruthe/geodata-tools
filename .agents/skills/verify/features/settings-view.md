# Settings view

The Settings view shows the configured window title and size, a frame, an aspect-ratio box, and a row of color swatches.

## Sub-features

- `settings-open` switches to Settings with `2`.
- `settings-labels` shows `Window:Title` and the `Width x Height` from configuration.
- `settings-tab` reaches Settings with `Tab` and leaves it with another `Tab`.
- `settings-back` returns to Map with `1`.

## How to get to it (user POV)

- Press `2` while the window is focused.
- Press `Tab` from the Map view.

## Driving it with app-session.sh

Preconditions:

- A session is running from `start`.

- **Open Settings.** Run `keys 2` and wait for `Switched view Map -> Settings`. Run `shot settings`. The screen shows `GeoTools verify` and `1200 x 800` above the frame and swatches.
- **Configured title.** Stop, then start with `VERIFY_TITLE=GeoTools demo`. Open Settings; the label reads `GeoTools demo` and the size still reads `1200 x 800`.
- **Tab rotation.** Run `keys Tab` and wait for `Switched view Settings -> Map`. The screen returns to the Map view.
- **Return with 1.** Run `keys 2` to open Settings, then `keys 1` and wait for `Switched view Settings -> Map`. The screenshot matches the Map capture.

## Gotchas

- A key press before the window has focus is dropped. The helper focuses the window, and the switch log line confirms delivery.
- The view reads `IOptions<WindowOptions>` once at construction. A configuration change needs a restart.
- The size labels show the configured `Window:Width` and `Window:Height`, not the live client size.
