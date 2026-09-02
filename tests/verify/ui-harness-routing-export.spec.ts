/*
 * REQ-UI-023 … REQ-UI-034 — /harness, /routing and /export.
 *
 * Black-box verification only: this file never touches application source, never writes to the
 * user's data folder and never presses a control that mutates disk state.
 *
 * Two buttons on these screens are deliberately NEVER pressed:
 *   • `edit-prices-save`  — it rewrites data/prices.json (REQ-UI-030 below).
 *   • `export-now`        — it writes data/reports/<user>/<date>/<framework>/{snapshot.md,tflens.json}
 *                           (REQ-UI-032 below).
 * Their presence, their enabled/disabled state and their validation behaviour are asserted instead.
 *
 * Blazor Server renders after a circuit round-trip, so every wait is explicit and generous.
 */
import { test, Page, Locator } from '@playwright/test';
import {
  signIn,
  gotoScreen,
  testid,
  renderCheck,
  tableCheck,
  collectErrors,
  USER1,
  DESKTOP,
  expect,
} from './_helpers';

const HARNESSES = ['claude-code', 'opencode', 'codex'] as const;
type Harness = (typeof HARNESSES)[number];

/** The two shapes a Figure is allowed to reach the screen in. */
const ONE_DP = /^[\d,]+\.\d$/;
const INSUFFICIENT = /insufficient data \(n=\d+\)/;
/** Figure.NotApplicable() renders as an em dash (ADR-007) — accepted, but always logged. */
const EM_DASH = '—';

let objErrors: string[] = [];

test.beforeEach(async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  objErrors = collectErrors(page);
  await signIn(page, USER1);
});

test.afterEach(async () => {
  if (objErrors.length > 0) {
    console.log(`CONSOLE-ERRORS (${objErrors.length}): ${objErrors.slice(0, 8).join(' | ')}`);
  }
});

/** Trimmed innerText of a testid, or null when the element is absent. */
async function textOf(page: Page, id: string): Promise<string | null> {
  const loc = page.locator(`[data-testid="${id}"]`).first();
  if ((await page.locator(`[data-testid="${id}"]`).count()) === 0) return null;
  const t = await loc.innerText().catch(() => '');
  return (t || '').replace(/\s+/g, ' ').trim();
}

async function exists(page: Page, id: string): Promise<boolean> {
  return (await page.locator(`[data-testid="${id}"]`).count()) > 0;
}

/**
 * Activates one of the /routing tabs.
 * TR-010: the `routing-tab-*` test id sits on the trigger's LABEL SPAN, not on the button, so the
 * click is aimed at the nearest [role="tab"] ancestor when there is one.
 */
async function activateRoutingTab(
  page: Page,
  name: 'drift' | 'models' | 'repricing' | 'poolable',
): Promise<void> {
  const label = await testid(page, `routing-tab-${name}`);
  const trigger: Locator = label.locator('xpath=ancestor-or-self::*[@role="tab"][1]');
  if ((await trigger.count()) > 0) {
    await trigger.first().click();
  } else {
    await label.click({ force: true });
  }
  await page
    .locator(`[data-testid="routing-panel-${name}"]`)
    .first()
    .waitFor({ state: 'visible', timeout: 20_000 });
  // Let the circuit finish swapping the panel's content in.
  await page.waitForTimeout(800);
}

/** Every data-testid currently in the DOM, minus the shell chrome. */
async function contentTestIds(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const chrome = /^(app-|nav-|sidebar-|framework-|user-|theme-|breadcrumb-|sync-|login-|toast)/;
    return Array.from(document.querySelectorAll('[data-testid]'))
      .map(e => e.getAttribute('data-testid') || '')
      .filter(id => id.length > 0 && !chrome.test(id));
  });
}

/**
 * Switches the header Framework switch and waits until the choice has taken.
 * The switch persists a cookie and raises Changed; a later page.goto recovers it on first render.
 */
async function switchFramework(page: Page, label: 'TechieFlow' | 'Playbook'): Promise<void> {
  const sw = await testid(page, 'framework-switch');
  const trigger = sw.locator('[role="tab"]').filter({ hasText: label }).first();
  if ((await trigger.count()) === 0) {
    // Fall back to the label span itself.
    await sw.getByText(label, { exact: false }).first().click();
  } else {
    await trigger.click();
  }
  await page.waitForTimeout(2_000);
  const selected = await page.evaluate(() => {
    const root = document.querySelector('[data-testid="framework-switch"]');
    if (!root) return '';
    const active = Array.from(root.querySelectorAll('[role="tab"]')).find(
      t =>
        t.getAttribute('aria-selected') === 'true' ||
        t.getAttribute('data-state') === 'active',
    );
    return (active?.textContent || '').trim();
  });
  console.log(`FRAMEWORK-SWITCH -> requested "${label}", active trigger reads "${selected}"`);
}

/* ────────────────────────────── /harness ────────────────────────────── */

