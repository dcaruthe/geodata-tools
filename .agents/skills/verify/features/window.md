# Window size

The window starts maximized and remembers a fixed size across sessions. The size is a runtime setting, so Maintenance changes it live and the launcher link, REST, GraphQL, and MCP expose it to agents.

## Sub-features

- `window-start-maximized` opens the window maximized by default, using `Window:Width` and `Window:Height` as the restore size.
- `window-setting` stores `window.size` as `maximized` or `<width>x<height>` in the state database.
- `window-maintenance` shows a `Window size` choice row after the Display rows.
- `window-live-apply` applies a changed setting in the render loop after a 150 ms settle delay.
- `window-resize-persist` saves a user resize or maximize change after the live size is stable for one second.
- `window-api` exposes the setting in `GET /api/settings`, `PUT /api/settings/window`, the GraphQL `setWindowSize` mutation, and the MCP `set_window` tool.

## How to get to it (user POV)

- Start the app. It opens maximized.
- Open Maintenance with `2`, move to `Window size`, and press `Left` or `Right`.
- Change it from an agent with `PUT /api/settings/window` or the MCP `set_window` tool.
- Resize the window on a desktop; the size is saved after it stops moving.

## Driving it with app-session.sh

Preconditions:

- `doctor` reports `ok`.
- Start from a fresh `VERIFY_DB` to prove the maximized default.

- **Start maximized.** Run `start`. The log contains `Window "GeoTools verify" maximized.` and `Window started maximized with restore size 1200x800`. Run `shot window-maximized`; under a window manager the window fills the screen. Xvfb has no window manager, so the maximize request cannot be seen there; the log line and `GET /api/state` are the proof.
- **Change from Maintenance.** Run `keys 2` and wait for `Switched view .*Maintenance`. The selection starts on `Display levels`; run `keys Down Down` to reach `Window size`. Run `shot window-row-before`; the row reads `maximized`. Run `keys Right` and wait for `Window size applied: 1280x800`. Run `shot window-row-1280x800`; the row reads `1280x800` and the status reads `Saved.`.
- **Read it from the API.** Run `api-start`, then `GET /api/state` and `GET /api/settings`. The launcher state reports `windowWidth: 1280`, `windowHeight: 800`, `windowMaximized: false`, and settings report `window: { maximized: false, width: 1280, height: 800 }`.
- **Persist across a restart.** Read `DB_PATH` from `status` or `.audit/verify/.session`. Run `app-stop`, then `stop`, then `VERIFY_DB=<DB_PATH> start`. The log contains `Window "GeoTools verify" 1280x800.` and `Window started at 1280x800`, and `GET /api/state` reports the same 1280x800 window. Run `shot window-restarted`.
- **Live apply from the link.** With a session and API running, `PUT /api/settings/window` with `{"maximized": false, "width": 1440, "height": 900}` returns `accepted: true`. Wait for `Window size applied: 1440x900`, then run `shot window-api-applied`.

## Pass predicates

- Startup: `Window started maximized with restore size <Window:Width>x<Window:Height>` in the log for a fresh database, and `windowMaximized: true` from `/api/state`.
- Maintenance change: `Window size applied: <preset>` in the log, the `Window size` row shows the new value, and the status reads `Saved.`.
- Persistence: after a restart against the same database, `Window started at <size>` appears instead of the maximized line, and `/api/state` reports the same width, height, and maximized flag.
- Link and API: `GET /api/settings` carries the stored window, and `PUT /api/settings/window` applies it live.
- Resize persistence: after a desktop resize or maximize change settles for one second, the next `GET /api/settings` reports the new size.

## Gotchas

- Xvfb has no window manager. `WindowState.Maximized` is reported by GLFW even when nothing maximizes the frame, so maximize is proven from the log and the API state, not from pixels.
- The Xvfb screen is 1280x1024. A fixed width of 1280 fits, while wider presets are clipped. Choose a size that fits the screen for screenshots.
- The 1280x800 preset is one step right of `maximized`. `WindowSizePresets` deliberately keeps `Display` short so the row fits.
- A live resize is ignored for 300 ms after an apply, because the window manager may adjust the frame. A smaller or larger live size is ignored until it stays stable for one second.
- Sizes below 320x240 or above 7680x4320 are never persisted. The setting itself accepts 640x480 to 7680x4320.
- The verify database is fresh per run by default. Reuse `VERIFY_DB` only for the restart step, and do not treat a reused database as proof of the maximized default.
