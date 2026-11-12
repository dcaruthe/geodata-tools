# Geodata pipeline

The runtime owns the pull, extraction, and geometry cache. The shell scripts are thin wrappers over the headless mode.

## Sub-features

- `pipeline-adopt` records current PBF and border files as artifacts instead of rebuilding them.
- `pipeline-extract` runs osmium, filters the requested levels, writes the `.levels` sidecar, and caches the geometry.
- `pipeline-download` downloads a missing or forced PBF with resume and conditional GET.
- `pipeline-cache` reads the parsed geometry from SQLite and skips osmium when the cache is current.
- `pipeline-skip` leaves current outputs untouched on the next run.
- `pipeline-failure` surfaces a failed stage on the loading screen and in Maintenance.
- `pipeline-headless` exposes the same pipeline through `--geodata`.
- `pipeline-wrappers` keeps `scripts/pull_geodata.sh` and `scripts/extract_borders.sh` as entry points.

## Fixture

`src/Shared/Tests/Fixtures/tiny.osm` builds `fixture-latest.osm.pbf`. It has one level 2 boundary and one level 4 boundary, so extraction is fast and needs no network. Regenerate the PBF with:

```bash
osmium cat src/Shared/Tests/Fixtures/tiny.osm \
  -o src/Shared/Tests/Fixtures/fixture-latest.osm.pbf --overwrite
```

Every recipe below uses a temp root, never the real `geodata/`.

## Driving it with app-session.sh

Preconditions:

- `osmium` is on `PATH` for the extraction recipes.
- `doctor` reports `ok`.

- **Extract a region.** Create a root and copy the fixture PBF:

  ```bash
  FIXTURE=$(mktemp -d)
  cp src/Shared/Tests/Fixtures/fixture-latest.osm.pbf "$FIXTURE/"
  VERIFY_GEODATA_ROOT="$FIXTURE" VERIFY_REGIONS=fixture VERIFY_NO_PULL=1 \
    .agents/skills/verify/scripts/app-session.sh start
  ```

  Run `wait 'Extracted fixture at levels'` and then `wait 'finished with Succeeded'`. The log names the levels and feature count, and `<root>/borders/fixture-boundaries.geojsonseq` exists. `cat <root>/borders/fixture-boundaries.levels` reads `2,4`.
- **Cache reuse.** Stop. Start again with the same root and `VERIFY_DB` pointing at the database from the first run. The log reads `Loaded 1 border files ... (1 cached, 0 parsed)` and no `Extracted` line. The run finishes in about half a second.
- **Skip current outputs.** Note `stat -c %Y <root>/borders/fixture-boundaries.geojsonseq` before a third run. After it, the mtime is unchanged and the log has no `Extracted` line.
- **Adopt existing files.** Build a second root with the PBF, the border file, and the sidecar, with the border newer than the PBF. Start with a fresh database and the same env. The log reads `Loaded 1 border files ... (0 cached, 1 parsed)` and there is no `Extracted` line. The pipeline adopted the files through the artifact table.
- **Download then extract.** Serve the fixture PBF over loopback:

  ```bash
  SERVER=$(mktemp -d)
  cp src/Shared/Tests/Fixtures/fixture-latest.osm.pbf "$SERVER/"
  python3 -m http.server 8765 --directory "$SERVER" >"$SERVER/http.log" 2>&1 &
  HTTP_PID=$!
  ROOT=$(mktemp -d)
  VERIFY_GEODATA_ROOT="$ROOT" VERIFY_REGIONS=fixture \
    VERIFY_SOURCE_BASE_URL=http://127.0.0.1:8765/ \
    .agents/skills/verify/scripts/app-session.sh start
  ```

  Run `wait 'Downloaded fixture:'`, `wait 'Extracted fixture at levels'`, and `wait 'finished with Succeeded'`. `$SERVER/http.log` shows `GET /fixture-latest.osm.pbf`. Stop the server with `kill "$HTTP_PID"`.
- **Force repull.** With the local server and a current fixture root, run `keys 2`, then send `Down` until the selection reaches `Force repull`, then `keys Return Return`. The server log shows another GET, the app log shows `Downloaded fixture:` again, and the run succeeds.
- **Failure.** Start a fixture root that has the PBF but no borders, with `VERIFY_OSMIUM=/bin/false` and `VERIFY_NO_PULL=1`. Wait for `finished with Failed`. The log names the failed stage (`level filter failed with exit code 1`), the loading screen shows the error, and no border file appears.
- **Headless status.** Run:

  ```bash
  Geodata__RootPath="$FIXTURE" Geodata__Regions__0=fixture \
    Storage__DatabasePath="$FIXTURE/state.db" \
    dotnet run --project src/GeoTools.Launcher -- --geodata status
  ```

  It prints each artifact with its kind, region, size, and status. `--geodata extract --region fixture` runs the same pipeline without a display.

## Gotchas

- Never point the download recipes at geofabrik. The real continental PBFs are about 80 GB.
- `Rebuild borders` on the real data takes hours. Use the fixture.
- `osmium` is the only external extraction dependency. `python3` is no longer used at runtime.
- A failed stage removes its partial output, so a later run does not trust a half-written border file.
- The `.levels` sidecar is still written and is the fastest way to inspect the extracted levels.
- The runtime records artifact sizes and modification times in SQLite. Deleting the database makes the next run parse the border files again and adopt the current outputs.
