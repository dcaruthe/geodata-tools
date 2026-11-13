#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT=$(cd -- "$(dirname -- "$(readlink -f -- "${BASH_SOURCE[0]}")")/../../../.." && pwd)
STATE_DIR="$REPO_ROOT/.audit/verify"
SESSION_FILE="$STATE_DIR/.session"
TITLE="${VERIFY_TITLE:-GeoTools verify}"
TIMEOUT="${VERIFY_TIMEOUT:-90}"
APP_DLL="$REPO_ROOT/src/GeoTools.Launcher/bin/Debug/net10.0/GeoTools.Launcher.dll"
API_DLL="$REPO_ROOT/src/GeoTools.Api/bin/Debug/net10.0/GeoTools.Api.dll"
GEODATA_ROOT="${VERIFY_GEODATA_ROOT:-$REPO_ROOT/geodata}"

die() {
  echo "$*" >&2
  exit 1
}

require_tools() {
  local missing=()
  local tool
  for tool in dotnet Xvfb xvfb-run import xdotool curl; do
    command -v "$tool" >/dev/null 2>&1 || missing+=("$tool")
  done

  [ "${#missing[@]}" -eq 0 ] || die "missing tools: ${missing[*]}"
}

load_session() {
  [ -f "$SESSION_FILE" ] || die "no verify session; run '$0 start' first"
  # shellcheck disable=SC1090
  source "$SESSION_FILE"
}

is_alive() {
  kill -0 "$1" 2>/dev/null
}

kill_pid() {
  local pid="$1"
  is_alive "$pid" || return 0
  kill "$pid" 2>/dev/null || true
  local i
  for i in $(seq 1 25); do
    is_alive "$pid" || return 0
    sleep 0.2
  done
  kill -9 "$pid" 2>/dev/null || true
}

doctor() {
  require_tools

  if [ -f "$SESSION_FILE" ]; then
    source "$SESSION_FILE"
    if is_alive "$APP_PID" || is_alive "$XVFB_PID"; then
      die "a verify session is running at $XDISPLAY ($RUN_DIR); stop it first"
    fi
    rm -f "$SESSION_FILE"
    echo "removed a stale session file"
  fi

  (cd "$REPO_ROOT" && dotnet build geodata-tools.slnx --nologo -v quiet) ||
    die "build failed"

  local borders="$GEODATA_ROOT/borders"
  if [ -d "$borders" ]; then
    echo "borders: $(find "$borders" -maxdepth 1 -name '*.geojsonseq' | wc -l) files in $borders"
  else
    echo "borders: missing $borders (the Map view will show its no-data message)"
  fi
  echo "geodata: $GEODATA_ROOT"
  echo "database: ${VERIFY_DB:-<run>/state.db}"

  echo "doctor: ok"
}

