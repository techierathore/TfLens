#!/usr/bin/env bash
# Regenerates tflens-fixtures/reference.json by running the parity oracle for real.
#
#   REQ-FN-061 / REQ-FN-064. `parity-repo` is the extras fixture: it carries all three harnesses plus
#   harness:null records, routing fields with drift, a tokens_scope:none run, an observed model with
#   no rate-card entry, and measured cost_usd on OpenCode only. The reference computes NONE of those —
#   that is the point, they have no oracle — but it does compute every figure the export shares with
#   it, so this file is what tools/parity-compare.py checks the export's key layout against.
#
#   The expected values are NEVER hand-written. The repo is already laid out the way tf-metrics.sh
#   expects (docs/metrics/*.jsonl plus a docs/<App>-Checklist.md for app_name()), and it deliberately
#   has NO .tfcore/core-config.yaml, so the oracle reports project_type "app" as inferred — one more
#   case the fixture exercises.
#
#   The parent directory is named `tflens-fixtures` so the oracle is invoked with a relative path that
#   reads as owner/name, which is how TfLens identifies a repository. That removes a cosmetic name
#   difference from the compare output without normalising anything after the fact.
#
# Usage:  bash tests/TfLens.Core.Tests/Fixtures/tflens-fixtures/make-reference.sh
set -euo pipefail

vHere="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
vFixtures="$(cd "$vHere/.." && pwd)"
vRoot="$(cd "$vFixtures/../../.." && pwd)"

cd "$vFixtures"
bash "$vRoot/.tfcore/telemetry/tf-metrics.sh" --rollup tflens-fixtures/parity-repo --json \
    > "$vHere/reference.json"

echo "wrote $vHere/reference.json"
