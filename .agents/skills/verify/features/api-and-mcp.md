# API and MCP

`GeoTools.Api` is a second host beside the launcher. It serves REST with Swagger, GraphQL with the HotChocolate IDE, and an MCP server over Streamable HTTP. All three surfaces sit on `GeoToolsFacade`. Live window state, logs, screenshots, and commands come from the launcher over a Unix-socket link. Geodata queries read the state database and the border files directly, so they still answer with the window closed.

## Sub-features

- `api-health` reports the API, the launcher link, and the database.
- `api-state` returns launcher state when connected and the local database summary always.
- `api-screenshot` renders the active view to a PNG. It returns 409 when the launcher is down.
- `api-navigate` switches the window to a view.
- `api-keys` sends arrows, Enter, Backspace, Home, and End to the active view. It cannot switch views; use `navigate` for that.
- `api-maintenance` reads the rows and runs select, adjust, activate (with confirm), and cancel.
- `api-settings` reads live settings from the launcher and falls back to the local database when it is down.
- `api-pipeline` queues check, repull, extract, cache, cancel, retry, and reset.
- `api-geodata` lists regions and queries border polylines as a summary or GeoJSON.
- `api-graphql` serves the same reads and commands at `/graphql` with the Nitro IDE at `/graphql/`.
- `api-swagger` documents the REST routes at `/swagger`.
- `api-mcp` exposes the tools at `/mcp` as Streamable HTTP in stateless mode.
- `api-link-key` creates `launcher.key` with mode 0600 on the launcher's first run.
- `api-link-rotation` replaces the session key every `Link:RotateSeconds`.
- `api-offline` keeps geodata available while reporting `launcherStatus: offline`.

## How to get to it (user POV)

- Start the launcher and leave it running.
- Start the API with `dotnet run --project src/GeoTools.Api`, or `app-session.sh api-start` for a verify run.
- Open `/swagger`, `/graphql/`, or point an MCP client at `/mcp`.

## Driving it with app-session.sh

Preconditions:

- `doctor` reports `ok`.
- Export `VERIFY_ROTATE_SECONDS=5` before both `start` and `api-start`. The rotation schedule is read at startup on both sides.
- `api-start` picks a free port from 5390 upward, not 5180. Read `API_PORT` from `status` or from `.audit/verify/.session`.

```bash
S=.agents/skills/verify/scripts/app-session.sh
export VERIFY_ROTATE_SECONDS=5
$S start
$S api-start
source .audit/verify/.session
A="http://127.0.0.1:$API_PORT"
```

- **Health and state.** `curl -s "$A/health"` returns `"status":"ok"`, `"launcher":"connected"`, and `"databaseAvailable":true`. `curl -s "$A/api/state"` returns `"launcherStatus":"connected"`, the active view, and `launcher.link.rotateSeconds` equal to 5.
- **Map screenshot.** `curl -s -o "$RUN_DIR/api-map.png" -w '%{http_code}\n' "$A/api/launcher/screenshot"` returns 200 and `file` reports a PNG at `Window:Width` by `Window:Height`. Save its `sha256sum`.
- **Navigate.** `curl -s -X POST "$A/api/launcher/navigate" -H 'Content-Type: application/json' -d '{"view":"Maintenance"}'` returns `"accepted":true`. `app.log` gains `Switched view Map -> Maintenance`.
- **Screenshot changes.** Take a second screenshot to `api-maintenance.png`. Its hash differs from the map capture. `app-session.sh shot maintenance` shows the same screen from the X root.
- **MCP tools.** POST `initialize` and then `tools/call` to `"$A/mcp"` with `Accept: application/json, text/event-stream`.
  - `{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"state","arguments":{}}}` returns one text block containing `"launcherStatus"`.
  - `{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"screenshot","arguments":{"width":600,"height":400}}}` returns an `"type":"image"` block. Decode its base64 `data` and assert the PNG is 600 by 400.
- **GraphQL and Swagger.** POST `{"query":"{ state { launcherStatus } settings { tolerance } }"}` to `/graphql` and expect `"launcherStatus":"connected"`. `GET /graphql/` with `Accept: text/html` returns the IDE, and `GET /swagger/v1/swagger.json` contains `/api/borders`.
- **Geodata.** `GET /api/borders?format=geojson&levels=2` returns a FeatureCollection with `LineString` features. `GET /api/regions` reports the fixture region, the available admin levels, and a non-zero `polylineCount`.
- **Rotation.** After about 7 seconds, `GET /api/state` shows `launcher.link.rotations` at 1 or more with a non-null `lastRotatedAt`. `app.log` contains `Accepted a link session key rotation`. A screenshot still returns 200 under the new key.
- **Launcher down.** Run `app-stop`, which kills only the app and leaves Xvfb and the API. `GET /health` still returns 200 with `"launcher":"offline"`, and `GET /api/state` keeps `launcherStatus: offline`. The screenshot and `/api/maintenance` return 409 with `"code":"launcher-offline"`. `GET /api/borders` still returns the fixture. Finish with `$S stop`.

## Gotchas

- REST screenshots are offscreen scene renders at the window size. `app-session.sh shot` captures the 1280x1024 X root. Never compare the two byte for byte.
- The MCP endpoint replies as SSE. A plain JSON POST without the two-value `Accept` header fails.
- The API's settings read goes through the link while the launcher is up, so it sees a change made in Maintenance immediately. The local fallback is a snapshot from API startup and is stale after an offline change.
- `update_settings` and the maintenance adjust commands return before the write finishes. The status reads `Change queued.` and the next settings read shows the new value.
- A GeoJSON query over 100000 points is refused. Narrow the bounds or raise `limit` within 2000; do not raise the point cap.
- `press_keys` cannot switch views. `navigate` is the only way to change the view through the link.
