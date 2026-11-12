# Admin level configuration

`Map:AdminLevels` selects which extracted levels the Map view draws. A level that is not in the data produces an explicit message instead of a blank map.

## Sub-features

- `levels-default` draws the single level from `appsettings.json`, which defaults to `2`.
- `levels-override` replaces the configured levels with `VERIFY_ADMIN_LEVEL` for one run.
- `levels-none` shows the no-levels message when the selection is empty or all values are below `1`.
- `levels-missing` lists the requested and extracted levels when the selection has no data.

## How to get to it (user POV)

- Edit `Map:AdminLevels` in `src/GeoTools.Launcher/appsettings.json` and restart, or
- set `Map__AdminLevels__0` in the environment for one run.

## Driving it with app-session.sh

Preconditions:

- `doctor` reports `ok`.
- The border data contains levels `2` and `4`.

- **Default level.** Start a normal session. The log reads `Composed map at 1200x800 with admin levels 2: ...`. Run `shot map`; the map draws level 2 only.
- **Override one level.** Stop, then run `VERIFY_ADMIN_LEVEL=6 .../app-session.sh start`. The log reads `admin levels 6`. Run `shot admin-level-6`; the screen reads `No borders at admin levels 6. Extracted: 2,4.`
- **No levels.** Start with `VERIFY_ADMIN_LEVEL=0` and a small borders directory. The log's level list is empty. Run `shot admin-level-none`; the screen reads `No admin levels selected. Set Map:AdminLevels in appsettings.json.`
- **Replacement, not merge.** With the override, the log lists only `6`. A run that lists `2,6` means the configured array was merged with the class default.

## Gotchas

- The environment value replaces the configured array. Setting one index does not add to it.
- Levels are sorted and de-duplicated before use, and values below `1` are dropped.
- Environment overrides need a restart. The app reads options at startup.
- `Extracted:` comes from the loaded border files, not from the `.levels` sidecar. Re-extract before expecting a level to appear there.
