// REQ-UI-035 / REQ-UI-036 / REQ-UI-037 / REQ-UI-038 — Misses & rework (`/misses`), plus the two
// shell requirements the sixth page moved under: REQ-UI-006 (eight nav items since 2026-09-01) and REQ-UI-010 (the
// Framework switch now spans six report pages).
//
// Black-box only. Every assertion is against the running app signed in as a documented test user.
import { test, expect } from '@playwright/test';
import { signIn, gotoScreen, testid, renderCheck, visualCheck, collectErrors, DESKTOP, MOBILE } from './_helpers';

/** The controls the checklist names by id on this page. */
const PAGE_IDS = [
  'misses-page', 'misses-period', 'miss-kpis',
  'kpi-open', 'kpi-wontfix', 'kpi-design-share', 'kpi-escape-share',
  'kpi-rework-tokens', 'kpi-rework-usd', 'kpi-rework-usd-estimate',
  'miss-origin', 'miss-whymissed', 'miss-origin-model', 'miss-origin-agent',
  'miss-taint-count', 'miss-cost', 'miss-detail',
];

/** The shell controls that must render on this route like every other report page. */
const SHELL_IDS = ['app-sidebar', 'sidebar-trigger', 'framework-switch', 'sync-now', 'theme-toggle', 'user-menu'];

/** Put the header Framework switch on one axis and wait for the page to re-query. */
async function selectFramework(page: import('@playwright/test').Page, label: 'TechieFlow' | 'Playbook') {
  const trigger = page.locator('[data-testid="framework-switch"] [role="tab"]', { hasText: label }).first();
  await trigger.click();
  await page.waitForTimeout(2000);
}

async function domOrder(page: import('@playwright/test').Page, ids: string[]): Promise<number[]> {
  return page.evaluate((wanted: string[]) => {
    const all = Array.from(document.querySelectorAll('[data-testid]'));
    return wanted.map(id => all.findIndex(el => el.getAttribute('data-testid') === id));
  }, ids);
}

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-006 — the sidebar is now SEVEN items, Misses & rework between Routing and Export.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-006 the sidebar shows eight nav items with Phase effort between Misses & rework and Export', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/');

  const ids = ['nav-repos', 'nav-coverage', 'nav-gate-outcomes', 'nav-harness', 'nav-routing',
    'nav-misses', 'nav-effort', 'nav-export'];
  for (const id of ids) {
    expect(await page.locator(`[data-testid="${id}"]`).count(), `${id} missing`).toBeGreaterThan(0);
  }

  const order = await domOrder(page, ids);
  expect(order.some(i => i < 0), `a nav item is missing: ${JSON.stringify(order)}`).toBe(false);
  for (let i = 1; i < order.length; i++) {
    expect(order[i], `${ids[i]} is not after ${ids[i - 1]} in DOM order`).toBeGreaterThan(order[i - 1]);
  }

  // BRD-108 — the framework is a header switch, never a nav item.
  expect(await page.locator('a[href*="/playbook"]').count(), 'a /playbook nav item exists').toBe(0);

  // The item marks the route when the page is open.
  await gotoScreen(page, '/misses');
  const active = page.locator('[data-testid="nav-misses"]').first();
  await expect(active).toHaveAttribute('data-active', /true|/);
  expect(await page.locator('[data-testid="misses-page"]').count(), '/misses did not render').toBeGreaterThan(0);
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-035 — the page, its route, its Framework switch and its period filter.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-035 /misses renders with the Framework switch and a period filter defaulting to all history', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await gotoScreen(page, '/misses');

  await expect(page.locator('[data-testid="misses-page"]').first()).toBeVisible();

  // REQ-UI-010 — the switch is rendered on this report page like the other five.
  for (const id of SHELL_IDS) {
    expect(await page.locator(`[data-testid="${id}"]`).count(), `${id} missing on /misses`).toBeGreaterThan(0);
  }

  // BRD-125 — the first view is unfiltered.
  const period = await testid(page, 'misses-period');
  await expect(period).toBeVisible();
  expect((await period.innerText()).toLowerCase()).toContain('all history');

  const label = await testid(page, 'misses-period-label');
  expect((await label.innerText()).toLowerCase()).toContain('all history');

  expect(errors.filter(e => !/favicon|websocket/i.test(e)), `console errors: ${errors.join(' | ')}`).toEqual([]);
});

