/*
 * REQ-UI-045 … REQ-UI-050 — Phase effort (`/effort`), the seventh report page, on both framework axes.
 *
 * Black-box only: nothing here reads application source, writes to the user's data folder, or presses a
 * control that mutates disk state. Every assertion is against the running app signed in as the
 * documented demo user.
 *
 * Two habits this file keeps, because the screen it grades exists to enforce them:
 *
 *   1. Where a branch depends on the live database, the test asserts WHICH branch it is in and logs it.
 *      A `/effort` whose fan-out coverage is 0 today and 40 tomorrow must fail for a real reason, never
 *      pass because it found nothing to look at.
 *   2. The vocabulary IS the requirement. `not observed` and `0 subagents` are the same pixel count and
 *      opposite claims, so the strings are asserted literally rather than approximated by a shape check.
 *
 * Nothing here pins a record count from the live store: shapes, orderings and words only.
 */
import { test, Page } from '@playwright/test';
import {
  signIn,
  gotoScreen,
  testid,
  renderCheck,
  tableCheck,
  visualCheck,
  collectErrors,
  USER1,
  DESKTOP,
  MOBILE,
  expect,
} from './_helpers';

/** The controls this screen is named by in the checklist, and the set the visual gate measures. */
const PAGE_IDS = [
  'effort-page', 'effort-period', 'effort-period-label', 'effort-budget-note',
  'effort-kpis', 'kpi-runs', 'kpi-wallclock', 'kpi-tokens-out', 'kpi-tokens-measured',
  'kpi-heaviest', 'kpi-fanout-coverage',
  'effort-denominators', 'effort-token-window', 'effort-scope-table',
  'effort-fanout-exclusions', 'effort-fanout-table', 'effort-observed-badge',
  'effort-phases', 'effort-phase-table', 'effort-phase-detail',
  'effort-routing', 'effort-routing-table', 'effort-routing-byphase-trigger',
  'effort-cost', 'effort-cost-table', 'effort-actor-note',
];

/** The shell controls that must render on this route like every other report page (REQ-UI-010). */
const SHELL_IDS = ['app-sidebar', 'sidebar-trigger', 'framework-switch', 'sync-now', 'theme-toggle', 'user-menu'];

/** The eight nav items, in the order the shell must render them (REQ-UI-006 as amended 2026-09-01). */
const NAV_IDS = [
  'nav-repos', 'nav-coverage', 'nav-gate-outcomes', 'nav-harness', 'nav-routing',
  'nav-misses', 'nav-effort', 'nav-export',
];

/** The two shapes an ADR-007 Figure is allowed to reach the screen in. */
const INSUFFICIENT = /insufficient data \(n=\d+\)/;
const EM_DASH = '—';

let objErrors: string[] = [];

test.beforeEach(async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  objErrors = collectErrors(page);
  await signIn(page, USER1);
});

test.afterEach(async () => {
  const real = objErrors.filter(e => !/favicon|websocket/i.test(e));
  if (real.length > 0) console.log(`CONSOLE-ERRORS (${real.length}): ${real.slice(0, 8).join(' | ')}`);
});

/* ───────────────────────────── local helpers ───────────────────────────── */

/** Trimmed, whitespace-collapsed innerText of a testid, or null when the element is absent. */
async function textOf(page: Page, id: string): Promise<string | null> {
  if ((await page.locator(`[data-testid="${id}"]`).count()) === 0) return null;
  const t = await page.locator(`[data-testid="${id}"]`).first().innerText().catch(() => '');
  return (t || '').replace(/\s+/g, ' ').trim();
}

async function exists(page: Page, id: string): Promise<boolean> {
  return (await page.locator(`[data-testid="${id}"]`).count()) > 0;
}

/** DOM index of each testid among all testids, so ordering is compared as the reader sees it. */
async function domOrder(page: Page, ids: string[]): Promise<number[]> {
  return page.evaluate((wanted: string[]) => {
    const all = Array.from(document.querySelectorAll('[data-testid]'));
    return wanted.map(id => all.findIndex(el => el.getAttribute('data-testid') === id));
  }, ids);
}

/** Puts the header Framework switch on one axis and waits for the page to re-query (ADR-016). */
async function switchFramework(page: Page, label: 'TechieFlow' | 'Playbook'): Promise<void> {
  const sw = await testid(page, 'framework-switch');
  const trigger = sw.locator('[role="tab"]').filter({ hasText: label }).first();
  if ((await trigger.count()) === 0) await sw.getByText(label, { exact: false }).first().click();
  else await trigger.click();
  await page.waitForTimeout(2_500);
  const active = await page.evaluate(() => {
    const root = document.querySelector('[data-testid="framework-switch"]');
    if (!root) return '';
    const on = Array.from(root.querySelectorAll('[role="tab"]')).find(
      t => t.getAttribute('data-state') === 'active' || t.getAttribute('aria-selected') === 'true');
    return (on?.textContent || '').replace(/\s+/g, ' ').trim();
  });
  console.log(`FRAMEWORK-SWITCH -> requested "${label}", active trigger reads "${active}"`);
}

/** The phase keys the page rendered, in the table's own order. */
async function phaseKeys(page: Page): Promise<string[]> {
  return page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid^="phase-cmd-"]'))
      .map(e => (e.getAttribute('data-testid') || '').replace(/^phase-cmd-/, '')));
}

/** Opens one phase's disclosure if it is not already open, and returns its four band labels. */
async function openPhase(page: Page, key: string): Promise<string[]> {
  const body = () => page.evaluate(
    (k: string) => {
      const card = document.querySelector(`[data-testid="effort-detail-${k}"]`);
      return !!card && !!card.querySelector('.tflens-detail-body');
    }, key);

  if (!(await body())) {
    await page.locator(`[data-testid="effort-detail-trigger-${key}"]`).first().click();
    await page.waitForTimeout(1_200);
  }
  expect(await body(), `effort-detail-${key} did not expand when its trigger was pressed`).toBe(true);

  return page.evaluate((k: string) => {
    const card = document.querySelector(`[data-testid="effort-detail-${k}"]`);
    return Array.from(card?.querySelectorAll('.tflens-panel-label') || [])
      .map(p => (p.textContent || '').replace(/\s+/g, ' ').trim());
  }, key);
}

/**
 * Resolves a CSS colour into an sRGB triple.
 * `--destructive` is authored in oklch, so a raw string comparison against a badge's `rgb(...)` would
 * "pass" for every colour in the product. color-mix in srgb puts both through the same conversion.
 */
