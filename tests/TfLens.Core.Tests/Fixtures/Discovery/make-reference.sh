#!/usr/bin/env bash
# Regenerates reference-rollup.json by running the parity oracle for real over the discovery-cost fixture.
#
#   REQ-FN-152 (BRD-220). The expected values are NEVER hand-written: this script lays the checked-in
#   made-up "Sample misses" stream out as the repository shape tf-metrics.sh reads (docs/metrics/,
#   a docs/<App>-Checklist.md, a .tfcore/core-config.yaml) under tests/.artifacts/harness/discovery —
#   which is gitignored — runs `tf-metrics.sh --rollup <repo> --json`, and writes the output beside the
#   fixture with the machine-specific repo path replaced by the fixture name.
#
#   The fixture covers every case of SCHEMA.md §5.5.12: measured (tree and main scope), measured with no
#   discovery_s, not-measured, a miss written before the field existed, a backfilled miss, and fixes
#   that are sole, shared by one run, unmeasured, and absent.
#
# Usage:  bash tests/TfLens.Core.Tests/Fixtures/Discovery/make-reference.sh
set -euo pipefail

vFixtures="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
vRoot="$(cd "$vFixtures/../../../.." && pwd)"
vScratch="$vRoot/tests/.artifacts/harness/discovery"
vRepo="$vScratch/sample-misses"

rm -rf "$vScratch"
mkdir -p "$vRepo/docs/metrics" "$vRepo/.tfcore"
cp "$vFixtures/sample__sample-misses/misses.jsonl" "$vRepo/docs/metrics/"
printf '# SampleMisses Checklist\n' > "$vRepo/docs/SampleMisses-Checklist.md"
printf 'metrics:\n  project_type: app\n' > "$vRepo/.tfcore/core-config.yaml"

bash "$vRoot/.tfcore/telemetry/tf-metrics.sh" --rollup "$vRepo" --json > "$vScratch/reference-rollup.raw.json"

python3 - "$vScratch" "$vFixtures" <<'PYEOF'
import json, os, sys
vScratch, vFixtures = sys.argv[1], sys.argv[2]
vRollup = json.load(open(os.path.join(vScratch, "reference-rollup.raw.json"), encoding="utf-8"))
for vRepo in vRollup["per_repo"]:
    vRepo["repo"] = os.path.basename(vRepo["repo"])
with open(os.path.join(vFixtures, "reference-rollup.json"), "w", encoding="utf-8") as vOut:
    json.dump(vRollup, vOut, indent=2)
    vOut.write("\n")
PYEOF

echo "wrote $vFixtures/reference-rollup.json"
