# TfLens — decisions I need from you

| | |
|---|---|
| App | TfLens |
| Written | 2026-09-09 |
| Waiting on | 2 decisions. Nothing has been changed yet. |

## What happened

While building the last phase of TfLens I ran into two piles of work that are not
mine to do from here, and both need you to start them in another window.

The first is the build tooling itself. Five separate problems turned up during the
pass, and each one was checked against the tool's own code before it was written
down. Two of them change what gets reported about your project: one hides work that
has never been started, so a pass can announce a phase finished while requirements
sit untouched, and the other counts a test that was deliberately skipped as a test
that failed, so "there is no data to show this yet" is recorded as a defect. The
other three are smaller but the same shape. All five live in files the framework
owns, so nothing in this repository can repair them.

The second is the project's own written documents. The checker refuses 236 things
across nineteen files — sections that no longer match the template, screens with no
field tables, mockups whose buttons do nothing, and one database sketch in the
architecture document that contradicts every table around it. Most dates from the
day the documents were split into three phases. The command that owns those
documents is the only one allowed to rewrite them.

Neither pile blocks the application. It builds, runs, and 69 of its 73 requirements
are verified.

## What I need you to decide

### 1. Fixing the five problems in the build tooling

The framework has a new self-check that can repair its own files. The question is
how much of it to hand over now. Everything needed is already written down in the
feedback file, one entry each, with the exact lines of code and a suggested fix.

Two of the five change what your reports say. The one that hides not-started work
cost this pass twelve requirements — they were built only because I read the
checklist directly instead of trusting the printed list. The one that treats a
skipped test as a failure is why two rows in the checklist say FAIL today when
nothing is wrong with them.

| Option | What happens | What it costs |
|---|---|---|
| **A — fix all five now** | The tooling stops hiding unstarted work, stops calling absent data a defect, and stops reporting a correctly-built screen as broken. | One session in the framework window. |
| **B — fix only the two that change reports** | The two that matter are gone; the three cosmetic ones stay and keep producing noise every run. | Less work now, a second pass later. |
| **C — leave all five** | Every future pass repeats them. The hidden-work one is the dangerous one: it will quietly skip new requirements again. | Nothing now, and a real risk of shipping a phase that was never finished. |

**My recommendation: A** — they are already diagnosed and written up, so the
expensive part is done; fixing three more costs very little on top.

### 2. Repairing the project's documents

The 236 findings are not evenly serious. One is a genuine contradiction: the
architecture document sketches a new database table with a different key and
different column types from every other table beside it, and the code follows the
surrounding house style rather than the sketch. That one should be settled before
anyone reads the document and believes it. The rest are shape and template drift.

| Option | What happens | What it costs |
|---|---|---|
| **A — repair everything in one pass** | All nineteen documents match their templates again. | The longest option, and most of it is cosmetic. |
| **B — settle the contradiction first, then the rest later** | The architecture stops disagreeing with the code immediately; the cosmetic backlog waits. | Two sessions instead of one. |
| **C — leave it** | The documents keep drifting and the checker's output stays too noisy to read. | Nothing now, more later. |

**My recommendation: B** — the contradiction is the only part that can mislead
someone; the rest can wait for a quiet moment.

## What I do when you answer

If you choose to fix the tooling, I do nothing here — that work happens in the
framework window, and the feedback file is its input. When it is done, tell me and I
will re-run the verifier so the two rows that wrongly say FAIL are graded again.

If you choose to repair the documents, I run the document command against this
project, starting with the architecture contradiction, then re-run the checker and
report what is left. The application is not touched and no requirement changes.

If you choose to do neither, I leave everything exactly as it is. The four
requirements that are not verified stay owner-gated for the reason already recorded,
and nothing else in the project is waiting on this.

## Copy this back to me

```
Fix the TechieFlow tooling problems TfLens found. Run tf-selfcheck and work
from docs/TfLens-TechieFlow-Feedback.md in the TfLens repo: entries TF-018,
TF-019, TF-020, TF-021 and TF-022. Each names the file, the line and a
suggested fix, and says what is NOT affected. Do TF-019 and TF-022 first —
they change what every project's reports say. Verify each fix against the
behaviour the entry describes, not just the code.
```

```
TfLens: decision 2 — go with option B. Run *amend-docs and settle the
architecture's MissReview table sketch first: it gives the table a bigserial
primary key and types UserId as text and Ts as timestamptz, while all four
sibling tables use no surrogate key, an integer UserId and a text Ts. The
code follows the siblings. Fix the document, then leave the rest of the
template drift for a later pass.
```