async function srgbOf(page: Page, css: string): Promise<[number, number, number]> {
  return page.evaluate((value: string) => {
    const probe = document.createElement('span');
    probe.style.position = 'absolute';
    probe.style.opacity = '0';
    probe.style.color = `color-mix(in srgb, ${value} 100%, transparent)`;
    document.body.appendChild(probe);
    const text = getComputedStyle(probe).color;
    probe.remove();
    const nums = (text.match(/-?[\d.]+/g) || []).slice(0, 3).map(Number);
    // color(srgb r g b) is 0..1; rgb(r, g, b) is 0..255.
    return (text.startsWith('color(') ? nums : nums.map(n => n / 255)) as [number, number, number];
  }, css);
}

/* ─────────────────────────────── REQ-UI-045 ─────────────────────────────── */

test('REQ-UI-045 /effort is reachable and is the eighth nav item, between Misses & rework and Snapshot export', async ({ page }) => {
  test.setTimeout(120_000);
  await gotoScreen(page, '/effort');

  await expect(page.locator('[data-testid="effort-page"]').first(), '/effort did not render its page root').toBeVisible();

  for (const id of NAV_IDS) {
    expect(await exists(page, id), `${id} is missing from the sidebar`).toBe(true);
  }
  const order = await domOrder(page, NAV_IDS);
  console.log(`NAV ORDER: ${JSON.stringify(NAV_IDS.map((id, i) => `${id}@${order[i]}`))}`);
  expect(order.some(i => i < 0), `a nav item is missing: ${JSON.stringify(order)}`).toBe(false);
  for (let i = 1; i < order.length; i++) {
    expect(order[i], `${NAV_IDS[i]} is rendered before ${NAV_IDS[i - 1]}`).toBeGreaterThan(order[i - 1]);
  }
  expect(NAV_IDS.indexOf('nav-effort'), 'nav-effort is not the eighth nav item').toBe(6);
  expect(NAV_IDS[NAV_IDS.indexOf('nav-effort') - 1], 'nav-effort does not follow nav-misses').toBe('nav-misses');
  expect(NAV_IDS[NAV_IDS.indexOf('nav-effort') + 1], 'nav-effort does not precede nav-export').toBe('nav-export');

  // BRD-108 — the framework is a header switch and never a nav item.
  expect(await page.locator('a[href*="/playbook"]').count(), 'a /playbook nav item exists').toBe(0);

  // The item marks the open route, and no other item claims it.
  const marks = await page.evaluate((ids: string[]) => ids.map(id => {
    const el = document.querySelector(`[data-testid="${id}"]`);
    return { id, current: el?.getAttribute('aria-current') ?? null, active: el?.hasAttribute('data-active') ?? false };
  }), NAV_IDS);
  console.log(`NAV ACTIVE MARKS on /effort: ${JSON.stringify(marks)}`);
  const effort = marks.find(m => m.id === 'nav-effort')!;
  expect(effort.current, 'nav-effort does not carry aria-current="page" while /effort is open').toBe('page');
  expect(effort.active, 'nav-effort does not carry the data-active marker while /effort is open').toBe(true);
  const strays = marks.filter(m => m.id !== 'nav-effort' && (m.current === 'page' || m.active));
  expect(strays, `another nav item is marked active while /effort is open: ${JSON.stringify(strays)}`).toEqual([]);
});

test('REQ-UI-045 /effort carries the Framework switch, defaults to all history and states the BRD-169 framing', async ({ page }) => {
  test.setTimeout(120_000);
  await gotoScreen(page, '/effort');

  // REQ-UI-010 — the switch is on this report page like the other six.
  for (const id of SHELL_IDS) {
    expect(await exists(page, id), `${id} missing on /effort`).toBe(true);
  }
  const axis = await page.evaluate(() => {
    const root = document.querySelector('[data-testid="framework-switch"]');
    return Array.from(root?.querySelectorAll('[role="tab"]') || []).map(t => ({
      label: (t.textContent || '').replace(/\s+/g, ' ').trim(),
      state: t.getAttribute('data-state'),
    }));
  });
  console.log(`framework-switch on /effort: ${JSON.stringify(axis)}`);
  expect(axis.length, 'the Framework switch must offer both axes on /effort').toBe(2);
  expect(axis.some(a => /TechieFlow/.test(a.label) && a.state === 'active'),
    `/effort did not open on the TechieFlow axis: ${JSON.stringify(axis)}`).toBe(true);

  // BRD-125 — the first view is unfiltered, and the closed trigger says so rather than showing its key.
  const period = (await textOf(page, 'effort-period')) || '';
  const label = (await textOf(page, 'effort-period-label')) || '';
  console.log(`effort-period = "${period}" · effort-period-label = "${label}"`);
  expect(period.toLowerCase(), 'effort-period does not default to All history').toContain('all history');
  expect(period, 'effort-period renders its raw bound key rather than its label (TR-020)').not.toBe('all');
  expect(label.toLowerCase(), 'effort-period-label does not state the window').toContain('all history');

  // BRD-169 — standing page copy, on screen without hovering anything, and true of an empty page too.
  const note = await testid(page, 'effort-budget-note');
  await expect(note, 'effort-budget-note is not visible').toBeVisible();
  const noteText = ((await textOf(page, 'effort-budget-note')) || '').toLowerCase();
  console.log(`effort-budget-note = "${noteText.slice(0, 200)}"`);
  expect(noteText, 'effort-budget-note must call itself a budgeting view').toContain('budgeting view');
  expect(noteText, 'effort-budget-note must deny being a quality scoreboard').toContain('not a quality scoreboard');
  expect(noteText, 'effort-budget-note must say a costlier phase is not evidence of inefficiency')
    .toMatch(/not\s+evidence|fact about what those phases/);
  expect(noteText, 'effort-budget-note must send quality questions to the misses screen').toContain('misses');

  const real = objErrors.filter(e => !/favicon|websocket/i.test(e));
  expect(real, `console errors on /effort: ${real.join(' | ')}`).toEqual([]);
});

/* ─────────────────────────────── REQ-UI-046 ─────────────────────────────── */

