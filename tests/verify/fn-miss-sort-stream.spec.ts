// REQ-FN-106 / REQ-FN-107 / REQ-FN-108 / REQ-FN-109 — the miss stream's `sort`, its honest
// denominator, the fourth record kind, and the amended-value count. Black-box: the records reach the
// app through its own Import journey and every assertion is read off the rendered page.
//
// The probe stream is built so each clause has a record that can only pass by being handled right:
//
//   four sorted misses          one per vocabulary value, so every legal row is exercised
//   one `weak-checks`           a value outside the four; coercion would fold it into `weak-check`
//   one after-floor unsorted    keeps the denominator from being degenerate (n of N with n < N)
//   two before-floor unsorted   records from before the question was asked
//   one before-floor + amend    an older record a `miss-amend` completed — sorted, whatever its date
//   two `review` records        a fourth kind that must reach no miss count, chart or filter
//
// The estate already holds ~200 misses, every one of them written before 2026-09-07 and carrying no
// sort. That is what makes this smoke worth running: a denominator computed over all misses would
// read "6 of 208", and the honest one reads "6 of 7" with the rest stated apart as predating.
import { test } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { execFileSync } from 'child_process';
import { signIn, gotoScreen, testid, expect, DESKTOP, MOBILE, collectErrors, visualCheck } from './_helpers';

const ROOT = process.cwd();
const WORK = path.join(ROOT, 'tests', '.artifacts', 'fn-miss-sort');
const BUNDLE = path.join(WORK, 'sort-probe.zip');

/** The source this spec creates and removes. Deliberately not an owner/name that exists. */
const SOURCE = 'verifysort/c1-probe';
const SHORT = 'c1-probe';

/** After the 2026-09-07 floor — a record that could have carried a sort. */
const AFTER = '2026-09-08T';

/** Before the floor — a record from before the question was asked. */
const BEFORE = '2026-08-20T';

const MISSES: string[] = [
  miss('MISS-C1-1', `${AFTER}09:00:00Z`, 'spec'),
  miss('MISS-C1-2', `${AFTER}09:01:00Z`, 'unsaid'),
  miss('MISS-C1-3', `${AFTER}09:02:00Z`, 'weak-check'),
  miss('MISS-C1-4', `${AFTER}09:03:00Z`, 'ignored'),
  miss('MISS-C1-5', `${AFTER}09:04:00Z`, 'weak-checks'),
  miss('MISS-C1-6', `${AFTER}09:05:00Z`, null),
  miss('MISS-C1-7', `${BEFORE}09:06:00Z`, null),
  miss('MISS-C1-8', `${BEFORE}09:07:00Z`, null),
  miss('MISS-C1-9', `${BEFORE}09:08:00Z`, null),
  JSON.stringify({
    v: 1, ts: `${AFTER}10:00:00Z`, kind: 'miss-amend', app: 'SortProbe',
    project_type: 'app', miss_id: 'MISS-C1-9', field: 'sort', value: 'ignored',
  }),
  JSON.stringify({
    v: 1, ts: `${AFTER}11:00:00Z`, kind: 'review', app: 'SortProbe', project_type: 'app',
    harness: 'claude-code', phase: 'build-review', corrections: 4,
    correction_run_id: `${AFTER}10:30:00Z`, tokens_correct: 120000,
  }),
  JSON.stringify({
    v: 1, ts: `${AFTER}12:00:00Z`, kind: 'review', app: 'SortProbe', project_type: 'app',
    harness: 'claude-code', phase: 'verify-review', corrections: 2,
    correction_run_id: `${AFTER}11:30:00Z`,
  }),
];

/**
 * Eligible misses in the probe: the six written after the floor plus the one an amend completed.
 *
 * A FLOOR, not an equality. The page reads every connected repository, so a miss logged against the
 * real estate after 2026-09-07 is eligible too and raises both counts — which is correct behaviour and
 * used to fail this test. What the clause is actually about is the DENOMINATOR RULE: that the ~200
 * records written before the field are excluded from it, and stated apart. Pinning an exact total
 * tested the fixture rather than the rule, and broke the first time the owner logged a real miss.
 */
const PROBE_ELIGIBLE = 7;

/** Probe misses carrying a sort: the four legal ones, the unrecognised one, and the amended one. */
const PROBE_SORTED = 6;

/** One `miss` record of the probe stream. */
function miss(id: string, ts: string, sort: string | null): string {
  return JSON.stringify({
    v: 1, ts, kind: 'miss', app: 'SortProbe', project_type: 'app', harness: 'claude-code',
    miss_id: id, req_id: 'REQ-PROBE-001', miss_class: 'unspecified-gap', found_by: 'owner',
    artifact: 'src/probe.cs', severity: 'minor', sort,
    what: `probe record ${id} — whose gap it was`,
  });
}

