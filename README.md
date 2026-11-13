# geodata-tools

Desktop map viewer for OpenStreetMap border extracts. The app provisions its own data. On first use it downloads the continental PBFs that are missing, extracts administrative borders with `osmium`, and caches the parsed geometry in SQLite. Later launches reuse the cache and skip work that is already current.

Requires the .NET 10 SDK, an X11 display, and `osmium` on `PATH` for extraction. `python3` is no longer required at runtime.

## Running

```bash
dotnet run --project src/GeoTools.Launcher
```

The window opens on a loading screen while the bootstrap worker prepares data, then switches to the map. Press `2` for Maintenance. Maintenance holds runtime settings, feature flags, per-region artifact status, and the actions that force a rebuild or repull. Press `1` to return to the map.

The state database lives at `geodata/state/geotools.db`. Keep it to keep the geometry cache and runtime settings. Delete it to rebuild derived state from the border files.

`scripts/pull_geodata.sh` and `scripts/extract_borders.sh` are thin wrappers over the headless pipeline mode, so there is one extraction implementation.

## Agent API

`GeoTools.Api` exposes the running window and the geodata to agents over three surfaces.

```bash
dotnet run --project src/GeoTools.Api
```

- REST with Swagger at `http://127.0.0.1:5180/swagger`
- GraphQL with the HotChocolate IDE at `http://127.0.0.1:5180/graphql/`
- MCP over Streamable HTTP at `http://127.0.0.1:5180/mcp`

The API reads the state database read-only, so it still answers geodata queries with the window closed. Live state, logs, screenshots, and commands need the launcher. `GET /api/state` reports `launcherStatus: offline` when it is not running.

`opencode.json` connects the `geotools` MCP server. The tools are `state`, `logs`, `screenshot`, `navigate`, `press_keys`, `maintenance`, `maintenance_select`, `maintenance_adjust`, `maintenance_activate`, `pipeline`, `latest_run`, `artifacts`, `regions`, `borders`, `settings`, and `update_settings`. `screenshot` returns a PNG image, so an agent can look at the map or the maintenance screen.

The launcher and the API share a key at `geodata/state/launcher.key`, created on the launcher's first run and gitignored. They talk over a Unix socket at `geodata/state/launcher.sock` with AES-256-GCM frames. The session key is replaced every 15 minutes and both sides log each rotation. The socket and the key are owner-only, and the HTTP surface binds loopback only.

An agent drives the window with `POST /api/launcher/navigate`, `POST /api/launcher/keys`, or `POST /api/maintenance/activate`. Destructive actions take `confirm: true`, which replaces the second Enter press.

## Headless data pipeline

```bash
dotnet run --project src/GeoTools.Launcher -- --geodata status
dotnet run --project src/GeoTools.Launcher -- --geodata rebuild [--force] [--region africa]
dotnet run --project src/GeoTools.Launcher -- --geodata pull|extract|cache [--force]
```

The headless mode uses the same database, artifacts, and message log as the window.

## Development

- Build: `dotnet build geodata-tools.slnx`
- Test: `dotnet test geodata-tools.slnx`
- Coverage: `scripts/coverage.sh` enforces 95% line coverage per Shared assembly and overall.
- Format: `dotnet tool restore && dotnet csharpier check src`

`geodata/` is gitignored and holds about 80 GB of PBFs in a full checkout. Unit tests use temp files and temp SQLite databases. The API integration tests run in process with a temp database and a border fixture. The verify skill in `.agents/skills/verify/` drives the real window under Xvfb.
