# Loading screen

The app opens on the loading view while the bootstrap worker prepares geodata. The view shows a spinner, the current stage, a progress bar, and the last error.

## Sub-features

- `loading-startup` opens the window before any border data is loaded.
- `loading-spinner` draws the animated spinner while work runs.
- `loading-progress` advances after each border file is parsed or read from the cache.
- `loading-transition` switches to the Map view when border data is ready.
- `loading-maintenance` lets the user press `2` during a run without being pulled to the map.
- `loading-error` keeps the loading screen and shows the failure when the run fails.

## How to get to it (user POV)

- Start the app. The loading screen is the first view.
- Nothing is needed to reach it. It leaves automatically when data is ready.

## Driving it with app-session.sh

Preconditions:

- `doctor` reports `ok`.
- Use the default fresh database so the border files must be parsed. That takes about 12 seconds.

- **Startup.** Run `start`. The log contains `Showing the loading view.` and `Loaded 8 border files ... (0 cached, 8 parsed)`. Run `shot loading-startup` immediately after `start`; the screen reads `Preparing geodata`, `Loading border data`, a spinner, and a progress bar.
- **Spinner and progress.** Run `shot loading-early`, wait 3 seconds, then `shot loading-late`. The second capture shows a larger fill and a higher percent. The `Loaded ... (0 cached, 8 parsed) in N ms` line names the work the bar tracked.
- **Transition.** Run `wait 'Composed map'`. The log contains `Switched view Loading -> Map`. Run `shot map-after-load`; the screen shows the map.
- **Warm start.** Stop, then start with the same `VERIFY_DB` (set it to a path you keep). The log contains `(8 cached, 0 parsed)` and the loading screen clears in about a second. The transition line is still logged.
- **Maintenance during a run.** Start with a fresh database and send `keys 2` within a second. The log contains `Switched view Loading -> Maintenance`. The State row reads Running and the Progress row names the stage. The map transition does not interrupt. `shot maintenance-during-load`.
- **Failure.** Run `start` with `VERIFY_GEODATA_ROOT` pointing at an empty directory and `VERIFY_NO_PULL=1`. The log contains `finished with Failed` and the pull step error about automatic download being disabled. Run `shot loading-failed`; the screen keeps `Preparing geodata`, shows the error near the bottom, and never switches to the map.

## Gotchas

- The spinner animates every frame; a screenshot proves it exists, not that it moved.
- A failed run does not switch to the map. Press `2` to reach Maintenance and retry.
- The progress bar counts border files. A cache hit for all files jumps to 100% in under a second.
- The verify database is fresh per run by default, so every `start` pays the parse. Set `VERIFY_DB` to reuse one.