test('REQ-UI-046 the five KPI tiles render, and the token, wall-clock and fan-out tiles carry their mandatory words', async ({ page }) => {
  test.setTimeout(120_000);
  await gotoScreen(page, '/effort');

  const tiles = ['kpi-runs', 'kpi-wallclock', 'kpi-tokens-out', 'kpi-heaviest', 'kpi-fanout-coverage'];
  for (const id of [...tiles, 'effort-kpis']) {
    const r = await renderCheck(page, id);
    console.log(`${id}: ${r.verdict} — ${r.detail}`);
    expect(r.verdict, `${id}: ${r.detail}`).toBe('RENDERS');
    expect(((await textOf(page, id)) || '').length, `${id} renders no text`).toBeGreaterThan(0);
  }
  const count = await page.locator('[data-testid="effort-kpis"] [data-testid^="kpi-"]').count();
  console.log(`effort-kpis holds ${count} kpi-* controls`);
  expect(count, 'the KPI row must hold five tiles').toBeGreaterThanOrEqual(tiles.length);

  // BRD-146 — `measured on n of N runs` is VISIBLE TEXT on the token tile, never a tooltip.
  const measured = (await textOf(page, 'kpi-tokens-measured')) || '';
  const tokensTile = (await textOf(page, 'kpi-tokens-out')) || '';
  console.log(`kpi-tokens-measured = "${measured}"`);
  expect(measured, 'kpi-tokens-measured must read "measured on n of N runs"').toMatch(/measured on \d+ of \d+ runs/);
  expect(tokensTile, 'the denominator must be in the token tile\'s own visible text')
    .toMatch(/measured on \d+ of \d+ runs/);

  const inTitles = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[title]'))
      .map(e => ({ id: e.getAttribute('data-testid'), title: e.getAttribute('title') || '' }))
      .filter(t => /measured on \d+ of \d+ runs/i.test(t.title)));
  expect(inTitles, `the n-of-N denominator is carried in a title attribute: ${JSON.stringify(inTitles)}`).toEqual([]);
  expect(await page.locator('[data-testid="kpi-tokens-measured"]').first().getAttribute('title'),
    'kpi-tokens-measured must not hide anything behind a tooltip').toBeNull();

  // BRD-146 — the excluded runs are named as excluded, never averaged in as zero.
  expect(tokensTile.toLowerCase(), 'the token tile must say the unmeasured runs are excluded, not zeroed')
    .toMatch(/excluded.*never averaged in as zero|never averaged in as zero/);

  // REQ-UI-046 — wall clock is never human effort, in words.
  const wall = ((await textOf(page, 'kpi-wallclock')) || '').toLowerCase();
  console.log(`kpi-wallclock = "${wall}"`);
  expect(wall, 'kpi-wallclock must say the figure is never human effort').toContain('never human effort');
  expect(wall, 'kpi-wallclock must name what it summed').toContain('duration_s');

  // BRD-151 — fan-out is a COVERAGE figure, `n of N`, on the KPI row.
  const fanout = (await textOf(page, 'kpi-fanout-coverage')) || '';
  console.log(`kpi-fanout-coverage = "${fanout}"`);
  expect(fanout.toLowerCase(), 'kpi-fanout-coverage must call itself a coverage figure').toContain('coverage');
  expect(fanout, 'kpi-fanout-coverage must read as "n of N"').toMatch(/\b\d[\d,]* of \d[\d,]*\b/);
  const [observed, runs] = (fanout.match(/(\d[\d,]*) of (\d[\d,]*)/) || ['', '0', '0'])
    .slice(1).map(n => Number(n.replace(/,/g, '')));
  expect(observed, `fan-out coverage claims ${observed} observed of ${runs} runs`).toBeLessThanOrEqual(runs);
  console.log(observed === 0
    ? `BRANCH REQ-UI-046: nothing is fan-out observed yet — the tile reads "${observed} of ${runs}" as coverage, not as a subagent count`
    : `BRANCH REQ-UI-046: ${observed} of ${runs} runs are fan-out observed`);

  // The heaviest tile names a phase rather than printing a bare figure.
  const heaviest = (await textOf(page, 'kpi-heaviest')) || '';
  console.log(`kpi-heaviest = "${heaviest}"`);
  expect(heaviest, 'kpi-heaviest must name the phase and its two shares')
    .toMatch(/(of all output tokens|of wall clock)|no phase has recorded a run/);
});

/* ─────────────────────────────── REQ-UI-047 ─────────────────────────────── */

