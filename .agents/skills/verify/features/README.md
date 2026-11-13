# GeoTools verification map

This directory is the maintained source for verifying the user-facing behavior of GeoTools. Read this index before driving the app, then use the matching feature file as the recipe.

## Baseline preconditions

- Start the session with `.agents/skills/verify/scripts/app-session.sh start`.
- `doctor` reports `ok`, a border count, and the geodata root.
- Every run gets a fresh state database at `<run>/state.db`. The first startup parses the borders and writes the geometry cache.
- Never drive an app or display that this verification run did not start.
- Never force a repull against the real geofabrik source. Use the fixture root and the local server recipe in `geodata-pipeline.md`.

## Driving conventions

- Start every recipe from the Map view unless its preconditions say otherwise.
- Send keys with `app-session.sh keys`; wait with `app-session.sh wait '<pattern>'` before capturing.
- Capture screenshots with `app-session.sh shot`.
- Treat every command as literal.
- Restore a configuration override by starting a fresh session with a fresh `VERIFY_DB`. Do not edit `appsettings.json` for a proof.
- Keep evidence under `.audit/verify/<run>/`; cleanup keeps that directory.

## Proof and skip reporting

- Capture the user action and the resulting state, not only the final screen.
- Pair every screenshot with a log line or an on-screen state that names the result.
- Report an unreachable path with the attempted command and the unmet precondition.
- Do not report a skipped entry point as verified through another path.

## Features

- [Loading screen](./loading-screen.md) covers startup, the spinner, progress, the transition to the map, entering Maintenance during a run, and a failed bootstrap.
- [Map view](./map-view.md) covers border rendering, the grid and crosshair flags, the key hint, the no-data state, and switching views.
- [Maintenance view](./maintenance-view.md) covers state, runtime settings, feature flags, artifact status, actions, confirmations, cancel, and reset.
- [Admin level configuration](./admin-level-config.md) covers display levels at runtime, extraction levels, the empty selection, and the missing-level message.
- [Geodata pipeline](./geodata-pipeline.md) covers adoption, download, extraction, the geometry cache, skip paths, failure, and the headless CLI.
- [API and MCP](./api-and-mcp.md) covers REST, Swagger, GraphQL, the MCP tools, the screenshot, the launcher link, key rotation, and the launcher-down state.
