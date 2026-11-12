#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT=$(cd -- "$(dirname -- "$(readlink -f -- "${BASH_SOURCE[0]}")")/.." && pwd)
cd "$REPO_ROOT"

# ADMIN_LEVELS is the old script's interface. Pass it through as the runtime
# extraction levels so one implementation owns validation.
if [ -n "${ADMIN_LEVELS:-}" ]; then
  export "Geodata__ExtractAdminLevelsList=$ADMIN_LEVELS"
fi

if [ "$#" -eq 0 ]; then
  exec dotnet run --project src/GeoTools.Launcher -- --geodata extract
fi

failures=0
for region in "$@"; do
  if ! dotnet run --project src/GeoTools.Launcher -- --geodata extract --region "$region"; then
    failures=$((failures + 1))
  fi
done

[ "$failures" -eq 0 ]