start() {
  require_tools
  [ ! -f "$SESSION_FILE" ] || die "a session file exists; run '$0 stop' or '$0 doctor'"

  mkdir -p "$STATE_DIR"
  RUN_DIR="$STATE_DIR/$(date -u +%Y%m%dT%H%M%SZ)-$$"
  mkdir -p "$RUN_DIR"

  if ! (cd "$REPO_ROOT" && dotnet build geodata-tools.slnx --nologo -v quiet) \
    >"$RUN_DIR/build.log" 2>&1; then
    tail -20 "$RUN_DIR/build.log" >&2
    die "build failed"
  fi
  [ -f "$APP_DLL" ] || die "missing $APP_DLL after build"

  local display_number=90
  while [ -e "/tmp/.X11-unix/X$display_number" ]; do
    display_number=$((display_number + 1))
  done
  XDISPLAY=":$display_number"

  Xvfb "$XDISPLAY" -screen 0 1280x1024x24 >"$RUN_DIR/xvfb.log" 2>&1 &
  XVFB_PID=$!
  local i
  for i in $(seq 1 50); do
    [ -S "/tmp/.X11-unix/X$display_number" ] && break
    sleep 0.1
  done
  if [ ! -S "/tmp/.X11-unix/X$display_number" ]; then
    kill_pid "$XVFB_PID"
    die "Xvfb did not start on $XDISPLAY"
  fi

  DB_PATH="${VERIFY_DB:-$RUN_DIR/state.db}"
  LINK_SOCKET="$RUN_DIR/launcher.sock"
  LINK_KEY="$RUN_DIR/launcher.key"
  local -a app_env=(
    "DISPLAY=$XDISPLAY"
    "Window__Title=$TITLE"
    "Storage__DatabasePath=$DB_PATH"
    "Geodata__RootPath=$GEODATA_ROOT"
    "Link__SocketPath=$LINK_SOCKET"
    "Link__KeyPath=$LINK_KEY"
    "Link__RotateSeconds=${VERIFY_ROTATE_SECONDS:-900}"
  )
  if [ -n "${VERIFY_ADMIN_LEVEL:-}" ]; then
    app_env+=("Map__AdminLevels__0=$VERIFY_ADMIN_LEVEL")
  fi
  if [ "${VERIFY_NO_PULL:-0}" = "1" ]; then
    app_env+=("Features__GeodataAutoPull=false")
  fi
  if [ "${VERIFY_RUN_ON_STARTUP:-1}" = "0" ]; then
    app_env+=("Geodata__RunOnStartup=false")
  fi
  if [ -n "${VERIFY_SOURCE_BASE_URL:-}" ]; then
    app_env+=("Geodata__SourceBaseUrl=$VERIFY_SOURCE_BASE_URL")
  fi
  if [ -n "${VERIFY_REGIONS:-}" ]; then
    app_env+=("Geodata__RegionList=$VERIFY_REGIONS")
  fi
  if [ -n "${VERIFY_OSMIUM:-}" ]; then
    app_env+=("Geodata__OsmiumPath=$VERIFY_OSMIUM")
  fi

  (
    cd "$REPO_ROOT"
    exec env "${app_env[@]}" dotnet "$APP_DLL"
  ) >"$RUN_DIR/app.log" 2>&1 &
  APP_PID=$!

  local ready=""
  for i in $(seq 1 "$TIMEOUT"); do
    if grep -q 'Window "' "$RUN_DIR/app.log" 2>/dev/null &&
      { grep -q 'Showing the loading view' "$RUN_DIR/app.log" 2>/dev/null ||
        grep -q 'Composed map' "$RUN_DIR/app.log" 2>/dev/null ||
        grep -q 'finished with' "$RUN_DIR/app.log" 2>/dev/null; }; then
      ready="yes"
      break
    fi
    if ! is_alive "$APP_PID"; then
      tail -20 "$RUN_DIR/app.log" >&2
      kill_pid "$XVFB_PID"
      die "the app exited before it became ready"
    fi
    sleep 1
  done

  if [ -z "$ready" ]; then
    tail -20 "$RUN_DIR/app.log" >&2
    kill_pid "$APP_PID"
    kill_pid "$XVFB_PID"
    die "the app was not ready after ${TIMEOUT}s"
  fi

  {
    printf 'RUN_DIR=%q\n' "$RUN_DIR"
    printf 'XDISPLAY=%q\n' "$XDISPLAY"
    printf 'APP_PID=%q\n' "$APP_PID"
    printf 'XVFB_PID=%q\n' "$XVFB_PID"
    printf 'TITLE=%q\n' "$TITLE"
    printf 'DB_PATH=%q\n' "$DB_PATH"
    printf 'GEODATA_ROOT=%q\n' "$GEODATA_ROOT"
    printf 'LINK_SOCKET=%q\n' "$LINK_SOCKET"
    printf 'LINK_KEY=%q\n' "$LINK_KEY"
    printf 'API_PORT=%q\n' ""
    printf 'API_PID=%q\n' ""
  } >"$SESSION_FILE"

  echo "session: $RUN_DIR"
  echo "display: $XDISPLAY"
  echo "title: $TITLE"
  echo "database: $DB_PATH"
  echo "link: $LINK_SOCKET"
  echo "log: $RUN_DIR/app.log"
}

