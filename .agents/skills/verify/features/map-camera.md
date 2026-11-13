# Map camera

WASD and the arrow keys pan the map camera. `+` and `-` on either keyboard half zoom about the
crosshair, which stays fixed at the viewport center. The map status line reports the camera center
and the zoom relative to the initial fit.

## Sub-features

- `map-pan-keys` moves the camera west, east, north, and south by 64 screen pixels per key.
- `map-zoom-keys` scales the camera by 1.4 per key, clamped to the fit scale and 64 times it.
- `map-camera-status` draws `Center <lat>N <lon>E   Zoom <factor>x` at the top left.
- `map-crosshair-center` keeps the crosshair at the viewport center while the borders move.
- `map-camera-initial` still fits the border bounds with 24 px padding before any key.

## How to get to it (user POV)

- Open the app and wait for the loading screen to finish, or press `1` for the Map view.
- Press `w`, `a`, `s`, or `d`, or the arrow keys, to pan.
- Press `+` (learned as `=` or keypad add) or `-` (minus or keypad subtract) to zoom.

## Driving it with app-session.sh

Preconditions:

- `doctor` reports `ok` with a border count above zero.
- A session is running from `start`, and the log has `Composed map`.
- The Map view is active. If the app is elsewhere, run `keys 1` and wait for
  `Switched view .* -> Map`.

Key names for `app-session.sh keys`:

| Action | Key name | Also accepted |
| --- | --- | --- |
| Pan north | `w` | `Up` |
| Pan south | `s` | `Down` |
| Pan west | `a` | `Left` |
| Pan east | `d` | `Right` |
| Zoom in | `plus` | `KP_Add`, `equal` with shift |
| Zoom out | `minus` | `KP_Subtract` |

- **Initial camera.** Run `wait 'Composed map'` and `shot map-camera-start`. The whole border
  bounds sit inside a 24 px margin and the status line reads `Zoom 1.0x`.
- **Zoom setup for panning.** At the fit scale the whole bounds fit, so a pan clamps back to the
  center. Run `keys plus plus plus` and wait for three `Camera center` lines. The status reads
  `Zoom 2.7x`, which leaves room to pan on both axes.
- **Pan each direction.** For each of `keys w`, `keys Right`, `keys a`, and `keys Down`, wait for
  a new `Camera center` line and a new `Composed map` line, then capture `shot map-pan-<key>`.
  The log line is `Camera center <longitude>,<latitude> zoom <scale>`.
  - `w`/`Up` raises the latitude; map content moves down.
  - `s`/`Down` lowers the latitude; map content moves up.
  - `a`/`Left` lowers the longitude; map content moves right.
  - `d`/`Right` raises the longitude; map content moves left.
- **Zoom each half.** For each of `keys plus`, `keys minus`, `keys KP_Add`, and
  `keys KP_Subtract`, wait for a new `Camera center` line and a new `Composed map` line, then
  capture `shot map-zoom-<key>`. The status factor multiplies by 1.4 on a zoom in and divides by
  1.4 on a zoom out.
- **Crosshair.** The crosshair is at `(width / 2, height / 2)` in every capture, including after
  a pan or zoom.

## Pass predicates

- `Camera center` appears once per handled pan or zoom key, with the formatted center and scale.
- After `w` the latitude in the status line increases. After `s` it decreases.
- After `a` the longitude in the status line decreases. After `d` it increases.
- After `plus` or `KP_Add` the status zoom factor increases by 1.4 from the current factor (until
  the 64x cap). After `minus` or `KP_Subtract` it decreases by 1.4 (until the 1.0x floor).
- The border ink shifts opposite the camera direction between the before and after captures,
  while the crosshair arms and circle stay centered.
- The first capture fits the border bounds with 24 px padding and reads `Zoom 1.0x`.
- Unhandled keys such as `Enter` or `Tab` do not print `Camera center` and do not move the camera.

## Gotchas

- Each pan or zoom recomposes the whole scene in this commit. At 1200x800 with admin level 2 a
  compose takes about 180 ms, so wait for the new `Composed map` line and then allow a moment
  before capturing. Waiting only for `Camera center` can capture the previous frame.
- A retained scene lands in a later commit.
- The camera starts uninitialised. The first pan or zoom fits the current geometry and then
  applies the key, so the first step is relative to the initial fit, not to a stale camera.
- A pan at the fit scale is clamped back to the centered fit because the whole bounds already
  fit. Zoom in before checking a pan direction if the assertion is about visible movement.
- The zoom floor is the fit scale, not a constant. Changing the window size changes the floor
  and the `Zoom` factor.
- The camera state is in memory only in this commit. Restarting the app returns to the initial
  fit.
