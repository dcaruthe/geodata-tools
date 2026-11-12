# Map view

The Map view draws the extracted administrative borders over a black background with a grid, a crosshair, and a key hint.

## Sub-features

- `map-borders` renders the borders for the configured admin levels after startup.
- `map-chrome` draws the grid, the crosshair, and the `1: map 2: settings Tab: next` hint.
- `map-switch` returns to the Map view from Settings with `1`.
- `map-no-data` shows the extraction instruction when no border files exist.

## How to get to it (user POV)

- Open the app. The Map view is active.
- Press `1` from another view.
- Press `Tab` until the Map view is active.

## Driving it with app-session.sh

Preconditions:

- `doctor` reports `ok` with a border count above zero.
- A session is running from `start`.

- **Initial render.** Wait for `Composed map at 1200x800 with admin levels 2: ...` in `app.log`. Run `shot map`. The screenshot shows continental outlines, the grid, the crosshair, and the key hint.
- **Configured levels.** Check that the log's level list matches `Map:AdminLevels` in `Launcher/appsettings.json`, which defaults to `2`. Only those levels are drawn.
- **Switch back.** Run `keys 2` and wait for `Switched view Map -> Settings`. Run `keys 1` and wait for `Switched view Settings -> Map`. Run `shot map-again`; it matches the first capture.
- **No border data.** Stop, then start with `VERIFY_BORDERS_DIR=/tmp/geotools-empty-borders`, where that directory exists and contains no `.geojsonseq` files. The screen reads `No border data in /tmp/geotools-empty-borders. Run scripts/extract_borders.sh.` and the app stays interactive.

## Gotchas

- The first compose takes 10-15 s while the border files load. Wait for the log line instead of a fixed sleep.
- Border geometry is cached for the process lifetime. Re-extract and restart to see new data.
- A missing display or GL context aborts before the `Window "` line. Run `doctor` and read `app.log`.
