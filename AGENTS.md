# geodata-tools

Desktop map viewer for OpenStreetMap border extracts. .NET 10 on Linux with an X11 display.

## Commands

- Build: `dotnet build geodata-tools.slnx`
- Test all: `dotnet test geodata-tools.slnx`
- One test class: `dotnet test src/Shared/Tests/GeoTools.Shared.Presentation.Tests --filter FullyQualifiedName~MapViewTests`
- Format: `dotnet tool restore && dotnet csharpier check src`, or `dotnet csharpier format src`
- Coverage gate: `scripts/coverage.sh`, at 95% line coverage per Shared assembly and overall
- Run the app: `dotnet run --project src/GeoTools.Launcher`
- Run the API: `dotnet run --project src/GeoTools.Api`
- Headless pipeline: `dotnet run --project src/GeoTools.Launcher -- --geodata status|pull|extract|cache|rebuild [--force] [--region name]`

## Layout

`Domain` (geo and color primitives) <- `Application` (projection, geometry, scene DTOs, view contracts, message contracts, link contracts, state contracts) <- `Infrastructure` (SQLite/EF persistence, message bus, geodata pipeline, GeoJSON parsing, Skia canvas, OpenGL surface, link crypto and transport) <- `Presentation` (views, workers, window, input routing, link server, DI) <- `Launcher` (host entry, headless CLI). Keep dependencies pointing left.

`GeoTools.Api` is a second host beside `Launcher`. It serves REST with Swagger, GraphQL with the HotChocolate IDE, and an MCP server, all over one `GeoToolsFacade`. It reads the state database read-only and drives the window through the launcher link. It is not a layer, so it may reference `Infrastructure` and `Application` but never `Presentation`.

- Each layer registers itself in its `ConfigureServices.cs`. `Launcher` calls `SetupPresentationLayer`, which chains the infrastructure and application layers.
- Most implementations are `internal` and contracts are `public`. Each Shared project's `Global.cs` holds `InternalsVisibleTo` for its test assembly.
- `RenderingWorker.StartAsync` blocks on the GLFW loop because GLFW needs the main thread. Do not move the loop to a background thread.
- Hosted service order is load-bearing: the message bus attaches subscriptions before it starts, the geodata worker starts before the render worker, and `RenderingWorker` is registered last.
- `SceneWindow.Create` pins X11 so Wayland's libdecor cannot pull GTK into the process. Do not remove it.

## Architecture rules

- The render thread never touches disk, HTTP, the process table, or the database. Views read immutable snapshots from `IProgressStore` and `IBorderGeometryStore`; subscribers swap the snapshots off the render thread.
- Link commands that touch the window cross `WindowCommandInbox` and run on the render thread inside `SceneWindow.OnUpdateFrame`. Reads answer from thread-safe snapshots without the pump. Settings commands run on the link thread because `ISettingsStore` is already thread-safe.
- The API opens the state database read-only and wraps the geometry cache in `ReadOnlyGeometryCache`. The launcher stays the single writer. An agent command that changes settings or queues work goes through the link, never through the API's own connection.
- Screenshots render the active scene through `SkiaOffscreenSurface`, the same renderer and canvas the window uses. They are not framebuffer grabs, so a GL-only defect will not show in a capture.
- Workers communicate through `IMessageBus`. Messages are appended to SQLite in one writer, delivered at least once, and subscribers persist cursors. Handlers are idempotent.
- The database uses WAL, `synchronous=FULL`, foreign keys, and a single writer. Schema changes go through `SqliteSchema` and `PRAGMA user_version`; do not add EF migrations.
- Every pipeline step is idempotent. Downloads use temp files and conditional GET, extraction writes a temp output and renames it, and startup adopts current files instead of rebuilding them.
- The region index is written only by the launcher. `Regions` holds one row per polygon feature (name, level, bounds, rings blob, parent id, parent-resolved flag, area, source key); `BorderLines` holds non-polygon features; `RegionResolutions` marks each resolved parent/level pair. Parents resolve lazily on the first focus or children read, and a geodata change clears every parent link and marker so nothing dangles.