window_id() {
  DISPLAY="$XDISPLAY" xdotool search --onlyvisible --name "$TITLE" | head -1
}

keys() {
  [ "$#" -gt 0 ] || die "usage: $0 keys <key> [key ...]"
  load_session
  is_alive "$APP_PID" || die "the app is not running"

  local window
  window=$(window_id)
  [ -n "$window" ] || die "no window matching '$TITLE'"

  DISPLAY="$XDISPLAY" xdotool windowfocus --sync "$window"
  DISPLAY="$XDISPLAY" xdotool key --clearmodifiers "$@"
  echo "sent: $*"
}

shot() {
  local name="${1:-capture}"
  load_session
  is_alive "$APP_PID" || die "the app is not running"

  DISPLAY="$XDISPLAY" import -window root "$RUN_DIR/$name.png"
  echo "$RUN_DIR/$name.png"
}

wait_for() {
  [ "$#" -gt 0 ] || die "usage: $0 wait <pattern> [timeout-seconds]"
  load_session
  local pattern="$1"
  local timeout="${2:-60}"
  local i
  for i in $(seq 1 "$timeout"); do
    if grep -qE "$pattern" "$RUN_DIR/app.log" 2>/dev/null; then
      echo "seen: $pattern"
      return 0
    fi
    if ! is_alive "$APP_PID"; then
      tail -20 "$RUN_DIR/app.log" >&2
      die "the app exited while waiting for: $pattern"
    fi
    sleep 1
  done
  tail -20 "$RUN_DIR/app.log" >&2
  die "timed out after ${timeout}s waiting for: $pattern"
}

status() {
  load_session
  local state="stopped"
  if is_alive "$APP_PID" && is_alive "$XVFB_PID"; then
    state="running"
  fi
  echo "state: $state"
  echo "display: $XDISPLAY"
  echo "title: $TITLE"
  echo "database: $DB_PATH"
  echo "link: $LINK_SOCKET"
  if [ -n "${API_PID:-}" ] && is_alive "$API_PID"; then
    echo "api: http://127.0.0.1:$API_PORT"
  else
    echo "api: stopped"
  fi
  echo "evidence: $RUN_DIR"
}

api_start() {
  load_session
  is_alive "$APP_PID" || die "the app is not running"
  [ -f "$API_DLL" ] || die "missing $API_DLL after build"
  [ -f "$LINK_KEY" ] || die "the launcher did not create $LINK_KEY"
  if [ -n "${API_PID:-}" ] && is_alive "$API_PID"; then
    die "the api is already running on port $API_PORT"
  fi

  API_PORT=$(find_port)
  local -a api_env=(
    "Urls=http://127.0.0.1:$API_PORT"
    "Storage__DatabasePath=$DB_PATH"
    "Geodata__RootPath=$GEODATA_ROOT"
    "Link__SocketPath=$LINK_SOCKET"
    "Link__KeyPath=$LINK_KEY"
    "Link__RotateSeconds=${VERIFY_ROTATE_SECONDS:-900}"
    "Logging__LogLevel__Default=Warning"
  )
  if [ -n "${VERIFY_REGIONS:-}" ]; then
    api_env+=("Geodata__RegionList=$VERIFY_REGIONS")
  fi

  (
    cd "$REPO_ROOT"
    exec env "${api_env[@]}" dotnet "$API_DLL"
  ) >"$RUN_DIR/api.log" 2>&1 &
  API_PID=$!

  local i
  for i in $(seq 1 60); do
    if curl -sS --max-time 1 "http://127.0.0.1:$API_PORT/health" >/dev/null 2>&1; then
      write_session
      echo "api: http://127.0.0.1:$API_PORT"
      echo "log: $RUN_DIR/api.log"
      return 0
    fi
    if ! is_alive "$API_PID"; then
      tail -20 "$RUN_DIR/api.log" >&2
      die "the api exited before it became ready"
    fi
    sleep 0.5
  done

  kill_pid "$API_PID"
  tail -20 "$RUN_DIR/api.log" >&2
  die "the api was not ready after 30s"
}

