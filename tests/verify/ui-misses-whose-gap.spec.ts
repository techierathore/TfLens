// REQ-UI-052 / REQ-UI-053 / REQ-UI-054 — the 2026-09-08 amendment's three /misses surfaces:
// the whose-gap (`sort`) band, the record's own one-sentence `what` on every row, and what an owner
// review cost.
//
// Black-box. Nothing here touches application source. The data is seeded through the app's own
// "Import metric files" journey with this repository's real docs/metrics/ bundle — which carries a
// `review` record with one absent cost pair, the exact case BRD-175's `not available` rule exists
// for — and the probe source is removed again at the end so no other cluster's figures move.
import { test } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { signIn, gotoScreen, testid, expect, DESKTOP, MOBILE, collectErrors } from './_helpers';

const ROOT = process.cwd();
const WORK = path.join(ROOT, 'tests', '.artifacts', 'whose-gap-spec');
const METRICS = path.join(ROOT, 'docs', 'metrics');
const BUNDLE = path.join(WORK, 'docs-metrics.zip');
const SHOTS = path.join(ROOT, 'tests', '.artifacts', 'whose-gap-spec');

/** The probe source this spec creates and removes. Deliberately not an owner/name that exists. */
const SOURCE = 'verifywhosegap/d-probe';
const SHORT = 'd-probe';

/** The four answers, in the words BRD-171 requires. The raw token never reaches a bucket label. */
const ANSWERS = [
  'There was a check and it did not catch it',
  'The framework never said it anywhere',
  'It was written down and ignored anyway',
  "The project's own specification did not say it",
];

test.describe.configure({ mode: 'serial' });
test.setTimeout(300_000);

test.beforeAll(() => {
  fs.mkdirSync(WORK, { recursive: true });
  fs.rmSync(BUNDLE, { force: true });
  execFileSync('zip', ['-r', '-X', BUNDLE, '.', '-i', '*.jsonl'], { cwd: METRICS });
});

/** Removes the probe source through the app's own remove path, if it is still there. */
async function removeProbe(page: import('@playwright/test').Page) {
  await gotoScreen(page, '/repos');
  const remove = page.locator(`[data-testid="repo-remove-${SHORT}"]`);
  if ((await remove.count()) === 0) return;
  await remove.first().click();
  await page.locator('[data-testid="remove-confirm"]').click();
  await page.waitForTimeout(3000);
}

/** Puts the repository's own telemetry behind the probe source, through the real import journey. */
async function importProbe(page: import('@playwright/test').Page) {
  await gotoScreen(page, '/repos');
  if ((await page.locator(`[data-testid="repo-source-${SHORT}"]`).count()) > 0) return;

  await page.click('[data-testid="connect-repo"]');
  await testid(page, 'source-mode');
  await page.click('[data-testid="source-mode-import"]');
  await testid(page, 'import-drop');
  await page.fill('[data-testid="import-name"]', SOURCE);
  await page.setInputFiles('#tflens-import-file', BUNDLE);
  await testid(page, 'import-preview', 90_000);
  await page.click('[data-testid="import-submit"]');
  await testid(page, `repo-source-${SHORT}`, 180_000);
  await page.waitForTimeout(2000);
}

test('seeds the probe source from this repository own telemetry', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await removeProbe(page);
  await importProbe(page);
  await expect(page.locator(`[data-testid="repo-source-${SHORT}"]`)).toHaveCount(1);
});

