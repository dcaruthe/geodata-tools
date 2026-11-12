#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT=$(cd -- "$(dirname -- "$(readlink -f -- "${BASH_SOURCE[0]}")")/.." && pwd)
cd "$REPO_ROOT"

GEODATA="geodata"
OUT_DIR="$GEODATA/borders"
WORK_DIR="$OUT_DIR/.work"
LOG="$OUT_DIR/extract.log"
ADMIN_LEVELS="${ADMIN_LEVELS:-2,4}"

ALL_REGIONS=(africa antarctica asia australia-oceania central-america europe north-america south-america)

if [ "$#" -gt 0 ]; then
  regions=("$@")
else
  regions=("${ALL_REGIONS[@]}")
fi

mkdir -p "$WORK_DIR"

log() { printf '%s\t%s\n' "$(date -Is)" "$*" >> "$LOG"; }

ADMIN_LEVELS="${ADMIN_LEVELS//[[:space:]]/}"
if ! [[ "$ADMIN_LEVELS" =~ ^[0-9]+(,[0-9]+)*$ ]]; then
  echo "ADMIN_LEVELS must be a comma-separated list of admin levels, got: '$ADMIN_LEVELS'" >&2
  exit 1
fi

IFS=',' read -r -a levels <<< "$ADMIN_LEVELS"
level_expressions=()
for level in "${levels[@]}"; do
  level_expressions+=("r/admin_level=$level")
done

build_region() {
  local region="$1"
  local input="$GEODATA/$region-latest.osm.pbf"
  local output="$OUT_DIR/$region-boundaries.geojsonseq"
  local levels_file="$OUT_DIR/$region-boundaries.levels"
  local boundary="$WORK_DIR/$region-boundary.osm.pbf"
  local admin="$WORK_DIR/$region-admin.osm.pbf"

  if [ ! -f "$input" ]; then
    log "MISSING   $region input=$input"
    echo "$region: missing input $input" >&2
    return 1
  fi

  if [ -f "$output" ] && [ "$output" -nt "$input" ] \
     && [ -f "$levels_file" ] && [ "$(cat "$levels_file")" = "$ADMIN_LEVELS" ]; then
    log "SKIP      $region levels=$ADMIN_LEVELS"
    echo "$region: up to date"
    return 0
  fi

  log "BUILD     $region levels=$ADMIN_LEVELS"
  echo "$region: extracting admin borders (levels=$ADMIN_LEVELS)"

  # Drop stale artifacts so a failed export cannot leave a partial output plus
  # an old sidecar that a later run would mistake for up to date.
  rm -f "$output" "$levels_file"

  # Two passes: osmium ORs the values inside one tag expression, so the
  # boundary=administrative and admin_level filters cannot be combined.
  if ! osmium tags-filter -O -o "$boundary" "$input" r/boundary=administrative; then
    log "FAILED    $region stage=boundary-filter"
    echo "$region: boundary filter failed, intermediates kept in $WORK_DIR" >&2
    return 1
  fi

  if ! osmium tags-filter -O -o "$admin" "$boundary" "${level_expressions[@]}"; then
    log "FAILED    $region stage=admin-filter"
    echo "$region: admin_level filter failed, intermediates kept in $WORK_DIR" >&2
    return 1
  fi

  if ! osmium export -O -f geojsonseq --geometry-types=polygon,linestring -a id \
       -o "$output" "$admin"; then
    log "FAILED    $region stage=export"
    echo "$region: export failed, intermediates kept in $WORK_DIR" >&2
    return 1
  fi

  printf '%s\n' "$ADMIN_LEVELS" > "$levels_file"
  rm -f "$boundary" "$admin"

  local size lines
  size=$(stat -c %s "$output")
  lines=$(wc -l < "$output")
  log "DONE      $region levels=$ADMIN_LEVELS size=$size features=$lines"
  echo "$region: done ($size bytes, $lines features)"
}

log "=== Extracting admin borders levels=$ADMIN_LEVELS regions=${regions[*]} ==="

failures=0
for region in "${regions[@]}"; do
  if ! build_region "$region"; then
    failures=$((failures + 1))
  fi
done

echo
echo "Border extraction summary (admin levels: $ADMIN_LEVELS)"
for region in "${regions[@]}"; do
  output="$OUT_DIR/$region-boundaries.geojsonseq"
  if [ -f "$output" ]; then
    printf '  %-56s %12s bytes %8s features\n' \
      "$output" "$(stat -c %s "$output")" "$(wc -l < "$output")"
  else
    printf '  %-56s %12s\n' "$output" "MISSING"
  fi
done

if [ "$failures" -gt 0 ]; then
  log "=== Finished with $failures failed region(s) ==="
  exit 1
fi

log "=== Extraction finished ==="