api_stop() {
  load_session
  if [ -n "${API_PID:-}" ] && is_alive "$API_PID"; then
    kill_pid "$API_PID"
  fi
  API_PID=""
  API_PORT=""
  write_session
  echo "api stopped"
}

app_stop() {
  load_session
  if is_alive "$APP_PID"; then
    kill_pid "$APP_PID"
  fi
  echo "app stopped; xvfb and api left running"
}

find_port() {
  local port=5390
  while curl -sS --max-time 0.3 "http://127.0.0.1:$port/health" >/dev/null 2>&1; do
    port=$((port + 1))
  done
  echo "$port"
}

write_session() {
  {
    printf 'RUN_DIR=%q\n' "$RUN_DIR"
    printf 'XDISPLAY=%q\n' "$XDISPLAY"
    printf 'APP_PID=%q\n' "$APP_PID"
    printf 'XVFB_PID=%q\n' "$XVFB_PID"
    printf 'TITLE=%q\n' "$TITLE"
    printf 'DB_PATH=%q\n' "$DB_PATH"
    printf 'GEODATA_ROOT=%q\n' "${GEODATA_ROOT:-}"
    printf 'LINK_SOCKET=%q\n' "${LINK_SOCKET:-}"
    printf 'LINK_KEY=%q\n' "${LINK_KEY:-}"
    printf 'API_PORT=%q\n' "${API_PORT:-}"
    printf 'API_PID=%q\n' "${API_PID:-}"
  } >"$SESSION_FILE"
}

stop() {
  if [ ! -f "$SESSION_FILE" ]; then
    echo "no verify session"
    return 0
  fi

  source "$SESSION_FILE"
  if [ -n "${API_PID:-}" ] && is_alive "$API_PID"; then
    kill_pid "$API_PID"
  fi
  kill_pid "$APP_PID"
  kill_pid "$XVFB_PID"
  rm -f "$SESSION_FILE"
  echo "stopped; evidence kept in $RUN_DIR"
}

usage() {
  cat <<EOF
usage: $0 <command>

  doctor     check tools, build, border data, and stale sessions
  start      start Xvfb and the app, then wait for the ready log lines
  api-start  start the API against this session's database and link socket
  api-stop   stop the API this session started
  app-stop   stop only the window, leaving Xvfb and the API running
  keys ...   send keys to the app window (1, 2, Tab, shift+Tab, Escape)
  shot ...   capture the root window to the run directory
  wait ...   wait for a log pattern, then report it
  status     report the session, database, link, and api state
  stop       kill the app, api, and Xvfb this session started

environment:
  VERIFY_TITLE        window title (default: $TITLE)
  VERIFY_TIMEOUT      readiness timeout in seconds (default: $TIMEOUT)
  VERIFY_GEODATA_ROOT geodata root (default: <repo>/geodata)
  VERIFY_DB           state database (default: <run>/state.db)
  VERIFY_NO_PULL      set to 1 to disable automatic downloads
  VERIFY_RUN_ON_STARTUP set to 0 to skip the startup run
  VERIFY_SOURCE_BASE_URL geodata source base URL (default: geofabrik)
  VERIFY_REGIONS      comma-separated region names for the pipeline
  VERIFY_OSMIUM       osmium executable (default: osmium)
  VERIFY_ADMIN_LEVEL  sets Map:AdminLevels to this single level
  VERIFY_ROTATE_SECONDS  link session key rotation interval (default: 900)
EOF
}

command="${1:-}"
case "$command" in
  doctor) doctor ;;
  start) start ;;
  api-start) api_start ;;
  api-stop) api_stop ;;
  app-stop) app_stop ;;
  keys)
    shift
    keys "$@"
    ;;
  shot)
    shift
    shot "$@"
    ;;
  wait)
    shift
    wait_for "$@"
    ;;
  status) status ;;
  stop) stop ;;
  *)
    usage
    exit 1
    ;;
esac
