# TfLens — TrBlazeUI feedback

> ## ✅ RESOLVED LIBRARY-SIDE 2026-08-31 — ships in the next release
>
> Triaged (`*triage-issues`) and fixed (`*fix-issues`) against current source under **REQ-UI-019**.
> Release build 0 warnings / 0 errors; **44/44** new headless-Chromium checks
> (`tests/verify/ui-tflens.spec.js`) with every existing suite still green — **166/166** executed
> checks in total. Evidence: `docs/TrBlazeUI-Checklist.md` REQ-UI-019, ledger `docs/.last-verify.json`.
>
> **First, the version gap — it accounts for a quarter of this file.** TfLens built against
> **2.0.0**; the library was already at **2.1.0** when this was filed. **Five entries were already
> fixed** and needed no library change at all:
>
> | Entry | Reported as | Reality on 2.1.0 |
> |---|---|---|
> | **TR-002** | "no responsive variants at all; 1 `@media` rule; 710 utilities" | **21** `@media` rules, **4,934** escaped responsive selectors, ~10,477 class selectors. `.trblazeui-col-md-6` really does sit inside `@media (min-width:48rem)`. |
> | **TR-003** | `PasswordStrength`/`CenteredPanel`/`StatTile`/`StatGroup`/`CodeBlock` absent | All present **and documented**. |
> | **TR-013** | `Typography*` drops unmatched attributes | All 11 splat `CaptureUnmatchedValues`. |
> | **TR-020** | `SelectValue` shows the raw key until the popover opens | `SelectContent` instantiates items while closed; `DisplayTextSelector` also exists. |
> | **TR-023** | `BreadcrumbLink` throws on `data-testid` | Splats on both render branches. |
>
> **The action for those five is to upgrade and delete the workarounds** — in particular the
> hand-written media queries in `AuthLayout.razor.css` / `Profile.razor.css`, and
> `PasswordStrengthMeter.razor`.
>
> **Everything else is now fixed.** Highlights, with the root cause where it differed from the
> filed diagnosis:
>
> - **TR-009** — confirmed exactly as filed and it was live in this library's *own* demo (500 records
>   with `ShowPagination="false"` rendering 5 rows). `ShowPagination` was a *chrome* flag; it now gates
>   the data. ⚠ **Breaking:** a grid you turned pagination off over will now paint every row.
> - **TR-008** — your merged TR-022 root cause was **exactly right**. `lucide.json` ships 212 aliases;
>   the *code generator* only ever read the `icons` map, so the C# had nothing to fall back to. Fixed at
>   the generator. One correction: a question glyph **does** exist (`circle-help` → `circle-question-mark`),
>   so the `list-checks` substitution is unnecessary. (`content/lucide.json` **is** in the nupkg and always
>   was — an intermediate triage note claiming otherwise was wrong and has been retracted, so your
>   name-lookup escape hatch works today.)
> - **TR-014** — your diagnosis ("AlertDialog is not built on `Primitives.Dialog`") was **not** the cause:
>   it is built on the primitive. `AlertDialogContent` pinned `CloseOnEscape="false"` as a literal with no
>   parameter to override, and `Dialog.Modal` was a parameter **nothing read** while the reference
>   documented it. Both fixed. Your second observation — Escape dying after a re-render — was correct and
>   subtle: Escape was an *element* handler, and a document-level helper had shipped with zero call sites.
> - **TR-011a** — the ApexCharts runtime **is** loaded (that part was wrong), and there is **no reflection
>   inference** — so the property-order projection workaround was cargo-culting a mechanism that does not
>   exist. `ChartBase.Items` was simply a parameter nothing read. It is now wired, and real `XValue`/`YValue`
>   were added — which also closes **TR-005**'s original ask.
> - **TR-018 / TR-019 / TR-016 / TR-012 / TR-024 / TR-025 / TR-027** — all fixed; see the CHANGELOG.
> - **TR-011b, TR-015, TR-026, TR-017, TR-004** were **documentation** defects, and the reference was the
>   single largest source of your build problems. §1's imports block was missing **30** namespaces
>   (including `Empty`); `DataTableColumn`, `DialogContent` and `AlertDialogContent` had no parameter
>   tables at all; the icon guidance forbade an API that works. All corrected.
> - **TR-001 / TR-021** were mostly closed in 2.1.0; the residual is fixed — `--font-sans`/`--font-mono`
>   were referenced by the base reset and never defined, and the `p-*`/`m-*`/`gap-*`/`size-*`/`space-*`
>   safelist stopped short of the scale `w-*`/`h-*` already covered. **Arbitrary values (`p-[3px]`,
>   `h-[34px]`) remain structurally unavailable** in a pre-built stylesheet — that constraint is real and
>   is now stated in the reference rather than left to be discovered.
>
> **Please re-test against the next published release and reopen anything that still bites.** Read the CHANGELOG's
> "Behaviour changes to review before upgrading" first — several fixes change behaviour by design.

Gaps found while building TfLens against **TrBlazeUI.Components 2.0.0** / **TrBlazeUI.Icons.Lucide 2.0.0**.
Each entry is a real blocker or defect met during the build, with the workaround that shipped so the
build never stopped for a library issue.

> **Numbering reconciled 2026-08-27.** Concurrent build clusters had independently allocated the same
> TR numbers four times (`TR-010`, `TR-011`, `TR-012`, `TR-014` each appeared twice). The **first**
> occurrence of each kept its number — those are the ones cited from the checklist and the DevGuide —
> and the four later duplicates were renumbered to `TR-015`…`TR-018`. All entries were then unique and
> every citation elsewhere in `docs/` resolved. `TR-006` and `TR-007` were never allocated and are
> deliberately left free. **Allocating a number: take the next free one, and never renumber an existing
> entry.**
>
> **Updated 2026-08-28 (handoff consolidation).** `TR-019`…`TR-022` were added by the Phase 3 build, and
> `TR-022` turned out to duplicate `TR-008` (both are `LucideIcon` failing to resolve `lucide.json`
> aliases) — it was merged into `TR-008` and survives as a redirect stub, because other documents already
> cite it. **The highest allocated number is `TR-022`; the next free one is `TR-023`.** That leaves
> **19 substantive entries** across 20 headings. The three stale `TR-006` / `TR-007` comments that used
> to sit in `src/` were corrected the same day to the numbers they actually meant (`TR-009`, `TR-010`,
> `TR-013`), so no citation anywhere now points at an unallocated number.
>
> **Re-verified 2026-08-28.** `TR-019`…`TR-022` were added since. Every ID in the file is unique, every
> in-file cross-reference resolves (`TR-002` ← TR-019/TR-021 · `TR-010` ← TR-019 · `TR-008` ⇄ `TR-022`),
> and no number was reused. `TR-006` and `TR-007` remain unallocated — see *Known-stale citations* below
> for the three `src/` comments that still name them. **The next free number is `TR-023`.**
>
> **Allocated since (2026-08-30):** `TR-023` (BreadcrumbLink passthrough), `TR-024` (TabsList
> passthrough + no track density), `TR-025` (DataTable has no density option) and `TR-026`
> (`DataTableColumn` width / per-cell class parameters undocumented). **The next free number is
> `TR-028`.** `TR-027` (BreadcrumbList always wraps) was added 2026-08-30 by the harness fix.
>
> **Collision corrected 2026-08-30.** Two `*build-phase` sub-agents running in parallel both allocated
> `TR-024` — the `TabsList` entry and the `DataTableColumn` entry. `TR-024` was already registered above
> to `TabsList`, so it keeps the number; the `DataTableColumn` entry was renumbered to **`TR-026`**, and
> the one in-file citation of it (inside `TR-025`) was repointed with it. Every ID in the file is unique
> again. The lesson is recorded as a process note, not a library gap: concurrent writers to a
> single-counter file need the number assigned by the orchestrator, not chosen by the writer.

---

## Summary

- **5 blockers, 11 majors, 8 minors, 0 nice-to-haves** — 24 entries, **all 24 open**. None is fixed
  upstream; every one shipped with a workaround instead.
- Last consolidated: 2026-08-28. **Re-tallied 2026-08-30** for the four entries added during the
  `*build-phase` mockup-parity FIX pass: `TR-023`, `TR-024` and `TR-026` are Medium (major),
  `TR-025` is Low (minor). Counted from the 24 `## TR-` headings less the one merge stub
  (`TR-022` → `TR-008`); `TR-006` and `TR-007` remain unallocated.

