#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT=$(cd -- "$(dirname -- "$(readlink -f -- "${BASH_SOURCE[0]}")")/../../../.." && pwd)
STATE_DIR="$REPO_ROOT/.audit/verify"
SESSION_FILE="$STATE_DIR/.session"
TITLE="${VERIFY_TITLE:-GeoTools verify}"
TIMEOUT="${VERIFY_TIMEOUT:-90}"
APP_DLL="$REPO_ROOT/src/GeoTools.Launcher/bin/Debug/net10.0/GeoTools.Launcher.dll"

die() {
  echo "$*" >&2
  exit 1
}

require_tools() {
  local missing=()
  local tool
  for tool in dotnet Xvfb xvfb-run import xdotool; do
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

  local borders="${VERIFY_BORDERS_DIR:-$REPO_ROOT/geodata/borders}"
  if [ -d "$borders" ]; then
    echo "borders: $(find "$borders" -maxdepth 1 -name '*.geojsonseq' | wc -l) files in $borders"
  else
    echo "borders: missing $borders (the Map view will show its no-data message)"
  fi

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

  local borders="${VERIFY_BORDERS_DIR:-$REPO_ROOT/geodata/borders}"
  local -a app_env=(
    "DISPLAY=$XDISPLAY"
    "Window__Title=$TITLE"
    "Map__BordersDirectory=$borders"
  )
  if [ -n "${VERIFY_ADMIN_LEVEL:-}" ]; then
    app_env+=("Map__AdminLevels__0=$VERIFY_ADMIN_LEVEL")
  fi

  (
    cd "$REPO_ROOT"
    exec env "${app_env[@]}" dotnet "$APP_DLL"
  ) >"$RUN_DIR/app.log" 2>&1 &
  APP_PID=$!

  local ready=""
  for i in $(seq 1 "$TIMEOUT"); do
    if grep -q 'Window "' "$RUN_DIR/app.log" 2>/dev/null &&
      grep -q 'Composed map' "$RUN_DIR/app.log" 2>/dev/null; then
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
  } >"$SESSION_FILE"

  echo "session: $RUN_DIR"
  echo "display: $XDISPLAY"
  echo "title: $TITLE"
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

status() {
  load_session
  local state="stopped"
  if is_alive "$APP_PID" && is_alive "$XVFB_PID"; then
    state="running"
  fi
  echo "state: $state"
  echo "display: $XDISPLAY"
  echo "title: $TITLE"
  echo "evidence: $RUN_DIR"
}

stop() {
  if [ ! -f "$SESSION_FILE" ]; then
    echo "no verify session"
    return 0
  fi

  source "$SESSION_FILE"
  kill_pid "$APP_PID"
  kill_pid "$XVFB_PID"
  rm -f "$SESSION_FILE"
  echo "stopped; evidence kept in $RUN_DIR"
}

usage() {
  cat <<EOF
usage: $0 <command>

  doctor    check tools, build, border data, and stale sessions
  start     start Xvfb and the app, then wait for the ready log lines
  keys ...  send keys to the app window (1, 2, Tab, shift+Tab, Escape)
  shot ...  capture the root window to the run directory
  status    report the session state and evidence directory
  stop      kill the app and Xvfb this session started

environment:
  VERIFY_TITLE        window title (default: $TITLE)
  VERIFY_TIMEOUT      readiness timeout in seconds (default: $TIMEOUT)
  VERIFY_BORDERS_DIR  borders directory (default: <repo>/geodata/borders)
  VERIFY_ADMIN_LEVEL  sets Map:AdminLevels to this single level
EOF
}

command="${1:-}"
case "$command" in
  doctor) doctor ;;
  start) start ;;
  keys)
    shift
    keys "$@"
    ;;
  shot)
    shift
    shot "$@"
    ;;
  status) status ;;
  stop) stop ;;
  *)
    usage
    exit 1
    ;;
esac