test('REQ-UI-023 harness screen shows three fixed columns with the compare-tokens-not-dollars note', async ({
  page,
}) => {
  test.setTimeout(120_000);
  await gotoScreen(page, '/harness');

  // The standing note.
  const note = await renderCheck(page, 'harness-note');
  expect(note.verdict, `harness-note: ${note.detail}`).toBe('RENDERS');
  const noteText = ((await textOf(page, 'harness-note')) || '').toLowerCase();
  expect(noteText, 'harness-note must say tokens may be compared').toMatch(/tokens?.*compar|compar.*tokens?/);
  expect(noteText, 'harness-note must say dollars may not').toContain('dollars');
  expect(noteText, 'harness-note must say dollars may NOT be compared').toMatch(
    /dollars (may not|are not|cannot)|dollars.*not|not.*dollars/,
  );

  // All three columns exist even when a harness has zero records.
  for (const h of HARNESSES) {
    expect(await exists(page, `harness-col-${h}`), `harness-col-${h} must exist even at zero records`).toBe(true);
  }

  // Fixed order, compared by DOM index.
  const order = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid^="harness-col-"]')).map(e =>
      e.getAttribute('data-testid'),
    ),
  );
  console.log(`HARNESS COLUMN ORDER: ${JSON.stringify(order)}`);
  expect(order).toEqual(['harness-col-claude-code', 'harness-col-opencode', 'harness-col-codex']);

  // A zero-record column shows the dash rather than disappearing.
  for (const h of HARNESSES) {
    const runs = await textOf(page, `harness-${h}-runs`);
    expect(runs, `harness-${h}-runs must render`).not.toBeNull();
    expect((runs || '').length, `harness-${h}-runs must not be blank`).toBeGreaterThan(0);
    if (runs === EM_DASH) console.log(`BRANCH: ${h} has zero records — column renders "—"`);
  }

  // Each column's table renders rows with no blank cells.
  for (const h of HARNESSES) {
    const t = await tableCheck(page, `harness-table-${h}`);
    console.log(`harness-table-${h}: ${t.verdict} — ${t.detail}`);
    expect(t.verdict, `harness-table-${h}: ${t.detail}`).toBe('RENDERS');
  }

  // Every charted total is also printed as text. The CLAUSE is unchanged; where it is satisfied moved
  // on 2026-09-01 (REQ-UI-023). Until then a `Harness | Total tokens` DataTable repeated the three
  // figures under the chart — this project's own workaround for a chart that carried no labels. The
  // approved design has no such table: it prints the value ABOVE its bar, which is also its answer to
  // a bar too short to see. The chart now does that, so the table was removed as owner-reported drift,
  // and the figures are asserted where they now live: the SVG labels, plus an off-screen list that
  // keeps them reachable without sight of the graphic.
  expect(await exists(page, 'tokens-table'), 'tokens-table was removed on 2026-09-01 — the design has no such table')
    .toBe(false);

  const chartValues = ((await textOf(page, 'tokens-chart-values')) || '').replace(/\s+/g, ' ');
  expect(chartValues.length, 'tokens-chart-values must carry the totals as text').toBeGreaterThan(0);

  for (const h of HARNESSES) {
    const total = await textOf(page, `tokens-total-${h}`);
    if (total === null) {
      console.log(`BRANCH: tokens-total-${h} absent`);
      continue;
    }
    expect(total.length, `tokens-total-${h} must not be blank`).toBeGreaterThan(0);
    expect(chartValues, `tokens-total-${h} ("${total}") must appear as text beside the chart`).toContain(total);
  }

  // The bars themselves must be labelled — that is what replaced the table, so its absence is a
  // regression to the exact state the owner reported: figures no reader could get at.
  const barLabels = await page.$$eval(
    '[data-testid="tokens-chart"] .apexcharts-datalabels text',
    els => els.map(e => (e.textContent || '').trim()).filter(t => t.length > 0),
  );
  console.log(`tokens-chart bar labels: ${JSON.stringify(barLabels)}`);
  expect(barLabels.length, 'every bar must print its own value (REQ-UI-023, owner UAT 2026-08-30)')
    .toBe(HARNESSES.length);

  // No raw y axis: the design draws none, and ApexCharts' default printed `3000000000` unscaled.
  const yAxisLabels = await page.$$eval(
    '[data-testid="tokens-chart"] .apexcharts-yaxis text',
    els => els.map(e => (e.textContent || '').trim()).filter(t => t.length > 0),
  );
  expect(yAxisLabels, 'the tokens chart must draw no y-axis labels').toEqual([]);

  console.log(`tokens-chart present: ${await exists(page, 'tokens-chart')}`);
});

test('REQ-UI-024 tokens per verified REQ is computed per harness, never pooled', async ({ page }) => {
  test.setTimeout(120_000);
  await gotoScreen(page, '/harness');

  const values: Record<string, string> = {};

  for (const h of HARNESSES) {
    const id = `harness-${h}-tokens-per-verified`;
    expect(await exists(page, id), `${id} must exist`).toBe(true);
    const raw = ((await textOf(page, id)) || '').replace(/\s+/g, ' ').trim();
    values[h] = raw;
    console.log(`${id} = "${raw}"`);

    expect(raw.length, `${id} must not be blank`).toBeGreaterThan(0);
    expect(raw, `${id} must never be a bare zero`).not.toBe('0');

    const isOneDp = ONE_DP.test(raw.replace(/\s/g, ''));
    const isInsufficient = INSUFFICIENT.test(raw);
    const isNotApplicable = raw === EM_DASH;
    if (isNotApplicable) {
      // Figure.NotApplicable — >= MinN verified verdicts but no tokens captured. Accepted, logged.
      console.log(`BRANCH: ${id} is Figure.NotApplicable ("—")`);
    }
    expect(
      isOneDp || isInsufficient || isNotApplicable,
      `${id} = "${raw}" must be a 1-dp number, "insufficient data (n=…)", or the NotApplicable dash`,
    ).toBe(true);
  }

  // Soft/logged: a pooled figure would make all three identical.
  const distinct = new Set(Object.values(values));
  const allInsufficient = Object.values(values).every(v => INSUFFICIENT.test(v));
  if (distinct.size === 1 && !allInsufficient) {
    console.log(
      `SOFT-WARN REQ-UI-024: all three harnesses report the identical value "${Object.values(values)[0]}" ` +
        `— check this is not a pooled figure.`,
    );
  } else {
    console.log(`REQ-UI-024 per-harness values: ${JSON.stringify(values)}`);
  }
});