**Severity words used in the entries map to those counts as:** `High` = blocker · `Medium` = major ·
`Low` = minor. Nothing in this file is filed nice-to-have. The entry bodies keep their original
`High`/`Medium`/`Low` wording — the mapping is stated here rather than applied to them, so no entry's
recorded severity was silently reinterpreted.

| Band | Count | Entries |
|---|---|---|
| **Blocker** (High) | 5 | TR-001 · TR-002 · TR-009 · TR-011 · **TR-021** |
| **Major** (Medium) | 8 | TR-003 · TR-005 · TR-008 · TR-010 · TR-014 · TR-018 · TR-019 · TR-020 |
| **Minor** (Low) | 6 | TR-004 · TR-012 · TR-013 · TR-015 · TR-016 · TR-017 |
| Nice-to-have | 0 | — |

Entries below are ordered **blocker → major → minor**, and by ID within a band. **IDs are unchanged** —
the order is a reading aid, never a renumbering.

### What changed in the 2026-08-28 consolidation

1. **`TR-022` merged into `TR-008`.** Same repro, same expected, same actual — one defect filed twice,
   the second time without re-reading the first. The lower ID keeps the entry; `TR-022` remains as a
   stub at the foot of the file so the citations in `docs/TfLens-DevGuide-Screens.md` and
   `PROJECT-STATUS.md` still resolve. TR-022's root-cause finding (the unread `aliases` map in
   `lucide.json`) is carried into TR-008 in full — it is the better half of the merge. **This is why
   the file holds 19 entries, not 20.**
2. **`TR-021` raised Medium → High.** A silent zero-width render with no error and no console warning
   is the same failure class this file already grades High in TR-009 and TR-011(a), and it defeats the
   usual visibility checks besides. The reason is written into the entry itself.
3. **No entry was renumbered, and no entry was deleted.**

### Known-stale citations elsewhere (not fixable from this file)

`TR-006` and `TR-007` were **never allocated** here, but three source comments still cite them, having
been written before the 2026-08-27 renumbering:

| Citation | Cites | Actually means |
|---|---|---|
| `src/TfLens/Components/Pages/Harness.razor:99` | `TR-006` | **TR-009** (`ShowPagination="false"` still truncates to `InitialPageSize`) |
| `src/TfLens/Components/Pages/GateOutcomes.razor:92` | `TR-006` | **TR-010** (`TabsTrigger` captures no unmatched attributes) |
| `src/TfLens/Components/Pages/GateOutcomes.razor:22` | `TR-007` | **TR-013** (the `Typography*` family) |

Left as-is deliberately: they are `src/` comments and this pass is documentation-only. Recorded here so
the next reader is not sent looking for two entries that do not exist. `docs/TfLens-DevGuide-Screens.md`
already lists its own `TR-007` mention under "stale comments to ignore".

---

## TR-001 — The shipped stylesheet references design tokens it never defines (Alert has no colour, `bg-sidebar` is transparent)

- **Severity:** High — every `Alert` variant renders colourless, and the whole sidebar/auth brand surface renders transparent.
- **Repro:** Install `TrBlazeUI.Components` 2.0.0, link `_content/TrBlazeUI.Components/trblazeui.css`, render
  `<Alert Variant="AlertVariant.Danger"><AlertTitle>Error</AlertTitle></Alert>` or any element with `class="bg-sidebar"`.
- **Expected:** The danger alert has a red tint, a red left accent and a red icon; `bg-sidebar` paints the sidebar surface.
- **Actual:** `trblazeui.css` emits `.bg-alert-danger-bg{background-color:var(--alert-danger-bg)}`,
  `.border-l-alert-danger{border-left-color:var(--alert-danger)}`, `.bg-sidebar{background-color:var(--sidebar)}` …
  but **defines none of those custom properties anywhere** in the package. The full undefined set is:
  `--alert-success`, `--alert-success-bg`, `--alert-info`, `--alert-info-bg`, `--alert-warning`,
  `--alert-warning-bg`, `--alert-danger`, `--alert-danger-bg`, `--sidebar`, `--sidebar-foreground`,
  `--sidebar-border`, `--sidebar-accent`, `--sidebar-accent-foreground`, `--sidebar-ring`,
  `--font-sans`, `--font-mono`. The `:where(:root)` / `:where(.dark)` blocks define only the base
  shadcn tokens (`--background` … `--ring`, `--radius`).
- **Encountered in:** REQ-UI-001..005 (every auth screen's `Alert`, the auth brand panel's `bg-sidebar`).
- **Workaround:** declare the missing custom properties on the page/layout root in the component's own
  scoped stylesheet (`AuthLayout.razor.css`, `Profile.razor.css`). Custom properties inherit, so every
  TrBlazeUI control rendered inside picks them up. This is per-page, not app-wide — the sidebar shell
  will need the same treatment.
- **Suggested fix:** ship the `--alert-*` and `--sidebar*` token values in the same `:where(:root)` /
  `:where(.dark)` blocks that already carry `--background` and friends.

---

## TR-002 — The shipped stylesheet contains no responsive variants at all

- **Severity:** High — no breakpoint can be expressed in markup, so every responsive layout has to be hand-written CSS.
- **Repro:** Search `trblazeui.css` (2.0.0) for any `md:`, `sm:` or `lg:` escaped class. There are none;
  the file contains exactly one `@media` rule (a `prefers-color-scheme`-independent one for the chart tooltip)
  and 710 utility classes total — only those the library itself uses.
- **Expected:** Tailwind's standard responsive variants (`md:grid-cols-2`, `md:flex-row`, `sm:p-8`, …) are usable
  from the `Class` parameter, as every TrBlazeUI example and the AI reference's own snippets assume.
- **Actual:** `md:grid-cols-2` etc. are absent, so `Class="grid gap-4 md:grid-cols-2"` renders a single column at
  every width. `grid-cols-2` itself is also absent (only `grid-cols-3` and `grid-cols-4` shipped).
  `.trblazeui-col-md-6` exists but is **not** wrapped in a media query — it is byte-identical to `.trblazeui-col-6`,
  so the "responsive" grid helper is not responsive either.
- **Encountered in:** REQ-UI-001..004 (auth split layout), REQ-UI-005 (two profile cards stacking under `md`).
- **Workaround:** hand-written media queries in Blazor scoped stylesheets (`AuthLayout.razor.css`, `Profile.razor.css`).
- **Suggested fix:** either ship the full Tailwind utility surface, or document that consumers must run their
  own Tailwind build over their markup — and put the `trblazeui-col-{sm,md,lg,xl}-*` helpers inside real media queries.

---

## TR-009 — `DataTable ShowPagination="false"` still truncates the grid to `InitialPageSize`

- **Severity:** High — the table silently drops rows with no pager, no count and no warning; the page looks complete.
- **Repro:** `<DataTable TData="GateRow" Data="@eightRows" ShowToolbar="false" ShowPagination="false">` with eight rows.
- **Expected:** turning pagination off renders every row — that is the only reason to turn it off. The AI
  reference describes `ShowPagination` as "Allow pagination; the bar auto-hides when all rows fit one page",
  which reads as "no pager, all rows".
- **Actual:** only the first `InitialPageSize` rows render (default **5**). With `ShowPagination="false"` there is
  no pager to reveal the rest, so rows six onwards are simply absent from the DOM. On `/gate-outcomes` this
  silently cut the `standards`, **`escaped`** and `unattributed` rows off the gate-catch distribution — the
  `escaped` row is the one row the requirement exists for.
