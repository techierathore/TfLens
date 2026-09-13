# TfLens — re-checking the five framework fixes of 2026-09-12

**The short version.** All five entries the framework fixed on 2026-09-12 — TF-037, TF-038, TF-039,
TF-040 and TF-041 — were re-checked here against their own "Verify from here" step. All five behave as
the framework said they would, and all five are now closed. Nothing is blocked, and nothing in TfLens
had to change for them.

Eleven older entries are still waiting to be re-checked here: TF-018, TF-020, TF-021, TF-025 to
TF-028, TF-030, and TF-033 to TF-035. They are fixed upstream; they simply have not had their check
run here yet.

---

## What each one showed

### TF-037 — a note line no longer hides the test result

Two halves, both proved.

The note only appears when a build changes side on the same machine, so the condition was forced: the
build-side marker under `src/TfLens/obj` was set to `windows` while the scoped stylesheets were still
on disk, then a WSL build was run. The note came out **first** and the verdict **second** — so the old
"keep the first line" rule would still have taken the note. The new rule takes the line that starts
`PASS`, `FAIL` or `NOT-RUN`, and it did: running the test gate over the same condition printed
`unit tests: PASS  test on wsl via ~/.dotnet/dotnet (rung 2)`, and the results file records the unit
tests as having run, with 19 rows passing and none failing. Before the fix that whole set read as
never run.

The second half: `tf-build.sh probe` printed two clean lines — the platform and the rung order — and no
error line at all.

### TF-038 — a border that is not drawn no longer counts as one

The mockup comparison was run over `/effort`, `/misses` and `/prices` at both widths.

On `/prices` the findings fell from **15 to 6**. Everything TF-038 promised would go, went: all four
"Remove" buttons, the "Add provider" button and the "Save rate" button no longer report a badge or a
border difference, and the coloured icon on the standing note no longer reports a colour difference.
Across all three screens there is now **no** "semantic colour differs" finding at all.

One badge-and-border pair survived, on the model filter box on `/prices`. That one is **real**, not the
old false positive: reading the drawn styles directly, the mockup's filter box has a 1px solid border in
near-black and the app's has none. So the tool is still reading a border that is actually drawn, which
is exactly what the fix was meant to preserve. That difference is an app-side gap, written up below.

### TF-039 — a table that scrolls inside its own card is no longer read as cut off

Same run, at 390px. There is now **no** "content is cut off horizontally" finding on `/effort` at that
width. Before the fix, three of them were reported there — on `effort-phases`, on `effort-routing`, and
on one unnamed block of the page.

One such finding does remain, on the sidebar at 1280px. It is not new and it is not this fix: it is
present in both of the pre-fix runs kept from 2026-09-11, so it predates the change and is a separate
matter.

### TF-040 — a command that chains another can now record its own first segment

This was reproduced on a throwaway copy of the telemetry stream, so the real records were never
touched.

The chained verify's record was written first, covering 18:13 to 21:13. The outer build's own first
segment, 16:53 to 18:13, was then offered **after** it — the exact case that used to be refused. It was
accepted, and the stream went to two records.

The guard still works: a run that genuinely overlaps, 17:30 to 19:00, was refused, nothing was written,
and the refusal named the record it collided with by command and by window.

### TF-041 — the checker no longer reports a document that is on disk as missing

The document check was run over the three checklists **without** naming the Phases document, which is
the case that used to produce a false failure. The words "Phases document" do not appear anywhere in
the output. The run finished with no failures, 179 warnings across the three documents, and the 50
findings that pre-date this session correctly carried as old rather than blocking. The cross-phase
rules still ran.

---

## The one thing found that is ours, not the framework's

**The model filter box on the Prices screen has no border, and its mockup does.** The approved mockup
draws a 1px solid border in near-black around the filter input; the running app draws none. This is a
small visual difference on one control, it is not new today, and nothing is broken by it — the filter
works. It was not fixed in this run because this run was a re-check, not a fix: code changes come from
a build or a fix command, not from here.

It is worth folding into the next fix pass over the Prices screen.
