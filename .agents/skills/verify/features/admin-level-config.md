# Admin level configuration

The map displays the levels selected at runtime. Extraction writes the levels selected for the pipeline. They are separate settings.

## Sub-features

- `levels-default` draws the display levels seeded from `Map:AdminLevels`, which defaults to `2`.
- `levels-override` replaces the seeded levels with `VERIFY_ADMIN_LEVEL` on a fresh database.
- `levels-presets` cycles none, `2`, `2,4`, and `2,4,6` from Maintenance.
- `levels-none` shows the no-levels message when the selection is empty.
- `levels-missing` lists the requested and extracted levels when the selection has no data.
- `levels-extract` changes the extraction levels; the sidecar and the Extracted levels row update after a rebuild.

## How to get to it (user POV)

- Open Maintenance with `2`, move to `Display levels`, and use `Left` or `Right`.
- Set `Map:AdminLevels` in `appsettings.json`, or `Map__AdminLevels__0` in the environment, before the first run.
- Set `Geodata:ExtractAdminLevels` or use the `Extract levels` row.

## Driving it with app-session.sh

Preconditions:

- `doctor` reports `ok`.
- The border data contains levels `2` and `4`.
- Display levels are seeded from configuration on a fresh `VERIFY_DB`.

- **Default level.** Start a normal session. The log reads `Composed map at 1200x800 with admin levels 2: ...`. Run `shot map`; the map draws level 2 only.
- **Runtime preset.** Run `keys 2`, select `Display levels` (already selected), then `keys Right` and `keys 1`. The log reads `admin levels 2,4` in a new compose line. Run `shot map-levels-2-4`.
- **Override one level.** Stop, then run `VERIFY_ADMIN_LEVEL=6 .../app-session.sh start` with a fresh database. The log reads `admin levels 6`. Run `shot admin-level-6`; the screen reads `No borders at admin levels 6. Extracted: 2,4.`
- **No levels.** Run `VERIFY_ADMIN_LEVEL=0 .../app-session.sh start`? A zero level fails configuration validation. Instead, select `Display levels` and press `Left` until the value reads `none`. Press `1`; the log's level list is empty and the screen reads `No admin levels selected. Change them in Maintenance (2).` Run `shot admin-level-none`.
- **Replacement, not merge.** With `VERIFY_ADMIN_LEVEL=6`, the log lists only `6`. A run that lists `2,6` means the configured array was merged with the class default.
- **Extraction levels.** With the fixture root from `geodata-pipeline.md`, open Maintenance, move to `Extract levels`, press `Right`, then select `Rebuild borders` and confirm. Wait for `Extracted fixture at levels 2,4,6`. `cat <root>/borders/fixture-boundaries.levels` reads `2,4,6`. The `Extracted levels` row updates after the refresh.

## Gotchas

- Runtime display levels persist in the database and override configuration. Use a fresh `VERIFY_DB` for an environment override.
- Levels are sorted and de-duplicated before use. Setters and configuration reject values below `1`; persisted rows with invalid values are dropped on load.
- `Extracted levels` comes from the loaded geometry, not from the `.levels` sidecar.
- Changing extraction levels does not re-extract immediately. The next startup or update check extracts because the artifact levels no longer match. `Rebuild borders` applies it now.
- Environment overrides need a restart. The app reads seeded configuration at startup.
