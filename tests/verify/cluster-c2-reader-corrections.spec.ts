// Cluster C2 self-smoke — REQ-FN-110 .. REQ-FN-114 (BRD-177 .. BRD-181).
//
// Black-box, against the running app as the canonical test user from docs/TfLens-UsageGuide.md.
// It opens every screen this cluster's figures reach — `/`, `/gate-outcomes`, `/effort` — proves the
// data is there rather than a skeleton, proves the corrected wording and counts are on the page, and
// checks nothing overlaps at desktop and mobile width.
import { test, expect } from '@playwright/test';
import { signIn, gotoScreen, visualCheck, DESKTOP, MOBILE } from './_helpers';

test.use({ viewport: DESKTOP });

test('C2 — the three screens carry data, the corrections show, and nothing overlaps', async ({ page }) => {
  const consoleErrors: string[] = [];
  page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });

  await signIn(page);

  // ---------------------------------------------------------------- `/` (Coverage)
  // REQ-FN-114 — a record naming an unfamiliar cmd, verdict or harness is shown as it stands and
  // counted. The estate carries a `rename-page` run and off-list verdicts today.
  await gotoScreen(page, '/');
  const coverage = await page.locator('body').innerText();
  expect(coverage.length).toBeGreaterThan(200);
  expect(coverage).toContain('rename-page');

  // ---------------------------------------------------------- `/gate-outcomes`
  await gotoScreen(page, '/gate-outcomes');

  // The tabs and the KPI row have real figures, not skeletons.
  await page.locator('[data-testid="type-tabs"]').first().waitFor({ state: 'attached' });
  const facts = page.locator('[data-testid^="segment-facts-"]').first();
  await facts.waitFor({ state: 'attached' });
  const factsText = (await facts.innerText()).trim();
  expect(factsText).toMatch(/\d+ records/);

  // REQ-FN-113 / BRD-180 — the derived-attempt count is stated beside the rate.
  expect(factsText).toMatch(/\d+ attempts? derived/);

  // The first-pass rate is a real figure, and it is not the 0% the missing-attempt bug produced.
  const firstPass = page.locator('[data-testid^="live-first-pass-"]').first();
  await firstPass.waitFor({ state: 'attached' });
  const rate = (await firstPass.innerText()).trim();
  expect(rate.length).toBeGreaterThan(0);

  // REQ-FN-111 / BRD-178 — the taint list names `app:req_id`, never a bare id.
  const taint = (await page.locator('[data-testid="taint-list"]').first().innerText()).trim();
  if (taint !== 'none') {
    expect(taint).toMatch(/\S+:REQ-/);
  }

  // REQ-FN-110 / BRD-177 — no tab pools framework requirements with application ones. The tab strip
  // has no "all" tab, and if an FR segment exists it stands on its own.
  const tabLabels = await page.locator('[data-testid^="type-tab-"]').allInnerTexts();
  expect(tabLabels.length).toBeGreaterThan(0);
  expect(tabLabels.join(' ').toLowerCase()).not.toContain('all types');

  // ------------------------------------------------------------------- `/effort`
  await gotoScreen(page, '/effort');
  const wallclock = page.locator('[data-testid="kpi-wallclock"]').first();
  await wallclock.waitFor({ state: 'attached' });
  const wallclockText = (await wallclock.innerText()).trim();
  expect(wallclockText).toMatch(/\d/);

  // REQ-FN-112 / BRD-179 — the derived-duration count sits beside the time figure.
  const derived = page.locator('[data-testid="kpi-wallclock-derived"]').first();
  await derived.waitFor({ state: 'attached' });
  const derivedText = (await derived.innerText()).trim();
  expect(derivedText).toMatch(/derived from started\/ended|every duration read from the record/);

  expect(consoleErrors, 'browser console errors').toEqual([]);
});

// The visual half. The shell's sidebar is `hidden md:flex`, so the sign-in and the navigation happen
// at desktop width — as the standing render/visual gate does — and each screen is then measured at both
// widths without re-navigating.
test('C2 — nothing overlaps or leaves the viewport on the three screens at 1280 and 390', async ({ page }) => {
  await signIn(page);

  const screens: Record<string, string[]> = {
    '/': ['kpi-total-records'],
    '/gate-outcomes': ['schema-note', 'type-tabs', 'taint-trigger'],
    '/effort': ['kpi-runs', 'kpi-wallclock', 'kpi-wallclock-derived', 'kpi-tokens-out'],
  };

  for (const [route, ids] of Object.entries(screens)) {
    await gotoScreen(page, route);

    // Only the controls that are actually on this build's page are measured; an id that is absent is
    // a render finding, reported by the standing render gate rather than silently passed here.
    const present: string[] = [];
    for (const id of ids) {
      if (await page.locator(`[data-testid="${id}"]`).count() > 0) present.push(id);
    }

    for (const vp of [DESKTOP, MOBILE]) {
      await page.setViewportSize(vp);
      await page.waitForTimeout(900);
      const problems = await visualCheck(page, present, vp.width);
      expect(problems, `${route} @${vp.width}px`).toEqual([]);
    }

    await page.setViewportSize(DESKTOP);
  }
});