## Data and config

- `geodata/` is gitignored and about 80 GB. Never commit it, and do not download it unless the task needs the pipeline. Tests use temp files and fakes.
- `scripts/pull_geodata.sh` and `scripts/extract_borders.sh` delegate to the headless runtime mode. `osmium` is the only external extraction dependency.
- `Map:AdminLevels` selects the levels the map draws. `Geodata:ExtractAdminLevels` selects the levels written to the border files. They are separate on purpose.
- The map is driven with `WASD` and the arrow keys (pan), `+`/`-` (zoom), `F` (focus the region under the crosshair), and `Backspace` (pop the focus stack).
- The region index reads over REST (`/api/admin-regions`, `/api/admin-regions/at`, `/api/admin-regions/stats`, `/api/admin-regions/{id}`, `/api/admin-regions/{id}/children`), GraphQL (`adminRegions`, `adminRegion`, `regionsAt`, `regionChildren`, `regionIndex`), and MCP (`admin_regions`, `admin_region`, `regions_at`, `region_children`, `region_index`). All are read-only and answer from the read-only database while the launcher is offline.
- `action:reindex` (and the `Reindex` pipeline action) clears the index and queues a `RebuildScope.RegionIndex` run, which plans only the Load step; the loader backfills the index from the warm geometry cache.
- `Geodata:RegionList` and `Geodata:ExtractAdminLevelsList` are comma-separated overrides that replace their arrays, because environment variables cannot express an array cleanly.
- `Storage:DatabasePath` defaults to `geodata/state/geotools.db`. Runtime settings and feature flags are stored there and layered over configuration.
- `Link:SocketPath` defaults to `geodata/state/launcher.sock` and `Link:KeyPath` to `geodata/state/launcher.key`. The launcher creates the key on its first run with mode 0600, and it is gitignored. `Link:RotateSeconds` defaults to 900 and replaces the session key on that interval.
- The API binds `http://127.0.0.1:5180` and accepts only loopback hosts. Its `Storage:ReadOnly` must stay true. `opencode.json` connects the `geotools` MCP server for agents.
- MCP tools live in `src/GeoTools.Api/Mcp/GeoToolsTools.cs` and stay thin over `GeoToolsFacade`, so REST, GraphQL, and MCP cannot drift. `borders` refuses a GeoJSON response over 100000 points; narrow the bounds instead of raising the cap.
- A relative `Geodata:RootPath` or `Storage:DatabasePath` resolves by walking up to `geodata-tools.slnx`.
- Missing border data is a supported state. The loading screen shows the bootstrap error, the map shows an instruction, and Maintenance offers a rebuild.

## Tests

- xunit with FluentAssertions and Moq. Names read `Method_expected_behavior`.
- Tests are headless. Never construct `SceneWindow` or an OpenGL context. Use hand-written fakes, `NullLogger`, and the local `TestOptionsMonitor`.
- Database tests use `TestDatabase` with a temp SQLite file. Clear pools before deleting a file, because Microsoft.Data.Sqlite pools connections.
- Coverage counts line coverage per Shared assembly. Three host adapters carry `[ExcludeFromCodeCoverage]` because they need a live GL context, so do not add the attribute to hide ordinary logic. `.agents/skills/verify/` proves those paths by running the real window.
- `src/Tests/GeoTools.Api.Tests` drives the API in process with `WebApplicationFactory` and the MCP client. It proves the REST shapes, the GraphQL query, the offline launcher, and the MCP handshake without a window.

## Verifying UI changes

Use the project skill in `.agents/skills/verify/`. It launches the app under Xvfb with an isolated state database, drives the keyboard, captures screenshots in `.audit/verify/`, and cleans up the processes it started. A fresh verify database parses the border files on first run, which takes about 12 seconds and proves the loading screen.

Run `.agents/skills/verify/scripts/app-session.sh api-start` after `start` to add the API on a free loopback port. The session uses its own socket and key under the run directory, so a verify run never touches the window a person is using.
