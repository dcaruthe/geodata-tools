#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT=$(cd -- "$(dirname -- "$(readlink -f -- "${BASH_SOURCE[0]}")")/.." && pwd)
cd "$REPO_ROOT"

exec dotnet run --project src/GeoTools.Launcher -- --geodata pull "$@"