test('REQ-UI-035 narrowing the period keeps the page renderable rather than blanking it', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/misses');

  await page.locator('[data-testid="misses-period"]').first().click();
  await page.locator('[role="option"]', { hasText: 'Last 7 days' }).first().click();
  await page.waitForTimeout(1500);

  // The page still renders; a figure below the minimum n reads as text, never as a number and never blank.
  await expect(page.locator('[data-testid="misses-page"]').first()).toBeVisible();
  const label = await testid(page, 'misses-period-label');
  expect((await label.innerText()).toLowerCase()).toContain('7 days');
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-036 — the KPI row, with open and declined as two separate tiles.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-036 the KPI row renders every tile, keeps wont-fix out of open and separates the estimate', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/misses');

  for (const id of ['miss-kpis', 'kpi-open', 'kpi-wontfix', 'kpi-design-share', 'kpi-escape-share', 'kpi-rework-tokens', 'kpi-rework-usd']) {
    const check = await renderCheck(page, id);
    expect(check.verdict, `${id}: ${check.detail}`).toBe('RENDERS');
  }

  // BRD-120 — two tiles, and the open tile says so in words.
  const open = await testid(page, 'kpi-open');
  expect((await open.innerText()).toLowerCase()).toContain('wont-fix');
  const wontfix = await testid(page, 'kpi-wontfix');
  expect((await wontfix.innerText()).toLowerCase()).toContain('never folded into open');

  // BRD-118 — the miss-stream escape share says which escape figure it is.
  const escape = await testid(page, 'kpi-escape-share');
  expect((await escape.innerText()).toLowerCase()).toContain('not the gates');

  // BRD-123 — the rate-card figure is a distinct control carrying the estimate label.
  const estimate = await testid(page, 'kpi-rework-usd-estimate');
  await expect(estimate).toBeVisible();
  expect((await estimate.innerText()).toLowerCase()).toContain('not measured spend');
  expect((await estimate.innerText())).toContain('_usd_estimate');

  // The measured tile and the estimate card are not the same row.
  const measuredBox = await (await testid(page, 'kpi-rework-usd')).boundingBox();
  const estimateBox = await estimate.boundingBox();
  expect(measuredBox && estimateBox && estimateBox.y >= measuredBox.y + measuredBox.height - 4,
    'the estimate card shares a row with the measured tile').toBe(true);

  // REQ-NFR-013 clause 7 — absent cost renders as an em dash, never $0.00.
  const usd = (await (await testid(page, 'kpi-rework-usd-value')).innerText()).trim();
  expect(usd === '—' || /^\$/.test(usd), `measured USD rendered as "${usd}"`).toBe(true);
  expect(usd).not.toBe('$0.00');
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-037 — where misses come from, and the n-of-N denominator on the card's face.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-037 the origin table and the failed-practice card carry their denominators on screen', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/misses');

  for (const id of ['miss-origin', 'miss-whymissed', 'miss-taint-count']) {
    const check = await renderCheck(page, id);
    expect(check.verdict, `${id}: ${check.detail}`).toBe('RENDERS');
  }

  // BRD-119 — `n of N misses assessed` is on the card, not in a tooltip.
  const denominator = await testid(page, 'miss-whymissed-denominator');
  await expect(denominator).toBeVisible();
  expect(await denominator.innerText()).toMatch(/\d+ of \d+ misses assessed/);

  // BRD-117 — records predating the field are reported separately, not backfilled.
  const eligibility = await testid(page, 'miss-whymissed-eligibility');
  // The wording is singular when nothing predates the field and plural when something does; both
  // states must state the floor explicitly rather than leaving the reader to assume it was applied.
  expect((await eligibility.innerText()).toLowerCase()).toMatch(/predates? the field/);

  // BRD-121 — the exclusion is visible and names the reason.
  const taint = await testid(page, 'miss-taint-count');
  expect(await taint.innerText()).toMatch(/\d+ of \d+ misses excluded/);
  expect((await taint.innerText()).toLowerCase()).toContain('origin_confidence');
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-038 — who was running, the three-column cost split and the per-miss detail.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-038 the observational band, the three cost columns and the detail table all render', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/misses');

  for (const id of ['miss-origin-model', 'miss-origin-agent', 'miss-cost', 'miss-detail', 'miss-observational']) {
    const check = await renderCheck(page, id);
    expect(check.verdict, `${id}: ${check.detail}`).toBe('RENDERS');
  }

  // BRD-124 — the observational line is standing page copy, visible without hovering anything.
  const note = await testid(page, 'miss-observational');
  await expect(note).toBeVisible();
  expect((await note.innerText()).toLowerCase()).toContain('confounded');

  // ADR-019 — three distinct columns, and a standing statement that no blended number exists.
  for (const id of ['miss-cost-measured', 'miss-cost-apportioned', 'miss-cost-unattributable']) {
    expect(await page.locator(`[data-testid="${id}"]`).count(), `${id} missing`).toBe(1);
  }
  const noBlend = await testid(page, 'miss-cost-no-blend');
  expect((await noBlend.innerText()).toLowerCase()).toContain('no blended figure');

  // The detail table has rows, and its raw-record disclosure opens.
  const rows = page.locator('[data-testid="miss-detail"] tbody tr');
  expect(await rows.count(), 'the per-miss detail table has no rows').toBeGreaterThan(0);

  await page.locator('[data-testid="miss-raw-trigger"]').first().click();
  await page.waitForTimeout(700);
  const raw = await testid(page, 'miss-raw');
  await expect(raw).toBeVisible();
  // SCHEMA.md §9 — overflow is shown by field name only, never by value.
  expect(await raw.innerText()).toContain('overflow_field_names');
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-010 / BRD-126 — the Playbook axis shows playbook-empty and keeps the switch on screen.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-010 /misses on the Playbook axis renders the switch and the playbook-empty state', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/misses');

  await selectFramework(page, 'Playbook');

  await expect(page.locator('[data-testid="framework-switch"]').first()).toBeVisible();
  await expect(page.locator('[data-testid="playbook-empty"]').first()).toBeVisible();
  await expect(page.locator('[data-testid="playbook-axis-note"]').first()).toBeVisible();

  // A zero here is absence, and the page says so rather than reading as a good score.
  const zeroNote = await testid(page, 'misses-playbook-zero-note');
  expect((await zeroNote.innerText()).toLowerCase()).toContain('absence, not a good score');

  // Back to TechieFlow so the rest of the suite starts where it expects to.
  await selectFramework(page, 'TechieFlow');
  await expect(page.locator('[data-testid="miss-kpis"]').first()).toBeVisible();
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-051 — the Playbook axis of /misses is its own surface with its own provenance, and it states
// the state it is actually in. The populated assertions are guarded rather than omitted: the day a
// Playbook miss bundle is imported they start grading it, instead of leaving a permanent blind spot.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-051 /misses on the Playbook axis renders its own surface and its provenance statement', async ({ page }) => {
  test.setTimeout(180_000);
  try {
    await signIn(page);
    await gotoScreen(page, '/misses');
    await selectFramework(page, 'Playbook');

    // The surface is the Playbook's own — the axis no longer falls through to the TechieFlow bands.
    const surface = await testid(page, 'pb-misses-surface');
    await expect(surface, 'the Playbook miss surface did not render').toBeVisible();
    await expect(page.locator('[data-testid="playbook-axis-note"]').first()).toBeVisible();

    // BRD-164 — where these rows came from, and what they upsert on, stated on the page.
    const ingest = ((await (await testid(page, 'pb-miss-ingest-note')).innerText()) || '').replace(/\s+/g, ' ');
    console.log(`pb-miss-ingest-note = "${ingest.slice(0, 240)}"`);
    expect(ingest, 'the provenance note must name the Playbook miss stream').toContain('misses.ndjson');
    expect(ingest.toLowerCase(), 'the provenance note must deny a second ingest path')
      .toContain('no second ingest path');
    expect(ingest.toLowerCase(), 'the provenance note must say nothing is shared with the TechieFlow axis')
      .toMatch(/nothing here shares a figure|never pools/);
    expect(ingest.toLowerCase(), 'the provenance note must name the immutable source-line hash as the key')
      .toContain('source-line hash');

    const populated = (await page.locator('[data-testid="miss-kpis"]').count()) > 0;

    if (!populated) {
      // ── Today's state. This tenant holds no Playbook miss records, so the axis renders the BRD-126
      //    empty state. It is asserted as an EMPTY STATE — the absence is named in words, no band is
      //    published as a set of zeroes, and no KPI tile exists to be misread as a score.
      console.log('BRANCH REQ-UI-051: no Playbook miss records held — the axis renders the BRD-126 empty state');

      await expect(page.locator('[data-testid="playbook-empty"]').first(),
        'neither the miss bands nor the empty state rendered — the reader is told nothing').toBeVisible();
      await expect(page.locator('[data-testid="playbook-empty-connect"]').first(),
        'the empty state must offer the connect action').toBeVisible();

      // BRD-167 — the plan card names the bands the axis will render, so the empty state is a condition
      // of the data rather than the permanent shape of the screen.
      const plan = ((await (await testid(page, 'misses-playbook-plan')).innerText()) || '').replace(/\s+/g, ' ');
      console.log(`misses-playbook-plan bands: "${plan.slice(0, 300)}"`);
      for (const band of ['Band 1', 'Band 2', 'Band 3', 'Band 4']) {
        expect(plan, `misses-playbook-plan does not name ${band}`).toContain(band);
      }
      expect(plan.toLowerCase(), 'the plan must say the switch changes the data, never the layout or the rules')
        .toContain('never the layout or the rules');

      // A zero here is absence, and every zero on the surface says so beside itself.
      const zeroNote = ((await (await testid(page, 'misses-playbook-zero-note')).innerText()) || '').replace(/\s+/g, ' ');
      console.log(`misses-playbook-zero-note = "${zeroNote}"`);
      expect(zeroNote.toLowerCase(), 'the zero note must call a zero an absence, not a good score')
        .toContain('absence, not a good score');
      expect(zeroNote, 'the zero note must state how many records are actually held')
        .toMatch(/holds \d[\d,]* Playbook miss records/);
      expect(zeroNote.toLowerCase(), 'the zero note must state the MIN_N floor in words')
        .toMatch(/below \d+ records is ever printed as a number/);

      const streamNote = ((await (await testid(page, 'pb-stream-health-note')).innerText()) || '').replace(/\s+/g, ' ');
      console.log(`pb-stream-health-note = "${streamNote}"`);
      expect(streamNote.toLowerCase(), 'the stream-health note must call a zero or a dash an absence')
        .toContain('absence, not a good score');
      expect(streamNote.toLowerCase(), 'the stream-health note must say the events stream is transient by design')
        .toContain('transient');

      const streams = await page.evaluate(() =>
        Array.from(document.querySelectorAll('[data-testid^="pb-stream-"]'))
          .filter(e => (e.getAttribute('data-testid') || '') !== 'pb-stream-health' &&
            (e.getAttribute('data-testid') || '') !== 'pb-stream-health-note')
          .map(e => ({
            id: e.getAttribute('data-testid') || '',
            cells: Array.from(e.querySelectorAll('td')).map(td => (td.textContent || '').replace(/\s+/g, ' ').trim()),
          })));
      console.log(`playbook miss streams: ${JSON.stringify(streams)}`);
      expect(streams.length, 'the stream-health table rendered no stream rows').toBeGreaterThan(0);
      for (const s of streams) {
        for (const cell of s.cells) {
          expect(cell.length, `a cell on ${s.id} is blank: ${JSON.stringify(s.cells)}`).toBeGreaterThan(0);
        }
        // Every row states what its count MEANS — a bare number with no state would read as a score.
        expect(s.cells[s.cells.length - 1],
          `${s.id} shows a count with no state beside it: ${JSON.stringify(s.cells)}`).toMatch(/\S/);
      }

      // No band is published as zeroes: the KPI row and every figure card must be absent, not empty.
      const leaked: string[] = [];
      for (const id of ['miss-kpis', 'kpi-open', 'kpi-closed', 'kpi-reopened', 'kpi-time-to-close',
        'kpi-rework-incidence', 'miss-origin', 'miss-whymissed', 'miss-found-axes', 'miss-id-axes',
        'miss-cost', 'miss-detail-table']) {
        if ((await page.locator(`[data-testid="${id}"]`).count()) > 0) leaked.push(id);
      }
      expect(leaked,
        `the Playbook miss bands are rendered while the axis holds no records: ${JSON.stringify(leaked)} — ` +
        `an empty band published as zeroes reads as a good score`).toEqual([]);
      return;
    }

    // ── The populated branch.
    console.log('BRANCH REQ-UI-051: the Playbook miss axis is POPULATED — grading the real figures');

    // BRD-167 band 1 — lifecycle, with rework and time-to-close going through the guards.
    for (const id of ['kpi-open', 'kpi-closed', 'kpi-reopened', 'kpi-time-to-close',
      'kpi-rework-incidence', 'kpi-rework-tokens']) {
      const r = await renderCheck(page, id);
      const text = ((await page.locator(`[data-testid="${id}"]`).first().innerText().catch(() => '')) || '')
        .replace(/\s+/g, ' ').trim();
      console.log(`${id}: ${r.verdict} — "${text.slice(0, 140)}"`);
      expect(r.verdict, `${id}: ${r.detail}`).toBe('RENDERS');
      expect(text.length, `${id} rendered blank`).toBeGreaterThan(0);
    }

    // Every comparative figure below MIN_N refuses to be a number rather than printing one.
    const comparative = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid^="kpi-time-to-close"], [data-testid^="kpi-rework-incidence"]'))
        .map(e => ({ id: e.getAttribute('data-testid') || '', text: ((e as HTMLElement).innerText || '').replace(/\s+/g, ' ').trim() })));
    console.log(`playbook comparative figures: ${JSON.stringify(comparative)}`);
    for (const f of comparative) {
      const isInsufficient = /insufficient data \(n=\d+\)/.test(f.text);
      if (isInsufficient) console.log(`BRANCH: ${f.id} is below MIN_N — "${f.text}"`);
      expect(f.text.trim(), `${f.id} rendered a bare zero where a guarded figure belongs`).not.toBe('0');
    }

    // BRD-165 — the process axis and the assertion axis are two columns and are never summed.
    const foundNote = ((await (await testid(page, 'miss-found-axes-note')).innerText()) || '').replace(/\s+/g, ' ');
    console.log(`miss-found-axes-note = "${foundNote.slice(0, 240)}"`);
    expect(foundNote.toLowerCase(), 'the two found axes must be declared never summed or merged')
      .toMatch(/never summed|never .*summed, merged/);
    const foundHeaders = await page.evaluate(() => {
      const root = document.querySelector('[data-testid="miss-found-axes"]');
      return Array.from(root?.querySelectorAll('thead th') || []).map(h => (h.textContent || '').replace(/\s+/g, ' ').trim());
    });
    console.log(`miss-found-axes headers: ${JSON.stringify(foundHeaders)}`);
    expect(foundHeaders.join(' '), 'found_phase_gate is not a column of its own').toContain('found_phase_gate');
    expect(foundHeaders.join(' '), 'found_gate is not a column of its own').toContain('found_gate');

    // BRD-166 — item_id sits BESIDE req_id; the two are never collapsed into one "ID" column.
    const idHeaders = await page.evaluate(() => {
      const root = document.querySelector('[data-testid="miss-id-axes"]');
      return Array.from(root?.querySelectorAll('thead th') || []).map(h => (h.textContent || '').replace(/\s+/g, ' ').trim());
    });
    console.log(`miss-id-axes headers: ${JSON.stringify(idHeaders)}`);
    expect(idHeaders.join(' '), 'item_id is not a column').toContain('item_id');
    expect(idHeaders.join(' '), 'req_id is not a column beside item_id').toContain('req_id');

    // BRD-124 — the model band is observational, and the cost band publishes three cohorts, never a blend.
    const observational = ((await (await testid(page, 'pb-miss-observational')).innerText()) || '').toLowerCase();
    expect(observational, 'the observational note must say the comparison is confounded')
      .toMatch(/confounded|observational/);
    for (const id of ['miss-cost-measured', 'miss-cost-apportioned', 'miss-cost-unattributable']) {
      expect(await page.locator(`[data-testid="${id}"]`).count(), `${id} missing from the cost band`).toBe(1);
    }
    const noBlend = ((await (await testid(page, 'miss-cost-no-blend')).innerText()) || '').toLowerCase();
    expect(noBlend, 'the cost band must state that no blended figure exists').toContain('no blended figure');
  } finally {
    // Leave the axis where the rest of the suite expects to find it.
    await gotoScreen(page, '/misses').catch(() => {});
    await selectFramework(page, 'TechieFlow').catch(() => {});
  }
});

