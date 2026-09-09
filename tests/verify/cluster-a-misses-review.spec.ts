// Cluster A smoke — REQ-FN-067, REQ-FN-070, REQ-FN-072..REQ-FN-076.
// Black-box: connects a real repository through /repos, syncs it, and reads the pages the rows name.
// Nothing here touches application source or seeds a fixture into the database.
import { test, expect } from '@playwright/test';
import { signIn, gotoScreen, DESKTOP } from './_helpers';

const REPO = 'techierathore/TfLens';
const SHOTS = 'tests/.artifacts/clusterA-smoke';

test.describe.configure({ mode: 'serial' });

test('connect and sync a repository whose miss stream carries a review record', async ({ page }) => {
  test.setTimeout(300_000);
  await page.setViewportSize(DESKTOP);
  await signIn(page);

  await gotoScreen(page, '/repos');

  const alreadyThere = await page.locator(`[data-testid="repo-sync-TfLens"]`).count();
  if (alreadyThere === 0) {
    await page.click('[data-testid="connect-repo"], [data-testid="repos-empty-connect"]');
    await page.waitForSelector('[data-testid="add-source-page"]', { timeout: 30_000 });
    await page.fill('[data-testid="connect-input"]', REPO);
    await page.click('[data-testid="connect-validate"]');
    await page.waitForSelector('[data-testid="connect-validation"]', { timeout: 60_000 });
    await expect(page.locator('[data-testid="connect-submit"]')).toBeEnabled({ timeout: 60_000 });
    await page.click('[data-testid="connect-submit"]');
    await page.waitForURL(u => u.pathname === '/repos', { timeout: 180_000 });
  }

  await gotoScreen(page, '/repos');
  await page.waitForSelector('[data-testid="repo-sync-TfLens"]', { timeout: 60_000 });
  await page.click('[data-testid="repo-sync-TfLens"]');
  await page.waitForTimeout(20_000);

  await gotoScreen(page, '/repos');
  await page.screenshot({ path: `${SHOTS}/repos.png`, fullPage: true });
  expect(await page.locator('[data-testid="repo-sync-TfLens"]').count()).toBeGreaterThan(0);
});

test('coverage reports the misses stream, with the review record inside it', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/');
  await page.screenshot({ path: `${SHOTS}/coverage.png`, fullPage: true });

  const body = await page.locator('body').innerText();
  expect(body).toContain('misses');
  // A review field must never be reported as one SCHEMA.md does not document.
  for (const field of ['reviewed_run_id', 'correction_run_id', 'tokens_produce', 'model_correct']) {
    expect(body).not.toContain(field);
  }
});

test('the misses page renders its figures and no review is counted as a miss', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/misses');
  await page.screenshot({ path: `${SHOTS}/misses.png`, fullPage: true });

  await expect(page.locator('[data-testid="miss-kpis"]').first()).toBeVisible();

  // Amended 2026-09-09 (REQ-UI-054, BRD-175). This assertion used to read "day1-review appears
  // nowhere on the page", which was the right test while TfLens had no review surface at all: the
  // only way a review phase could have reached /misses then was by being counted as a miss. BRD-175
  // gives reviews their own band, so the phase name now legitimately appears there — and the
  // property BRD-174 actually protects is narrower and stronger: a review enters no miss count,
  // chart, filter or per-miss row. That is what is checked here.
  for (const phase of ['day1-review', 'build-review', 'verify-review', 'handoff-review']) {
    const detail = await page.locator('[data-testid="miss-detail-table"]').innerText();
    expect(detail, `${phase} must not appear in the per-miss table`).not.toContain(phase);

    const origin = await page.locator('[data-testid="miss-origin"]').innerText();
    expect(origin, `${phase} must not appear as an origin phase`).not.toContain(phase);

    const sortBand = await page.locator('[data-testid="miss-sort-table"]').innerText();
    expect(sortBand, `${phase} must not appear in the whose-gap distribution`).not.toContain(phase);
  }

  // And the band that IS allowed to name them is the review band, nowhere else.
  await expect(page.locator('[data-testid="miss-review-cost"]')).toHaveCount(1);
});

test('gate-outcomes and export render on both framework axes', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);

  for (const route of ['/gate-outcomes', '/export']) {
    await gotoScreen(page, route);
    const name = route.replace('/', '');
    await page.screenshot({ path: `${SHOTS}/${name}-techieflow.png`, fullPage: true });
    expect(await page.locator('[data-testid="app-sidebar"]').count()).toBe(1);
  }
});

test('no page scrolls horizontally at 390px', async ({ page }) => {
  // Sign in at desktop width: the shared helper waits for the sidebar, which is deliberately
  // hidden below the md breakpoint. The narrow viewport is applied once the shell is up.
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await page.setViewportSize({ width: 390, height: 844 });

  for (const route of ['/repos', '/', '/misses', '/gate-outcomes', '/export']) {
    await page.goto(route);
    await page.waitForLoadState('networkidle').catch(() => {});
    await page.waitForTimeout(2500);
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, `${route} overflows horizontally by ${overflow}px`).toBeLessThanOrEqual(1);
    await page.screenshot({ path: `${SHOTS}/mobile-${route === '/' ? 'coverage' : route.slice(1)}.png`, fullPage: true });
  }
});