test('REQ-UI-047 the phase table has Measured as a column, sorts by output share and never reports 0 subagents', async ({ page }) => {
  test.setTimeout(150_000);
  await gotoScreen(page, '/effort');

  const t = await tableCheck(page, 'effort-phase-table');
  console.log(`effort-phase-table: ${t.verdict} — ${t.detail}`);
  expect(t.verdict, `effort-phase-table: ${t.detail}`).toBe('RENDERS');

  // BRD-151 — `Measured` is a COLUMN, asserted as a header cell rather than as a word on the page.
  const headers = await page.evaluate(() => {
    const root = document.querySelector('[data-testid="effort-phase-table"]');
    return Array.from(root?.querySelectorAll('thead th') || []).map(h => (h.textContent || '').trim());
  });
  console.log(`effort-phase-table headers: ${JSON.stringify(headers)}`);
  expect(headers.map(h => h.toLowerCase()), 'Measured is not a column of the phase table').toContain('measured');
  for (const col of ['command phase', 'runs', 'share of output', 'fan-out']) {
    expect(headers.map(h => h.toLowerCase()), `the phase table has no "${col}" column`).toContain(col);
  }

  const rows = await page.evaluate(() => {
    const root = document.querySelector('[data-testid="effort-phase-table"]');
    const idx = (name: string) => Array.from(root?.querySelectorAll('thead th') || [])
      .findIndex(h => (h.textContent || '').trim().toLowerCase() === name);
    const cShare = idx('share of output'), cMeasured = idx('measured'), cFanout = idx('fan-out'),
      cTokens = idx('output tokens'), cCmd = idx('command phase');
    return Array.from(root?.querySelectorAll('tbody tr') || []).map(tr => {
      const tds = Array.from(tr.querySelectorAll('td')).map(td => (td.textContent || '').replace(/\s+/g, ' ').trim());
      return { cmd: tds[cCmd], share: tds[cShare], measured: tds[cMeasured], fanout: tds[cFanout], tokens: tds[cTokens], all: tds };
    });
  });
  console.log(`effort-phase-table rows: ${JSON.stringify(rows.map(r => [r.cmd, r.share, r.measured, r.fanout, r.tokens]))}`);
  expect(rows.length, 'the phase table rendered no rows').toBeGreaterThan(0);

  for (const r of rows) {
    for (const cell of r.all) {
      expect(cell.length, `a cell on row "${r.cmd}" is blank: ${JSON.stringify(r.all)}`).toBeGreaterThan(0);
    }
    expect(r.measured, `the Measured cell on "${r.cmd}" must read "n of N", got "${r.measured}"`)
      .toMatch(/^\d[\d,]* of \d[\d,]*$/);
  }

  // REQ-UI-047 — one row per cmd, sorted by output-token share descending.
  const shares = rows.map(r => ({ cmd: r.cmd, raw: r.share, pct: Number((r.share.match(/-?[\d.]+/) || ['NaN'])[0]) }));
  const comparable = shares.filter(s => Number.isFinite(s.pct));
  console.log(`share-of-output series: ${JSON.stringify(shares.map(s => `${s.cmd}=${s.raw}`))}`);
  if (comparable.length < 2) {
    console.log(`BRANCH REQ-UI-047: only ${comparable.length} row carries a numeric share — ordering not exercised`);
  }
  expect(comparable.length, 'no row carries a readable share of output — the ordering claim is untestable')
    .toBeGreaterThan(0);
  for (let i = 1; i < comparable.length; i++) {
    expect(comparable[i].pct,
      `"${comparable[i].cmd}" (${comparable[i].raw}) sits below "${comparable[i - 1].cmd}" (${comparable[i - 1].raw}) ` +
      `but carries the larger share of output — the table is not sorted descending`)
      .toBeLessThanOrEqual(comparable[i - 1].pct);
  }

  // ADR-026 / TF-005 — a phase with no observed fan-out reads `not observed`, and NEVER `0 subagents`.
  let notObserved = 0, observedRows = 0;
  for (const r of rows) {
    expect(r.fanout, `the Fan-out cell on "${r.cmd}" reads "${r.fanout}" — it must be "n observed" or "not observed"`)
      .toMatch(/^(not observed|\d[\d,]* observed)$/);
    expect(r.fanout, `the Fan-out cell on "${r.cmd}" reads "0 observed" — an absence of observation is never a zero`)
      .not.toBe('0 observed');
    if (r.fanout === 'not observed') notObserved++; else observedRows++;
  }
  console.log(`BRANCH REQ-UI-047: ${notObserved} phases read "not observed", ${observedRows} report an observed count`);
  expect(notObserved + observedRows, 'no phase row carried a fan-out verdict').toBe(rows.length);

  const body = (await page.locator('body').innerText()).replace(/\s+/g, ' ');
  expect(/\b0\s+subagents\b/i.test(body),
    '"0 subagents" appears on /effort — an absence of observation must never be rendered as a measurement of zero')
    .toBe(false);
  // The phrase "spawns no subagents" DOES appear on the page — inside the exclusion card's own
  // explanation of why pooling the two exclusions would be the defect. That sentence is the guard, not
  // a violation of it, so what is asserted is that the phrase only ever appears as the thing the page
  // refuses to say: quoted, and beside the truth it would displace.
  const claims = body.match(/[^.]*\bno subagents\b[^.]*/gi) || [];
  console.log(`sentences mentioning "no subagents": ${JSON.stringify(claims)}`);
  for (const claim of claims) {
    expect(/we did not look|would|quarant|never/i.test(claim),
      `/effort states "no subagents" as a finding rather than as the claim it refuses to make: "${claim.trim()}"`)
      .toBe(true);
  }

  // Cross-cutting: a phase measured on 0 runs shows the dash, never a token zero.
  const unmeasured = rows.filter(r => /^0 of /.test(r.measured));
  console.log(`BRANCH REQ-UI-047: ${unmeasured.length} phases carry no token window on any run`);
  for (const r of unmeasured) {
    expect(r.tokens,
      `"${r.cmd}" is measured on 0 runs but its Output tokens cell reads "${r.tokens}" — an unmeasured phase must ` +
      `render "${EM_DASH}", never a zero`).toBe(EM_DASH);
  }
});

/* ─────────────────────────────── REQ-UI-048 ─────────────────────────────── */

