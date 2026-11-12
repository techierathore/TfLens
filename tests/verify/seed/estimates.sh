#!/usr/bin/env bash
# Seed for the Estimates screen (docs/mockups/estimates.html) — REQ-UI-078..080, BRD §3.1.
#
# The mockup draws the made-up Sample app's three cards, which a fresh app does not hold. This imports
# the checked-in Sample app test files (tests/TfLens.Core.Tests/Fixtures/Estimates/) into test user 2's
# account through the app's own /repos import, exactly as the owner would. Idempotent: an existing
# sample/sample-app source is left as it is. `--remove` takes both sources out again through the app's
# own Remove path. No real repository's data is involved. The app's address comes in as TF_BASE.
set -euo pipefail
vRoot="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
cd "$vRoot"
exec node tests/verify/seed/estimates-seed.mjs "$@"
