# TfLens — decisions I need from you

| | |
|---|---|
| App | TfLens |
| Written | 2026-09-08 |
| Waiting on | 1 decision. Nothing has been changed yet. |

## What happened

Today's amendment is finished and correct. The twelve new requirements, the seven edits, the
refreshed mockups and the updated checklist are all written, and every check on the rows I wrote
passes.

One thing stops me marking the command closed. The framework caps a single checklist at 100
requirements and TfLens now has 192. It was already over that line before today — it stood at 180
when I started, and it has been over since roughly the end of August. The framework's answer is to
split the project into phases, giving each phase its own requirements file, and it asks me to put
that to you rather than do it on my own. It is right to ask: TfLens is built, verified and about to
be deployed, and reorganising the two documents everything else points at is not a step to take
quietly at the end of an unrelated piece of work.

## What I need you to decide

### 1. Whether to split the requirements into one file per phase

Everything about TfLens lives in two files today: one requirements document and one checklist. The
framework wants three of each — one pair per delivery phase — because a file with 192 requirements
is past the point where anyone can read it. The work already carries phase labels, so the split
follows lines that exist. Phase 1 keeps the current filenames; phases 2 and 3 move beside them.

The cost is that many things point at those two files by name — the architecture, the developer
guide, the deployment checklist, the status page, and several hundred internal cross-references.
Each has to be found and repointed, and a requirement landing in the wrong file is exactly the quiet
inconsistency this project exists to catch.

| Option | What happens | What it costs |
|---|---|---|
| **A — split, as its own job** | Done in a separate session with nothing else in flight, every cross-reference checked afterwards; today's amendment closes as complete-but-over-cap | Half a session. The risk is a reference left pointing at the old place, which is why it wants its own run |
| **B — split now, inside this command** | The same work, done immediately | The same work at the end of a long session, on documents I have been editing all afternoon — the worst moment to reorganise them |
| **C — leave it, and record why** | The files stay as they are; the status page notes the cap is knowingly exceeded and the warning stops being treated as a fault | No work. The files keep growing and the next command hits the same block |

**My recommendation: A** — the cap's reasoning is sound and the split is worth doing, but not as an
afterthought to something else.

## What I do when you answer

- **A** — I close today's amendment and note the cap in the project status as a known, accepted
  state. When you next open TfLens, the split is the first thing I do: create the two new pairs of
  files, move each requirement to the phase it already carries, repoint every reference, then
  re-run the link and document checks end to end.
- **B** — I start the split now, in this session, and report when the checks come back clean.
- **C** — I record the decision in the project status and the architecture's decision log, so the
  next person to hit this warning finds the reason rather than re-opening the question.

## Copy this back to me

```
TfLens: decision 1 — go with option A. Close today's amendment; do the phase split as its own job next session.
```

```
TfLens: decision 1 — go with option B. Do the phase split now, in this session, before closing anything.
```

```
TfLens: decision 1 — go with option C. Leave the two files as they are and record that the cap is knowingly exceeded.
```
