#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT=$(cd -- "$(dirname -- "$(readlink -f -- "${BASH_SOURCE[0]}")")/.." && pwd)
cd "$REPO_ROOT"

OUT_DIR="artifacts/coverage"
THRESHOLD="${COVERAGE_THRESHOLD:-95}"

rm -rf "$OUT_DIR"
mkdir -p "$OUT_DIR"

# The collector writes one cobertura report per test project. Reports overlap
# because each test project also instruments the projects it references.
dotnet test geodata-tools.slnx \
  --collect:"XPlat Code Coverage" \
  --results-directory "$OUT_DIR" \
  --logger "console;verbosity=minimal"

python3 - "$OUT_DIR" "$THRESHOLD" <<'PY'
import glob
import os
import sys
import xml.etree.ElementTree as ET
from collections import defaultdict

out_dir, threshold_text = sys.argv[1], sys.argv[2]
threshold = float(threshold_text)

ASSEMBLIES = [
    "GeoTools.Shared.Domain",
    "GeoTools.Shared.Application",
    "GeoTools.Shared.Infrastructure",
    "GeoTools.Shared.Presentation",
]

# Keyed by assembly and file basename. No two files share a basename inside one
# assembly today; the per-file table makes a future collision visible.
lines = defaultdict(dict)
reports = 0

for path in glob.glob(os.path.join(out_dir, "**", "coverage.cobertura.xml"), recursive=True):
    reports += 1
    root = ET.parse(path).getroot()
    for package in root.iter("package"):
        assembly = package.get("name")
        if assembly not in ASSEMBLIES:
            continue
        for cls in package.iter("class"):
            filename = os.path.basename(cls.get("filename"))
            for line in cls.iter("line"):
                number = int(line.get("number"))
                hits = int(line.get("hits"))
                lines[(assembly, filename)][number] = max(
                    lines[(assembly, filename)].get(number, 0), hits
                )

if reports == 0:
    sys.exit(f"no coverage.cobertura.xml found under {out_dir}")

totals = {assembly: [0, 0] for assembly in ASSEMBLIES}
rows = []

for (assembly, filename), hits in sorted(lines.items()):
    covered = sum(1 for value in hits.values() if value > 0)
    valid = len(hits)
    totals[assembly][0] += covered
    totals[assembly][1] += valid
    rows.append((assembly, filename, covered, valid))

missing = [assembly for assembly, (covered, valid) in totals.items() if valid == 0]
if missing:
    sys.exit(f"no coverage reported for: {', '.join(missing)}")

report = []
report.append(f"{'Assembly / file':70s} {'Lines':>12s}  {'Coverage':>9s}")
report.append("-" * 95)

for assembly, filename, covered, valid in rows:
    report.append(
        f"{assembly + '/' + filename:70s} {covered:5d}/{valid:<6d}  {100 * covered / valid:8.1f}%"
    )

report.append("")
report.append(f"{'Assembly':70s} {'Lines':>12s}  {'Coverage':>9s}")
report.append("-" * 95)
for assembly in ASSEMBLIES:
    covered, valid = totals[assembly]
    report.append(f"{assembly:70s} {covered:5d}/{valid:<6d}  {100 * covered / valid:8.1f}%")

covered_total = sum(covered for covered, _ in totals.values())
valid_total = sum(valid for _, valid in totals.values())
report.append("")
report.append(
    f"{'Total':70s} {covered_total:5d}/{valid_total:<6d}  "
    f"{100 * covered_total / valid_total:8.1f}%"
)

failures = []
for assembly in ASSEMBLIES:
    covered, valid = totals[assembly]
    actual = 100 * covered / valid
    if actual < threshold:
        failures.append(f"{assembly} is at {actual:.1f}%")

total = 100 * covered_total / valid_total
if total < threshold:
    failures.append(f"total is at {total:.1f}%")

report.append("")
if failures:
    report.append(
        f"Coverage gate failed at {threshold:.1f}% per assembly and overall: "
        + "; ".join(failures)
    )
else:
    report.append(f"Coverage gate passed at {threshold:.1f}% per assembly and overall.")

text = "\n".join(report)
print(text)
with open(os.path.join(out_dir, "summary.txt"), "w", encoding="utf-8") as handle:
    handle.write(text + "\n")

sys.exit(1 if failures else 0)
PY
