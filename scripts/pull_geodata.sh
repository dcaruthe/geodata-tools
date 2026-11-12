#!/usr/bin/env bash
set -euo pipefail

# Create and enter the gitignored downloads directory (DO NOT COMMIT GEODATA)
mkdir -pv geodata
cd geodata

LOG="update.log"

log() { printf '%s\t%s\n' "$(date -Is)" "$*" >> "$LOG"; }

urls=(
  "https://download.geofabrik.de/africa-latest.osm.pbf"
  "https://download.geofabrik.de/antarctica-latest.osm.pbf"
  "https://download.geofabrik.de/asia-latest.osm.pbf"
  "https://download.geofabrik.de/australia-oceania-latest.osm.pbf"
  "https://download.geofabrik.de/central-america-latest.osm.pbf"
  "https://download.geofabrik.de/europe-latest.osm.pbf"
  "https://download.geofabrik.de/north-america-latest.osm.pbf"
  "https://download.geofabrik.de/south-america-latest.osm.pbf"
)

log "=== Pulling latest geodata ==="

for url in "${urls[@]}"; do
  fname=$(basename "$url")

  before=""
  if [ -f "$fname" ]; then
    before=$(stat -c %Y "$fname")        # GNU/Linux
    # before=$(stat -f %m "$fname")      # macOS — use this instead
  fi

  if wget -N --wait=30 --tries=3 "$url"; then
    after=$(stat -c %Y "$fname")
    size=$(stat -c %s "$fname")
    if [ "$before" != "$after" ]; then
      log "UPDATED   $fname  size=$size"
    else
      log "UNCHANGED $fname  size=$size"
    fi
  else
    log "FAILED    $fname"
  fi
done

echo "$(date -I)" > UPDATED_AT
log "=== pull finished ==="