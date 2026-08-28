// REQ-UI-014 / REQ-UI-039 smoke — Coverage's fifth stream row and the miss data-quality facts.
//
// Two halves, deliberately separated.
//
// The tests that run on ANY dataset assert what must always be true: five stream rows per TechieFlow
// repository, and one sentence per data-quality fact. A zero is a real answer there, so they do not
// need a warning state to be meaningful.
//
// The tests guarded by TFLENS_SEEDED assert the POPULATED warning states, which the owner's live data
// does not reach (4 misses, 4 fixes, 0 orphans, one project type per repository — a healthy stream).
// They are skipped rather than weakened, because an assertion that passes whether or not the control
// appeared proves nothing. To run them, seed and then tear down again — the seed used on 2026-08-28,
// which produced the screenshots in tests/.artifacts/shots/:
//
//   INSERT INTO "Miss"  (…, "SourceSha", …) VALUES (2,'techierathore/TechieRag','SMOKE-SEED', …);  -- 2 misses, no fixes
//   INSERT INTO "MissFix"   (…) VALUES (…,'SMOKE-SEED',…,'SMOKE-MISS-NOBODY',…);                   -- an orphan fix
//   INSERT INTO "MissAmend" (…) VALUES (…,'SMOKE-SEED',…,'SMOKE-MISS-NOBODY','why_missed','other');-- an orphan amend
//   INSERT INTO "Gate"  (…) VALUES (2,'techierathore/TechieRag','SMOKE-SEED',…,'docs',…), (…,'app',…);
//   -- then: DELETE FROM "Miss"/"MissFix"/"MissAmend"/"Gate" WHERE "SourceSha" = 'SMOKE-SEED';
//
// Every seeded row carries SourceSha 'SMOKE-SEED' precisely so the teardown is exact.
import { test, expect } from '@playwright/test';
import { signIn, gotoScreen, testid, visualCheck, DESKTOP, MOBILE } from './_helpers';

const REPOS = ['TechieBlog', 'TechieFlow', 'TechieRag', 'TrBlazeUI'];
const SEEDED = process.env.TFLENS_SEEDED === '1';

test('REQ-UI-014: every per-repo stream table has FIVE rows, misses last', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/');

  for (const name of REPOS) {
    const rows = await page.$$eval(
      `[data-testid="repo-streams-${name}"] tbody tr td:first-child`,
      cells => cells.map(c => (c.textContent || '').trim().split(/\s+/)[0]));
    console.log(`STREAM ROWS ${name}: ${JSON.stringify(rows)}`);
    expect(rows, `${name} stream rows`).toEqual(['runs', 'gates', 'sessions', 'commits', 'misses']);
  }

  // `misses` carries `backfilled` on every record, so the column prints a number. Sessions and
  // commits never carry the field and print an em dash — "0" there would read as "none were
  // backfilled" rather than "the field does not exist here".
  const backfilled = await page.$$eval(
    '[data-testid="repo-streams-TechieFlow"] tbody tr',
    rows => rows.map(r => Array.from(r.querySelectorAll('td')).map(c => (c.textContent || '').trim())));
  console.log('TechieFlow BACKFILLED COLUMN: ' + JSON.stringify(backfilled.map(r => [r[0].split(/\s+/)[0], r[2]])));
  expect(backfilled[4][2], 'the misses row prints a backfilled count, not an em dash').toMatch(/^\d/);
  expect(backfilled[2][2], 'the sessions row has no such field').toBe('—');
});

test('REQ-UI-039: the miss data-quality card states each fact in words', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/');

  const card = await testid(page, 'miss-quality');
  console.log('MISS QUALITY: ' + (await card.innerText()).replace(/\s+/g, ' ').slice(0, 700));

  // Every fact carries a label, a number and a sentence; a bare figure is what this forbids.
  for (const id of ['escapes-missing-why', 'orphan-misses', 'miss-backfilled']) {
    const text = (await (await testid(page, id)).innerText()).trim();
    console.log(`${id}: ${text.replace(/\s+/g, ' ').slice(0, 220)}`);
    expect(text.length, `${id} is empty`).toBeGreaterThan(20);
    expect(text).toMatch(/\d/);
  }

  expect(await (await testid(page, 'escapes-missing-why')).innerText()).toMatch(/why_missed/);
  expect(await (await testid(page, 'orphan-misses')).innerText()).toMatch(/miss-fix naming no stored miss/);
});

test('REQ-UI-039: the data-quality facts are on Coverage and NOT on the /misses KPI row', async ({ page }) => {
  await signIn(page);

  await gotoScreen(page, '/');
  expect(await page.locator('[data-testid="escapes-missing-why"]').count()).toBe(1);
  expect(await page.locator('[data-testid="orphan-misses"]').count()).toBe(1);

  await gotoScreen(page, '/misses');
  const onMisses = await page.locator('[data-testid="escapes-missing-why"], [data-testid="orphan-misses"]').count();
  console.log(`DATA-QUALITY IDS ON /misses: ${onMisses}`);
  expect(onMisses, 'they are data-quality facts, not quality figures (REQ-UI-039)').toBe(0);
});