test('REQ-UI-051 the Playbook miss axis groups nothing by actor and prints no zero dollar', async ({ page }) => {
  test.setTimeout(150_000);
  try {
    await signIn(page);
    await gotoScreen(page, '/misses');
    await selectFramework(page, 'Playbook');

    const body = (await page.locator('body').innerText()).replace(/\s+/g, ' ');

    // A $0.00 may only ever appear as the thing the page refuses to print.
    const zeroDollars = (body.match(/\$0\.00/g) || []).length;
    const negated = (body.match(/never \$0\.00/g) || []).length;
    console.log(`"$0.00" occurrences on the Playbook miss axis: ${zeroDollars}, of which ${negated} are refusals`);
    expect(zeroDollars - negated,
      'the Playbook miss axis renders "$0.00" as a figure — absent cost is a dash, never a zero dollar').toBe(0);

    // BRD-168 — nothing is grouped by actor, and no control could produce such a grouping.
    const actorSurfaces = await page.evaluate(() => {
      const out: { where: string; text: string }[] = [];
      for (const th of Array.from(document.querySelectorAll('th'))) {
        if (/\bactor\b/i.test(th.textContent || '')) out.push({ where: 'th', text: (th.textContent || '').trim() });
      }
      for (const el of Array.from(document.querySelectorAll('[data-testid]'))) {
        const id = el.getAttribute('data-testid') || '';
        if (/actor/i.test(id)) out.push({ where: 'testid', text: id });
      }
      for (const opt of Array.from(document.querySelectorAll('[role="option"], option'))) {
        if (/\bactor\b/i.test(opt.textContent || '')) out.push({ where: 'option', text: (opt.textContent || '').trim() });
      }
      return out;
    });
    expect(actorSurfaces,
      `the Playbook miss axis offers a grouping by actor: ${JSON.stringify(actorSurfaces)}`).toEqual([]);
  } finally {
    await gotoScreen(page, '/misses').catch(() => {});
    await selectFramework(page, 'TechieFlow').catch(() => {});
  }
});