test.describe.configure({ mode: 'serial' });
test.setTimeout(300_000);

test.beforeAll(() => {
  const vMetrics = path.join(WORK, 'metrics');
  fs.rmSync(WORK, { recursive: true, force: true });
  fs.mkdirSync(vMetrics, { recursive: true });
  fs.writeFileSync(path.join(vMetrics, 'misses.jsonl'), MISSES.join('\n') + '\n');
  execFileSync('zip', ['-r', '-X', BUNDLE, '.', '-i', '*.jsonl'], { cwd: vMetrics });
});

/** Removes the probe source through the app's own remove path, if it is still there. */
async function removeProbe(page: import('@playwright/test').Page) {
  await gotoScreen(page, '/repos');
  const vRemove = page.locator(`[data-testid="repo-remove-${SHORT}"]`);
  if ((await vRemove.count()) === 0) {
    return;
  }

  await vRemove.first().click();
  await page.locator('[data-testid="remove-confirm"]').click();
  await gotoScreen(page, '/repos');
  await expect(page.locator(`[data-testid="repo-source-${SHORT}"]`)).toHaveCount(0);
}

test('the probe stream imports with every kind read and no line discarded', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await removeProbe(page);

  await gotoScreen(page, '/repos');
  await page.click('[data-testid="connect-repo"]');
  await testid(page, 'source-mode');
  await page.click('[data-testid="source-mode-import"]');
  await testid(page, 'import-drop');
  await page.fill('[data-testid="import-name"]', SOURCE);
  await page.setInputFiles('#tflens-import-file', BUNDLE);
  await testid(page, 'import-preview', 60_000);
  await page.click('[data-testid="import-submit"]');

  // The import runs on the circuit; navigating away before the row appears cancels it mid-stream and
  // leaves a partial source behind, which is a truncated dataset masquerading as a real one.
  const vBadge = await testid(page, `repo-source-${SHORT}`, 120_000);
  await page.waitForTimeout(2000);
  expect((await vBadge.innerText()).trim()).toBe('Imported');
});

test('the whose-gap band reads against the eligible misses and states the rest apart', async ({ page }) => {
  const vErrors = collectErrors(page);
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/misses');

  // REQ-FN-107 — `n of N sorted`, where N is the misses ELIGIBLE to carry the field. The estate's
  // ~200 pre-2026-09-07 records are not in it; a denominator over all misses would read 6 of 208.
  const vDenominator = ((await (await testid(page, 'miss-sort-denominator')).innerText()) || '').trim();
  const vMatch = vDenominator.match(/(\d+)\s+of\s+(\d+)\s+sorted/);
  expect(vMatch, `expected "n of N sorted", got "${vDenominator}"`).not.toBeNull();

  const vSorted = Number(vMatch![1]);
  const vEligible = Number(vMatch![2]);
  expect(vSorted).toBeGreaterThanOrEqual(PROBE_SORTED);
  expect(vEligible).toBeGreaterThanOrEqual(PROBE_ELIGIBLE);
  expect(vSorted).toBeLessThan(vEligible);

  // The rule the denominator has to obey: it is the misses ELIGIBLE to carry the field, never all of
  // them. The estate holds ~200 records from before 2026-09-07, so a denominator over every miss would
  // be two orders of magnitude larger than this one.
  expect(vEligible).toBeLessThan(100);

  // REQ-FN-107 — the records from before the field are stated in those words, apart, and are never
  // pooled into the unsorted count above.
  const vPredates = ((await (await testid(page, 'miss-sort-predates')).innerText()) || '').trim();
  expect(vPredates.toLowerCase()).toContain('predate');
  const vPredatesN = Number((vPredates.match(/(\d+)\s+miss(?:es)?\s+predates?/) ?? [])[1] ?? 0);
  expect(vPredatesN).toBeGreaterThanOrEqual(2);

  // …and they are not the unanswered count. This is the clause: the records that predate the field
  // are one figure, the eligible records that carry no answer are another, and pooling them would
  // report a record from before the question was asked as one that declined to answer it.
  const vUnanswered = Number((vPredates.match(/(\d+)\s+carry no answer/) ?? [])[1] ?? -1);
  expect(vUnanswered).toBe(vEligible - vSorted);
  expect(vUnanswered).not.toBe(vPredatesN);

  // REQ-FN-106 — the out-of-vocabulary value is counted here too, in its own sentence.
  expect(vPredates).toContain('outside the four');

  expect(vErrors, vErrors.join(' | ')).toHaveLength(0);
});

