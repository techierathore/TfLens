# Playbook self-test events stream

`events.ndjson` in this folder is a copy of a file published by the upstream Playbook repository:

- **Repository:** `techierathore/AI-First-Playbook`
- **Path:** `.miss-selftest/verification/telemetry/events.ndjson`
- **What it is:** the Playbook's own self-test output — 8 lines covering an `implement` phase
  (session `ses_build`) and a `fix` phase (session `ses_fix`, plus one sub-agent session
  `ses_child1` whose `parentID` is `ses_fix`). It is not a captured working session.
- **Copied:** unmodified, on 2026-09-11. sha256
  `37dfcc8e67a2e98e1060dc908112f978d41604ddbc8f476ac2a3e2f0b88dd401`. Do not edit it; if the
  upstream file changes, copy the new one over this one and update the hash here.

## How it is used

Only `tests/verify/verify-export-playbook.spec.ts` (the REQ-FN-067 test) reads it. That test
signs in as test user 2 (`tflenstest2@techierathore.com`), imports this file through the `/repos`
Import screen as a uniquely named source (prefix `cla-fn067-`), checks the Playbook axis of
`/gate-outcomes`, and removes the source again in a `finally` block — so nothing is left behind
after a run. It is never loaded into the demo account (user 1).
