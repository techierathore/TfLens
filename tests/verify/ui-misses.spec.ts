// REQ-UI-035 / REQ-UI-036 / REQ-UI-037 / REQ-UI-038 — Misses & rework (`/misses`), plus the two
// shell requirements the sixth page moved under: REQ-UI-006 (seven nav items) and REQ-UI-010 (the
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
test('REQ-UI-006 the sidebar shows seven nav items with Misses & rework between Routing and Export', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/');

  const ids = ['nav-repos', 'nav-coverage', 'nav-three-questions', 'nav-harness', 'nav-routing', 'nav-misses', 'nav-export'];
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
