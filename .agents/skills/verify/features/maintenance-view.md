# Maintenance view

The Maintenance view owns runtime settings, feature flags, per-region artifact status, and the actions that rebuild data. It replaces the old Settings view.

## Sub-features

- `maintenance-open` switches to Maintenance with `2` or `Tab`.
- `maintenance-state` shows the current phase, the active progress row, and the last run.
- `maintenance-display-levels` cycles the display-level presets at runtime.
- `maintenance-tolerance` changes the simplify tolerance and queues a geometry-cache rebuild; the final status reads `Saved.`.
- `maintenance-extract-levels` cycles the extraction-level presets and persists them; the next startup or update check re-extracts because the artifact levels no longer match.
- `maintenance-flags` toggles map grid, crosshair, and automatic download.
- `maintenance-artifacts` lists each region with its PBF and border sizes.
- `maintenance-actions` queues update checks, repulls, border rebuilds, cache rebuilds, retries, and resets.
- `maintenance-confirm` asks for a second `Return` on destructive actions.
- `maintenance-cancel` cancels the active run with `BackSpace` or the cancel action.
- `maintenance-navigation` returns to Map with `1`, cycles with `Tab`, and closes with `Escape`.
- `maintenance-layout` keeps the selection bar inside its row, fills the list to the bottom block, splits the key hint on a narrow window, and draws a scrollbar only when rows are hidden.

## How to get to it (user POV)

- Press `2` while the window is focused, from the map or the loading screen.
- Press `Tab` from the Map view.

## Driving it with app-session.sh

Preconditions:

- A session is running from `start`.
- `wait 'Composed map'` so the first run has finished.

- **Open.** Run `keys 2` and wait for `Switched view Map -> Maintenance`. Run `shot maintenance`. The screen shows `Maintenance`, the key hint, the State row reading `Ready`, the Last run row, the Display, Extraction, Feature flags, Data, and Actions sections. At 1280 px the labels and values sit in separate columns, so the screen has `State    Ready` rather than `State: Ready`. Rows run State, Last run, Display, Extracted levels, Display levels, Simplify tolerance, Extraction, Extract levels, Feature flags, Map grid, Crosshair, Auto download, Data, the region rows, Actions, then the seven actions.
- **Layout.** At 1200x800 the whole list fits and no scrollbar renders. The selection bar covers `Display levels` without touching `Simplify tolerance` above or below. Capture the armed `Force repull` prompt at 1200x260: one row sits above the amber prompt, which sits above the status and the footer. Capture at 420x800: the hint is two lines, ends inside the window, and the values read inline. `GET /api/launcher/screenshot?width=&height=` composes any size offscreen; `shot` proves the GL path at the window size.
- **State and artifacts.** The State row reads Ready after the run. The Data rows name each region with its PBF and border sizes. Run `shot maintenance-data`.
- **Display levels.** The selection starts on `Display levels` (value `2`). Run `keys Right`, then `keys 1` and wait for `admin levels 2,4`. Run `shot map-levels`. The map now draws both levels.
- **Tolerance.** The tolerance row is one below display levels. Run `keys 2`, `keys Down`, `keys Right`. The row value changes to a larger tolerance, a cache rebuild is queued, and the final status reads `Saved.`. Wait for `Bootstrap run .* finished with Succeeded`; the log shows `(0 cached, 8 parsed)` on the rebuild.
- **Flags.** Move to `Map grid` and press `Return`. The value flips to `off` and the status reads `Saved.`. Run `keys 1`; the map has no grid. Run `shot map-no-grid`. Repeat for `Crosshair`.
- **Confirmation.** Move to `Force repull` and press `Return`; the prompt reads `Enter again to confirm. Moving or leaving cancels.` and the status reads `Press Enter again to force repull.` Press `Down` and the prompt clears without queueing. `Return` on the same row confirms. Leaving the view also clears it.
- **Cancel.** On the real data, select `Rebuild geometry cache` and press `Return`; the run starts and parses for about 12 seconds. Wait for the State row to read `Running: ...` or the Progress row to appear, then move to `Cancel running work` and press `Return`. The action stays disabled until the view refreshes its state, so pressing it within the same instant as the rebuild is a no-op. `BackSpace` publishes a cancel immediately without the row. Wait for `finished with Cancelled`; the log names the run.
- **Retry failed events.** Select `Retry failed events` and press `Return`; the status reads `Failed events will be retried.`
- **Reset settings.** Select `Reset settings`, press `Return`, then `Return` again to confirm. The status reads `Saved.`, display levels return to `2`, and the flags return to `on`.
- **Navigation.** Run `keys Tab` and wait for `Switched view Maintenance -> Map`. Run `keys 2` then `keys 1` and wait for `Switched view Maintenance -> Map`. `Escape` closes the window.

## Gotchas

- `Force repull` against the real geofabrik source downloads tens of gigabytes. Only run it with the fixture root and local server in `geodata-pipeline.md`.
- `Rebuild borders` on the real data takes hours. Use the fixture for the live proof.
- The update and rebuild actions are disabled while a run is active. `Retry failed events` and `Reset settings` stay enabled. Disabled rows are still selectable and ignore `Return`.
- Runtime settings live in the state database. A fresh `VERIFY_DB` starts from the configuration defaults; only user changes are stored as rows.
- The view refreshes when it becomes active and when a bootstrap message arrives; the row values update without a restart.
- The list sizes itself to the footer, status, or prompt, whichever sits highest. A short window shows fewer rows rather than drawing over the bottom block.
- A 4 px scrollbar at the right edge appears only when rows are hidden; its thumb marks the visible window. The old `more above` and `more below` labels are gone.
- Section labels render uppercase at 12 px; rows stay at 14 px.
- `Left` and `Right` on a toggle flip it; `Return` on a choice advances it. Selection skips section headers and the rows marked not selectable. Region artifact rows are `Info` but selectable, so `Down` stops on them.
- A late progress message cannot reopen a step that already finished; the final status wins.