- **Encountered in:** REQ-UI-020 (`gate-dist-{type}`, eight fixed rows in the reference's gate order).
- **Workaround:** set `InitialPageSize` above the row count (`InitialPageSize="32"`) whenever pagination is off.
- **Suggested fix:** when `ShowPagination` is `false`, ignore `InitialPageSize` and render the whole `Data`
  sequence; or document loudly that `InitialPageSize` still applies.

---

## TR-011 — `BarChart` renders an empty `<div>` (the ApexCharts runtime is never loaded), and a portalled `DialogContent` cannot be sized by the consumer

Two defects met on the same screen; both make a control unusable as shipped.

**(a) The chart renders nothing.**

- **Severity:** High — the component produces a blank box, with no error, no console warning and no fallback.
- **Repro:** `<ChartContainer><BarChart TItem="Point" Items="@points" Height="240" /></ChartContainer>` in a
  stock Blazor Server app that references `TrBlazeUI.Components` 2.0.0 and links `trblazeui.css`.
- **Expected:** bars.
- **Actual:** the rendered DOM is `<div class="w-full" data-slot="bar-chart"><div id="0e17…"></div></div>` —
  the mount point is created and never filled. `Blazor-ApexCharts` is a package dependency, but no
  `apexcharts` script or JS module is loaded (the package's own `staticwebassets/js/` ships twelve interop
  files and none of them is a chart), and nothing in the component or the docs tells a consumer to add one.
- **Encountered in:** REQ-UI-028 (tokens-by-model totals).
- **Workaround:** the totals are drawn as a small CSS bar row in the page's scoped stylesheet, each bar
  labelled with the exact figure the table beside it states.
- **Suggested fix:** load the ApexCharts asset from the component (or document the required script tag), and
  make an unrenderable chart show its data as a table rather than an empty box.

**(b) A dialog cannot be widened.**

- **Severity:** Medium.
- **Repro:** `<DialogContent Class="my-wide-dialog">` with `.my-wide-dialog { max-width: 44rem }` in the
  calling page's scoped stylesheet, with or without `::deep`.
- **Expected:** the dialog is 44rem wide.
- **Actual:** it stays at `max-w-lg` (512px). `DialogContent` is rendered into `div.trblazeui-portal`
  directly under `<body>`, outside the calling component's subtree, so a Blazor scoped stylesheet can never
  match it — `::deep` included, since that still requires an ancestor inside the component. The only
  escape hatches the shipped CSS offers are `max-w-sm` / `max-w-md` / `max-w-lg` / `max-w-none`, and
  `max-w-none` with the component's own `w-full` means edge-to-edge.
- **Encountered in:** REQ-UI-030 (a five-column rate-card table in a 512px dialog).
- **Workaround:** size the cells instead (`w-16 text-right` on each `Input`) and keep the table in an
  `overflow-x-auto` box.
- **Suggested fix:** ship a `Size`/`MaxWidth` parameter on `DialogContent`, or a documented CSS custom
  property the portal honours.

---

## TR-021 — The shipped stylesheet's spacing/sizing scale has holes (`w-20`, `mt-3`, `my-4` are absent), so a control sized with one silently collapses to zero

- **Severity:** High — **raised from Medium on 2026-08-28 consolidation.** A component given
  `Class="w-20"` renders with **zero width**: no error, no warning, no console message, and a control
  that looks deliberately hidden rather than broken. That is the same failure class this file already
  grades High elsewhere — TR-009 (rows silently dropped, "the page looks complete") and TR-011(a) (an
  empty chart box, "no error, no console warning and no fallback") — and this one is worse than both,
  because the collapsed control still passes every "is it visible?" test. Filed Medium when it was
  written; nothing about the evidence supported that, so it is corrected here rather than left
  inconsistent with its own neighbours. **This is the highest-value entry in the file:** the whole
  utility surface is generated from the library's own internal usage, so the hole is unbounded and
  every consumer meets it eventually, on a different class each time.
- **Repro:** `<Progress Value="24" Class="w-20" />` in an app that links only
  `_content/TrBlazeUI.Components/trblazeui.css`. Then measure:
  `document.querySelector('[role="progressbar"]').getBoundingClientRect().width`.
- **Expected:** 5rem. The class is part of the Tailwind scale the library's own markup and its AI
  reference use, and the neighbouring steps (`w-16`, `w-40`) are present.
- **Actual:** `0`. `trblazeui.css` emits `.w-2 .w-3 .w-4 … .w-14 .w-16 .w-40 .w-64 .w-72` and stops — the
  file carries only the utilities the library's own components happen to use, so the scale reads as
  complete while several steps are missing. The same is true of `mt-3` and `my-4` (both `mt-2` and `mt-4`
  exist, so the gap is invisible until a build lands on the wrong step). This is the sibling of TR-002:
  there the responsive variants were byte-identical no-ops, here the base utilities are simply absent.
- **Encountered in:** REQ-UI-037 — the failed-practice share bars on `/misses` (`miss-whymissed`), which
  rendered as a zero-width `Progress` beside a correct percentage; and the `Separator`s in the cost band.
- **Workaround:** use a step the file actually ships (`w-16` here) or declare the rule in the page's own
  scoped CSS. Before shipping a screen, grep the class names used against `trblazeui.css` — a class the
  file does not define fails silently and geometry gates only catch it when the collapse causes an overlap.
- **Suggested fix:** ship the full base scale for the utilities the components and the AI reference name,
  or document exactly which subset is supported so a consumer knows when to write their own rule.

---

## TR-003 — `PasswordStrength`, `CenteredPanel`, `StatTile` / `StatGroup` and `CodeBlock` do not exist in 2.0.0

- **Severity:** Medium — the components the design spec was written against are not in the shipped package.
- **Repro:** `TrBlazeUI.Components` 2.0.0 `lib/net10.0/TrBlazeUI.Components.dll` contains no
  `TrBlazeUI.Components.PasswordStrength`, `…CenteredPanel`, `…Stat`, or `…CodeBlock` namespace or type.
  The package's own `docs/TrBlazeUI-AI-Reference.md` does not document them either.
- **Expected:** `docs/TfLens-UIDesign.md` §Library gaps records these as present in the GitHub repo at mockup time
  and mandates their use.
- **Actual:** absent from the NuGet package the project consumes.
- **Encountered in:** REQ-UI-002 / REQ-UI-004 / REQ-UI-005 (`PasswordStrength`), REQ-UI-001..004 (`CenteredPanel`).
- **Workaround:** `PasswordStrengthMeter.razor` composes the meter from the library's own `Progress`
  (the fallback §Library gaps itself names); the auth card is centred by the layout's own flexbox instead of
  `CenteredPanel`.
- **Suggested fix:** publish a NuGet build that carries the components already in the repo, or correct the
  reference doc to match what 2.0.0 actually ships.

---

## TR-005 — The chart components have no `XValue` / `YValue`; the AI reference documents an API that does not exist

- **Severity:** Medium — every documented chart snippet fails to compile.
- **Repro:** `<BarChart TItem="Point" Items="@points" XValue="@(p => p.Label)" YValue="@(p => p.Total)" />`,
  copied verbatim from `TrBlazeUI-AI-Reference.md` §"Chart (ApexCharts wrapper)".
- **Expected:** compiles, per the reference.
- **Actual:** `error CS8917: The delegate type could not be inferred` — `BarChart<TItem>` (and every other
  chart type, all deriving from `ChartBase<TItem>`) exposes only
  `Items`, `Config` (`ChartConfig` → `ChartSeriesConfig { Label, Color }`), `Height`, `Width`, `Class`,
  `ShowLegend`, `LegendPosition`, `ShowDataLabels`, `ShowTooltip`, `Title`, `EnableAnimations`, plus the
  per-type `Variant`. Categories and series are inferred from `TItem`'s properties by reflection; there is
  no accessor pair at all, so a caller cannot say which property is the category and which is the value.
- **Encountered in:** REQ-UI-028 (tokens-by-model totals chart).
- **Workaround:** shape a purpose-built projection type whose property order matches what the reflection
  expects (`record ModelTotal(string Model, double Total)`) and pass only `Items`. Because the mapping is
  implicit and undocumented, TfLens also renders every charted value as text in the table beside the chart —
  which `docs/TfLens-UIDesign.md` §Library gaps already requires for a different reason.
- **Suggested fix:** add `XValue` / `YValue` (or a `ChartSeries` child component) as the reference already
  claims, or correct the reference to document the reflection contract and its ordering rules.

---

## TR-008 — `TrBlazeUI.Icons.Lucide` 2.0.0 does not resolve the pre-rename Lucide names, so every `*-circle` / `alert-*` name renders nothing

> **Merged 2026-08-28.** `TR-022` was filed on 2026-08-28 as a separate entry (*"`LucideIcon` does not
> resolve the aliases in its own `lucide.json`"*) before this entry was re-read. It carries the **same
> repro** (`<LucideIcon Name="check-circle" Size="16" />`), the **same expected** (the circled check
> renders) and the **same actual** (nothing renders), so it is the same defect, not a second one — and
> `docs/TfLens-DevGuide-Screens.md:203` says as much: *"Confirmed again 2026-08-28 as `TR-022`"*.
> Per this file's numbering rule the lower ID wins, so the two are merged here under **TR-008** and
> `TR-022` is kept as a stub that points at this entry, because `docs/TfLens-DevGuide-Screens.md` and
> `PROJECT-STATUS.md` already cite it. **TR-022's contribution is kept and it is the valuable part:** it
> supplies the root cause this entry originally guessed at. The severity is TR-008's Medium (the higher
> of the two); TR-022 had been filed Low.

- **Severity:** Medium — the icon silently disappears; there is no build error and no runtime error, only a
  `data-trblazeui-missing-icon` placeholder that occupies the right box and draws nothing.
- **Repro:** `<LucideIcon Name="check-circle" Size="16" />`, `<LucideIcon Name="alert-triangle" Size="16" />`,
  `<LucideIcon Name="help-circle" Size="16" />`, `<LucideIcon Name="x-circle" Size="16" />`.
- **Expected:** the icons render. These are the names the package's own `TrBlazeUI-AI-Reference.md` uses
  (`<Alert Variant="AlertVariant.Danger"><LucideIcon Name="alert-circle" …>`), and the names
  `docs/TfLens-UIDesign.md` and every mockup were written against. They are also real Lucide names that
  are **present in the package's own `content/lucide.json`**, under `aliases`.
- **Actual:** nothing renders for any pre-rename spelling. Probed on the running app,
  `circle-check`, `circle-alert`, `triangle-alert`, `circle-x`, `badge-check`, `x`, `clock`, `percent`,
  `target`, `shield-check`, `ban`, `bug`, `list-checks`, `gauge`, `trending-up`, `trending-down`, `flag`,
  `zap`, `chart-bar` and `bar-chart-3` all render; `check-circle`, `check-circle-2`, `alert-circle`,
  `alert-triangle`, `x-circle`, `help-circle` and `circle-help` all render nothing. There is no
  `circle-help` under either name, so a "question" glyph is simply unavailable.
  This is already visible in the shipped shell: the sidebar's **Gate outcomes** item (`shield-check`; `help-circle` when this was logged) has no
  icon at all, and `Repos.razor`'s rate-limit and private-repo alerts (`alert-triangle`) have none either.
- **Root cause (from the merged TR-022, 2026-08-28):** the names are **not missing from the package** —
  this entry's original reading ("2.0.0 carries only the post-rename names") was one layer short.
  `lucide.json` ships two maps, `icons` and `aliases`, and the aliased spellings are all present in
  `aliases`. The component looks the name up in **`icons` only**, so every aliased spelling
  (`check-circle` → `circle-check`, and the rest of Lucide's renamed set) resolves to nothing. There is
  no warning and no fallback glyph, which is why a blank icon is indistinguishable from a deliberate one.
- **Encountered in:** REQ-UI-018 (the three KPI tiles' icons and the empty state's icon on
  `/gate-outcomes`); and, as TR-022, REQ-UI-036 — the measured-USD tile's accent chip on `/misses`
  rendered as an empty coloured square.
- **Workaround:** use the canonical name from the `icons` map — `circle-check`, `triangle-alert`, `x` —
  and substitute `list-checks` where no question glyph exists. A quick check against
  `~/.nuget/packages/trblazeui.icons.lucide/<version>/content/lucide.json` settles any name.
- **Suggested fix:** **fall back to the `aliases` map on a miss in `icons`** — the data is already in the
  package, only the lookup is one map short, so this is a small change that fixes every aliased name at
  once. Failing that, ship the pre-rename aliases into `icons` directly. Either way, emit a build-time or
  development-time warning naming the closest match for a name that resolves in neither map, and update
  `TrBlazeUI-AI-Reference.md` to the names the package actually carries.

---

## TR-010 — `TabsTrigger` / `TabsContent` / `TabsList` capture no unmatched attributes, so a tab cannot carry a test id

- **Severity:** Medium — it makes the tab controls unaddressable from any test or analytics attribute, and it
  fails at **runtime**, not at compile time.
- **Repro:** `<TabsTrigger Value="drift" data-testid="routing-tab-drift">Routing drift</TabsTrigger>`.
- **Expected:** the attribute lands on the rendered trigger, as it does on `Tabs`, `Card`, `Badge`, `Button`,
  `Alert`, `DataTable`, `Empty` and every other component in the library.
- **Actual:** `System.InvalidOperationException: Object of type 'TrBlazeUI.Components.Tabs.TabsTrigger' does not
  have a property matching the name 'data-testid'` — a 500 on the page, with nothing at build time to warn you.
  `TabsTrigger` exposes only `Value`, `Disabled`, `ChildContent`, `Class`; `TabsList` and `TabsContent` only
  `ChildContent`/`Class` (+`Value`/`ForceMount`). `Dialog`, `DialogHeader` and `DialogFooter` have the same gap.
- **Encountered in:** REQ-UI-027 (the `routing-tab-{key}` ids the acceptance asserts literally).
- **Workaround:** put the id on a `<span>` inside the trigger's `ChildContent` — a click on the span still
  activates the trigger, and the id is where a test can find it.
- **Suggested fix:** add `[Parameter(CaptureUnmatchedValues = true)] Dictionary<string, object>? AdditionalAttributes`
  to `TabsList`, `TabsTrigger`, `TabsContent`, `DialogHeader` and `DialogFooter`, and splat it, as the rest of
  the library already does.

---

## TR-014 — `AlertDialog` has no Escape handling at all, and no `CloseOnEscape` / `Modal` to turn it on

- **Severity:** Medium — a confirmation dialog that cannot be dismissed with Escape fails a plain
  accessibility expectation, and there is no parameter to opt in.
- **Repro:** `<AlertDialog @bind-Open="objIsRemoveOpen"><AlertDialogContent>…<AlertDialogCancel>…</AlertDialogCancel>
  <AlertDialogAction>…</AlertDialogAction></AlertDialogContent></AlertDialog>`, open it, press **Escape**.
- **Expected:** Escape closes the alert dialog, the same way it closes `Dialog` and `Sheet` — the shadcn/ui
  AlertDialog this component mirrors closes on Escape by default, and the package's own `Dialog` documents
  `Modal` as "Dismiss on outside click/Escape".
- **Actual:** nothing happens; only the `AlertDialogCancel` button closes it. `TrBlazeUI.Components.AlertDialog.AlertDialog`
  exposes only `ChildContent` / `Open` / `OpenChanged` / `DefaultOpen` / `OnOpenChange`, and `AlertDialogContent`
  only `ChildContent` / `Class` / `AdditionalAttributes` — there is no `Modal`, no `CloseOnEscape`, no
  `OnEscapeKeyDown`. Unlike `Dialog`, `AlertDialog` is not built on `TrBlazeUI.Primitives.Dialog.DialogContent`
  (there is no `AlertDialog` type in `TrBlazeUI.Primitives` at all), so it never inherits that primitive's
  `CloseOnEscape` / `TrapFocus` / `LockScroll` behaviour. Passing `CloseOnEscape="true"` through
  `AdditionalAttributes` only emits a stray HTML attribute.
- **Encountered in:** REQ-UI-013 (the remove-repo confirmation on `/repos`), whose acceptance names Escape.
- **Related, same page:** the Connect `Dialog` — which *does* have `CloseOnEscape` via the primitive — stopped
  honouring Escape once a validation result had re-rendered its content (REQ-UI-012's recorded observation).
  Both were cured by the same page-level listener below, which suggests the primitive's document `keydown`
  registration does not survive a content re-render.
- **Workaround:** own the key in the page. `src/TfLens/Components/Pages/Repos.razor.js` adds one capture-phase
  `document` `keydown` listener and calls a `[JSInvokable]` method on the component, which sets the bound
  `Open` field to `false`; the listener ignores the press when an open `role="listbox"` popup (Select/Combobox)
  should consume it, and the page implements `IAsyncDisposable` to remove it. Costs an interop hop per Escape
  and has to be repeated on every page that uses `AlertDialog`.
- **Suggested fix:** build `AlertDialog` on the same `Primitives.Dialog` layer `Dialog` uses, or at minimum add
  `Modal` / `CloseOnEscape` / `OnEscapeKeyDown` to `AlertDialogContent` with `CloseOnEscape` defaulting to true;
  and re-register the primitive's Escape listener on re-render so a dialog whose body changes keeps its key
  handling.

---

## TR-018 — A closed `CollapsibleContent` keeps its children in normal flow, so the collapsed panel still occupies (and overlaps) the space below it

- **Severity:** Medium — a collapsed disclosure silently lands on top of whatever follows it in the page,
  and the collision is invisible in a screenshot (the text is clipped) but real to every geometry-based
  layout gate, to hit-testing, and to a screen reader walking the accessibility tree.
- **Repro:** render a card whose body is
  `<Collapsible @bind-Open="objIsOpen"><CollapsibleTrigger>…</CollapsibleTrigger><CollapsibleContent><div data-testid="body">…tall content…</div></CollapsibleContent></Collapsible>`
  with `objIsOpen` **false**, and put a sibling element after the card. Then measure:
  `document.querySelector('[data-testid="body"]').getBoundingClientRect()`.
- **Expected:** a closed `CollapsibleContent` contributes no box — either its children are not rendered at
  all, or the subtree is `display:none` / `hidden` so nothing inside it reports a non-zero rect. That is
  what the shadcn/ui Collapsible this component mirrors does (Radix sets `hidden` on closed content).
- **Actual:** the children are rendered and laid out normally inside a zero-height, `overflow: hidden`
  wrapper. `overflow:hidden` clips the paint but does **not** clip layout, so the inner element still
  reports its full natural height — measured 45px of vertical overlap with the next sibling card on a
  1280px viewport. `getComputedStyle` reports neither `display:none` nor `visibility:hidden`, so the
  usual "is it visible?" tests all say yes.
- **Encountered in:** REQ-UI-034 — `Components/Shared/Playbook/PlaybookObservedFields.razor`, the
  "Observed fields" disclosure on the Coverage page's Playbook state. The visual gate in
  `tests/verify/ui-playbook-state.spec.ts` failed with
  `playbook-observed-fields overlaps pb-coverage-notes by 958x45px @1280`.
- **Workaround:** guard the content in the component itself —
  `<CollapsibleContent>@if (objIsOpen) { <div …>…</div> }</CollapsibleContent>` — so the closed state has
  no subtree at all and therefore no phantom box. The disclosure also now opens by default, which is what
  the mockup shows. The cost is that the content is re-created on every open rather than merely revealed,
  so any transient state inside a collapsible has to live outside it.
- **Suggested fix:** put `hidden` (or `display:none`) on `CollapsibleContent`'s root while closed, the way
  Radix's Collapsible does; if the animation needs the box to exist mid-transition, drop it once the
  transition ends rather than leaving it in flow indefinitely.

---

## TR-019 — `DialogContent` never scrolls, so a dialog taller than the viewport is simply unreachable below the fold

- **Severity:** Medium — content and controls disappear off the bottom of the screen with no scrollbar and
  no indication that anything is missing, and the consumer has no parameter to turn a scroll on.
- **Repro:** put ~900px of fields into `<Dialog><DialogContent>…<DialogFooter>…</DialogFooter></DialogContent></Dialog>`
  and open it at 1280x800 or 390x844.
- **Expected:** either the content area between `DialogHeader` and `DialogFooter` scrolls (the shadcn/ui
  dialog this mirrors caps the panel at `max-h-[calc(100%-2rem)]` and lets the body scroll, keeping the
  header and the footer pinned), or a `MaxHeight` / `ScrollBody` parameter exists to opt in.
- **Actual:** `DialogContent` renders a fixed, centred panel with no height cap and no `overflow` on any
  of its parts. The panel grows past the viewport, the overlay does not scroll, and the bottom of the
  dialog — including `DialogFooter` and its primary action — is unreachable. `DialogContent` exposes only
  `Class`, `ShowCloseButton` and `ChildContent`; there is no height, scroll or size parameter, and the
  same is true of `DialogHeader` / `DialogFooter` (which additionally capture no unmatched attributes,
  TR-010).
- **Encountered in:** REQ-UI-040 — the Add-source dialog on `/repos`, whose import mode carries a mode
  fork, four fields, a drop zone, a five-row preview table and a summary. At 390x844 the Import button
  was below the fold and could not be pressed.
- **Workaround:** the page owns the scroll. Everything between the header and the footer is wrapped in a
  single `.tflens-dialog-body` (`Components/Pages/Repos.razor.css`) with `max-height: 68vh; overflow-y:
  auto`, which pins the header and the footer and scrolls the middle. `vh` and `max-h-*` are not in the
  shipped stylesheet either (TR-002), so the rule has to live in the page's scoped CSS rather than in a
  utility class. The cost is that a control scrolled out of the body still reports its true rect to a
  geometry check, so a naive overlap gate sees the clipped control colliding with the footer.
- **Suggested fix:** cap `DialogContent` at the viewport and make the region between `DialogHeader` and
  `DialogFooter` `overflow-y: auto`, as shadcn/ui does; failing that, add a `MaxHeight` parameter and let
  the consumer choose.

---

## TR-020 — `SelectValue` renders the raw bound value until `SelectContent` has rendered once, so a closed select shows its key rather than its label

- **Severity:** Medium — the control's resting state shows an internal identifier to the user; the correct
  label only appears after the popover has been opened once, which most readers never do.
- **Repro:** `<Select TValue="string" Value="@objPeriod" ValueChanged="…"><SelectTrigger><SelectValue Placeholder="All history (default)" /></SelectTrigger><SelectContent><SelectItem TValue="string" Value="all" Text="All history (default)">All history (default)</SelectItem>…</SelectContent></Select>`
  with `objPeriod = "all"`, and read the trigger without opening it.
- **Expected:** the trigger shows the selected item's `Text` — that is the whole point of `SelectItem.Text`
  existing beside `Value`, and it is what the shadcn/ui Select this mirrors does.
- **Actual:** the trigger renders the literal string `all`. `SelectItem` registers its `Text` with the
  parent only when it is rendered, and `SelectContent` renders nothing until the popover is opened, so at
  first paint the parent's value→text map is empty and `SelectValue` falls back to `Value.ToString()`.
  Opening the popover once fixes it for the rest of the circuit, which makes it look intermittent.
- **Encountered in:** REQ-UI-035 — the period filter on `/misses` (`data-testid="misses-period"`), which
  must show **All history** on the first view (BRD-125). It showed `all`.
- **Workaround:** pass `DisplayTextSelector` on `Select` — a `Func<TValue, string>` the component consults
  without waiting for the items to render. `Components/Pages/Misses.razor` maps the period key back to its
  option label there. The `Text` on each `SelectItem` is kept so the open popover is unaffected.
- **Suggested fix:** let `SelectItem`s register eagerly (render `SelectContent`'s items into a hidden
  registration pass, or have `Select` collect its children's `Value`/`Text` at parameter-set time) so the
  closed trigger can resolve a label before the popover has ever been opened.

---

## TR-004 — `Alert` and `Button` have no icon slot that can be combined with child content

- **Severity:** Low — documented, but it forces a positional convention rather than a named slot.
- **Repro:** `<Alert Variant="AlertVariant.Danger"><Icon>…</Icon><AlertTitle>…</AlertTitle></Alert>`.
- **Expected:** a named `Icon` fragment usable alongside `AlertTitle` / `AlertDescription`.
- **Actual:** RZ10012 — mixing an explicit fragment with implicit child content is a Razor compile error, so the
  icon has to be the first loose child and the component's CSS positions it by position.
- **Encountered in:** every alert and every icon button on REQ-UI-001..005.
- **Workaround:** place `<LucideIcon>` as the first loose child, as the package's AI reference instructs.
- **Suggested fix:** accept an `Icon` `RenderFragment` and render `ChildContent` beside it, so the two are independent.

---

## TR-012 — `DataTable` cannot hide its header row, so it cannot render a key/value table

- **Severity:** Low — cosmetic, but it affects every small two-column table.
- **Repro:** `<DataTable ShowToolbar="false" ShowPagination="false">` with two columns.
- **Expected:** a `ShowHeader` parameter beside `ShowToolbar` and `ShowPagination`, since the library ships no
  plain `Table` primitive and the docs direct key/value tables at `DataTable`.
- **Actual:** the `<thead>` always renders. In a label/value table the left cell *is* the label, so the header
  repeats it and adds a strip the design does not have.
- **Encountered in:** REQ-UI-023 (the ten-row key/value table inside each of the three harness columns).
- **Workaround:** declare the headers anyway (the component needs them for its column metadata) and hide the
  row from the page's scoped stylesheet with `.tflens-kv ::deep thead { display: none; }`.
- **Suggested fix:** add `ShowHeader` (default `true`), or ship the plain `Table` / `TableRow` / `TableCell`
  primitives the shadcn design language already defines.

---

## TR-013 — The `Typography*` components drop every unmatched attribute, so they cannot carry a `data-testid`

- **Severity:** Low — but it forces a wrapper element around every asserted line of prose.
- **Repro:** `<TypographyMuted data-testid="harness-null-footnote">…</TypographyMuted>`.
- **Expected:** the attribute lands on the rendered `<p>`, as it does on `Card`, `Alert`, `Badge` and `DataTable`.
- **Actual:** `TypographyMuted` (and its siblings) declare only `ChildContent` and `Class` — there is no
  `[Parameter(CaptureUnmatchedValues = true)]`, so the attribute is a compile-time error rather than markup.
- **Encountered in:** REQ-UI-026 (`data-testid="harness-null-footnote"` on the not-detected footnote line).
- **Workaround:** wrap the typography in a plain `<div>` that carries the test id.
- **Suggested fix:** add the unmatched-values parameter to the `Typography*` family, matching every other
  component in the package.

---

## TR-015 — `CardHeader` is a CSS grid, so a `Class="flex …"` on it cannot lay out a header row

- **Severity:** Low — the documented shadcn KPI/card pattern does not reproduce without an extra wrapper.
- **Repro:** `<CardHeader Class="flex flex-row items-center justify-between gap-2"><CardTitle>…</CardTitle><Badge>…</Badge></CardHeader>`.
- **Expected:** title and badge on one row, badge pushed right — the composition the package's own AI
  reference prescribes for a KPI card ("`CardHeader class="flex flex-row items-center justify-between pb-2"`").
- **Actual:** each child lands on its own grid row; `justify-between` does nothing. `CardHeader` renders with
  the shipped `data-slot="card-header"` grid rules, and the merged `flex` classes lose to them, so a card
  title, its two badges and its status badge stack into four lines.
- **Encountered in:** REQ-UI-014 (repo cards) and REQ-UI-017 (the Rebuild card).
- **Workaround:** give `CardHeader` a single child `<div class="flex w-full flex-wrap items-center justify-between gap-2">`
  and put the real header content inside that.
- **Suggested fix:** either honour a caller-supplied display class in the merge, or correct the AI reference so
  it stops prescribing a layout the component overrides.

---

## TR-016 — `Badge` has no success/warning variant, so a status badge cannot carry its own semantics

- **Severity:** Low — but every "healthy / needs attention / broken" badge collapses to two colours.
- **Repro:** `<Badge Variant="BadgeVariant.Warning">2 streams stale</Badge>`.
- **Expected:** the four alert semantics `Alert` already ships (`Success`, `Info`, `Warning`, `Danger`) are
  available on `Badge` too; the approved mockups use a green "synced" badge and an amber "2 streams stale" badge.
- **Actual:** `BadgeVariant` is `Default | Secondary | Destructive | Outline`. A warning has to borrow
  `Destructive`, which reads as an error, or `Secondary`, which reads as neutral.
- **Encountered in:** REQ-UI-014 (per-repo status badge: synced / stale / sync error).
- **Workaround:** `Secondary` for healthy and `Destructive` for both stale and failed, losing the distinction
  between "this needs attention" and "this is broken".
- **Suggested fix:** add `Success`, `Info` and `Warning` to `BadgeVariant`, reusing the `--alert-*` tokens
  `Alert` already defines.

---

## TR-017 — `Empty` lives in `TrBlazeUI.Components.Empty`, which the reference's `_Imports` list omits

- **Severity:** Low — silent, and the failure looks like working markup.
- **Repro:** follow §1 "_Imports.razor" of the package's AI reference verbatim, then use `<Empty Title="…">`.
- **Expected:** the component resolves.
- **Actual:** RZ10012 *warning* (not an error), and Razor emits a literal `<empty>` element: the page compiles,
  renders unstyled inline text, and nothing fails. The type is real — `TrBlazeUI.Components.Empty.Empty` — the
  reference's import list simply does not include its namespace, unlike `Alert`, `Badge`, `Card` and the rest.
- **Encountered in:** REQ-UI-014 (the no-repos-on-this-framework state).
- **Workaround:** `@using TrBlazeUI.Components.Empty` on the page. The same trap applies to any other
  namespace missing from that list.
- **Suggested fix:** complete the `_Imports.razor` block in the reference, and ship a `_Imports` snippet
  generated from the assembly so it cannot drift.

---

## TR-022 — merged into TR-008 (`LucideIcon` does not resolve `lucide.json` aliases)

> **⇢ MERGED 2026-08-28 — see [TR-008](#tr-008--trblazeuiiconslucide-200-does-not-resolve-the-pre-rename-lucide-names-so-every--circle--alert--name-renders-nothing).**
> Filed on 2026-08-28 as a fresh finding, it is the same defect as TR-008: same repro
> (`<LucideIcon Name="check-circle" Size="16" />`), same expected, same actual. Per this file's numbering
> rule the lower ID keeps the entry. **The number is retained as this stub, not deleted**, because
> `docs/TfLens-DevGuide-Screens.md` (lines 203 and 1686) and `PROJECT-STATUS.md` cite `TR-022` and those
> citations must keep resolving. TR-022's substantive contribution — that `lucide.json` has an `aliases`
> map the component never consults, which is the **root cause** TR-008 had only guessed at — is carried
> into TR-008 in full, under "Root cause". Nothing was dropped in the merge.
>
> **Not counted** in this file's severity totals; it is an alias, not an entry.

---

---

## TR-023 — `BreadcrumbLink` drops every unmatched attribute, so a breadcrumb cannot carry a `data-testid`

**Severity:** Medium · **Raised:** 2026-08-28 · **Status:** open

`TrBlazeUI.Components.Breadcrumb.BreadcrumbLink` declares no
`[Parameter(CaptureUnmatchedValues = true)]`, so any attribute it does not know about is not ignored
— it **throws**:

```
InvalidOperationException: Object of type 'TrBlazeUI.Components.Breadcrumb.BreadcrumbLink'
does not have a property matching the name 'data-testid'.
```

This is the third component in the same family — `TabsTrigger` (TR-010) and `Typography*` (TR-013)
behave identically — which is what makes it worth raising as a pattern rather than a third one-off.
The cost is not the workaround (a `<span data-testid>` inside the link, which is what TfLens does);
it is that the failure is a **runtime 500 on first render**, not a compile error. Two new pages in
this codebase shipped, built cleanly, and returned a Blazor error page the first time they were
opened — twice, for two different components in the same family.

**Ask:** add `CaptureUnmatchedValues` to every leaf component that renders a real DOM element, or
state in the reference which components accept passthrough attributes and which throw. A consumer
cannot tell by looking, and the build will not tell them either.

**Reproduce:** `<BreadcrumbLink Href="/x" data-testid="y">z</BreadcrumbLink>` → 500 on render.

---

## TR-024 — `TabsList` takes no unmatched attributes and offers no density variant, so the segmented track cannot be addressed

**Severity:** Medium · **Raised:** 2026-08-30 · **Status:** open

**Repro.** A segmented control built from `Tabs` / `TabsList` / `TabsTrigger`, where the *track* — the
node TrBlazeUI paints `inline-flex h-10 items-center justify-center rounded-md bg-muted p-1
text-muted-foreground` on — needs to be identified by a test hook and sized to a design spec:

```razor
<Tabs Value="@objValue" ValueChanged="OnChangedAsync">
    <TabsList data-testid="framework-switch">   @* does not compile *@
        <TabsTrigger Value="a">A</TabsTrigger>
    </TabsList>
</Tabs>
```

**Expected.** Either `TabsList` accepts passthrough attributes like `Tabs` does, or `Tabs` exposes the
track's own density (a `Size` / `Density` variant, or a `ListClass`) so a consumer can reach it without
one.

**Actual.** Two separate gaps that compound:

1. `TabsList` declares only `ChildContent` and `Class` — no `AdditionalAttributes`. The markup above
   fails with `Object of type 'TrBlazeUI.Components.Tabs.TabsList' does not have a property matching the
   name 'data-testid'`. This is the same family as TR-010 (`TabsTrigger`), TR-013 (`Typography*`) and
   TR-023 (`BreadcrumbLink`) — the fourth instance, and the second inside `Tabs` alone. Verified against
   `TrBlazeUI.Components.xml` 2.0.0, which lists `AdditionalAttributes` on `Tabs` and on nothing else in
   the family.
2. `Tabs` has no density variant. The track is fixed at `h-10` / `rounded-md` / `p-1` (40px, 6px, 4px).
   A compact header control — the shadcn/ui segmented control every dashboard mockup draws at ~34px —
   has no supported way to ask for that, and `Class` on `Tabs` lands on the *outer* element, not the
   track.

**Encountered in.** `src/TfLens/Components/Shared/FrameworkSwitch.razor` (REQ-UI-010), fixing a BRD-144
mockup-parity finding that fired on all six report pages at both viewport widths: the parity gate reads
chrome off the element carrying the `data-testid`, and that element could only be `Tabs` — which paints
nothing — while the real track sat one level below on `TabsList`, out of reach.

**Workaround.** Move the ground UP: `bg-muted` goes on the `Tabs` root (which does take the testid), and
`TabsList` is flattened back to a bare flex row (`background: transparent; height: auto; padding: 0;
border-radius: 0`) from `ShellHeader.razor.css` via `::deep [role="tablist"]`, so exactly one track is
painted. The 34px / 8px / 3px geometry is written as CSS rather than `Class` utilities because TR-002
still bites — the pre-built `trblazeui.css` carries no arbitrary-value utilities, so `p-[3px]`,
`h-[34px]` and `text-[13px]` are all absent at runtime and silently do nothing.

**Suggested fix.** Add `[Parameter(CaptureUnmatchedValues = true)]` to `TabsList` (and, per TR-023, to
every leaf that renders a real DOM element), and expose the track density on `Tabs` — a `Size` enum, or
at minimum a `ListClass` parameter that reaches the element the library actually styles.

---

## TR-026 — `DataTableColumn` has width and per-cell class parameters, and the AI reference documents none of them

**Severity:** Medium · **Raised:** 2026-08-30 · **Status:** open

**Repro:** A `DataTable` whose columns hold identifiers with hyphens in them — `fix-issues`,
`claude-opus-5` — inside any container narrower than the table's preferred width.

**Expected:** the reference tells a consumer that a column's width can be constrained, or that a
class can be put on the column's cells, so the id can be kept on one line.

**Actual:** `.trblazeui/TrBlazeUI-AI-Reference.md` §"DataTable (Generic)" has a parameter table for
`DataTable` and **no parameter table at all for `DataTableColumn`** — every example shows only
`TData`/`TValue`/`Property`/`Header`/`Sortable`/`Filterable`/`CellTemplate`. The component in fact
ships `Width`, `MinWidth`, `MaxWidth`, `CellClass`, `HeaderClass`, `Format`, `Visible` and `Id`,
which is only discoverable by opening `lib/net10.0/TrBlazeUI.Components.xml` inside the NuGet
package. `CellClass`/`HeaderClass` are exactly the right fix here and were found by decompiling
rather than by reading the docs.

The reason it matters more than a missing table usually would: the rendered table is `w-full`
inside the component's own `overflow-auto` box, so CSS auto table layout **compresses a column
below its own text** rather than letting the box scroll. A hyphen is a soft-wrap opportunity, so
`fix-issues` silently became `fix-` / `issues` in a 54px cell that needed 65px, and
`claude-opus-5` became two lines in an 89px cell that needed 99px. The value is still present and
still not clipped, so nothing in the DOM says it is broken — only a human or a line-count check
sees it. A consumer who cannot find `CellClass` in the reference has no obvious way out.

**Encountered in:** `src/TfLens/Components/Pages/Routing.razor`, the `drift-table` (REQ-UI-027),
at 1280 and 390.

**Workaround:** `CellClass="whitespace-nowrap" HeaderClass="whitespace-nowrap"` on the `cmd`,
`tier_model` and `observed model` columns. It works exactly as wanted — the class lands on the
`th` and on every `td` of the column, the column's min-content rises to the whole token, and the
overflow lands on the component's own scroll box instead of on the page.

**Suggested fix:** add a `DataTableColumn` parameter table to the AI reference covering `Width` /
`MinWidth` / `MaxWidth` / `CellClass` / `HeaderClass` / `Format` / `Visible` / `Id`, and say in the
`DataTable` section that the table is `w-full` inside a scroll box so columns compress before the
box scrolls — with the nowrap recipe named as the way to hold an identifier column together.

---

## TR-025 — `DataTable` has no density option, so 160px of a narrow table's width goes to cell padding and only `::deep` can get it back

**Severity:** Low · **Raised:** 2026-08-30 · **Status:** open

**Repro.** A five-column `DataTable` inside a `Card` that is itself half of a two-column grid — the
per-repo stream table on TfLens's Coverage page, one card per repository, `Stream` / `Records` /
`Backfilled` / `Newest` / `Days since`.

**Expected:** a way to ask the component for a tighter table — a `Density` / `Compact` parameter,
or a documented class hook on the cells — the way the same library's `Button` and `Badge` expose
size variants.

**Actual:** cells are hard-coded to `px-4` (16px a side). Measured on the live app at 1280: the
card gives the table a 420px box and the five columns wanted 432px, of which **160px — 38% — was
cell padding**. There is no parameter for it. `DataTable` takes `Class`, and `DataTableColumn`
takes `CellClass` / `HeaderClass` (TR-026), but a Tailwind `px-2.5` passed through `CellClass`
loses to the component's own `px-4`: both are single-class selectors, so the winner is stylesheet
order, and `trblazeui.css` emits `px-4` after `px-2.5`. Passing the smaller padding through the
documented parameter therefore does nothing at all, silently.

**Encountered in:** `src/TfLens/Components/Pages/Coverage.razor`, `repo-streams-*` (REQ-UI-014 /
BRD-144), at 1280 and 390.

**Workaround:** a scoped-CSS file, `Coverage.razor.css`, whose `.coverage-streams[b-xxxxx] th, td`
selector outranks `.px-4` on specificity rather than on order:

```css
.coverage-streams ::deep th,
.coverage-streams ::deep td { padding-left: 10px; padding-right: 10px; }
```

10px a side gave 60px back across the five columns, which was more than the widened date column
needed, and every card's table then fit its box at 1280 instead of overflowing it.

**Suggested fix:** add a `Density` parameter to `DataTable` (`Comfortable` default, `Compact` at
~8-10px) — a five-column table in a half-width card is an ordinary dashboard shape, not an edge
case. Failing that, say in the reference that `CellClass` cannot override padding and name the
`::deep` recipe, because the parameter appears to work and does not.

---

## TR-027 — `BreadcrumbList` hard-codes a two-axis `gap` and always wraps, so a two-item trail cannot be held on one line

**Severity:** Low · **Raised:** 2026-08-30 · **Status:** open

**Repro.** The app shell's header is one 64px row at desktop, ending in a fixed-width control cluster;
the breadcrumb is the elastic item in the middle:

```razor
<Breadcrumb>
    <BreadcrumbList>
        <BreadcrumbItem><span>Reports</span></BreadcrumbItem>
        <BreadcrumbSeparator />
        <BreadcrumbItem><BreadcrumbPage>Harness comparison</BreadcrumbPage></BreadcrumbItem>
    </BreadcrumbList>
</Breadcrumb>
```

`BreadcrumbList` renders `<ol class="flex flex-wrap items-center gap-2.5 …">`. Two things are fixed
there and neither is a parameter:

* **`flex-wrap` is always on.** A two-item trail under width pressure therefore breaks *between the
  separator and the page name* — `Reports ›` on one line, `Harness comparison` on the next — which
  reads as two rows of chrome rather than one trail. There is no `Wrap` switch, and no
  `CaptureUnmatchedValues` on `BreadcrumbList` to pass `class="flex-nowrap"` either.
* **`gap-2.5` applies to both axes.** 10px is generous for a `›` separator (the approved mockup uses
  6px) and, once the list *has* wrapped, it also inserts 10px of pure vertical padding between the
  two lines, which is what pushed this header from 64px to 87px before it was tracked down.

The second point is the more general one: every `gap-N` in the library is a two-axis gap, so any
wrapping row pays for its column spacing again on every extra row.

**Encountered in:** `src/TfLens/Components/Shared/ShellHeader.razor` (REQ-UI-010, REQ-UI-023), at 1280.

**Workaround:** scoped CSS reaching in with `::deep`, because the `ol` is the child component's own
root and cannot carry this file's scope attribute:

```css
@media (min-width: 64rem) {
    .tflens-header-crumb ::deep ol { flex-wrap: nowrap; column-gap: 4px; white-space: nowrap; }
}
```

**Suggested fix:** give `BreadcrumbList` a `Wrap` parameter (default `true`, so nothing changes) and
split its gap into `gap-x-2.5 gap-y-0` — a breadcrumb's rows never want the column spacing between
them. Adding `CaptureUnmatchedValues` would also do, and would settle TR-023's family of cases at the
same time.

## TR-028 — `BarChart` exposes no axis, grid or data-label control and no route to `ApexChartOptions`, so a chart cannot be made to match an approved design

**Severity:** High · **Raised:** 2026-09-01 · **Status:** open

**Repro.** `docs/mockups/harness.html` draws *Total tokens by harness* the way a comparison chart is
normally drawn when the values span orders of magnitude: **no y axis, no gridlines, and the value
printed above each bar** (`68.6M`, `19.0M`, `0.61M`). The mockup's own card description names the
components to build it with — `ChartContainer → BarChart`. So:

```razor
<ChartContainer>
    <BarChart TItem="HarnessTokenTotal" Items="@objChartRows" Height="260px" Width="100%"
              ShowLegend="false" ShowTooltip="true">
        <ApexPointSeries TItem="HarnessTokenTotal" Items="@objChartRows" SeriesType="SeriesType.Bar"
                         XValue="@(r => r.Harness)" YValue="@(r => (decimal)r.Tokens)"
                         PointColor="@(r => ChartColourFor(r.Harness))" />
    </BarChart>
</ChartContainer>
```

`BarChart`'s entire public surface for this is `ShowLegend`, `ShowTooltip`, `ShowDataLabels`,
`Height`, `Width`, `BarWidth`. There is **no** `Options`, no `Xaxis`/`Yaxis`, no `Grid`, and no
formatter — so ApexCharts' defaults are the only chart you can have. On this screen's real data
(claude-code 3.2B, opencode 814k, codex 18.5M) the defaults produced:

* a linear y axis printing **raw counts** — `3000000000`, unscaled and unseparated;
* horizontal gridlines and a rounded plot border the design has none of;
* **no value labels at all**, so with claude-code at 3.2B the other two bars were a 0–1px line with
  no figure anywhere near them.

Two of the three numbers the card exists to compare were unreadable. This is not a cosmetic gap: on
a product whose one claim is that its figures can be quoted, a chart that can only render an
unscaled axis is a chart that cannot be shipped.

**Encountered in:** `src/TfLens/Components/Pages/Harness.razor` (REQ-UI-023), owner UAT 2026-08-30.

**Workaround — drop the wrapper.** `ApexChart<T>` (the component `BarChart` wraps) *does* take
`Options`, so the fix was to stop using `BarChart`:

```razor
<ChartContainer>
    <ApexChart TItem="HarnessTokenTotal" Options="@objChartOptions" Height="260" Width="@("100%")">
        <ApexPointSeries … ShowDataLabels="true" />
    </ApexChart>
</ChartContainer>
```

with `Grid { Show = false }`, `Yaxis = [new YAxis { Show = false }]`, and a `DataLabels.Formatter`
carrying the same compact notation the table cells use. That is the whole value of the wrapper
given up to change three options.

**A second, separable defect found in the same hour:** `ChartContainer` renders its own
`rounded-lg border bg-card shadow-sm p-6` surface. Placed inside a `Card` — which is where a chart
almost always goes, and where both the mockup and the library's own reference put it — it draws a
**card inside a card**: a second bordered, shadowed panel around the plot. There is no `Bare` or
`Variant` switch, so the only way to get the design's flush chart is scoped CSS unpicking the
container's own decoration:

```css
.tflens-chart ::deep > div { border: 0; background: transparent; box-shadow: none; padding: 0; }
```

**Suggested fix:** (1) give `BarChart` (and its siblings) an `Options` parameter merged over the
wrapper's defaults — one parameter settles every case of this shape, rather than growing a
`ShowYAxis` / `ShowGrid` / `LabelFormatter` surface one report at a time. (2) Give `ChartContainer` a
`Bare` parameter, or drop the card chrome entirely and let the caller supply it — a *container* that
paints a card is doing two jobs, and the second one is almost always already done.

---

## TR-029 — `Badge` is a fixed-height `rounded-full` pill with no wrapping treatment, so a multi-word label collapses into an unreadable shape on a phone

**Severity:** Medium · **Raised:** 2026-09-01 · **Status:** open

**Repro.** The Playbook stream-health card on `/misses` states each stream's state in a badge, exactly
as `docs/mockups/misses-playbook.html` draws it — including the transient stream, whose state the
mockup writes as a sentence:

```razor
<Badge Variant="BadgeVariant.Outline">transient · best-effort, absence is not an error</Badge>
```

**Expected:** at 390px the label wraps to two or three lines inside a pill that grows with it — which
is what the approved mockups do, in one rule they had to write by hand:

```css
@media (max-width: 700px) {
  .badge { white-space: normal; height: auto; min-height: 22px; padding: 2px 8px; border-radius: 12px; text-align: left; }
}
```

**Actual:** the shipped badge is `inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs
font-semibold …` — a **fixed-height** pill with a 999px radius and no `white-space` handling. The text
wraps anyway, so at 390 the label spills past a pill that has not grown with it: measured in the
browser, the four-line label overlapped its own rounded ends and the row beside it, and read as two
overlapping shapes rather than one badge. There is no `Truncate`, `Wrap` or size parameter to say
which of the two behaviours the caller wants.

**Encountered in:** `src/TfLens/Components/Shared/Playbook/PlaybookMissesSurface.razor` (REQ-UI-051),
1280 and 390 smoke, 2026-09-01.

**Workaround — shorten the label and move the sentence to prose.** The badge now says `transient` and
the card's footer carries "…is transient and best-effort — it is the exporter's input, rotates, and
TfLens never stores it, so its absence is not an error." That is the better copy anyway, so the cost
here was small; it will not be small on a screen whose badge text is data rather than an author's
sentence, and the alternative — a `::deep` rule per component reaching a child's root element, since
scoped CSS cannot reach it directly — is the shape TR-025 and TR-016 already document.

**Suggested fix:** drop the fixed height in favour of a `min-height` and let the pill grow (the
mockups' own rule), or add a `Wrap` / `Truncate` parameter so the caller states which behaviour a long
label should get. Either settles every case; neither needs a new variant.

---

## TR-030 — `CollapsibleTrigger` renders an `inline-block` button with a bare `class="group"`, so a disclosure header cannot span its own row

**Severity:** Low · **Raised:** 2026-09-01 · **Status:** open

**Repro.** The per-phase disclosures on `/effort` are drawn by `docs/mockups/effort.html` as one full
width row: a chevron, the command, a muted summary, a spacer, and an observation badge flush right.

```razor
<Collapsible Open="@vIsOpen" OpenChanged="…">
    <CollapsibleTrigger>
        <span class="tflens-detail-trigger">
            <LucideIcon Name="chevron-down" Size="16" />
            <span class="font-mono">build-phase</span>
            <span class="text-muted-foreground">— 24 runs · 21h 40m · 24.2M output</span>
            <span class="tflens-spacer"></span>
            <Badge Variant="BadgeVariant.Outline">6 of 24 observed</Badge>
        </span>
    </CollapsibleTrigger>
    …
</Collapsible>
```

**Expected:** the trigger fills the row it is placed in — or exposes a parameter, a class hook or a
documented `AsChild` so the caller can say so — the way `SidebarMenuButton` already does for exactly
this layout.

**Actual:** the rendered element is `<button class="group">`, `display: inline-block`, with no width
and no other class. It shrinks to fit its content, so the spacer has nothing to push against and the
badge sits immediately after the summary text rather than at the right edge. `CollapsibleTrigger`
takes no `Class` parameter either, so the only way to reach the button is a `::deep` rule from an
ancestor the consuming page owns — a page's own scoped CSS cannot match a child component's root,
which is the same wall TR-016, TR-025 and TR-029 each end at.

**Workaround:**

```css
.tflens-effort ::deep .tflens-detail button { width: 100%; }
```

**Suggested fix:** add `Class` to `CollapsibleTrigger` (every other TrBlazeUI component that renders a
box has one), or default the trigger to `w-full text-left` since a disclosure header is a row in every
design that has one. The first is the smaller change and settles the general case.

**Encountered in:** `src/TfLens/Components/Pages/Effort.razor` (REQ-UI-048), 1280 and 390 smoke,
2026-09-01.
