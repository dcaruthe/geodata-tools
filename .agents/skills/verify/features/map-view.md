# Map view

The Map view draws the extracted administrative borders over a black background with a grid, a crosshair, a key hint, and a status line.

## Sub-features

- `map-borders` renders the borders for the configured display levels after startup.
- `map-chrome` draws the grid, the crosshair, the `WASD/arrows pan   +/- zoom   F focus   Backspace back   1: map   2: maintenance   Tab: view` hint, and the `Center <lat>N <lon>E   Zoom <factor>x` status line.
- `map-status` shows the camera status line at the top left and, while a region is focused, a focus status message above it.
- `map-grid-flag` removes the grid when `Map grid` is off.
- `map-crosshair-flag` removes the crosshair when `Crosshair` is off.
- `map-switch` returns to the Map view from Maintenance with `1`.
- `map-no-data` shows the maintenance instruction when no border files exist, or the bootstrap error when the run failed.
- `map-level-messages` lists the extracted levels when the selection has no data, and asks for a selection when none is chosen.

## How to get to it (user POV)

- Open the app and wait for the loading screen to finish.
- Press `1` from Maintenance.
- Press `Tab` until the Map view is active.

## Driving it with app-session.sh

Preconditions:

- `doctor` reports `ok` with a border count above zero.
- A session is running from `start`.

- **Initial render.** Run `wait 'Composed map'`. The log reads `Composed map at 1200x800 with admin levels 2: ...`. Allow about a second for the frame to present, then run `shot map`. A two-level map takes roughly 0.8 seconds to compose, so capturing immediately can show the previous view. The screenshot shows continental outlines, the grid, the crosshair, and the key hint.
- **Configured levels.** The log's level list matches `Map:AdminLevels`, which defaults to `2`. Only those levels are drawn.
- **Grid flag.** Run `keys 2`, wait for `Switched view Map -> Maintenance`, toggle `Map grid` to off (see `maintenance-view.md`), then `keys 1`. The map recomposes without the grid. Run `shot map-no-grid`. Toggle it back.
- **Crosshair flag.** Toggle `Crosshair` off, return to Map, and run `shot map-no-crosshair`. The center crosshair is gone.
- **Switch back.** Run `keys 2`, wait for `Switched view Map -> Maintenance`, then `keys 1` and wait for `Switched view Maintenance -> Map`. Run `shot map-again`; it matches the first capture.
- **No border data.** Start with `VERIFY_GEODATA_ROOT` pointing at an empty directory and `VERIFY_NO_PULL=1`. The run fails; send `keys 1`. The screen reads `No border data. <error> Press 2 for maintenance.` The app stays interactive.
- **No levels and missing levels.** In Maintenance, cycle `Display levels` to `none` and press `1`. The screen reads `No admin levels selected. Change them in Maintenance (2).` Start with `VERIFY_ADMIN_LEVEL=6` on a fresh database; the screen reads `No borders at admin levels 6. Extracted: 2,4.`
- **Early map.** `1` and `Tab` reach the Map view before the automatic transition. With `VERIFY_RUN_ON_STARTUP=0` on a fresh database no run starts, so the screen shows `No border data. Press 2 for maintenance and run Rebuild borders.` The `Loading border data.` text needs a run in the Running phase, which `VERIFY_RUN_ON_STARTUP=0` does not produce.

## Gotchas

- The first compose after a fresh database waits for the border files. Wait for the log line instead of a fixed sleep.
- Border geometry is cached in SQLite. Re-extract and restart a new session to see new data.
- A missing display or GL context aborts before the `Window "` line. Run `doctor` and read `app.log`.
- The map reads runtime display levels, not the extraction levels. Changing them in Maintenance recomposes without touching the border files.
- The map can compose before the loading transition if the user presses `1`. The phase message, not the view switch, tells you whether data is ready.