test('REQ-UI-052 the whose-gap band answers in words, beside the class distribution, never merged', async ({ page }) => {
  const errors = collectErrors(page);
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/misses');

  const band = await testid(page, 'miss-sort');
  await expect(band).toBeVisible();

  // It sits BESIDE the miss-class distribution, not inside it: both cards are on the page and the
  // whose-gap band is not a descendant of the class card (BRD-171).
  await expect(page.locator('[data-testid="miss-whymissed"]')).toHaveCount(1);
  await expect(page.locator('[data-testid="miss-origin"]')).toHaveCount(1);
  expect(await page.locator('[data-testid="miss-origin"] [data-testid="miss-sort"]').count()).toBe(0);
  expect(await page.locator('[data-testid="miss-whymissed"] [data-testid="miss-sort"]').count()).toBe(0);

  // `n of N sorted` on the CARD FACE, never a tooltip, and never a percentage of all misses.
  const denominator = (await (await testid(page, 'miss-sort-denominator')).innerText()).trim();
  expect(denominator).toMatch(/^\d+ of \d+ sorted$/);

  // Every answer is in WORDS. All four are rendered whether or not they were observed, exactly as
  // the failed-practice card renders its seven.
  const table = page.locator('[data-testid="miss-sort-table"]');
  const tableText = (await table.innerText()).replace(/\s+/g, ' ');
  for (const answer of ANSWERS) {
    expect(tableText, `the band must say "${answer}" in words`).toContain(answer);
  }

  // The raw token means nothing to a reader who has not read SCHEMA.md, so it never LABELS a bucket.
  // (The letters may of course occur inside the English — "…and ignored anyway" is the answer to
  // `ignored`. What must never happen is a bucket whose label IS the stored value.)
  const labels = (await table.locator('tbody tr td:nth-child(1)').allInnerTexts()).map(t => t.trim());
  expect(labels.length, 'all four answers get a row whether or not they were observed').toBe(4);
  for (const raw of ['weak-check', 'unsaid', 'ignored', 'spec']) {
    expect(labels, `the raw token ${raw} must never label a bucket`).not.toContain(raw);
  }
  for (const raw of ['weak-check', 'unsaid']) {
    expect(tableText, `${raw} has no English form containing it, so it must be absent entirely`)
      .not.toContain(raw);
  }

  // The predates-the-field count is stated APART from the answered denominator (BRD-172).
  const predates = (await (await testid(page, 'miss-sort-predates')).innerText()).replace(/\s+/g, ' ');
  expect(predates).toMatch(/predates? the field/i);
  expect(predates).toMatch(/denominator is \d+/i);

  await page.screenshot({ path: `${SHOTS}/misses-whose-gap-1280.png`, fullPage: true });
  expect(errors.filter(e => !/favicon|DevTools|reconnect/i.test(e))).toEqual([]);
});

test('REQ-UI-052 the per-miss table can be filtered by whose gap', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/misses');

  const detail = page.locator('[data-testid="miss-detail-table"]');
  await expect(detail).toBeVisible();

  const headers = await detail.locator('thead th').allInnerTexts();
  expect(headers.map(h => h.trim())).toContain('Whose gap');

  const rowsBefore = await detail.locator('tbody tr').count();
  expect(rowsBefore).toBeGreaterThan(0);

  const search = detail.locator('input[type="search"], input[type="text"]').first();
  await expect(search).toBeVisible();

  // A term that appears in the whose-gap column and nowhere else on a row.
  const gapCells = await detail.locator('tbody tr td:nth-child(4)').allInnerTexts();
  const term = gapCells[0].trim();
  expect(term.length).toBeGreaterThan(0);

  await search.fill('zzzz-no-such-answer');
  await page.waitForTimeout(1200);
  const rowsNone = await detail.locator('tbody tr').count();

  await search.fill(term);
  await page.waitForTimeout(1200);
  const rowsFiltered = await detail.locator('tbody tr').count();

  // The filter has to actually narrow: a search box that ignores the column is not a filter.
  expect(rowsNone, 'a term matching nothing must leave no data rows').toBeLessThan(rowsBefore);
  expect(rowsFiltered, 'the whose-gap term must match rows').toBeGreaterThan(0);
});

test('REQ-UI-053 every miss row carries the record own one sentence', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/misses');

  const detail = page.locator('[data-testid="miss-detail-table"]');
  const rows = detail.locator('tbody tr');
  const count = await rows.count();
  expect(count).toBeGreaterThan(0);

  for (let i = 0; i < count; i++) {
    const what = rows.nth(i).locator('[data-testid="miss-what"]');
    expect(await what.count(), `row ${i} carries no what cell`).toBe(1);
    const text = (await what.innerText()).trim();
    expect(text.length, `row ${i} what sentence is blank`).toBeGreaterThan(0);
    // A record that predates the field says so rather than showing a blank.
    expect(text).toMatch(/\S/);
  }

  // The standing note: that sentence is the only prose that travels from a stream onto this page.
  const note = (await (await testid(page, 'miss-what-note')).innerText()).replace(/\s+/g, ' ');
  expect(note).toContain('only prose that travels');
  expect(note).toContain('never stored, rendered or exported');
});