test('REQ-UI-048 the per-phase detail expands into four ordered bands, each carrying its own caveat', async ({ page }) => {
  test.setTimeout(180_000);
  await gotoScreen(page, '/effort');

  const keys = await phaseKeys(page);
  console.log(`phases on the page: ${JSON.stringify(keys)}`);
  expect(keys.length, 'no phase rendered a detail disclosure').toBeGreaterThan(0);

  const key = keys[0];
  const labels = await openPhase(page, key);
  console.log(`effort-detail-${key} band labels: ${JSON.stringify(labels)}`);

  // REQ-UI-048 — the four bands, IN ORDER.
  const wanted = ['1 · time', '2 · tokens', '3 · by model', '4 · fan-out'];
  expect(labels.length, `effort-detail-${key} rendered ${labels.length} bands, not four`).toBe(4);
  labels.forEach((label, i) => {
    expect(label.toLowerCase().startsWith(wanted[i]),
      `band ${i + 1} of effort-detail-${key} reads "${label}" — the order must be Time, Tokens, By model, Fan-out`)
      .toBe(true);
  });

  // BRD-146 — the Tokens band carries its own denominator on its label.
  const tokensBadge = (await textOf(page, `tokens-measured-${key}`)) || '';
  console.log(`tokens-measured-${key} = "${tokensBadge}"`);
  expect(tokensBadge, `the Tokens band of "${key}" must carry "measured on n of N runs"`)
    .toMatch(/measured on \d+ of \d+ runs/);

  // The mean sits BESIDE the median rather than instead of it.
  const tokenHeaders = await page.evaluate((k: string) => {
    const card = document.querySelector(`[data-testid="effort-detail-${k}"]`);
    const band = Array.from(card?.querySelectorAll('.tflens-panel') || [])[1];
    return Array.from(band?.querySelectorAll('thead th') || []).map(h => (h.textContent || '').trim().toLowerCase());
  }, key);
  console.log(`tokens band headers: ${JSON.stringify(tokenHeaders)}`);
  expect(tokenHeaders.some(h => h.includes('median')), 'the Tokens band has no median column').toBe(true);
  expect(tokenHeaders.some(h => h.includes('mean')), 'the Tokens band has no mean column beside the median').toBe(true);

  // BRD-150 — the model band is observational, not causal, and says so.
  const models = await page.evaluate((k: string) => {
    const card = document.querySelector(`[data-testid="effort-detail-${k}"]`);
    const band = Array.from(card?.querySelectorAll('.tflens-panel') || [])[2];
    return {
      text: ((band as HTMLElement)?.innerText || '').replace(/\s+/g, ' ').trim(),
      hasNone: !!band?.querySelector(`[data-testid="models-none-${k}"]`),
      rows: band?.querySelectorAll('tbody tr').length ?? 0,
    };
  }, key);
  if (models.hasNone) {
    console.log(`BRANCH REQ-UI-048: "${key}" carries no model attribution — the band states the absence`);
    expect(models.text.toLowerCase(), 'the empty model band must call the absence an absence, not a phase that ran on no model')
      .toMatch(/not observed, rather than/);
  } else {
    console.log(`BRANCH REQ-UI-048: "${key}" attributes ${models.rows} model rows`);
    expect(models.rows, 'the model band shows neither rows nor its absence note').toBeGreaterThan(0);
    expect(models.text.toLowerCase(), 'the model band must say the ranking is observational, not causal')
      .toContain('observational, not causal');
    expect(models.text, 'the model band must name the split it was computed from').toContain('model_tokens_out');
  }

  // BRD-149 — the fan-out band states `observed_n of runs` BEFORE the numbers.
  const fan = await page.evaluate((k: string) => {
    const card = document.querySelector(`[data-testid="effort-detail-${k}"]`);
    const band = Array.from(card?.querySelectorAll('.tflens-panel') || [])[3];
    const kids = Array.from(band?.children || []);
    const alertIdx = kids.findIndex(c => (c.getAttribute('data-testid') || '').startsWith('fanout-alert-'));
    const numbersIdx = kids.findIndex(c => !!c.querySelector('table') ||
      (c.getAttribute('data-testid') || '').startsWith('fanout-none-'));
    const alert = band?.querySelector(`[data-testid="fanout-alert-${k}"]`) as HTMLElement | null;
    return {
      alertIdx,
      numbersIdx,
      alertText: (alert?.innerText || '').replace(/\s+/g, ' ').trim(),
      isObserved: !band?.querySelector(`[data-testid="fanout-none-${k}"]`),
      noneText: ((band?.querySelector(`[data-testid="fanout-none-${k}"]`) as HTMLElement | null)?.innerText || '')
        .replace(/\s+/g, ' ').trim(),
    };
  }, key);
  console.log(`fanout band of "${key}": alertIdx=${fan.alertIdx} numbersIdx=${fan.numbersIdx} observed=${fan.isObserved}`);
  console.log(`fanout-alert-${key} = "${fan.alertText.slice(0, 200)}"`);
  expect(fan.alertIdx, `the fan-out band of "${key}" renders no coverage alert`).toBeGreaterThanOrEqual(0);
  expect(fan.numbersIdx, `the fan-out band of "${key}" renders neither figures nor its absence note`).toBeGreaterThanOrEqual(0);
  expect(fan.alertIdx,
    `the fan-out figures of "${key}" are rendered before the "observed_n of runs" coverage statement`)
    .toBeLessThan(fan.numbersIdx);
  expect(fan.alertText, `the fan-out band of "${key}" must lead with "n of N runs observed"`)
    .toMatch(/^\d[\d,]* of \d[\d,]* runs observed/);

  if (fan.isObserved) {
    console.log(`BRANCH REQ-UI-048: "${key}" has observed runs — the spawn figures are rendered`);
    const spawns = (await textOf(page, `spawns-total-${key}`)) || '';
    console.log(`spawns-total-${key} = "${spawns}"`);
    expect(spawns.length, `spawns-total-${key} rendered blank`).toBeGreaterThan(0);
  } else {
    console.log(`BRANCH REQ-UI-048: "${key}" has no observed run — the band reads "not observed"`);
    expect(fan.noneText.toLowerCase(), 'the unobserved fan-out band must say the figure is absent, not zero')
      .toContain('absent, not zero');
    expect(await exists(page, `spawns-total-${key}`),
      `spawns-total-${key} is rendered for a phase nothing was observed in`).toBe(false);
  }

  // BRD-149 — declared vs measured, with the measured figure named as authoritative.
  const bandText = await page.evaluate((k: string) => {
    const card = document.querySelector(`[data-testid="effort-detail-${k}"]`) as HTMLElement | null;
    return (card?.innerText || '').replace(/\s+/g, ' ');
  }, key);
  expect(bandText.toLowerCase(), 'the fan-out band must carry the declared-vs-measured line')
    .toContain('declared vs measured');
  expect(bandText.toLowerCase(), 'the declared-vs-measured line must name the measured figure as authoritative')
    .toContain('measured figure is authoritative');
});

/* ─────────────────────────────── REQ-UI-049 ─────────────────────────────── */

test('REQ-UI-049 the routing band publishes routed / drifted / unknown and never paints drift as a failure', async ({ page }) => {
  test.setTimeout(150_000);
  await gotoScreen(page, '/effort');

  const r = await tableCheck(page, 'effort-routing-table');
  console.log(`effort-routing-table: ${r.verdict} — ${r.detail}`);
  expect(r.verdict, `effort-routing-table: ${r.detail}`).toBe('RENDERS');

  for (const outcome of ['routed', 'drifted', 'unknown']) {
    expect(await exists(page, `routing-${outcome}`), `routing-${outcome} is missing from the routing band`).toBe(true);
    expect((await textOf(page, `routing-${outcome}`)) || '', `routing-${outcome} does not name its outcome`)
      .toBe(outcome);
    const count = (await textOf(page, `routing-count-${outcome}`)) || '';
    console.log(`routing-${outcome} = ${count}`);
    expect(count, `routing-count-${outcome} is blank`).toMatch(/^\d[\d,]*$/);
  }

  // `unknown` keeps its own count rather than being folded into either side.
  const unknown = ((await textOf(page, 'effort-routing')) || '').toLowerCase();
  expect(unknown, 'the routing card must say what unknown means').toContain('no declared route on the record');

  // REQ-UI-049 — `drifted` is NOT destructive, asserted on the class AND on the rendered colour.
  const styling = await page.evaluate(() => {
    const out: Record<string, { cls: string; color: string; bg: string; border: string }> = {};
    for (const outcome of ['routed', 'drifted', 'unknown']) {
      const el = document.querySelector(`[data-testid="routing-${outcome}"]`);
      if (!el) continue;
      const cs = getComputedStyle(el);
      out[outcome] = { cls: el.className, color: cs.color, bg: cs.backgroundColor, border: cs.borderColor };
    }
    return out;
  });
  console.log(`routing badge styling: ${JSON.stringify(styling)}`);
  expect(styling.drifted, 'routing-drifted rendered no badge to style').toBeTruthy();
  expect(styling.drifted.cls, 'routing-drifted carries a destructive class — drift is observed, never enforced')
    .not.toMatch(/destructive/i);
  expect(styling.drifted.cls, 'routing-drifted must carry the informational tone class').toContain('tflens-badge-info');

  const destructive = await srgbOf(page, 'var(--destructive)');
  const drifted = await srgbOf(page, styling.drifted.color);
  const near = (a: number[], b: number[]) => a.every((v, i) => Math.abs(v - b[i]) < 0.06);
  console.log(`--destructive srgb = ${JSON.stringify(destructive.map(n => +n.toFixed(3)))} · ` +
    `routing-drifted srgb = ${JSON.stringify(drifted.map(n => +n.toFixed(3)))}`);
  expect(near(drifted, destructive),
    `routing-drifted is painted the destructive colour (${JSON.stringify(drifted)} ≈ ${JSON.stringify(destructive)})`)
    .toBe(false);
  // And it is not merely a different red: drift may not land in the destructive bucket at all.
  const [dr, dg, db] = drifted;
  expect(dr > dg + 0.15 && dr > db + 0.15,
    `routing-drifted is red-dominant (r=${dr.toFixed(2)}, g=${dg.toFixed(2)}, b=${db.toFixed(2)}) — a red pill ` +
    `asserts a routing policy the framework does not have`).toBe(false);

  // The footer says it in words too, so the rule survives a restyle.
  const footer = ((await textOf(page, 'effort-routing')) || '').toLowerCase();
  expect(footer, 'the routing card must state that drift is styled as information, never as an error')
    .toMatch(/never as an error|information, never/);

  // The same three counts per phase, where the engine holds them.
  await page.locator('[data-testid="effort-routing-byphase-trigger"]').first().click();
  await page.waitForTimeout(1_200);
  const byPhase = await page.evaluate(() => {
    const t = document.querySelector('[data-testid="effort-routing-byphase"]');
    if (!t) return null;
    return {
      headers: Array.from(t.querySelectorAll('thead th')).map(h => (h.textContent || '').trim().toLowerCase()),
      rows: t.querySelectorAll('tbody tr').length,
    };
  });
  console.log(`effort-routing-byphase: ${JSON.stringify(byPhase)}`);
  expect(byPhase, 'the by-command-phase disclosure did not open').not.toBeNull();
  for (const col of ['command phase', 'routed', 'drifted', 'unknown']) {
    expect(byPhase!.headers, `the by-phase routing table has no "${col}" column`).toContain(col);
  }
  expect(byPhase!.rows, 'the by-phase routing table rendered no rows').toBeGreaterThan(0);
});