test('REQ-UI-039: misses without fixes is a WARNING, and the health badge is not reddened', async ({ page }) => {
  test.skip(!SEEDED, 'needs the seeded dataset — the owner\'s live stream has a fix for every miss');
  await signIn(page);
  await gotoScreen(page, '/');

  const warn = await testid(page, 'misses-without-fixes');
  const text = (await warn.innerText()).trim();
  console.log('NO-FIXES WARNING: ' + text.replace(/\s+/g, ' ').slice(0, 400));

  expect(text).toContain('TechieRag');
  // The word carries the meaning, not the colour (REQ-NFR-005).
  expect(text).toMatch(/telemetry gap, not a defect backlog/i);

  // Never an error, and never a failed health badge FOR THIS REASON. TechieRag's badge does read
  // "sync error" on the owner's live data — a real, pre-existing GitHub rate-limit failure recorded
  // in SyncState, nothing to do with misses. What REQ-UI-039 forbids is the miss stream driving the
  // badge, so the assertion is on the badge's vocabulary: sync and staleness only.
  expect(await page.locator('[data-testid="coverage-error"]').count()).toBe(0);
  const badge = (await (await testid(page, 'repo-state-TechieRag')).innerText()).trim();
  console.log('TechieRag STATE BADGE: ' + badge);
  expect(badge).not.toMatch(/miss|fix/i);
  expect(badge).toMatch(/^(synced|imported|not synced yet|sync error|\d+ streams stale)$/);

  // The alert itself is a warning, not a danger — the word carries it, and the variant agrees.
  const variantClass = await warn.getAttribute('class');
  console.log('NO-FIXES ALERT CLASS: ' + variantClass);
  expect(variantClass ?? '').not.toMatch(/destructive|danger/i);

  // ...and it is repeated on the repository it is true of.
  expect(await page.locator('[data-testid="repo-no-fixes-TechieRag"]').count()).toBe(1);
});

test('REQ-UI-039: the reclassification split names both segments and calls each a period', async ({ page }) => {
  test.skip(!SEEDED, 'needs the seeded dataset — no live repository has been reclassified');
  await signIn(page);
  await gotoScreen(page, '/');

  const split = await testid(page, 'repo-reclassified-TechieRag');
  const text = (await split.innerText()).trim();
  console.log('RECLASSIFICATION: ' + text.replace(/\s+/g, ' ').slice(0, 500));

  expect(text).toContain("'app'");
  expect(text).toContain("'docs'");
  expect(text).toMatch(/period of the project/i);
  expect(text).toMatch(/never two projects/i);

  const summary = await testid(page, 'reclassified-summary');
  console.log('RECLASSIFICATION SUMMARY: ' + (await summary.innerText()).replace(/\s+/g, ' ').slice(0, 300));
});

test('REQ-UI-039: Coverage is visually clean at 1280 and 390', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/');

  const ids = [
    'coverage-status', 'coverage-kpis', 'miss-quality', 'miss-quality-total', 'escapes-missing-why',
    'orphan-misses', 'miss-backfilled', 'misses-without-fixes', 'reclassified-summary',
    'repo-streams-TechieRag', 'repo-reclassified-TechieRag', 'repo-no-fixes-TechieRag',
    'unknown-fields', 'rebuild-card',
  ];
  const present: string[] = [];
  for (const id of ids) {
    if (await page.locator(`[data-testid="${id}"]`).count() > 0) present.push(id);
  }
  console.log('CONTROLS ON /: ' + present.join(', '));
  expect(present).toContain('miss-quality');
  expect(present).toContain('escapes-missing-why');
  if (SEEDED) {
    expect(present).toContain('misses-without-fixes');
    expect(present).toContain('repo-reclassified-TechieRag');
  }

  for (const vp of [DESKTOP, MOBILE]) {
    await page.setViewportSize(vp);
    await page.waitForTimeout(900);

    const problems = await visualCheck(page, present, vp.width);
    console.log(`VISUAL @${vp.width}: ` + (problems.length ? problems.join(' | ') : 'clean'));
    expect(problems, `visual problems at ${vp.width}`).toEqual([]);

    await page.screenshot({ path: `tests/.artifacts/shots/coverage-misses-${vp.width}.png`, fullPage: false });
  }

  await page.setViewportSize(DESKTOP);
});

test('REQ-UI-039: the Playbook axis has no miss block to be wrong about', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/');

  await page.evaluate(() => {
    const sw = document.querySelector('[data-testid="framework-switch"]');
    const trig = Array.from(sw?.querySelectorAll('[role="tab"], button') ?? []).find(
      e => /playbook/i.test((e as HTMLElement).innerText || ''));
    (trig as HTMLElement | undefined)?.click();
  });
  await page.waitForTimeout(2500);

  // The Playbook axis emits no misses.jsonl, so the card is ABSENT rather than showing zeros: an
  // empty block on an axis that has no such stream would read as "we looked and found nothing wrong".
  const count = await page.locator('[data-testid="miss-quality"]').count();
  console.log(`miss-quality ON PLAYBOOK: ${count}`);
  expect(count, 'a stream that does not exist reports nothing, not zero').toBe(0);
  expect(await page.locator('[data-testid="playbook-empty"], [data-testid="pb-coverage-surface"]').count())
    .toBeGreaterThan(0);

  const problems = await visualCheck(page, ['app-sidebar', 'framework-switch'], DESKTOP.width);
  console.log('PLAYBOOK VISUAL @1280: ' + (problems.length ? problems.join(' | ') : 'clean'));
  expect(problems).toEqual([]);
});