test('REQ-UI-025 only OpenCode shows dollars and there is no cross-harness dollar total', async ({
  page,
}) => {
  test.setTimeout(120_000);
  await gotoScreen(page, '/harness');

  expect(await exists(page, 'opencode-cost'), 'opencode-cost card must exist').toBe(true);
  const cardText = (await textOf(page, 'opencode-cost')) || '';
  expect(cardText, 'opencode-cost title must say "Measured dollars"').toContain('Measured dollars');
  expect(cardText, 'opencode-cost title must name OpenCode').toMatch(/OpenCode/i);

  const noteText = (await textOf(page, 'opencode-cost-note')) || '';
  expect(noteText, 'opencode-cost-note must state the null-by-design rule').toContain(
    'not measured (null by design)',
  );

  const valueText = (await textOf(page, 'opencode-cost-value')) || '';
  const hasFigure = /\$\s?[\d,]+(\.\d+)?/.test(valueText);
  const saysNoRecords = /no OpenCode records yet/i.test(valueText);
  console.log(`BRANCH REQ-UI-025: opencode-cost-value = "${valueText}"`);
  expect(
    hasFigure || saysNoRecords,
    `opencode-cost-value must be a $ figure or "no OpenCode records yet", got "${valueText}"`,
  ).toBe(true);
  if (hasFigure) {
    console.log(`opencode-cost-basis = "${(await textOf(page, 'opencode-cost-basis')) || '(absent)'}"`);
  }

  // Walk every text node in the rendered page.
  const scan = await page.evaluate(() => {
    const card = document.querySelector('[data-testid="opencode-cost"]');
    const money = /\$\s?[\d,]+(\.\d+)?/g;
    const wording = /total (cost|spend|dollars)|combined (cost|spend)|across harness/gi;
    const hardWording = /total (cost|spend|dollars)|combined (cost|spend)/gi;

    const dollars: { match: string; inCard: boolean; context: string; owner: string }[] = [];
    const words: { match: string; context: string }[] = [];
    const hard: { match: string; context: string }[] = [];

    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    let node: Node | null = walker.nextNode();
    while (node) {
      const parent = node.parentElement;
      const tag = (parent?.tagName || '').toLowerCase();
      if (parent && tag !== 'script' && tag !== 'style') {
        const text = node.nodeValue || '';
        const inCard = !!card && card.contains(parent);
        let ownerId = '';
        let walk: Element | null = parent;
        while (walk && !ownerId) {
          ownerId = walk.getAttribute('data-testid') || '';
          walk = walk.parentElement;
        }
        for (const m of text.match(money) || []) {
          dollars.push({ match: m, inCard, context: text.trim().slice(0, 120), owner: ownerId });
        }
        for (const m of text.match(wording) || []) {
          words.push({ match: m, context: text.trim().slice(0, 120) });
        }
        for (const m of text.match(hardWording) || []) {
          hard.push({ match: m, context: text.trim().slice(0, 120) });
        }
      }
      node = walker.nextNode();
    }
    return { dollars, words, hard, cardFound: !!card };
  });

  expect(scan.cardFound, 'opencode-cost subtree must be findable for the money scan').toBe(true);
  console.log(`MONEY SCAN: ${scan.dollars.length} $ matches, ${scan.words.length} money-wording matches`);
  for (const d of scan.dollars) {
    console.log(`  $ "${d.match}" inOpenCodeCard=${d.inCard} owner=${d.owner || '(none)'} ctx="${d.context}"`);
  }
  for (const w of scan.words) console.log(`  wording "${w.match}" ctx="${w.context}"`);

  const strays = scan.dollars.filter(d => !d.inCard);
  expect(
    strays,
    `every $ figure on /harness must live inside the opencode-cost card; strays: ${JSON.stringify(strays)}`,
  ).toEqual([]);

  // "across harness" legitimately appears in the required harness-note ("tokens may be compared across
  // harness"), so only the true cross-harness-total phrasings are a hard failure.
  expect(
    scan.hard,
    `no cross-harness money total may appear on /harness; found: ${JSON.stringify(scan.hard)}`,
  ).toEqual([]);

  // The two unmeasured harnesses say so rather than showing a zero.
  for (const h of ['claude-code', 'codex'] as Harness[]) {
    const cost = ((await textOf(page, `harness-${h}-cost`)) || '').toLowerCase();
    console.log(`harness-${h}-cost = "${cost}"`);
    expect(cost, `harness-${h}-cost must read "not measured"`).toContain('not measured');
    expect(cost, `harness-${h}-cost must not show a dollar zero`).not.toContain('$0');
    expect(cost.trim(), `harness-${h}-cost must not be a bare zero`).not.toBe('0');
  }
});

