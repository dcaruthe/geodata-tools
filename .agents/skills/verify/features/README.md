# GeoTools verification map

This directory is the maintained source for verifying the user-facing behavior of GeoTools. Read this index before driving the app, then use the matching feature file as the recipe.

## Baseline preconditions

- Start the session with `.agents/skills/verify/scripts/app-session.sh start`.
- The repo contains `geodata/borders/*.geojsonseq`, built by `scripts/extract_borders.sh`.
- `doctor` reports `ok` and the expected border count.
- Never drive an app or display that this verification run did not start.

## Driving conventions

- Start every recipe from the Map view unless its preconditions say otherwise.
- Send keys with `app-session.sh keys`; wait for the matching log line before capturing.
- Capture screenshots with `app-session.sh shot`.
- Treat every command as literal.
- Restore a configuration override by starting a fresh session. Do not edit `appsettings.json` for a proof.
- Keep evidence under `.audit/verify/<run>/`; cleanup keeps that directory.

## Proof and skip reporting

- Capture the user action and the resulting state, not only the final screen.
- Pair every screenshot with a log line or an on-screen state that names the result.
- Report an unreachable path with the attempted command and the unmet precondition.
- Do not report a skipped entry point as verified through another path.

## Features

- [Map view](./map-view.md) covers border rendering, the grid, the crosshair, the key hint, and switching to Map.
- [Settings view](./settings-view.md) covers the window title and size labels and switching between views.
- [Admin level configuration](./admin-level-config.md) covers `Map:AdminLevels` selection and the no-levels and no-data messages.
- [Border extraction](./border-extraction.md) covers the CLI pipeline, its levels sidecar, and its skip and failure paths.