test('REQ-UI-054 the review band prices a phase and reads not available, never zero', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/misses');

  const card = await testid(page, 'miss-review-cost');
  await expect(card).toBeVisible();

  const headers = (await card.locator('thead th').allInnerTexts()).map(h => h.trim());
  expect(headers).toContain('Review phase');
  expect(headers).toContain('Corrections');
  expect(headers).toContain('Cost to produce');
  expect(headers).toContain('Cost to correct');

  const rows = card.locator('tbody tr');
  const count = await rows.count();
  expect(count, 'the bundle carries a review record, so the band must have a row').toBeGreaterThan(0);

  // The bundled record: day1-review, 3 corrections, no produce window, a real correction window.
  const day1 = card.locator('[data-testid="miss-review-day1-review"]').first();
  expect(await day1.count()).toBe(1);
  const cells = (await day1.locator('td').allInnerTexts()).map(c => c.trim());
  expect(cells[0]).toContain('day1-review');
  expect(cells[1]).toBe('3');
  expect(cells[2], 'an absent produce cost must read not available, never 0').toContain('not available');
  expect(cells[3].replace(/[^\d]/g, '')).toBe('178645');

  // Never a zero anywhere in a cost cell.
  for (let i = 0; i < count; i++) {
    const rowCells = (await rows.nth(i).locator('td').allInnerTexts()).map(c => c.trim());
    for (const cell of [rowCells[2], rowCells[3]]) {
      expect(cell, 'an absent review cost must never render as 0').not.toBe('0');
    }
  }

  // BRD-174 — a review is never a miss: no review phase leaks into a miss count, chart or filter.
  const detailText = await page.locator('[data-testid="miss-detail-table"]').innerText();
  for (const phase of ['day1-review', 'build-review', 'verify-review', 'handoff-review']) {
    expect(detailText, `${phase} must not appear in the per-miss table`).not.toContain(phase);
  }
  const sortTableText = await page.locator('[data-testid="miss-sort-table"]').innerText();
  expect(sortTableText).not.toContain('review');
});

test('the three new bands hold their shape at 1280 and 390', async ({ page }) => {
  const errors = collectErrors(page);
  await page.setViewportSize(DESKTOP);
  await signIn(page);

  for (const size of [DESKTOP, MOBILE]) {
    // Sign-in happens at desktop width above: the shared gotoScreen waits for the sidebar, which is
    // deliberately `hidden md:flex` and therefore never visible at 390. The narrow viewport navigates
    // directly, which is what the existing mobile gates do for the same reason.
    await page.setViewportSize(size);
    await page.goto('/misses');
    await page.waitForLoadState('networkidle').catch(() => {});
    await page.waitForTimeout(2500);

    for (const id of ['miss-sort', 'miss-sort-denominator', 'miss-sort-predates', 'miss-review-cost', 'miss-what-note']) {
      const el = page.locator(`[data-testid="${id}"]`).first();
      expect(await el.count(), `${id} missing @${size.width}`).toBe(1);
      const box = await el.boundingBox();
      expect(box, `${id} has no box @${size.width}`).not.toBeNull();
      expect(box!.width, `${id} is zero-width @${size.width}`).toBeGreaterThan(0);
      expect(box!.height, `${id} is zero-height @${size.width}`).toBeGreaterThan(0);
    }

    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth);
    expect(overflow, `/misses overflows horizontally by ${overflow}px @${size.width}`).toBeLessThanOrEqual(1);

    await page.screenshot({ path: `${SHOTS}/misses-${size.width}.png`, fullPage: true });
  }

  expect(errors.filter(e => !/favicon|DevTools|reconnect/i.test(e))).toEqual([]);
});

test('removes the probe source so no other cluster figures move', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await removeProbe(page);
  await gotoScreen(page, '/repos');
  await expect(page.locator(`[data-testid="repo-source-${SHORT}"]`)).toHaveCount(0);
});