/* ─────────────────────── cross-cutting refusals (REQ-NFR-013, BRD-168) ─────────────────────── */

test('REQ-UI-045..049 no unmeasured figure reaches /effort as a zero, and nothing is grouped by actor', async ({ page }) => {
  test.setTimeout(240_000);
  await gotoScreen(page, '/effort');

  // ADR-007 — the per-run token figure is the page's one Figure. Below MIN_N it must refuse to be a
  // number. Open every phase so both sides of the floor are exercised on today's data.
  const keys = await phaseKeys(page);
  for (const key of keys) await openPhase(page, key);

  const means = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid^="tokens-mean-"]'))
      .map(e => ({ id: e.getAttribute('data-testid') || '', text: (e.textContent || '').trim() })));
  console.log(`per-run token figures: ${JSON.stringify(means)}`);
  expect(means.length, 'no phase rendered a per-run token figure').toBeGreaterThan(0);

  let below = 0, numeric = 0;
  for (const m of means) {
    expect(m.text.length, `${m.id} rendered blank`).toBeGreaterThan(0);
    expect(m.text, `${m.id} rendered a bare zero — an unsupported figure is never 0`).not.toBe('0');
    if (INSUFFICIENT.test(m.text)) { below++; continue; }
    if (m.text === EM_DASH) { console.log(`BRANCH: ${m.id} is Figure.NotApplicable ("${EM_DASH}")`); continue; }
    expect(m.text.replace(/,/g, ''), `${m.id} = "${m.text}" is neither a number nor "insufficient data (n=…)"`)
      .toMatch(/^[\d.]+$/);
    numeric++;
  }
  console.log(`BRANCH: ${below} phases are below MIN_N and print "insufficient data (n=…)", ${numeric} print a number`);
  expect(below + numeric, 'no per-run figure took either branch').toBeGreaterThan(0);

  // A figure below the floor must never also be a number: the two forms are mutually exclusive.
  for (const m of means.filter(x => INSUFFICIENT.test(x.text))) {
    expect(m.text.replace(/insufficient data \(n=\d+\)/, '').trim(),
      `${m.id} prints "insufficient data" AND a number: "${m.text}"`).toBe('');
  }

  // BRD-160 / REQ-NFR-013 clause 7 — measured dollars only, and absence is a dash, never $0.00.
  const costs = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid^="cost-"]'))
      .map(e => ({ id: e.getAttribute('data-testid') || '', text: (e.textContent || '').trim() })));
  console.log(`measured spend cells: ${JSON.stringify(costs)}`);
  expect(costs.length, 'the measured-spend card rendered no cells').toBeGreaterThan(0);
  for (const c of costs) {
    expect(c.text === EM_DASH || /^\$/.test(c.text), `${c.id} reads "${c.text}" — expected a $ figure or "${EM_DASH}"`)
      .toBe(true);
    expect(c.text, `${c.id} reads $0.00 — an unmeasured harness never ran for free`).not.toBe('$0.00');
  }
  // `$0.00` DOES appear once on the page, inside the card footer that forbids it ("A dash is 'no
  // provider cost recorded', never $0.00 and never 'free'"). What must never happen is a $0.00 rendered
  // as a FIGURE, so every occurrence has to be the negated one.
  const body = (await page.locator('body').innerText()).replace(/\s+/g, ' ');
  const zeroDollars = (body.match(/\$0\.00/g) || []).length;
  const negated = (body.match(/never \$0\.00/g) || []).length;
  console.log(`"$0.00" occurrences on /effort: ${zeroDollars}, of which ${negated} are the standing refusal`);
  expect(zeroDollars - negated,
    `/effort renders "$0.00" as a figure — absent spend is a dash, never a zero dollar`).toBe(0);

  // BRD-169 — a budgeting screen carries no rate-card estimate at all.
  expect(/rate-card estimate/i.test(body), 'the standing "no rate-card estimate" statement is missing').toBe(true);
  expect(/there is no rate-card estimate anywhere on this page/i.test(body),
    '/effort must state that no rate-card estimate appears on it').toBe(true);

  // BRD-168 — nothing on this page is grouped by actor, and no control could produce such a grouping.
  const note = (await textOf(page, 'effort-actor-note')) || '';
  console.log(`effort-actor-note = "${note}"`);
  expect(note.toLowerCase(), 'effort-actor-note must state that no figure is grouped by actor')
    .toContain('grouped by');
  expect(note.toLowerCase(), 'effort-actor-note must name the actor field').toContain('actor');
  expect(note.toLowerCase(), 'effort-actor-note must deny any parameter, filter or toggle producing one')
    .toContain('no parameter, filter or toggle');

  const actorSurfaces = await page.evaluate(() => {
    const out: { where: string; text: string }[] = [];
    for (const th of Array.from(document.querySelectorAll('th'))) {
      if (/\bactor\b/i.test(th.textContent || '')) out.push({ where: 'th', text: (th.textContent || '').trim() });
    }
    for (const el of Array.from(document.querySelectorAll('[data-testid]'))) {
      const id = el.getAttribute('data-testid') || '';
      if (/actor/i.test(id) && id !== 'effort-actor-note') out.push({ where: 'testid', text: id });
    }
    for (const opt of Array.from(document.querySelectorAll('[role="option"], option'))) {
      if (/\bactor\b/i.test(opt.textContent || '')) out.push({ where: 'option', text: (opt.textContent || '').trim() });
    }
    return out;
  });
  expect(actorSurfaces, `/effort offers a grouping by actor: ${JSON.stringify(actorSurfaces)}`).toEqual([]);
});

