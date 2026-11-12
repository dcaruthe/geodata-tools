# geodata-tools

Tooling for working with OpenStreetMap data.

After `scripts/pull_geodata.sh`, run `scripts/extract_borders.sh` to build the border geometry the Map view loads; `ADMIN_LEVELS` selects which admin levels are extracted.

## Development

Requires the .NET 10 SDK. The app needs an X11 display.

- Build: `dotnet build geodata-tools.slnx`
- Test: `dotnet test geodata-tools.slnx`
- Coverage: `scripts/coverage.sh` enforces 95% line coverage per Shared assembly and overall.
- Format: `dotnet tool restore && dotnet csharpier check src`
