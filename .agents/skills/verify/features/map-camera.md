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
- `map-scene-retained` keeps the level-filtered screen-space polylines and reuses them across a
  same-scale pan by transforming the points in place.
- `map-scene-culled` emits only the retained shapes whose screen bounds intersect the viewport, so
  off-screen geometry never reaches the scene or the renderer.

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

## Retained scene

`RetainedMapScene` keeps the level-filtered screen-space polylines and their screen bounds. A pan
at the same scale transforms the retained points in place. A zoom rebuilds the projection so the
coverage margin and simplification tolerance stay one viewport at the new scale. A change to the
geometry, settings, admin levels, view size, grid flag, or crosshair flag also rebuilds, as does a
view outside the retained coverage. Every compose emits only the shapes that intersect the visible
viewport. The compose log line ends with
`retained <n>, visible <n>/<total> polylines, translated|rebuilt in <n> ms`.

Measurements at 1200x800 with admin level 2, database
`.audit/verify/20261113T115424Z-300813/state.db`, run
`.audit/verify/20261113T122154Z-332362`:

| Step | Before (`ea59e47`) | After | Mode |
| --- | --- | --- | --- |
| Initial fit | 175 ms | 175 ms | rebuilt |
| First compose after a zoom | 56-95 ms | 152-200 ms | rebuilt |
| Pan `Right` at 2.7x | 81-86 ms | 5 ms | translated |
| Pan `w` at 2.7x | 81-95 ms | 4 ms | translated |
| Pan `a` at 2.7x | 82-96 ms | 4 ms | translated |
| Pans at 5.4x before the coverage exit | n/a | 2-3 ms | translated |
| Pan that leaves the coverage at 5.4x | n/a | 140 ms | rebuilt |

The rebuilds are slower than a reuse because they project the coverage margin, which can cover the
whole world at low zoom. A same-scale pan is the path the target covers.

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
- A same-scale pan logs `translated` and composes in under 20 ms.
- The first compose after a zoom logs `rebuilt`, and later same-scale pans log `translated`.
- The compose line reports `retained` at least `visible`; when the coverage includes shapes outside
  the viewport, `visible` is smaller, which proves the cull.
- After a pan that leaves the coverage, the compose logs `rebuilt`, and the next pan logs
  `translated` again.

## Gotchas

- A zoom rebuilds the retained projection at the new scale and takes about 150-170 ms at 1200x800
  with admin level 2. A same-scale pan transforms and takes 2-5 ms. Wait for the new `Composed map`
  line before capturing; waiting only for `Camera center` can capture the previous frame.
- The coverage margin is one full viewport on every side. A pan can leave it only at high zoom
  (above about 4x), where the viewport is a small part of the coverage. A low-zoom pan stays inside
  and transforms.
- The camera starts uninitialised. The first pan or zoom fits the current geometry and then
  applies the key, so the first step is relative to the initial fit, not to a stale camera.
- A pan at the fit scale is clamped back to the centered fit because the whole bounds already
  fit. Zoom in before checking a pan direction if the assertion is about visible movement.
- The zoom floor is the fit scale, not a constant. Changing the window size changes the floor
  and the `Zoom` factor.
- The camera state is in memory only in this commit. Restarting the app returns to the initial
  fit.
