# Map focus

Pressing `f` focuses the administrative region under the crosshair. The camera fits the whole
region with 24 px padding, the region's outline is drawn in the highlight color `(255, 204, 102)`
with its name label, and the next extracted admin level is revealed inside it. `Backspace` pops
the focus stack. The same focus, blur, pan, zoom, and map-state operations are exposed over the
link, REST, GraphQL, and MCP.

## Sub-features

- `map-focus-hit` hit-tests the camera center against the region index and focuses the deepest
  region that contains it.
- `map-focus-frame` fits the region's projected bounds with 24 px padding so every border is in
  frame and at least one axis is tight.
- `map-focus-highlight` draws the focused region's outline in `(255, 204, 102)` and its name label
  at the region's top-left.
- `map-focus-children` reveals the next extracted admin level inside the focused region in
  `(140, 190, 235)`.
- `map-focus-stack` pushes each focused region; `Backspace` pops back to the parent and then to the
  world fit.
- `map-focus-status` reports the focused region, the revealed level and child count, or "deepest
  extracted level" when no deeper level exists. A key press first shows
  `Finding region under the crosshair...`.
- `map-api-focus` runs focus, blur, pan, and zoom over the link, REST, GraphQL, and MCP, and
  reports the camera, zoom, viewport, focus stack, visible levels, and window size.

## How to get to it (user POV)

- Open the app and wait for the map. Move the crosshair over a country with the arrow keys or WASD.
- Press `f` to focus the region under the crosshair. Press `f` again on a state to focus that state.
- Press `Backspace` to pop back to the parent, then again to return to the world.

## Driving it with app-session.sh

Preconditions:

- `doctor` reports `ok` with a border count above zero.
- A session is running against an indexed database, and the Map view is active. `wait 'Composed map'`.

Key names for `app-session.sh keys`: `f` focuses, `BackSpace` blurs.

- **Focus Germany.** `keys f` after centering the crosshair near `10.45, 51.16`. Wait for
  `Focused region region:51477`, then `shot focus-germany`. The log reads
  `Focused region region:51477 Deutschland at level 2 revealing level 4 with 19 children`.
  The whole country fills the frame, its outline is `(255, 204, 102)`, the label reads
  `Deutschland`, and the state borders are `(140, 190, 235)`.
- **Focus a state.** Center the crosshair near `11.4, 48.8` and press `f`. The log reads
  `Focused region region:2145268 Bayern at level 4 revealing level 0 with 0 children`. Because
  the extract has no level 6, the status reads `deepest extracted level` and the camera frames
  Bavaria.
- **Blur.** Press `Backspace` twice. The stack returns to Germany, then to the world fit. The
  overlay clears and the world fit returns. `shot focus-blur-parent` shows Germany highlighted and
  `shot focus-blur-world` shows the unhighlighted world.

### Driving it with the API

After `api-start`, the camera and focus are scriptable. Read `API_PORT` from `status`.

- **Map state.** `curl -s "$A/api/map"` returns `centerLongitude`, `centerLatitude`, `scale`,
  `zoom`, `viewport`, `focus`, `visibleLevels`, `status`, `windowWidth`, and `windowHeight`.
  `zoom` is the scale relative to the fit of the loaded geometry bounds.
- **Focus.** `curl -s -X POST "$A/api/map/focus" -H 'Content-Type: application/json' -d '{"longitude":10.45,"latitude":51.16}'`
  returns `accepted` and waits are driven with `wait 'Focused region region:51477'`.
- **Focus by id.** `POST /api/map/focus-region` with `{"regionId":"region:51477"}`.
- **Blur.** `POST /api/map/blur` pops the stack; it returns 409 when nothing is focused.
- **Pan and zoom.** `POST /api/map/pan` with `{"dx":64,"dy":0}` and `POST /api/map/zoom` with
  `{"direction":1}`. The log gains a `Camera center` line per command and a `Composed map` line.
- **GraphQL.** `{ mapState { centerLongitude zoom } }` and the mutations `focusAt`,
  `focusRegion`, `blurFocus`, `pan`, and `zoom`.
- **MCP.** The tools are `map_state`, `focus_at`, `focus_region`, `blur_focus`, `pan`, and `zoom`,
  where `zoom` takes `direction` 1 for in and -1 for out.

## Pass predicates

- `GET /api/map` reports `zoom: 1` and an empty `focus` before any focus, with the fit camera.
- After focusing Germany, `focus` is `[region:51477]`, `visibleLevels` is `[2,4]`, and the
  viewport bounds from the same payload contain the focused region's bounds, so every border is in
  frame.
- The `Focused region` log line names the region, its level, the revealed level, and the child
  count. The compose line reports the rebuilt scene at the new zoom.
- After focusing a level-4 state with no deeper extract, the status reads `deepest extracted
  level`, the focus stack is `[<country>, <state>]`, and the camera frames the state.
- `POST /api/map/blur` twice returns the stack to the parent and then to empty, restoring the world
  fit (`zoom: 1`).
- `POST /api/map/zoom` with `direction` 1 prints a `Camera center` line with a higher zoom factor;
  `POST /api/map/pan` with `dx` 64 moves the longitude east and logs `translated`.
- The focused region's outline is `(255, 204, 102)`, the revealed state borders are
  `(140, 190, 235)`, and the region name label is on screen. The blur-to-world capture has no
  highlight pixels.
- With the launcher down, `GET /api/map` and every map command route return 409.

## Gotchas

- Focus is bounded by the extracted levels. The default extract is `[2,4]`, so a country reveals
  its level-4 states, and a state reports `deepest extracted level`.
- The camera moves as soon as the hit test returns; the region and its children load off the
  render thread. Wait for the `Focused region` log line and the next `Composed map` line before
  capturing.
- The border store keeps the extracted admin levels during focus. The focused visible set is
  separate state, so Maintenance still reports every extracted level and the missing-level message
  stays correct.
- A blur pops one level at a time. Two blurs from a state go to the country and then to the world.
- Focus depth is bounded by the extracted levels; the label and highlight are the overlay store,
  which clears on a blur to the world.
