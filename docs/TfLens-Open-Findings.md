# TfLens — Open findings, in plain English

Written 2026-09-15, after the UI verify. This explains four things the verify turned up that the
checklist's "117 of 117 Verified" did not show.

> **Update, later on 2026-09-15 (`*fix-issues`) — all four are fixed.**
> 1. REQ-UI-050: each Playbook execution row now carries the harness its producer detected, and the
>    Harness filter uses it. A new unit test proves rows from two harnesses separate. Re-verified.
> 2. REQ-UI-072: a model with only one OpenRouter price is now skipped, never saved with the other at $0.
>    The new unit test failed before the fix and passes after. Re-verified.
> 3. REQ-NFR-003: the `deploy.yml` line carries the approved "safe line" note; the secret-hygiene test and
>    all 130 guardrail tests pass. (The verify could not link that test to the row by name, so it wrote the
>    row as not observable rather than as a fresh pass.)
> 4. REQ-NFR-015: the cache test now recognises fingerprints such as `TfLens.<code>.styles.css`; it passes
>    on a Release build.
>
> The sections below describe the problems as they were before the fix.

---

## Part 1 — Two screens that passed their tests but still have a known bug

### First, how a "Verified" is decided

Each requirement in the checklist has an automated test. The test opens the real app in a browser, does
what the requirement describes, and checks the result. If the test passes (along with the screen checks),
the row is written **Verified**.

The catch: **a test only proves what it actually tries.** If a bug lives in a part of the screen the test
never touches, the test still passes and the row still says Verified.

That is what happened to the two rows below. On 2026-09-14, while writing the developer guide, two real
bugs were found by reading the code. Both rows were marked "Needs re-verify". No code has changed since.
Today's verify ran their tests again, the tests passed, and both rows went back to Verified — but the tests
never go near the bugs, so the bugs are almost certainly still there.

### Bug 1 — REQ-UI-050: on `/effort`, in Playbook view, the "Harness" filter does nothing

**What the user sees.** Open `/effort`, switch the header to **Playbook**. Above the table there is a row
of filters, one of them a **Harness** dropdown (for example Claude Code or OpenCode). Pick a harness. The
table does not change — it keeps showing every row, whatever you pick.

**Why.** The dropdown is drawn and its choices are real, but the code that decides which rows to show
never looks at what you picked. It checks the other filters (phase, project type, verdict, completeness,
coverage, fan-out, period) but not harness (`PlaybookEffortSurface.razor`, around lines 1358, 1620 and
1708). The small note on screen admits two other filters "select nothing yet" but does not mention harness,
so a user has no warning.

**What the test checks today.** Only that the filter row and its dropdowns appear on screen. It never
picks a harness and never checks that the rows change. So it passes even though the filter is broken.

**What it affects.** Only that one dropdown on the Playbook view of `/effort`. The TechieFlow view, the
Playbook summary tiles and charts, and the other Playbook filters all work.

**Severity.** Low — a filter that silently does nothing, not wrong numbers.

### Bug 2 — REQ-UI-072: on `/prices`, a price that OpenRouter does not publish can be saved as $0

**Background.** OpenRouter publishes two prices per model: one for input tokens and one for output tokens.
TfLens reads them when you press **Refresh** on the OpenRouter card. The requirement says: if a price is
not published, **skip it — never store it as zero**, because a $0 price makes that model look free and
understates every cost figure built on it.

**What goes wrong.** The code skips a model only when **both** prices are missing. If just **one** is
missing (say the output price is blank), it stores that one as **$0** and keeps the model
(`PriceProviders.cs`, around lines 294 and 311–314). The code's own comment says the opposite ("skipped
rather than stored at zero"), so the comment and the code disagree.

**What the tests check today.**
- that a provider with **no rates at all** shows the words "never priced at zero";
- that typing `-1` **by hand** into the rate box is refused.

Neither of those is the bug. No test feeds the Refresh a model with **one** missing price and checks that
it is not saved as $0. So the tests pass while the bug stays.

**What it affects.** Only OpenRouter models where exactly one of the two prices is missing, and only after
a Refresh. The `-1` rule, typed rates, the clash report and paging are fine. How many models this hits
today is not measured.

**Severity.** Low on screen, but it can quietly make a cost look lower than it is.

### What should happen for both