test('REQ-UI-026 undetected-harness records are a footnote, never a fourth column', async ({ page }) => {
  test.setTimeout(120_000);
  await gotoScreen(page, '/harness');

  if (!(await exists(page, 'harness-null-footnote'))) {
    // Valid only at n = 0 — there is no other honest reason to omit it.
    console.log('BRANCH: no undetected records — harness-null-footnote is absent');
    // Nothing may claim otherwise: assert no fourth column crept in either.
    const cols = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid^="harness-col-"]')).map(e =>
        e.getAttribute('data-testid'),
      ),
    );
    expect(cols.length, 'there must never be more than three harness columns').toBe(3);
    return;
  }

  const foot = ((await textOf(page, 'harness-null-footnote')) || '').replace(/\s+/g, ' ');
  console.log(`harness-null-footnote = "${foot}"`);
  expect(foot, 'footnote must count the undetected records').toMatch(/\d[\d,]* records with harness not detected/);
  expect(foot, 'footnote must say the records are excluded').toContain('excluded');

  const m = foot.match(/([\d,]+) records with harness not detected/);
  const count = (m ? m[1] : '').replace(/,/g, '');

  // Soft: the same count turning up as a column's Runs value would suggest the undetected records were
  // merged into a named harness.
  for (const h of HARNESSES) {
    const runs = ((await textOf(page, `harness-${h}-runs`)) || '').replace(/,/g, '').trim();
    if (runs === EM_DASH || runs.length === 0) continue;
    expect
      .soft(
        runs,
        `harness-${h}-runs (${runs}) equals the undetected-record count (${count}) — check the ` +
          `harness:null records were not merged into a named column`,
      )
      .not.toBe(count);
  }

  // And still exactly three columns.
  const cols = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid^="harness-col-"]')).map(e =>
      e.getAttribute('data-testid'),
    ),
  );
  expect(cols.length, 'the footnote must not become a fourth column').toBe(3);
});

/* ────────────────────────────── /routing ────────────────────────────── */

test('REQ-UI-027 routing drift tab renders its KPIs and an unrouted-first drift table', async ({ page }) => {
  test.setTimeout(150_000);
  await gotoScreen(page, '/routing');
  expect(await exists(page, 'routing-error'), 'routing must not render its error alert').toBe(false);

  for (const tab of ['drift', 'models', 'repricing', 'poolable']) {
    expect(await exists(page, `routing-tab-${tab}`), `routing-tab-${tab} must exist`).toBe(true);
  }

  await activateRoutingTab(page, 'drift');

  for (const kpi of ['kpi-routing-fields', 'kpi-unrouted', 'kpi-distinct-models']) {
    const r = await renderCheck(page, kpi);
    console.log(`${kpi}: ${r.verdict} — ${r.detail}`);
    expect(r.verdict, `${kpi}: ${r.detail}`).toBe('RENDERS');
    expect(((await textOf(page, kpi)) || '').length, `${kpi} must render non-empty text`).toBeGreaterThan(0);
  }

  const hasTable = await exists(page, 'drift-table');
  if (!hasTable) {
    const empty = ((await textOf(page, 'drift-empty')) || '').toLowerCase();
    console.log(`BRANCH REQ-UI-027: drift-empty — "${empty}"`);
    expect(empty, 'drift-empty must read "no routing fields captured yet"').toContain(
      'no routing fields captured yet',
    );
    return;
  }

  console.log(`BRANCH REQ-UI-027: drift-table renders. drift-row-count = "${await textOf(page, 'drift-row-count')}"`);
  const t = await tableCheck(page, 'drift-table');
  console.log(`drift-table: ${t.verdict} — ${t.detail}`);
  expect(t.verdict, `drift-table: ${t.detail}`).toBe('RENDERS');

  const headers = (
    await page.evaluate(() => {
      const root = document.querySelector('[data-testid="drift-table"]');
      if (!root) return '';
      return Array.from(root.querySelectorAll('thead th'))
        .map(th => (th.textContent || '').trim())
        .join(' | ')
        .toLowerCase();
    })
  ).replace(/\s+/g, ' ');
  console.log(`drift-table headers: ${headers}`);
  for (const col of ['cmd', 'tier', 'tier_model', 'model', 'models', 'routed', 'ts']) {
    expect(headers, `drift-table must have a ${col} column`).toContain(col);
  }

  // routed:false rows carry a destructive "drift" badge and must all precede the routed rows.
  const ordering = await page.evaluate(() => {
    const root = document.querySelector('[data-testid="drift-table"]');
    if (!root) return { drift: [] as number[], other: [] as number[] };
    const rows = Array.from(root.querySelectorAll('tbody tr'));
    const drift: number[] = [];
    const other: number[] = [];
    rows.forEach((r, i) => {
      const text = (r.textContent || '').toLowerCase();
      if (text.includes('routed:false') || /\bdrift\b/.test(text)) drift.push(i);
      else other.push(i);
    });
    return { drift, other };
  });
  console.log(`drift rows: ${ordering.drift.length} unrouted, ${ordering.other.length} routed/unknown`);
  if (ordering.drift.length > 0 && ordering.other.length > 0) {
    expect(
      Math.max(...ordering.drift),
      'every routed:false row must appear before every other row',
    ).toBeLessThan(Math.min(...ordering.other));
  } else {
    console.log('BRANCH: ordering not exercised — the visible page holds only one class of row');
  }
});

