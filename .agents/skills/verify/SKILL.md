---
name: verify
description: Launch and drive the GeoTools desktop window to prove the loading screen, map, maintenance view, runtime geodata pipeline, runtime settings, and keyboard navigation. Use for UI, rendering, configuration, pipeline, or OpenGL changes.
---

# Verify GeoTools

Drive the real window under Xvfb. The unit tests cover the pure layers. This skill covers the GLFW window, the Skia and OpenGL surface, the loading screen, and the composition a user sees.

## Launch

Run `.agents/skills/verify/scripts/app-session.sh start`. It builds the solution, picks a free X display, starts `Xvfb` at 1280x1024x24, and runs the launcher with an isolated state database at `<run dir>/state.db` and the real geodata root. It waits for `Window "` and `Showing the loading view`, then prints the run directory, display, database, and log path.

A fresh database parses the border files on first run. That takes about 12 seconds and writes the geometry cache, so the first start proves the loading screen. Later runs against the same `VERIFY_DB` load the cache in under a second.

Session state lives in `.audit/verify/.session`, and every later subcommand reads it. A window this helper did not start is not yours to drive.

For hands-on work on a desktop display, run `dotnet run --project src/GeoTools.Launcher` directly. Do not use that window for evidence.

## Doctor

Run `.agents/skills/verify/scripts/app-session.sh doctor` first whenever a run looks wrong. It checks `dotnet`, `Xvfb`, `xvfb-run`, ImageMagick `import`, and `xdotool` on `PATH`, builds the solution, reports the border file count and the geodata root, and removes a stale session file. It exits non-zero with the failing check.

## Drive

Send keys with `.agents/skills/verify/scripts/app-session.sh keys <key> ...`. The helper finds the window by title, focuses it, and sends real XTEST key events.

- Views: `1` map, `2` maintenance, `Tab` next, `shift+Tab` previous, `Escape` close.
- Maintenance: `Up`, `Down`, `Left`, `Right`, `Return`, `BackSpace`, `Home` (first row), `End` (last row).

Wait for a log line with `.agents/skills/verify/scripts/app-session.sh wait '<pattern>' [seconds]`. It exits non-zero if the pattern never appears or the app exits.

Capture the screen with `.agents/skills/verify/scripts/app-session.sh shot <name>`. It writes `.audit/verify/<run>/<name>.png` from the root window.

## Environment

- `VERIFY_DB` state database. Defaults to `<run>/state.db`.
- `VERIFY_GEODATA_ROOT` geodata root. Defaults to `<repo>/geodata`.
- `VERIFY_NO_PULL=1` sets `Features:GeodataAutoPull=false`.
- `VERIFY_RUN_ON_STARTUP=0` skips the automatic bootstrap run.
- `VERIFY_SOURCE_BASE_URL` overrides `Geodata:SourceBaseUrl`, for a local fixture server.
- `VERIFY_REGIONS` sets the pipeline regions, comma separated, and replaces the configured list.
- `VERIFY_OSMIUM` overrides the `osmium` executable path, for a failure proof.
- `VERIFY_ADMIN_LEVEL` sets `Map:AdminLevels` to one level for the run.
- `VERIFY_TITLE` window title. Defaults to `GeoTools verify`.
- `VERIFY_TIMEOUT` readiness timeout in seconds.

Runtime settings persist in the database, so a configuration override only wins on a fresh `VERIFY_DB`. That is the default for every run.

## Evidence

Every run writes to `.audit/verify/<UTC stamp>-<pid>/`. Keep `app.log`, `xvfb.log`, the database, screenshots, and a short `summary.md` that names the feature IDs, the key presses, and the observed state.

A proof must exercise the user path. Start from the state the recipe names, send the key, wait for the log line, and capture the screen after the resulting state. A screenshot alone does not prove a view switch or a pipeline stage. The log line does.

Run the feature recipes in `features/` from a fresh session unless the recipe says otherwise.

## Cleanup

Run `.agents/skills/verify/scripts/app-session.sh stop`. It kills only the app and Xvfb PIDs recorded for this session, then removes the session file. Never kill by process name. Evidence survives cleanup.

If a run crashed, `doctor` removes a stale session file after confirming the recorded PIDs are gone.

## Helpers

- `scripts/app-session.sh doctor` checks the environment.
- `scripts/app-session.sh start` starts Xvfb and the app.
- `scripts/app-session.sh keys <key> ...` drives the window.
- `scripts/app-session.sh wait <pattern>` waits for a log line.
- `scripts/app-session.sh shot <name>` captures the root window.
- `scripts/app-session.sh status` reports the session, database, and evidence directory.
- `scripts/app-session.sh stop` tears down only what the session started.
