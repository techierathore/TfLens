#!/usr/bin/env bash
# Regenerates reference.json by running the parity oracle for real.
#
#   REQ-FN-054. The expected values are NEVER hand-written: this script lays the checked-in fixture
#   streams out as the repository shape `tf-metrics.sh` expects (docs/metrics/*.jsonl, a
#   docs/<App>-Checklist.md for app_name(), and .tfcore/core-config.yaml for project_type()) under
#   tests/.artifacts/harness/ — which is gitignored — then runs
#
#       .tfcore/telemetry/tf-metrics.sh --rollup <alpha> <beta> <gamma> --json
#
#   and writes the output beside the fixtures. The scratch repos are not committed because .tfcore/
#   is gitignored everywhere; this script is the committed, reproducible record of how they are built.
#
#   Repo layout under test:
#     alpha  project_type: app      (declared)   live + backfilled records, one tainted REQ
#     beta   project_type: (absent) (inferred)   segments as `unclassified`, only 2 REQs -> below MinN
#     gamma  project_type: library  (declared)   a second project type that must never pool with app
#
# Usage:  bash tests/TfLens.Core.Tests/Fixtures/Engine/make-reference.sh
set -euo pipefail

vFixtures="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
vRoot="$(cd "$vFixtures/../../../.." && pwd)"
vScratch="$vRoot/tests/.artifacts/harness/parity"

rm -rf "$vScratch"

build_repo() {
    vName="$1"; vApp="$2"; vType="$3"
    vRepo="$vScratch/$vName"
    mkdir -p "$vRepo/docs/metrics"
    cp "$vFixtures/$vName"/*.jsonl "$vRepo/docs/metrics/"
    printf '# %s Checklist\n' "$vApp" > "$vRepo/docs/$vApp-Checklist.md"
    if [ -n "$vType" ]; then
        mkdir -p "$vRepo/.tfcore"
        printf 'metrics:\n  project_type: %s\n' "$vType" > "$vRepo/.tfcore/core-config.yaml"
    fi
}

build_repo alpha AlphaApp app
build_repo beta  BetaWeb  ""
build_repo gamma GammaLib library

# tf-metrics.sh is a bash wrapper that execs python3 with its own heredoc, so it is invoked as bash.
bash "$vRoot/.tfcore/telemetry/tf-metrics.sh" \
    --rollup "$vScratch/alpha" "$vScratch/beta" "$vScratch/gamma" --json \
    > "$vScratch/reference.raw.json"

# `repo` is the scratch directory path, which is machine-specific and has no TfLens counterpart
# (TfLens keys a repository by owner/name). Replace it with the fixture name so the checked-in file
# is stable across machines; every other key is the oracle's own output, untouched.
python3 - "$vScratch/reference.raw.json" "$vFixtures/reference.json" <<'PYEOF'
import json, os, sys
vData = json.load(open(sys.argv[1], encoding="utf-8"))
for vRepo in vData["per_repo"]:
    vRepo["repo"] = os.path.basename(vRepo["repo"])
with open(sys.argv[2], "w", encoding="utf-8") as vOut:
    json.dump(vData, vOut, indent=2)
    vOut.write("\n")
PYEOF

echo "wrote $vFixtures/reference.json"