test('REQ-UI-028 tokens by observed model prints every charted value as text', async ({ page }) => {
  test.setTimeout(150_000);
  await gotoScreen(page, '/routing');
  await activateRoutingTab(page, 'models');

  if (!(await exists(page, 'model-tokens'))) {
    const empty = await renderCheck(page, 'model-tokens-empty');
    console.log(`BRANCH REQ-UI-028: model-tokens-empty — ${empty.verdict}: ${empty.detail}`);
    expect(empty.verdict, `model-tokens-empty: ${empty.detail}`).toBe('RENDERS');
    return;
  }

  const t = await tableCheck(page, 'model-tokens');
  console.log(`model-tokens: ${t.verdict} — ${t.detail}`);
  expect(t.verdict, `model-tokens: ${t.detail}`).toBe('RENDERS');

  const headers = (
    await page.evaluate(() => {
      const root = document.querySelector('[data-testid="model-tokens"]');
      if (!root) return '';
      return Array.from(root.querySelectorAll('thead th'))
        .map(th => (th.textContent || '').trim())
        .join(' | ')
        .toLowerCase();
    })
  ).replace(/\s+/g, ' ');
  console.log(`model-tokens headers: ${headers}`);
  for (const col of ['model', 'in', 'out', 'cache read', 'cache write', 'total']) {
    expect(headers, `model-tokens must have a "${col}" column`).toContain(col);
  }

  // Every value drawn as a bar must also be readable as text in the table beside it.
  const bars = await page.evaluate(() => {
    const root = document.querySelector('[data-testid="model-tokens-bars"]');
    if (!root) return [] as { label: string; figure: string }[];
    const out: { label: string; figure: string }[] = [];
    for (const bar of Array.from(root.children)) {
      const spans = Array.from(bar.querySelectorAll('span')).map(s => (s.textContent || '').trim());
      const figure = spans.find(s => /[\d]/.test(s)) || '';
      const label = spans[spans.length - 1] || '';
      const title = bar.getAttribute('title') || '';
      out.push({ label: title || label, figure });
    }
    return out;
  });
  console.log(`model-tokens-bars: ${bars.length} bars — ${JSON.stringify(bars.slice(0, 8))}`);
  expect(bars.length, 'model-tokens-bars must draw at least one bar when the table has rows').toBeGreaterThan(0);

  const tableText = ((await textOf(page, 'model-tokens')) || '').replace(/\s+/g, ' ');
  for (const bar of bars) {
    expect(bar.figure.length, `a bar carries no readable figure: ${JSON.stringify(bar)}`).toBeGreaterThan(0);
    expect(tableText, `charted value "${bar.figure}" must also appear as text in model-tokens`).toContain(
      bar.figure,
    );
  }
});

test('REQ-UI-029 every repricing figure — the delta included — carries an estimate label', async ({
  page,
}) => {
  test.setTimeout(150_000);
  await gotoScreen(page, '/routing');
  await activateRoutingTab(page, 'repricing');

  for (const card of ['repricing-actual', 'repricing-max', 'repricing-delta']) {
    expect(await exists(page, card), `${card} must exist`).toBe(true);
  }

  // The estimate label is TEXT on every card, the delta card included (explicitly required).
  for (const card of ['repricing-actual', 'repricing-max', 'repricing-delta']) {
    const id = `${card}-estimate`;
    const r = await renderCheck(page, id);
    const text = (await textOf(page, id)) || '';
    console.log(`${id}: ${r.verdict} — "${text}"`);
    expect(r.verdict, `${id}: ${r.detail}`).toBe('RENDERS');
    expect(text.trim().length, `${id} must be text, not colour alone`).toBeGreaterThan(0);
    expect(text.toLowerCase(), `${id} must contain "estimate"`).toContain('estimate');
  }

  for (const card of ['repricing-actual', 'repricing-max', 'repricing-delta']) {
    const id = `${card}-value`;
    const text = ((await textOf(page, id)) || '').trim();
    console.log(`${id} = "${text}"`);
    // A repricing card shows a dollar estimate when there is something to price. When there
    // is not — every run excluded by tokens_scope, or no observed model carries a rate — the
    // honest render is Figure.NotApplicable ("—") or insufficient data (n=…), never a
    // fabricated $0. All three forms are acceptable; a blank or a bare "0" is not.
    expect(text, `${id} must show a $ figure, "—", or insufficient data — never a blank or a bare 0`)
      .toMatch(/\$\s?[\d,]+(\.\d+)?|^[—–-]$|insufficient data \(n=\d+\)/);
  }
  console.log(`repricing-delta-share = "${(await textOf(page, 'repricing-delta-share')) || '(absent)'}"`);

  for (const id of ['repricing-actual-excluded', 'repricing-max-excluded']) {
    const text = ((await textOf(page, id)) || '').toLowerCase();
    console.log(`${id} = "${text}"`);
    expect(text.length, `${id} must render`).toBeGreaterThan(0);
    expect(text, `${id} must state the excluded count`).toMatch(/\d/);
    expect(text, `${id} must mention tokens_scope or excluded`).toMatch(/tokens_scope|excluded/);
  }

  if (await exists(page, 'missing-prices')) {
    const warn = ((await textOf(page, 'missing-prices')) || '').replace(/\s+/g, ' ');
    console.log(`BRANCH REQ-UI-029: missing-prices — "${warn}"`);
    expect(warn, 'missing-prices must name a model').toMatch(/[A-Za-z0-9][\w.\-:/]{2,}/);
    expect(warn.toLowerCase(), 'missing-prices must say the tokens are excluded / not priced').toMatch(
      /left out|excluded|not priced|no entry in prices\.json/,
    );
    // The honest wording is "rather than priced at zero"; a bare claim of zero pricing is the
    // failure. (An earlier `not.toMatch(/priced at zero(?! )/)` here was itself buggy: it fired
    // on the correct sentence whenever "zero" ended it with punctuation. The check below is the
    // one that actually distinguishes the two cases.)
    expect(
      /priced at zero/.test(warn.toLowerCase()) && !/rather than priced at zero/.test(warn.toLowerCase()),
      'missing-prices must never say the model WAS priced at zero',
    ).toBe(false);
  } else {
    console.log('BRANCH REQ-UI-029: no missing-prices warning — every observed model is priced');
  }
});

