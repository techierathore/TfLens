# TfLens — TrBlazeUI feedback

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

---

## Summary

- **5 blockers, 8 majors, 6 minors, 0 nice-to-haves** — 19 entries, **all 19 open**. None is fixed
  upstream; every one shipped with a workaround instead.
- Last consolidated: 2026-08-28

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
| `src/TfLens/Components/Pages/ThreeQuestions.razor:92` | `TR-006` | **TR-010** (`TabsTrigger` captures no unmatched attributes) |
| `src/TfLens/Components/Pages/ThreeQuestions.razor:22` | `TR-007` | **TR-013** (the `Typography*` family) |

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
  no pager to reveal the rest, so rows six onwards are simply absent from the DOM. On `/three-questions` this
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
  This is already visible in the shipped shell: the sidebar's **Three questions** item (`help-circle`) has no
  icon at all, and `Repos.razor`'s rate-limit and private-repo alerts (`alert-triangle`) have none either.
- **Root cause (from the merged TR-022, 2026-08-28):** the names are **not missing from the package** —
  this entry's original reading ("2.0.0 carries only the post-rename names") was one layer short.
  `lucide.json` ships two maps, `icons` and `aliases`, and the aliased spellings are all present in
  `aliases`. The component looks the name up in **`icons` only**, so every aliased spelling
  (`check-circle` → `circle-check`, and the rest of Lucide's renamed set) resolves to nothing. There is
  no warning and no fallback glyph, which is why a blank icon is indistinguishable from a deliberate one.
- **Encountered in:** REQ-UI-018 (the three KPI tiles' icons and the empty state's icon on
  `/three-questions`); and, as TR-022, REQ-UI-036 — the measured-USD tile's accent chip on `/misses`
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