/* ─────────────────────────────── REQ-UI-050 ─────────────────────────────── */

test('REQ-UI-050 the Playbook axis of /effort renders its own surface and states the state it is actually in', async ({ page }) => {
  test.setTimeout(240_000);
  try {
    await gotoScreen(page, '/effort');
    await switchFramework(page, 'Playbook');

    // The page shell, the standing axis note and the filters render on EVERY state — the repository
    // select is how a reader reaches a supported repository from an unsupported one.
    await expect(page.locator('[data-testid="effort-page"]').first(), '/effort vanished on the Playbook axis').toBeVisible();
    await expect(page.locator('[data-testid="framework-switch"]').first(), 'the Framework switch left the header').toBeVisible();
    for (const id of ['playbook-axis-note', 'pb-effort-filters', 'pb-effort-filter-repository', 'pb-effort-filter-scope']) {
      const r = await renderCheck(page, id);
      console.log(`${id}: ${r.verdict} — ${r.detail}`);
      expect(r.verdict, `${id}: ${r.detail}`).toBe('RENDERS');
    }
    expect(await exists(page, 'pb-effort-error'), 'the Playbook effort surface rendered its error alert').toBe(false);

    // The five states are always on the page, and exactly one of them is named as active.
    const stateNames = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid^="pb-effort-state-"]'))
        .map(e => (e.getAttribute('data-testid') || '').replace(/^pb-effort-state-/, ''))
        .filter(n => n !== 'active'));
    const active = (await textOf(page, 'pb-effort-state-active')) || '';
    console.log(`pb-effort states listed: ${JSON.stringify(stateNames)} · active = "${active}"`);
    expect(stateNames.length, 'the five empty/unsupported states are not all listed').toBeGreaterThanOrEqual(5);
    expect(stateNames, `the active state "${active}" is not one of the listed states`).toContain(active);

    const populated = await exists(page, 'pb-effort-summary');

    if (!populated) {
      // ── BRD-163 — today's state. The numeric body is withheld on purpose, and the page says which
      //    absence it is. This branch asserts the withholding STRUCTURALLY, so a regression that
      //    quietly renders zeroed tiles fails here rather than reading as an improvement.
      const unsupported = await exists(page, 'pb-effort-unsupported');
      const empty = await exists(page, 'pb-effort-empty');
      console.log(`BRANCH REQ-UI-050: no phase executions to render — state "${active}" ` +
        `(unsupported banner=${unsupported}, no-bundle empty=${empty})`);

      expect(unsupported || empty,
        `the Playbook effort surface renders neither the BRD-163 unsupported banner nor the no-bundle empty ` +
        `state, yet has no numeric body either — the reader is told nothing`).toBe(true);

      if (unsupported) {
        const text = (await textOf(page, 'pb-effort-unsupported')) || '';
        console.log(`pb-effort-unsupported = "${text.slice(0, 260)}"`);
        expect(text.toLowerCase(), 'the banner must say phase effort telemetry is unsupported for this harness')
          .toContain('unsupported for this harness');
        expect(text.toLowerCase(), 'the banner must call this a data gap').toContain('data gap');
        expect(text.toLowerCase(),
          'the banner must distinguish a data gap from a harness that ran and spent nothing')
          .toMatch(/ran and spent nothing/);
        expect(text.toLowerCase(), 'the banner must state that the gap is never rendered as 0')
          .toMatch(/never rendered as 0|never as 0/);
        expect(text.toLowerCase(), 'the banner must state that the gap is never an empty measured figure')
          .toContain('empty measured figure');
      } else {
        const text = (await textOf(page, 'pb-effort-empty')) || '';
        console.log(`pb-effort-empty = "${text.slice(0, 260)}"`);
        expect(text.toLowerCase(), 'the no-bundle empty state must say no bundle has been imported')
          .toContain('bundle');
      }

      // No zero, and no empty measured figure: the whole numeric body must be absent, not zeroed.
      const withheld = ['pb-effort-summary', 'pb-effort-tile-completed', 'pb-effort-tile-wallclock',
        'pb-effort-tile-active', 'pb-effort-tile-fanout', 'pb-effort-tile-tokens',
        'pb-effort-tile-incomplete', 'pb-effort-tile-quarantined',
        'pb-cost-measured', 'pb-cost-measured-value', 'pb-cost-estimate',
        'pb-effort-charts', 'pb-effort-tables', 'pb-effort-executions', 'pb-executions-table'];
      const leaked: string[] = [];
      for (const id of withheld) if (await exists(page, id)) leaked.push(id);
      expect(leaked,
        `the numeric body is rendered while the surface reports no executions: ${JSON.stringify(leaked)} — ` +
        `a data gap must never be published as a set of zeroes`).toEqual([]);

      // Nothing on the surface reads as a bare figure: every element that could be one is either absent
      // or carries words. A stray "0" or an empty value box is exactly the defect BRD-163 forbids.
      const bareFigures = await page.evaluate(() => {
        const root = document.querySelector('[data-testid="pb-effort-filters"]')?.parentElement;
        if (!root) return [] as { id: string; text: string }[];
        const out: { id: string; text: string }[] = [];
        for (const el of Array.from(root.querySelectorAll('[data-testid^="pb-"]'))) {
          if (el.querySelector('[data-testid]')) continue; // containers are judged by their leaves
          const text = ((el as HTMLElement).innerText || '').trim();
          if (text === '0' || text === '0.0' || text === '$0.00' || text === '') {
            out.push({ id: el.getAttribute('data-testid') || '', text });
          }
        }
        return out;
      });
      expect(bareFigures,
        `the unsupported Playbook effort surface renders a zero or an empty measured figure: ${JSON.stringify(bareFigures)}`)
        .toEqual([]);
      return;
    }

    // ── The populated branch. Guarded, not omitted: the moment a Playbook repo with a normalized
    //    phase producer is connected, these assertions start grading it instead of silently passing.
    console.log('BRANCH REQ-UI-050: the Playbook effort surface is POPULATED — grading the numeric body');

    const tiles = ['pb-effort-tile-completed', 'pb-effort-tile-wallclock', 'pb-effort-tile-active',
      'pb-effort-tile-fanout', 'pb-effort-tile-tokens', 'pb-cost-measured',
      'pb-effort-tile-incomplete', 'pb-effort-tile-quarantined'];
    for (const id of tiles) {
      const r = await renderCheck(page, id);
      const text = (await textOf(page, id)) || '';
      console.log(`${id}: ${r.verdict} — "${text.slice(0, 140)}"`);
      expect(r.verdict, `${id}: ${r.detail}`).toBe('RENDERS');
      // BRD-161 — every figure carries its own `n of N eligible` beneath it, never in a page footer.
      expect(text, `${id} does not carry its own eligible cohort`).toMatch(/\d[\d,]* of \d[\d,]*/);
    }

    // BRD-160 — measured dollars and the rate-card estimate are separate cards sharing no total.
    const measuredBox = await (await testid(page, 'pb-cost-measured')).boundingBox();
    const estimate = await testid(page, 'pb-cost-estimate');
    await expect(estimate, 'the rate-card estimate card is missing').toBeVisible();
    const estimateBox = await estimate.boundingBox();
    expect(measuredBox && estimateBox && estimateBox.y >= measuredBox.y + measuredBox.height - 4,
      'the rate-card estimate shares a row with the measured cost tile').toBe(true);
    const estimateValue = (await textOf(page, 'pb-cost-estimate-value')) || '';
    console.log(`pb-cost-estimate-value = "${estimateValue}"`);
    expect(/^\$/.test(estimateValue),
      `pb-cost-estimate-value reads "${estimateValue}" — REQ-NFR-023 forbids a rate-card dollar on an effort surface`)
      .toBe(false);

    // BRD-156 / ADR-027 — the three timing concepts, stated where the timing figures are read.
    const timing = ((await textOf(page, 'pb-effort-timing-note')) || '').toLowerCase();
    expect(timing, 'the timing note must deny that active time is human effort').toContain('human effort');
    expect(timing, 'the timing note must say the two interval sums are never added').toContain('never added');

    for (const id of ['pb-effort-charts', 'pb-effort-tables', 'pb-effort-executions']) {
      const r = await renderCheck(page, id);
      console.log(`${id}: ${r.verdict} — ${r.detail}`);
      expect(r.verdict, `${id}: ${r.detail}`).toBe('RENDERS');
    }

    // Every comparative figure below MIN_N refuses to be a number.
    const pbFigures = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid^="pb-active-active-"], [data-testid^="pb-phase-elapsed-"]'))
        .map(e => ({ id: e.getAttribute('data-testid') || '', text: (e.textContent || '').trim() })));
    console.log(`pb comparative figures: ${JSON.stringify(pbFigures.slice(0, 12))}`);
    for (const f of pbFigures) {
      expect(f.text.length, `${f.id} rendered blank`).toBeGreaterThan(0);
      expect(f.text, `${f.id} rendered a bare zero`).not.toBe('0');
    }
  } finally {
    await gotoScreen(page, '/effort').catch(() => {});
    await switchFramework(page, 'TechieFlow').catch(() => {});
  }
});