test('REQ-UI-030 the edit-prices dialog refuses a negative and a blank rate', async ({ page }) => {
  test.setTimeout(150_000);
  await gotoScreen(page, '/routing');
  await activateRoutingTab(page, 'repricing');

  const open = await testid(page, 'edit-prices');
  await open.click();

  const dialog = await testid(page, 'edit-prices-dialog');
  await dialog.waitFor({ state: 'visible', timeout: 20_000 });
  const t = await tableCheck(page, 'edit-prices-table');
  console.log(`edit-prices-table: ${t.verdict} — ${t.detail}`);
  expect(t.verdict, `edit-prices-table: ${t.detail}`).toBe('RENDERS');

  let cell = dialog.locator('input[type="number"]').first();
  if ((await dialog.locator('input[type="number"]').count()) === 0) {
    cell = dialog.locator('input').first();
  }
  await cell.waitFor({ state: 'visible', timeout: 20_000 });
  const original = await cell.inputValue();
  console.log(`first rate cell original value = "${original}"`);

  // 1) A negative rate.
  await cell.fill('-5');
  await cell.blur().catch(() => {});
  await page.waitForTimeout(1_000);
  const invalid = page.locator('[data-testid="edit-prices-invalid"]').first();
  await invalid.waitFor({ state: 'attached', timeout: 20_000 });
  expect(await invalid.count(), 'edit-prices-invalid must appear for a negative rate').toBeGreaterThan(0);
  const save = page.locator('[data-testid="edit-prices-save"]').first();
  await expect(save, 'edit-prices-save must be disabled while a rate is negative').toBeDisabled({
    timeout: 20_000,
  });

  // 2) A blank cell in an otherwise-priced row is still not saveable.
  await cell.fill('');
  await cell.blur().catch(() => {});
  await page.waitForTimeout(1_000);
  await expect(save, 'edit-prices-save must stay blocked while a rate is blank').toBeDisabled({
    timeout: 20_000,
  });
  console.log(
    `edit-prices-invalid still present after blanking: ${await page
      .locator('[data-testid="edit-prices-invalid"]')
      .count()}`,
  );

  // NEVER press edit-prices-save — it rewrites data/prices.json. Cancel discards the edits, which is
  // what keeps this test non-destructive: OpenPricesDialogAsync rebuilds the rows from disk each time.
  const cancel = await testid(page, 'edit-prices-cancel');
  await cancel.click();
  await page.waitForTimeout(1_000);
  const closed = await page.evaluate(() => {
    const d = document.querySelector('[data-testid="edit-prices-dialog"]') as HTMLElement | null;
    if (!d) return true;
    const style = window.getComputedStyle(d);
    return style.display === 'none' || style.visibility === 'hidden' || d.offsetParent === null;
  });
  expect(closed, 'edit-prices-cancel must close the dialog').toBe(true);
});

test('REQ-UI-031 poolable metrics tiles state insufficient data rather than a blank', async ({ page }) => {
  test.setTimeout(150_000);
  await gotoScreen(page, '/routing');
  await activateRoutingTab(page, 'poolable');

  const tiles = [
    'pooled-rework',
    'pooled-batch',
    'pooled-throughput',
    'pooled-tokens-per-verified',
    'pooled-commit-cadence',
  ];

  for (const tile of tiles) {
    expect(await exists(page, tile), `${tile} must exist`).toBe(true);
    const r = await renderCheck(page, tile);
    console.log(`${tile}: ${r.verdict} — ${r.detail}`);
    expect(r.verdict, `${tile}: ${r.detail}`).toBe('RENDERS');

    const value = ((await textOf(page, `${tile}-value`)) || (await textOf(page, tile)) || '')
      .replace(/\s+/g, ' ')
      .trim();
    console.log(`${tile} value = "${value}"`);
    expect(value.length, `${tile} must render non-empty text`).toBeGreaterThan(0);

    // Below the floor the tile must say so literally; it may never be a value-less blank.
    const looksNumeric = /[\d]/.test(value.replace(/insufficient data \(n=\d+\)/, ''));
    const isInsufficient = INSUFFICIENT.test(value);
    const isNotApplicable = value.includes(EM_DASH);
    if (isInsufficient) console.log(`BRANCH: ${tile} is below the floor — "${value}"`);
    if (isNotApplicable) console.log(`BRANCH: ${tile} is Figure.NotApplicable ("—")`);
    expect(
      isInsufficient || isNotApplicable || looksNumeric,
      `${tile} must show a number, "insufficient data (n=…)" or the NotApplicable dash — got "${value}"`,
    ).toBe(true);
  }

  const cadence = ((await textOf(page, 'pooled-commit-cadence')) || '').toLowerCase();
  console.log(`pooled-commit-cadence tile text = "${cadence}"`);
  expect(cadence, 'the commit-cadence sub-line must mention the collapsed duplicates').toMatch(
    /duplicate.*collaps|collaps.*duplicate/,
  );
});

