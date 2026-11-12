# geodata-tools

Desktop map viewer for OpenStreetMap border extracts. .NET 10 on Linux with an X11 display.

## Commands

- Build: `dotnet build geodata-tools.slnx`
- Test all: `dotnet test geodata-tools.slnx`
- One test class: `dotnet test src/Shared/Tests/GeoTools.Shared.Presentation.Tests --filter FullyQualifiedName~MapViewTests`
- Format: `dotnet tool restore && dotnet csharpier check src`, or `dotnet csharpier format src`
- Coverage gate: `scripts/coverage.sh`, at 95% line coverage per Shared assembly and overall
- Run the app: `dotnet run --project src/GeoTools.Launcher`

## Layout

`Domain` (geo and color primitives) <- `Application` (projection, geometry, scene DTOs, view contracts) <- `Infrastructure` (GeoJSON border loading, Skia canvas, OpenGL surface) <- `Presentation` (views, components, window, render worker, DI) <- `Launcher` (host entry). Keep dependencies pointing left.

- Each layer registers itself in its `ConfigureServices.cs`. `Launcher` calls `SetupPresentationLayer`, which chains the infrastructure and application layers.
- Most implementations are `internal` and contracts are `public`. Each Shared project's `Global.cs` holds `InternalsVisibleTo` for its test assembly.
- `RenderingWorker.StartAsync` blocks on the GLFW loop because GLFW needs the main thread. Do not move the loop to a background thread.
- `SceneWindow.Create` pins X11 so Wayland's libdecor cannot pull GTK into the process. Do not remove it.

## Data and config

- `geodata/` is gitignored and about 80 GB. Never commit it, and do not download it unless the task needs the pipeline. Tests use temp files and fakes.
- `scripts/pull_geodata.sh` downloads continental PBFs with `wget`. `scripts/extract_borders.sh [region ...]` builds `geodata/borders/*.geojsonseq` with `osmium` and `python3`; `ADMIN_LEVELS` defaults to `2,4`. Both target GNU/Linux.
- Config lives in `Launcher/appsettings.json` and is copied to the output directory. A relative `Map:BordersDirectory` resolves by walking up to `geodata-tools.slnx`.
- Border geometry is cached for the process lifetime. Restart after re-extracting.
- Override config for one run from the environment, as in `Window__Title=GeoToolsVerify` or `Map__AdminLevels__0=6`.
- Missing border data is a supported state. The map renders an instruction instead of failing.

## Tests

- xunit with FluentAssertions and Moq. Names read `Method_expected_behavior`.
- Tests are headless. Never construct `SceneWindow` or an OpenGL context. Use hand-written fakes, `NullLogger`, and the local `TestOptionsMonitor`.
- Coverage counts line coverage per Shared assembly. Three host adapters carry `[ExcludeFromCodeCoverage]` because they need a live GL context, so do not add the attribute to hide ordinary logic. `.agents/skills/verify/` proves those paths by running the real window.

## Verifying UI changes

Use the project skill in `.agents/skills/verify/`. It launches the app under Xvfb, drives the keyboard, captures screenshots in `.audit/verify/`, and cleans up the processes it started.
