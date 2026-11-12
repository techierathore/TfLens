# TfLens — decisions I need from you

| | |
|---|---|
| App | TfLens |
| Written | 2026-11-12 |
| Waiting on | 1 decision. Nothing has been changed yet. |

## What happened

Phase 4 asks that the snapshot export carry the estimates, laid out exactly as the framework's own
metrics script prints them, and that the parity check then pass with no estimates difference. I ran
the parity check for real on the Sample app test files. Every estimates figure, 318 of them, matches,
and there is no difference anywhere in the estimates part.

The check as a whole still does not pass, for two reasons that have nothing to do with estimates.
The framework's script gained two miss-cost figures on 5 November (what finding a defect cost, and the
cost per miss) that TfLens does not print yet. And for a repository with no records at all, the script
takes the app name from a checklist file that TfLens never downloads. The phase 4 line says "passes
with no estimates difference". Read word for word, it cannot pass until those two are dealt with, and
neither is phase 4 work. That choice is yours.

## What I need you to decide

### 1. How the estimates export is judged while the two older differences remain

The export of estimates is built and matches. The question is whether that is enough to call it done,
or whether it waits until the whole parity check passes again.

| Option | What happens | What it costs |
|---|---|---|
| **A — Judge it on the estimates part** | The phase 4 line is reworded to "every estimates figure is compared and none differs"; the export row moves on to verification now. | Nothing more in phase 4. The two older differences stay open with no requirement behind them, so a whole parity run keeps failing. |
| **B — Wait for the whole check** | The export row stays blocked; a new requirement is added for the two miss-cost figures and the empty-repository app name; it is built, then the whole check is run again. | One more requirement and its build before phase 4 can close. |
| **C — Both** | A now for the export row, and a new requirement for the two older differences, so the whole parity check passes again later. | One more requirement, built when you choose; phase 4 is not held up. |

**My recommendation: C** — the estimates work is finished and proven, and the older differences are a real gap that every future parity run will hit, so they deserve a requirement of their own.

## What I do when you answer

1. A or C: reword the export row's acceptance line through the documents command, then set the row to Implemented (about ten minutes).
2. B or C: add one requirement for the two miss-cost figures and the empty-repository app name, with its own acceptance line.
3. Re-run the parity check over the Sample app files and record the result in the decision log.

## Copy this back to me

```
TfLens: decision 1 — go with option C. Judge the estimates export on the estimates part now, and add a new requirement for the two missing miss-cost figures and the empty-repository app name.
```