/* ────────────────────────────── /export ────────────────────────────── */

test('REQ-UI-032 export screen offers the export, the dataset SHAs and past snapshots', async ({ page }) => {
  test.setTimeout(150_000);
  await gotoScreen(page, '/export');

  // export-now is asserted present + enabled ONLY. It is deliberately never pressed: pressing it
  // writes snapshot.md and tflens.json into data/reports/<user>/<date>/<framework>/.
  const exportNow = await testid(page, 'export-now');
  await expect(exportNow, 'export-now must be enabled').toBeEnabled({ timeout: 20_000 });
  console.log(`export-parser-version = "${(await textOf(page, 'export-parser-version')) || '(absent)'}"`);
  console.log(`export-framework-note = "${(await textOf(page, 'export-framework-note')) || '(absent)'}"`);

  const target = (await textOf(page, 'export-target')) || '';
  console.log(`export-target = "${target}"`);
  expect(target.length, 'export-target must render').toBeGreaterThan(0);
  expect(target, 'export-target must show the path the snapshot is written to').toContain('data/reports');
  const muted = await page.evaluate(() => {
    const e = document.querySelector('[data-testid="export-target"]');
    return e ? e.className : '';
  });
  console.log(`export-target classes: ${muted}`);
  expect(String(muted), 'export-target must be rendered muted').toMatch(/muted|text-xs/);

  // Dataset SHAs.
  if (await exists(page, 'dataset-shas-table')) {
    const t = await tableCheck(page, 'dataset-shas-table');
    console.log(`dataset-shas-table: ${t.verdict} — ${t.detail}`);
    expect(t.verdict, `dataset-shas-table: ${t.detail}`).toBe('RENDERS');
    const rows = await page.locator('[data-testid="dataset-shas-table"] tbody tr').count();
    const copies = await page.locator('[data-testid^="copy-sha-"]').count();
    console.log(`dataset SHAs: ${rows} rows, ${copies} copy buttons`);
    expect(copies, 'each SHA row must carry a copy-sha-* button').toBeGreaterThanOrEqual(rows);
  } else {
    const empty = await renderCheck(page, 'dataset-shas-empty');
    console.log(`BRANCH REQ-UI-032: dataset-shas-empty — ${empty.verdict}: ${empty.detail}`);
    expect(empty.verdict, `dataset-shas-empty: ${empty.detail}`).toBe('RENDERS');
  }

  // Past snapshots.
  expect(await exists(page, 'snapshots'), 'snapshots section must exist').toBe(true);
  if (await exists(page, 'snapshots-table')) {
    const t = await tableCheck(page, 'snapshots-table');
    console.log(`BRANCH REQ-UI-032: snapshots-table — ${t.verdict}: ${t.detail}`);
    expect(t.verdict, `snapshots-table: ${t.detail}`).toBe('RENDERS');
    const rows = await page.locator('[data-testid="snapshots-table"] tbody tr').count();
    const md = await page.locator('[data-testid^="snapshot-md-"]').count();
    const json = await page.locator('[data-testid^="snapshot-json-"]').count();
    console.log(`snapshots: ${rows} rows, ${md} snapshot.md links, ${json} tflens.json links`);
    expect(md, 'every snapshot row needs a snapshot-md-* link').toBeGreaterThanOrEqual(rows);
    expect(json, 'every snapshot row needs a snapshot-json-* link').toBeGreaterThanOrEqual(rows);
  } else {
    const empty = ((await textOf(page, 'snapshots-empty')) || '').toLowerCase();
    console.log(`BRANCH REQ-UI-032: snapshots-empty — "${empty}"`);
    expect(empty, 'snapshots-empty must read "no snapshots yet"').toContain('no snapshots yet');
  }
});

test('REQ-UI-033 the quotable banner agrees with the last-parity card', async ({ page }) => {
  test.setTimeout(150_000);
  await gotoScreen(page, '/export');

  const banner = await testid(page, 'quotable-banner');
  let text = ((await banner.innerText().catch(() => '')) || '').replace(/\s+/g, ' ').trim();
  if (!/^(NOT QUOTABLE|QUOTABLE)/.test(text)) {
    // The whole sentence is also carried on the element's title attribute.
    const attr = (await banner.getAttribute('title')) || '';
    console.log(`quotable-banner innerText did not lead with the status; title = "${attr}"`);
    if (/^(NOT QUOTABLE|QUOTABLE)/.test(attr.trim())) text = attr.trim();
  }
  console.log(`quotable-banner = "${text}"`);
  expect(text, 'quotable-banner must start with QUOTABLE or NOT QUOTABLE').toMatch(/^(NOT QUOTABLE|QUOTABLE)\b/);

  const isNotQuotable = text.startsWith('NOT QUOTABLE');
  const hasParityNone = await exists(page, 'parity-none');
  const hasParityFacts = await exists(page, 'parity-facts');
  console.log(
    `BRANCH REQ-UI-033: ${isNotQuotable ? 'NOT QUOTABLE' : 'QUOTABLE'} · parity-none=${hasParityNone} · parity-facts=${hasParityFacts}`,
  );

  if (isNotQuotable) {
    const warned = hasParityNone || /warning|no parity/i.test((await textOf(page, 'parity-record')) || '');
    expect(warned, 'NOT QUOTABLE must be accompanied by parity-none or a warning parity card').toBe(true);
  } else {
    // Consistency: QUOTABLE must not coexist with the "no parity run" card.
    expect(hasParityNone, 'QUOTABLE must not coexist with parity-none').toBe(false);
    expect(await exists(page, 'parity-record'), 'parity-record must exist when QUOTABLE').toBe(true);

    const facts = ((await textOf(page, 'parity-facts')) || '').replace(/\s+/g, ' ');
    console.log(`parity-facts = "${facts.slice(0, 300)}"`);
    expect(facts.toLowerCase(), 'parity-facts must name a date').toContain('date');
    expect(facts, 'parity-facts must name a real date').toMatch(/\d{4}-\d{2}-\d{2}/);
    expect(facts.toLowerCase(), 'parity-facts must name the parser version').toContain('parser version');
    expect(facts.toLowerCase(), 'parity-facts must name the script hash').toContain('script hash');

    expect(await exists(page, 'parity-output'), 'parity-output must be rendered').toBe(true);
    const isCodeBlock = await page.evaluate(() => {
      const e = document.querySelector('[data-testid="parity-output"]');
      return !!e && (!!e.querySelector('pre') || !!e.querySelector('code'));
    });
    expect(isCodeBlock, 'parity-output must render as a CodeBlock (pre/code)').toBe(true);
  }
});

