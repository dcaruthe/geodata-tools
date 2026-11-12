# Border extraction

`scripts/extract_borders.sh` turns downloaded OSM PBF files into the GeoJSON Text Sequence files the Map view loads.

## Sub-features

- `extract-region` builds one region or all regions.
- `extract-levels` keeps only `ADMIN_LEVELS` and writes the `.levels` sidecar.
- `extract-skip` skips a region whose output is newer than its input and whose sidecar matches.
- `extract-bad-levels` rejects a malformed `ADMIN_LEVELS` and exits non-zero.
- `extract-missing-input` reports a missing PBF and continues with the other regions.

## How to get to it (user POV)

- Run `scripts/extract_borders.sh [region ...]` from the repo root.
- Set `ADMIN_LEVELS=2,4` to choose levels. The default is `2,4`.

## Driving it with app-session.sh

Preconditions:

- `osmium` and `python3` are on `PATH`.
- `geodata/<region>-latest.osm.pbf` exists for the regions under test.
- The app is stopped. This feature needs no display, so `app-session.sh` is only needed when the Map view consumes the result.

- **Up-to-date skip.** Run `scripts/extract_borders.sh africa`. With a current output and sidecar, stdout reads `africa: up to date`, the exit code is `0`, and the output file keeps its mtime.
- **Levels sidecar.** Run `cat geodata/borders/africa-boundaries.levels`. It reads `2,4` when the last extraction used the default levels. The script uses the sidecar to decide whether it can skip.
- **The map sees the levels.** Start a session with the extracted data. The Map view's `Extracted:` list matches the sidecar contents.
- **Bad levels.** Run `ADMIN_LEVELS=2,x scripts/extract_borders.sh africa`. The exit code is `1` and stderr reads `ADMIN_LEVELS must be a comma-separated list of admin levels, got: '2,x'`.
- **Missing input.** Run `scripts/extract_borders.sh no-such-region`. The exit code is `1`, stderr reads `no-such-region: missing input geodata/no-such-region-latest.osm.pbf`, and the summary marks the region `MISSING`.

## Gotchas

- `scripts/pull_geodata.sh` downloads tens of gigabytes. It is never part of a verification run.
- A failed export removes the stale output and sidecar first. Re-run after fixing the cause; do not trust a partial file.
- The script uses GNU `stat` and `readlink -f`. It targets Linux.
- Region names must match the PBF base names, for example `africa` for `geodata/africa-latest.osm.pbf`.
