# TfLens — TrBlazeUI feedback

Gaps found while building TfLens against **TrBlazeUI.Components 2.0.0** / **TrBlazeUI.Icons.Lucide 2.0.0**.
Each entry is a real blocker or defect met during the build, with the workaround that shipped so the
build never stopped for a library issue.

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

## TR-008 — `TrBlazeUI.Icons.Lucide` 2.0.0 carries only the new Lucide names, so every `*-circle` / `alert-*` name renders nothing

- **Severity:** Medium — the icon silently disappears; there is no build error and no runtime error, only a
  `data-trblazeui-missing-icon` placeholder that occupies the right box and draws nothing.
- **Repro:** `<LucideIcon Name="check-circle" Size="16" />`, `<LucideIcon Name="alert-triangle" Size="16" />`,
  `<LucideIcon Name="help-circle" Size="16" />`, `<LucideIcon Name="x-circle" Size="16" />`.
- **Expected:** the icons render. These are the names the package's own `TrBlazeUI-AI-Reference.md` uses
  (`<Alert Variant="AlertVariant.Danger"><LucideIcon Name="alert-circle" …>`), and the names
  `docs/TfLens-UIDesign.md` and every mockup were written against.
- **Actual:** the 2.0.0 icon set carries **only** the post-rename Lucide names. Probed on the running app,
  `circle-check`, `circle-alert`, `triangle-alert`, `circle-x`, `badge-check`, `x`, `clock`, `percent`,
  `target`, `shield-check`, `ban`, `bug`, `list-checks`, `gauge`, `trending-up`, `trending-down`, `flag`,
  `zap`, `chart-bar` and `bar-chart-3` all render; `check-circle`, `check-circle-2`, `alert-circle`,
  `alert-triangle`, `x-circle`, `help-circle` and `circle-help` all render nothing. There is no
  `circle-help` under either name, so a "question" glyph is simply unavailable.
  This is already visible in the shipped shell: the sidebar's **Three questions** item (`help-circle`) has no
  icon at all, and `Repos.razor`'s rate-limit and private-repo alerts (`alert-triangle`) have none either.
- **Encountered in:** REQ-UI-018 (the three KPI tiles' icons and the empty state's icon on `/three-questions`).
- **Workaround:** use the new names — `circle-check`, `triangle-alert`, `x` — and substitute `list-checks`
  where no question glyph exists.
- **Suggested fix:** ship the pre-rename aliases (Lucide itself keeps them as aliases), or at minimum log a
  warning naming the closest match, and update `TrBlazeUI-AI-Reference.md` to the names the package actually
  carries.

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

## TR-010 — `CardHeader` is a CSS grid, so a `Class="flex …"` on it cannot lay out a header row

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

## TR-011 — `Badge` has no success/warning variant, so a status badge cannot carry its own semantics

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

## TR-012 — `Empty` lives in `TrBlazeUI.Components.Empty`, which the reference's `_Imports` list omits

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
