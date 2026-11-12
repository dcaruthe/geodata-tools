# geodata-tools

Desktop map viewer for OpenStreetMap border extracts. .NET 10 on Linux with an X11 display.

## Commands

- Build: `dotnet build geodata-tools.slnx`
- Test all: `dotnet test geodata-tools.slnx`
- One test class: `dotnet test src/Shared/Tests/GeoTools.Shared.Presentation.Tests --filter FullyQualifiedName~MapViewTests`
- Format: `dotnet tool restore && dotnet csharpier check src`, or `dotnet csharpier format src`
- Coverage gate: `scripts/coverage.sh`, at 95% line coverage per Shared assembly and overall
- Run the app: `dotnet run --project src/GeoTools.Launcher`
- Headless pipeline: `dotnet run --project src/GeoTools.Launcher -- --geodata status|pull|extract|cache|rebuild [--force] [--region name]`

## Layout

`Domain` (geo and color primitives) <- `Application` (projection, geometry, scene DTOs, view contracts, message contracts, state contracts) <- `Infrastructure` (SQLite/EF persistence, message bus, geodata pipeline, GeoJSON parsing, Skia canvas, OpenGL surface) <- `Presentation` (views, workers, window, input routing, DI) <- `Launcher` (host entry, headless CLI). Keep dependencies pointing left.

- Each layer registers itself in its `ConfigureServices.cs`. `Launcher` calls `SetupPresentationLayer`, which chains the infrastructure and application layers.
- Most implementations are `internal` and contracts are `public`. Each Shared project's `Global.cs` holds `InternalsVisibleTo` for its test assembly.
- `RenderingWorker.StartAsync` blocks on the GLFW loop because GLFW needs the main thread. Do not move the loop to a background thread.
- Hosted service order is load-bearing: the message bus attaches subscriptions before it starts, the geodata worker starts before the render worker, and `RenderingWorker` is registered last.
- `SceneWindow.Create` pins X11 so Wayland's libdecor cannot pull GTK into the process. Do not remove it.

## Architecture rules

- The render thread never touches disk, HTTP, the process table, or the database. Views read immutable snapshots from `IProgressStore` and `IBorderGeometryStore`; subscribers swap the snapshots off the render thread.
- Workers communicate through `IMessageBus`. Messages are appended to SQLite in one writer, delivered at least once, and subscribers persist cursors. Handlers are idempotent.
- The database uses WAL, `synchronous=FULL`, foreign keys, and a single writer. Schema changes go through `SqliteSchema` and `PRAGMA user_version`; do not add EF migrations.
- Every pipeline step is idempotent. Downloads use temp files and conditional GET, extraction writes a temp output and renames it, and startup adopts current files instead of rebuilding them.

## Data and config

- `geodata/` is gitignored and about 80 GB. Never commit it, and do not download it unless the task needs the pipeline. Tests use temp files and fakes.
- `scripts/pull_geodata.sh` and `scripts/extract_borders.sh` delegate to the headless runtime mode. `osmium` is the only external extraction dependency.
- `Map:AdminLevels` selects the levels the map draws. `Geodata:ExtractAdminLevels` selects the levels written to the border files. They are separate on purpose.
- `Geodata:RegionList` and `Geodata:ExtractAdminLevelsList` are comma-separated overrides that replace their arrays, because environment variables cannot express an array cleanly.
- `Storage:DatabasePath` defaults to `geodata/state/geotools.db`. Runtime settings and feature flags are stored there and layered over configuration.
- A relative `Geodata:RootPath` or `Storage:DatabasePath` resolves by walking up to `geodata-tools.slnx`.
- Missing border data is a supported state. The loading screen shows the bootstrap error, the map shows an instruction, and Maintenance offers a rebuild.

## Tests

- xunit with FluentAssertions and Moq. Names read `Method_expected_behavior`.
- Tests are headless. Never construct `SceneWindow` or an OpenGL context. Use hand-written fakes, `NullLogger`, and the local `TestOptionsMonitor`.
- Database tests use `TestDatabase` with a temp SQLite file. Clear pools before deleting a file, because Microsoft.Data.Sqlite pools connections.
- Coverage counts line coverage per Shared assembly. Three host adapters carry `[ExcludeFromCodeCoverage]` because they need a live GL context, so do not add the attribute to hide ordinary logic. `.agents/skills/verify/` proves those paths by running the real window.

## Verifying UI changes

Use the project skill in `.agents/skills/verify/`. It launches the app under Xvfb with an isolated state database, drives the keyboard, captures screenshots in `.audit/verify/`, and cleans up the processes it started. A fresh verify database parses the border files on first run, which takes about 12 seconds and proves the loading screen.