test('an out-of-vocabulary sort keeps its own row and is never filed under a legal one', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/misses');

  const vTable = (await (await testid(page, 'miss-sort-table')).innerText()).toLowerCase();

  // REQ-FN-106 — the value is shown as it stands, and shown as unrecognised.
  expect(vTable).toContain('weak-checks');

  // The four legal rows are all present, so the near-miss value could not have been merged into one.
  const vRows = await page.$$eval('[data-testid="miss-sort-table"] tbody tr', aRows =>
    aRows.map(aRow => Array.from(aRow.querySelectorAll('td')).map(aCell => (aCell.textContent ?? '').trim())));
  expect(vRows.length).toBeGreaterThanOrEqual(5);

  // The recognised `weak-check` row still counts exactly the one record that carried it: had
  // `weak-checks` been coerced, this row would read 2.
  const vCounts = vRows.map(aRow => aRow.join(' | ')).join('\n');
  expect(vCounts).not.toMatch(/weak-check\s*\|\s*2\b/);
});

test('the whose-gap band states how many values an amendment completed', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/misses');

  // REQ-FN-109 — one probe record was sorted only by a `miss-amend`, and the page says so.
  const vAmended = ((await (await testid(page, 'miss-sort-amended')).innerText()) || '').trim();
  expect(vAmended.length).toBeGreaterThan(0);
  expect(vAmended).toMatch(/\b1\b/);
  expect(vAmended.toLowerCase()).toContain('amend');
});

test('review records are read, stated apart, and enter no miss count', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/misses');

  // REQ-FN-108 — the two reviews reach the page as reviews. They are not misses: the miss KPI row
  // and the sort denominator above are computed without them.
  await expect(page.locator('[data-testid="miss-review-build-review"]')).toHaveCount(1);
  await expect(page.locator('[data-testid="miss-review-verify-review"]')).toHaveCount(1);

  // …and are counted as reviews, in their own words, apart from every miss figure.
  const vReviews = ((await (await testid(page, 'miss-review-phases')).innerText()) || '').toLowerCase();
  expect(vReviews).toContain('review records');

  const vSortTable = (await (await testid(page, 'miss-sort-table')).innerText()).toLowerCase();
  expect(vSortTable).not.toContain('build-review');
  expect(vSortTable).not.toContain('verify-review');

  const vDenominator = ((await (await testid(page, 'miss-sort-denominator')).innerText()) || '').trim();
  // Same floor rather than an equality, and for the same reason (see PROBE_ELIGIBLE).
  const vEligibleHere = Number((vDenominator.match(/of\s+(\d+)\s+sorted/) ?? [])[1] ?? 0);
  expect(vEligibleHere).toBeGreaterThanOrEqual(PROBE_ELIGIBLE);
});

test('the page carrying the new figures is clean at 1280 and 390', async ({ page }) => {
  const vErrors = collectErrors(page);
  const vIds = [
    'misses-page', 'miss-kpis', 'miss-sort', 'miss-sort-denominator', 'miss-sort-table',
    'miss-sort-predates', 'miss-sort-amended', 'miss-whymissed', 'miss-type',
  ];

  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/misses');

  // The shell hides the sidebar below the md breakpoint, so the route is entered at desktop width
  // and the viewport narrowed afterwards — the same order the standing visual gate uses.
  for (const vViewport of [DESKTOP, MOBILE]) {
    await page.setViewportSize(vViewport);
    await page.waitForTimeout(900);

    const vProblems = await visualCheck(page, vIds, vViewport.width);
    await page.locator('[data-testid="miss-sort"]').scrollIntoViewIfNeeded().catch(() => {});
    await page.waitForTimeout(400);
    await page.locator('[data-testid="miss-sort"]').screenshot({
      path: `tests/.artifacts/misses/sort-band-${vViewport.width}.png`,
    }).catch(() => {});
    expect(vProblems, `whose-gap band @${vViewport.width}: ${vProblems.join(' | ')}`).toEqual([]);

    const vOverflow = await page.evaluate(() => {
      const vPane = document.querySelector('.tflens-page') as HTMLElement | null;
      return vPane ? vPane.scrollWidth - vPane.clientWidth : 0;
    });
    expect(vOverflow, `the page pane scrolls sideways by ${vOverflow}px @${vViewport.width}`)
      .toBeLessThanOrEqual(2);
  }

  // Coverage — the app's landing route — reads the same eligibility floor through the same code
  // path (LateGateCoverageCalculator against MetricsConstants.FieldSince), so it is smoked too.
  await page.setViewportSize(DESKTOP);
  await gotoScreen(page, '/');
  await expect(page.locator('[data-testid="cov-field-completeness"]')).toBeVisible();
  await expect(page.locator('[data-testid="cov-miss-diagnostics"]')).toBeVisible();

  expect(vErrors, vErrors.join(' | ')).toHaveLength(0);
});

test('the probe source is removed and the estate is left as it was found', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await removeProbe(page);
});