/* ───────────────────── visual-truth gate, both axes, 1280 and 390 ───────────────────── */

for (const viewport of [DESKTOP, MOBILE]) {
  test(`REQ-UI-045..050 visual gate on both framework axes @${viewport.width}`, async ({ page }) => {
    test.setTimeout(240_000);
    try {
      // Sign-in and navigation happen at desktop: the shell's sidebar is `hidden md:flex`, so the
      // helper's wait-for-sidebar never resolves on a 390px viewport. The MEASUREMENT below happens at
      // the requested width, which is what the gate is about.
      await gotoScreen(page, '/effort');
      await page.setViewportSize(viewport);
      await page.waitForTimeout(1_000);

      const techieflow = await visualCheck(page, [...PAGE_IDS, ...SHELL_IDS], viewport.width);
      await page.screenshot({ path: `tests/.artifacts/effort/techieflow-${viewport.width}.png`, fullPage: true }).catch(() => {});
      expect(techieflow, `TechieFlow axis @${viewport.width}: ${techieflow.join(' | ')}`).toEqual([]);

      // The shell's page pane scrolls vertically on its own, so documentElement never reports the
      // sideways overflow the shared helper looks for. A band that overruns its column shows up here.
      const pane = await page.evaluate(() => {
        const p = document.querySelector('.tflens-page') as HTMLElement | null;
        return p ? p.scrollWidth - p.clientWidth : 0;
      });
      console.log(`TechieFlow pane overflow @${viewport.width}: ${pane}px`);
      expect(pane, `the page pane scrolls sideways by ${pane}px @${viewport.width}`).toBeLessThanOrEqual(2);

      await page.setViewportSize(DESKTOP);
      await switchFramework(page, 'Playbook');
      await page.setViewportSize(viewport);
      await page.waitForTimeout(1_000);

      const playbookIds = ['effort-page', 'effort-period', 'effort-period-label', 'effort-budget-note',
        'playbook-axis-note', 'pb-effort-filters', 'pb-effort-filter-repository', 'pb-effort-filter-reset',
        'pb-effort-filter-scope', 'pb-effort-unsupported', 'pb-effort-states', 'pb-effort-state-active',
        'pb-effort-summary', 'pb-cost-measured', 'pb-cost-estimate', 'pb-effort-executions',
        ...SHELL_IDS];
      const playbook = await visualCheck(page, playbookIds, viewport.width);
      await page.screenshot({ path: `tests/.artifacts/effort/playbook-${viewport.width}.png`, fullPage: true }).catch(() => {});
      expect(playbook, `Playbook axis @${viewport.width}: ${playbook.join(' | ')}`).toEqual([]);

      const pbPane = await page.evaluate(() => {
        const p = document.querySelector('.tflens-page') as HTMLElement | null;
        return p ? p.scrollWidth - p.clientWidth : 0;
      });
      console.log(`Playbook pane overflow @${viewport.width}: ${pbPane}px`);
      expect(pbPane, `the Playbook page pane scrolls sideways by ${pbPane}px @${viewport.width}`).toBeLessThanOrEqual(2);
    } finally {
      await page.setViewportSize(DESKTOP);
      await gotoScreen(page, '/effort').catch(() => {});
      await switchFramework(page, 'TechieFlow').catch(() => {});
    }
  });
}
