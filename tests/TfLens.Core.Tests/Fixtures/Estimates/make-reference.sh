#!/usr/bin/env bash
# Regenerates reference-estimates.json and reference-rollup.json by running the parity oracle for real.
#
#   REQ-FN-148 / REQ-FN-151 (BRD-212, BRD-218). The expected values are NEVER hand-written: this
#   script lays the checked-in Sample app fixture streams out as the repository shape `tf-metrics.sh`
#   expects (docs/metrics/*.jsonl, a docs/<App>-Checklist.md for app_name(), a .tfcore/core-config.yaml
#   for project_type()) under tests/.artifacts/harness/estimates — which is gitignored — then runs
#
#       .tfcore/telemetry/tf-metrics.sh --estimates <sample-app> <sample-empty> --json
#       .tfcore/telemetry/tf-metrics.sh --rollup    <sample-app> <sample-empty> --json
#
#   and writes both outputs beside the fixtures. The scratch repos are not committed; this script is
#   the committed, reproducible record of how they are built (same contract as Fixtures/Engine/
#   make-reference.sh).
#
# Repo layout under test (BRD §3.1 — the Sample app is made up and exists only as these files):
#     sample-app    44 estimate records (two estimating models on phase 1, one on phase 2,
#                   after handoff basis:none everywhere) + 15 run records
#     sample-empty  an empty estimates.jsonl and no runs: "no estimate yet"
#
# Usage:  bash tests/TfLens.Core.Tests/Fixtures/Estimates/make-reference.sh
set -euo pipefail

vFixtures="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
vRoot="$(cd "$vFixtures/../../../.." && pwd)"
vScratch="$vRoot/tests/.artifacts/harness/estimates"

rm -rf "$vScratch"

build_repo() {
    vDir="$1"; vApp="$2"
    vRepo="$vScratch/$vDir"
    mkdir -p "$vRepo/docs/metrics" "$vRepo/.tfcore"
    cp "$vFixtures/sample__$vDir"/*.jsonl "$vRepo/docs/metrics/"
    printf '# %s Checklist\n' "$vApp" > "$vRepo/docs/$vApp-Checklist.md"
    printf 'metrics:\n  project_type: app\n' > "$vRepo/.tfcore/core-config.yaml"
}

build_repo sample-app SampleApp
build_repo sample-empty SampleEmpty

# tf-metrics.sh is a bash wrapper that execs python3 with its own heredoc, so it is invoked as bash.
bash "$vRoot/.tfcore/telemetry/tf-metrics.sh" \
    --estimates "$vScratch/sample-app" "$vScratch/sample-empty" --json \
    > "$vScratch/reference-estimates.raw.json"

bash "$vRoot/.tfcore/telemetry/tf-metrics.sh" \
    --rollup "$vScratch/sample-app" "$vScratch/sample-empty" --json \
    > "$vScratch/reference-rollup.raw.json"

# `repo` is the scratch directory path, which is machine-specific and has no TfLens counterpart
# (TfLens keys a repository by owner/name). Replace it with the fixture name so the checked-in files
# are stable across machines; every other key is the oracle's own output, untouched.
python3 - "$vScratch" "$vFixtures" <<'PYEOF'
import json, os, sys
vScratch, vFixtures = sys.argv[1], sys.argv[2]
vRollup = json.load(open(os.path.join(vScratch, "reference-rollup.raw.json"), encoding="utf-8"))
for vRepo in vRollup["per_repo"]:
    vRepo["repo"] = os.path.basename(vRepo["repo"])
with open(os.path.join(vFixtures, "reference-rollup.json"), "w", encoding="utf-8") as vOut:
    json.dump(vRollup, vOut, indent=2)
    vOut.write("\n")
vEstimates = json.load(open(os.path.join(vScratch, "reference-estimates.raw.json"), encoding="utf-8"))
with open(os.path.join(vFixtures, "reference-estimates.json"), "w", encoding="utf-8") as vOut:
    json.dump(vEstimates, vOut, indent=2)
    vOut.write("\n")
PYEOF

echo "wrote $vFixtures/reference-estimates.json and $vFixtures/reference-rollup.json"
