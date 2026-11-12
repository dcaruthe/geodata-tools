---
name: verify
description: Launch and drive the GeoTools desktop map window to prove the Map and Settings views, border loading, admin-level configuration, and keyboard navigation. Use for UI, rendering, configuration, or OpenGL changes.
---

# Verify GeoTools

Drive the real window under Xvfb. The unit tests cover the pure layers. This skill covers the GLFW window, the Skia and OpenGL surface, and the composition a user sees.

## Launch

Run `.agents/skills/verify/scripts/app-session.sh start`. It builds the solution, picks a free X display, starts `Xvfb` at 1280x1024x24, runs the launcher with `Window__Title=GeoTools verify` and the real borders directory, and waits for the `Window "` and `Composed map` log lines. Startup takes about 10-15 s because the Map view loads 1 GB of border files.

The command prints the run directory, display, and log path. Session state lives in `.audit/verify/.session`, and every later subcommand reads it. A window this helper did not start is not yours to drive.

For hands-on work on a desktop display, run `dotnet run --project src/GeoTools.Launcher` directly. Do not use that window for evidence.

The app renders a `No border data ...` message when the borders directory is empty and keeps running. That is a valid state, not a launch failure.

## Doctor

Run `.agents/skills/verify/scripts/app-session.sh doctor` first whenever a run looks wrong. It checks `dotnet`, `Xvfb`, `xvfb-run`, ImageMagick `import`, and `xdotool` on `PATH`, builds the solution, reports the border file count, and removes a stale session file. It exits non-zero with the failing check.

## Drive

Send keys with `.agents/skills/verify/scripts/app-session.sh keys <key> ...`. The helper finds the window by title, focuses it, and sends real XTEST key events. The app accepts `1` (Map), `2` (Settings), `Tab` (next view), `shift+Tab` (previous view), and `Escape` (close).

Capture the screen with `.agents/skills/verify/scripts/app-session.sh shot <name>`. It writes `.audit/verify/<run>/<name>.png` from the root window.

For a single-level run, set `VERIFY_ADMIN_LEVEL=6` before `start`. For a different borders directory, set `VERIFY_BORDERS_DIR`. The helper passes them as `Map__AdminLevels__0` and `Map__BordersDirectory`.

## Evidence

Every run writes to `.audit/verify/<UTC stamp>-<pid>/`. Keep `app.log`, `xvfb.log`, screenshots, and a short `summary.md` that names the feature IDs, the key presses, and the observed state.

A proof must exercise the user path. Start from the Map view, press the key, capture the screen after the resulting state, and confirm the matching log line (`Switched view Map -> Settings`, `Composed map at ... with admin levels ...`). A screenshot alone does not prove a view switch. The log line does.

Run the feature recipes in `features/` from a fresh session unless the recipe says otherwise.

## Cleanup

Run `.agents/skills/verify/scripts/app-session.sh stop`. It kills only the app and Xvfb PIDs recorded for this session, then removes the session file. Never kill by process name. Evidence survives cleanup.

If a run crashed, `doctor` removes a stale session file after confirming the recorded PIDs are gone.

## Helpers

- `scripts/app-session.sh doctor` checks the environment.
- `scripts/app-session.sh start` starts Xvfb and the app.
- `scripts/app-session.sh keys <key> ...` drives the window.
- `scripts/app-session.sh shot <name>` captures the root window.
- `scripts/app-session.sh status` reports the session and evidence directory.
- `scripts/app-session.sh stop` tears down only what the session started.