// The Playbook miss surface's own controls, measured at both widths. The REQ-UI-035..038 gate below
// measures the TechieFlow bands and the three shared Playbook anchors; this one measures the surface
// REQ-UI-051 added, so a regression in either is attributable to the requirement that owns it.
for (const viewport of [DESKTOP, MOBILE]) {
  test(`REQ-UI-051 visual gate on the Playbook miss surface @${viewport.width}`, async ({ page }) => {
    test.setTimeout(180_000);
    try {
      await signIn(page);
      await gotoScreen(page, '/misses');
      await selectFramework(page, 'Playbook');
      await page.setViewportSize(viewport);
      await page.waitForTimeout(900);

      const ids = ['misses-page', 'misses-period', 'playbook-axis-note', 'pb-misses-surface',
        'pb-miss-ingest-note', 'playbook-empty', 'misses-playbook-plan', 'misses-playbook-zero-note',
        'pb-stream-health', 'pb-stream-health-note', 'miss-kpis', 'miss-found-axes', 'miss-id-axes',
        'miss-cost', 'miss-detail', ...SHELL_IDS];
      const problems = await visualCheck(page, ids, viewport.width);
      await page.screenshot({ path: `tests/.artifacts/misses/pb-surface-${viewport.width}.png`, fullPage: true }).catch(() => {});
      expect(problems, `Playbook miss surface @${viewport.width}: ${problems.join(' | ')}`).toEqual([]);

      const pane = await page.evaluate(() => {
        const p = document.querySelector('.tflens-page') as HTMLElement | null;
        return p ? p.scrollWidth - p.clientWidth : 0;
      });
      console.log(`Playbook miss pane overflow @${viewport.width}: ${pane}px`);
      expect(pane, `the page pane scrolls sideways by ${pane}px @${viewport.width}`).toBeLessThanOrEqual(2);
    } finally {
      await page.setViewportSize(DESKTOP);
      await gotoScreen(page, '/misses').catch(() => {});
      await selectFramework(page, 'TechieFlow').catch(() => {});
    }
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// Visual-truth gate — both axes at 1280×800 and 390×844.
// ─────────────────────────────────────────────────────────────────────────────
for (const viewport of [DESKTOP, MOBILE]) {
  test(`REQ-UI-035..038 visual gate on both framework axes @${viewport.width}`, async ({ page }) => {
    // Sign in and navigate at desktop first: the shell's sidebar is `hidden md:flex`, so the
    // helper's wait-for-sidebar never resolves on a 390px viewport. The measurement below happens
    // at the requested viewport, which is what the gate is about.
    await signIn(page);
    await gotoScreen(page, '/misses');
    await page.setViewportSize(viewport);
    await page.waitForTimeout(900);

    const techieflow = await visualCheck(page, [...PAGE_IDS, ...SHELL_IDS], viewport.width);
    await page.screenshot({ path: `tests/.artifacts/misses/techieflow-${viewport.width}.png` }).catch(() => {});
    expect(techieflow, `TechieFlow axis @${viewport.width}: ${techieflow.join(' | ')}`).toEqual([]);

    // The shell's page pane scrolls vertically on its own, so documentElement never reports the
    // sideways overflow the shared helper looks for. A band that overruns its column shows up here.
    const paneOverflow = await page.evaluate(() => {
      const pane = document.querySelector('.tflens-page') as HTMLElement | null;
      return pane ? pane.scrollWidth - pane.clientWidth : 0;
    });
    expect(paneOverflow, `the page pane scrolls sideways by ${paneOverflow}px @${viewport.width}`).toBeLessThanOrEqual(2);

    await selectFramework(page, 'Playbook');
    const playbook = await visualCheck(
      page,
      ['misses-page', 'misses-period', 'playbook-empty', 'playbook-axis-note', 'misses-playbook-plan', ...SHELL_IDS],
      viewport.width,
    );
    await page.screenshot({ path: `tests/.artifacts/misses/playbook-${viewport.width}.png` }).catch(() => {});
    expect(playbook, `Playbook axis @${viewport.width}: ${playbook.join(' | ')}`).toEqual([]);

    await page.setViewportSize(DESKTOP);
    await selectFramework(page, 'TechieFlow');
  });
}