1. Write a test that actually tries the bug (pick a harness and expect fewer rows; refresh with one price
   missing and expect no $0). Run it — it should **fail** today, which proves the bug is real.
2. Fix the code.
3. Run the test again — it should now **pass**. Only then is "Verified" trustworthy for these rows.

Prompt:
```
/TechieFlow:agents:flow-master *fix-issues TfLens
```

---

## Part 2 — Two failures found outside the screens that were checked

The verify was asked to check the UI rows only. While running the whole test suite it also saw two
failures in other rows. They did not change any UI verdict, but you should know about them.

### Failure 1 — REQ-NFR-003 (the "no secrets in the repository" test) now fails because of today's pipeline change

**What this test does.** It reads every configuration file in the repository and fails if any line looks
like it contains a password or token, so a real secret can never be committed by accident.

**What it caught.** Today I changed the deploy pipeline (`.github/workflows/deploy.yml`) so the Docker
build can read TrBlazeUI from your private GitHub feed. The new lines are:

```
          secrets: |
            nuget_pat=${{ secrets.TrBlazeUiPackagesToken || secrets.GITHUB_TOKEN }}
```

The test sees the word `secrets:` and fails.

**Is anything actually leaked?** No. That line holds only the **name** of the GitHub secret, not its
value. GitHub fills in the real token while the pipeline runs, and it never appears in the file.

**So what is wrong?** Only that the test cannot tell a secret's name from a secret's value. The test
already has an agreed way to mark a safe line: put the note `NFR-003-OK: <reason>` on that same line. I did
not add that note, so the test fails. This is a small miss on my side from today's change.

**What it affects.** The unit test run shows a failure until the note is added. The pipeline itself and the
deploy are not affected.

**Fix.** Add the note to the flagged line, for example
`secrets: |  # NFR-003-OK: names a GitHub secret, holds no value`, then re-run the unit tests.

### Failure 2 — REQ-NFR-015 (the "browser must never show an old copy of a file" test) — most likely a test that is too strict

**What this test protects against.** On 2026-08-30 you saw a page that looked broken because your browser
was still using an **old copy** of the stylesheet from before a rebuild. To stop that happening again, this
test looks at every stylesheet and script a page loads and requires one of two things:
- the file name carries a **fingerprint** (a short random code that changes whenever the file changes, so
  a new version always gets a new name and the browser can never mix them up), or
- the server tells the browser to **check for a newer copy** every time.

**What it reported.** On `/login` and the signed-in pages, two files are served with the setting "keep this
for a year, it never changes":
- `TfLens.ttggo5rqsp.styles.css`
- `ReconnectModal.abdmv1u4y3.razor.js`

**Why this is most likely the test, not the app.** Look at the names: `ttggo5rqsp` and `abdmv1u4y3`
**are** fingerprints. A new build gives these files new names, so "keep for a year" is correct and safe.
But the test only recognises a fingerprint when it sits **directly before** `.css` or `.js`
(like `app.ab12cd34.css`). In these names it sits before `.styles.css` and `.razor.js`, so the test does not
spot it and wrongly reports the files as unsafe.

**Why it passed before.** Earlier verify runs started the app as a **Debug** (development) build, where
these files are served with "check for a newer copy every time", which the test accepts. Today's run used a
**Release** (production) build, which uses fingerprints plus "keep for a year" — the correct production
setting that the test does not recognise.

**How sure is this.** It comes from reading the test's rule and the file names, not from a separate
experiment. It is not yet confirmed by, for example, running the test on a Debug build again.

**What it affects.** Nothing a user sees: the files are fingerprinted, so a stale copy cannot be served.
Only this test's result.

**Fix.** Teach the test to recognise a fingerprint anywhere in the file name (such as
`name.<code>.styles.css`), then run it on a Release build to confirm it passes.

---

## Summary table

| # | Row | What is wrong, in one line | Real bug in the app? | Affects users? |
|---|---|---|---|---|
| 1 | REQ-UI-050 | Playbook "Harness" filter on `/effort` changes nothing | Yes | Yes, lightly |
| 2 | REQ-UI-072 | One missing OpenRouter price is saved as $0 | Yes | Yes, costs can read low |
| 3 | REQ-NFR-003 | Secret test flags a line that only names a secret | No — a missing "safe" note | No |
| 4 | REQ-NFR-015 | Cache test does not recognise this kind of fingerprinted name | Most likely no — test too strict | No |