test('REQ-UI-034 the five report pages each render a Playbook state with no axis mixing', async ({ page }) => {
  test.setTimeout(300_000);

  const routes = ['/', '/gate-outcomes', '/harness', '/routing', '/export'];
  const report: Record<string, unknown>[] = [];

  try {
    await gotoScreen(page, '/');
    await switchFramework(page, 'Playbook');

    for (const route of routes) {
      await gotoScreen(page, route);
      await page.waitForTimeout(1_500);

      const ids = await contentTestIds(page);
      const hasPlaybookEmpty = ids.includes('playbook-empty');
      const hasConnect = ids.includes('playbook-empty-connect');
      const hasAxisNote = ids.includes('playbook-axis-note');
      const errorIds = ids.filter(i => i.endsWith('-error'));
      const gateDist = ids.filter(i => i.startsWith('gate-dist-'));
      const pbPhases = ids.filter(i => i.startsWith('pb-phases-'));
      const bodyText = (await page.locator('body').innerText().catch(() => '')) || '';

      const row = {
        route,
        hasPlaybookEmpty,
        hasConnect,
        hasAxisNote,
        errorIds,
        gateDist,
        pbPhases,
        bodyChars: bodyText.trim().length,
        testids: ids.slice(0, 40),
      };
      report.push(row);
      console.log(`PLAYBOOK ${route}: ${JSON.stringify(row)}`);

      // Hard: the page renders, and it does not render a failure alert.
      expect(errorIds, `${route} must not render an error alert on the Playbook axis`).toEqual([]);
      expect(bodyText.trim().length, `${route} must render content on the Playbook axis`).toBeGreaterThan(80);

      // The empty state, when shown, must offer the connect action.
      if (hasPlaybookEmpty) {
        expect(hasConnect, `${route}: playbook-empty must carry its connect action`).toBe(true);
      }

      // CRITICAL SEPARATION: no phase_gate data may share a surface with TechieFlow gate data.
      // On the Playbook axis the gate-outcomes gate distribution must not be populated.
      if (gateDist.length > 0) {
        const populated = await page.evaluate(() => {
          const out: { id: string; rows: number }[] = [];
          for (const e of Array.from(document.querySelectorAll('[data-testid^="gate-dist-"]'))) {
            const id = e.getAttribute('data-testid') || '';
            if (id.startsWith('gate-dist-note-') || id.startsWith('gate-dist-unlisted-')) continue;
            out.push({ id, rows: e.querySelectorAll('tbody tr').length });
          }
          return out;
        });
        console.log(`PLAYBOOK ${route}: gate-dist tables observed — ${JSON.stringify(populated)}`);
        for (const g of populated) {
          expect(
            g.rows,
            `${route}: ${g.id} renders ${g.rows} TechieFlow gate rows while the Playbook axis is selected ` +
              `— phase_gate and gate must never share a surface`,
          ).toBe(0);
        }
      }

      // pb-phases-*, when present, must not be blank.
      for (const id of pbPhases) {
        const t = ((await textOf(page, id)) || '').trim();
        console.log(`PLAYBOOK ${route}: ${id} = "${t.slice(0, 80)}"`);
        expect(t.length, `${route}: ${id} must render non-empty`).toBeGreaterThan(0);
      }

      // Soft: the page should declare which axis it is on — either the Playbook empty state or the
      // standing axis note. Pages that re-query on the Playbook axis but reuse the TechieFlow surface
      // carry neither, which is what this records for the verifier to judge.
      expect
        .soft(
          hasPlaybookEmpty || hasAxisNote,
          `${route} renders neither playbook-empty nor playbook-axis-note on the Playbook axis; ` +
            `observed testids: ${JSON.stringify(ids.slice(0, 25))}`,
        )
        .toBe(true);
    }

    console.log(`REQ-UI-034 SUMMARY: ${JSON.stringify(report)}`);
  } finally {
    // Leave the axis where later tests expect to find it.
    await gotoScreen(page, '/').catch(() => {});
    await switchFramework(page, 'TechieFlow').catch(() => {});
  }
});
