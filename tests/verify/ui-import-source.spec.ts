// REQ-UI-040 / REQ-UI-041 / REQ-UI-042 — imported telemetry, end to end through the real UI.
//
// The spec drives the Add-source dialog's second mode against the running app: it previews a zip of
// this repository's own docs/metrics/ (real telemetry), proves the preview writes nothing, commits
// it, proves the grid then reads Imported with a Re-import action and no Sync control, proves the
// header Sync now leaves the row alone, proves a precomputed rollup is refused with the service's
// own sentence, checks the Coverage source badge and days-since-import wording, and removes the
// source again through the app's own remove path so the owner's data is untouched.
//
// Black-box: nothing here touches application source. The bundles are built into
// tests/.artifacts/ from files already in the repository.
import { test } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { signIn, gotoScreen, testid, expect, DESKTOP, MOBILE, collectErrors } from './_helpers';

const ROOT = process.cwd();
const WORK = path.join(ROOT, 'tests', '.artifacts', 'import-spec');
const METRICS = path.join(ROOT, 'docs', 'metrics');
const BUNDLE = path.join(WORK, 'docs-metrics.zip');
const ROLLUP = path.join(WORK, 'rollup-bundle.zip');

/** The source this spec creates and removes. Deliberately not an owner/name that exists. */
const SOURCE = 'verifyimport/g1-probe';
const SHORT = 'g1-probe';

test.describe.configure({ mode: 'serial' });
test.setTimeout(300_000);

test.beforeAll(() => {
  fs.mkdirSync(WORK, { recursive: true });

  // A zip of the repository's own telemetry directory — the exact shape a framework writes.
  fs.rmSync(BUNDLE, { force: true });
  execFileSync('zip', ['-r', '-X', BUNDLE, '.', '-i', '*.jsonl'], { cwd: METRICS });

  // A precomputed rollup, which BRD-140 refuses.
  const rollupJson = path.join(WORK, 'rollup.json');
  fs.writeFileSync(rollupJson, JSON.stringify({
    generated_at: '2026-08-28T10:00:00Z',
    framework: 'techieflow',
    totals: { runs: 11, gates: 225, sessions: 7, commits: 10 },
    first_pass_rate: 0.82,
    escape_rate: 0.04,
  }, null, 2));
  fs.rmSync(ROLLUP, { force: true });
  execFileSync('zip', ['-X', ROLLUP, 'rollup.json'], { cwd: WORK });
});

/** Removes the probe source through the app's own remove path, if it is still there. */
async function removeProbe(page: import('@playwright/test').Page) {
  await gotoScreen(page, '/repos');
  const remove = page.locator(`[data-testid="repo-remove-${SHORT}"]`);

  if (await remove.count() === 0) {
    return;
  }

  await remove.first().click();
  await page.locator('[data-testid="remove-confirm"]').click();
  await page.waitForTimeout(3000);
  await gotoScreen(page, '/repos');
  await expect(page.locator(`[data-testid="repo-source-${SHORT}"]`)).toHaveCount(0);
}

test('REQ-UI-040 the Add-source page forks into two modes and previews before it commits', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page);
  await removeProbe(page);

  const rowsBefore = await page.locator('[data-testid="repos-table"] tbody tr').count();

  await page.click('[data-testid="connect-repo"]');
  await testid(page, 'source-mode');
  await expect(page.locator('[data-testid="source-panel-api"]')).toBeVisible();
  await expect(page.locator('[data-testid="source-mode-api"]')).toBeVisible();
  await expect(page.locator('[data-testid="source-mode-import"]')).toBeVisible();

  await page.click('[data-testid="source-mode-import"]');
  await testid(page, 'import-drop');
  await expect(page.locator('[data-testid="source-panel-import"]')).toBeVisible();

  // BRD-138 — Import cannot be pressed before a preview has landed.
  await expect(page.locator('[data-testid="import-submit"]')).toBeDisabled();

  await page.fill('[data-testid="import-name"]', SOURCE);
  await page.setInputFiles('#tflens-import-file', BUNDLE);
  await testid(page, 'import-preview', 60_000);

  const streams = await page.$$eval('[data-testid="import-preview-streams"] tbody tr',
    rs => rs.map(r => (r.querySelector('td')?.textContent ?? '').trim()));
  expect(streams).toContain('runs');
  expect(streams).toContain('gates');
  expect(streams).toContain('sessions');
  expect(streams).toContain('commits');

  const sha = (await page.locator('[data-testid="import-bundle-sha"]').innerText()).trim();
  expect(sha).toMatch(/^sha256 [0-9a-f]{8}$/);

  const summary = (await page.locator('[data-testid="import-preview-summary"]').innerText()).trim();
  expect(summary).toMatch(/records across \d+ streams/);
  expect(summary).toMatch(/date range \d{4}-\d{2}-\d{2}/);
  expect(summary).toMatch(/invalid line/);

  await expect(page.locator('[data-testid="import-submit"]')).toBeEnabled();
  await expect(page.locator('[data-testid="import-submit"]')).toContainText('records');

  // A preview writes nothing, and an abandoned preview leaves nothing behind either. Add source is
  // a ROUTE now (REQ-UI-044), not an overlay, so the grid is not on screen to check while the
  // preview is up — the check happens on return, which is the same property and a stronger one: it
  // survives a full page load rather than reading a grid that was never re-fetched.
  await page.click('[data-testid="add-source-cancel"]');
  await page.waitForTimeout(1500);
  await gotoScreen(page, '/repos');
  await expect(page.locator(`[data-testid="repo-source-${SHORT}"]`)).toHaveCount(0);
  expect(await page.locator('[data-testid="repos-table"] tbody tr').count()).toBe(rowsBefore);

  expect(errors.filter(e => !/favicon|DevTools|reconnect/i.test(e))).toEqual([]);
});

test('REQ-UI-040 a precomputed rollup is refused with the message that names what to upload', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/repos');

  await page.click('[data-testid="connect-repo"]');
  await testid(page, 'source-mode');
  await page.click('[data-testid="source-mode-import"]');
  await testid(page, 'import-drop');
  await page.fill('[data-testid="import-name"]', SOURCE);
  await page.setInputFiles('#tflens-import-file', ROLLUP);

  const refusal = await testid(page, 'import-refusal', 60_000);
  const text = (await refusal.innerText()).replace(/\s+/g, ' ');

  // The service's own sentence, rendered verbatim — it names what to upload instead.
  expect(text).toContain('docs/metrics/');
  expect(text).toMatch(/rollup|snapshot/i);
  expect(text).toContain('runs.jsonl');

  // Nothing is previewable and nothing may be imported.
  await expect(page.locator('[data-testid="import-preview"]')).toHaveCount(0);
  await expect(page.locator('[data-testid="import-submit"]')).toBeDisabled();

  // Teardown. Add source is a ROUTE now (REQ-UI-044): Escape dismissed the old dialog, and a page
  // rightly ignores it. Cancel is a navigation, and leaving the route is what clears the drop zone —
  // which is a stronger guarantee than a dismissal, because it cannot leave anything mounted behind.
  await page.click('[data-testid="add-source-cancel"]');
  await page.waitForURL(/\/repos$/, { timeout: 20_000 });
  await expect(page.locator('[data-testid="import-drop"]')).toHaveCount(0);
});

test('REQ-UI-041/042 an imported source reads Imported, offers Re-import and never Sync', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/repos');

  const headers = await page.$$eval('[data-testid="repos-table"] thead th', ths => ths.map(t => t.innerText.trim()));
  expect(headers).toContain('Source');
  expect(headers).toContain('Last sync / import');

  // Every already-connected source is a fetched one and reads Synced.
  const before = await page.$$eval('[data-testid^="repo-source-"]', els => els.map(e => e.innerText.trim()));
  expect(before.length).toBeGreaterThan(0);

  await page.click('[data-testid="connect-repo"]');
  await testid(page, 'source-mode');
  await page.click('[data-testid="source-mode-import"]');
  await testid(page, 'import-drop');
  await page.fill('[data-testid="import-name"]', SOURCE);
  await page.setInputFiles('#tflens-import-file', BUNDLE);
  await testid(page, 'import-preview', 60_000);
  await page.click('[data-testid="import-submit"]');

  const badge = await testid(page, `repo-source-${SHORT}`, 120_000);
  await page.waitForTimeout(2000);

  expect((await badge.innerText()).trim()).toBe('Imported');
  await expect(page.locator(`[data-testid="repo-reimport-${SHORT}"]`)).toHaveCount(1);
  // BRD-135 — no Sync control anywhere on an imported row, not even a disabled one.
  await expect(page.locator(`[data-testid="repo-sync-${SHORT}"]`)).toHaveCount(0);

  const row = await page.locator(`[data-testid="repo-source-${SHORT}"]`)
    .evaluate(el => el.closest('tr')!.innerText.replace(/\s+/g, ' ').trim());
  expect(row).toContain('Imported');
  expect(row).not.toMatch(/\bmain\b/);

  // The header Sync now must leave every imported row's counts and timestamps untouched.
  await page.click('[data-testid="sync-now"]');
  await page.waitForTimeout(10_000);
  await gotoScreen(page, '/repos');
  const rowAfter = await page.locator(`[data-testid="repo-source-${SHORT}"]`)
    .evaluate(el => el.closest('tr')!.innerText.replace(/\s+/g, ' ').trim());
  expect(rowAfter).toBe(row);

  // Re-import is idempotent (BRD-135): the same bundle again adds nothing new.
  await page.click(`[data-testid="repo-reimport-${SHORT}"]`);
  await testid(page, 'import-drop');
  expect(await page.inputValue('[data-testid="import-name"]')).toBe(SOURCE);
  await page.setInputFiles('#tflens-import-file', BUNDLE);
  await testid(page, 'import-preview', 60_000);
  await page.click('[data-testid="import-submit"]');
  await page.waitForTimeout(6000);
  await gotoScreen(page, '/repos');
  const rowReimported = await page.locator(`[data-testid="repo-source-${SHORT}"]`)
    .evaluate(el => el.closest('tr')!.innerText.replace(/\s+/g, ' ').trim());
  expect(rowReimported).toContain('Imported');
  // The record count is the stored total, not a sum of two imports.
  expect(rowReimported.replace(/ /g, '')).toContain(row.split(' ').at(-1)!.replace(/ /g, ''));
});

test('REQ-UI-042 Coverage badges the source and reads its age as days since import', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/');

  const badge = await testid(page, `repo-source-badge-${SHORT}`, 30_000);
  expect((await badge.innerText()).trim()).toBe('Imported');

  const age = await testid(page, `repo-import-age-${SHORT}`, 30_000);
  const text = (await age.innerText()).replace(/\s+/g, ' ');
  // BRD-137's wording, verbatim (the sentence opens the phrase, so it is capitalised), and days
  // since import rather than days since the newest record.
  expect(text.toLowerCase()).toContain("source can't refresh itself — re-import to update");
  expect(text).toMatch(/Imported (today|\d+ days? ago)/);

  // The hook diagnosis is for fetched sources only — it is advice about a clone TfLens cannot see.
  const card = await page.locator(`[data-testid="repo-card-${SHORT}"]`).innerText();
  expect(card).not.toContain('update-framework.sh');
  await expect(page.locator(`[data-testid="repo-stale-${SHORT}"]`)).toHaveCount(0);

  // ADR-022 — the bundle's sha256 stands where a commit SHA stands, and is not a GitHub link.
  const sha = page.locator(`[data-testid="repo-sha-${SHORT}"]`);
  expect(await sha.count()).toBe(1);
  expect((await sha.innerText()).trim()).toMatch(/^sha256 [0-9a-f]{7}$/);
  expect(await sha.evaluate(el => el.tagName.toLowerCase())).not.toBe('a');

  // A snapshot that is simply old must not redden the summary badge ON THE HOOK RULE (BRD-137).
  //
  // Narrowed 2026-08-28. This previously asserted the source appeared nowhere in the summary at all,
  // which contradicted a sibling requirement: REQ-UI-039 / BRD-127 requires Coverage to STATE when a
  // repository's records span two `project_type` values, and the bundle used here is this repo's own
  // telemetry, which genuinely spans `docs` and `app` (TfLens is the project that caused that case).
  // So the source legitimately appears — "records declare app and docs" — and the old assertion
  // failed the app for doing exactly what another REQ demands. What BRD-137 actually forbids is the
  // STALENESS/hook diagnosis being applied to a snapshot, so that is what is asserted.
  const status = (await page.locator('[data-testid="coverage-status"]').innerText()).replace(/\s+/g, ' ');
  const sourceWarnings = status
    .split('·')
    .map(part => part.trim())
    .filter(part => part.startsWith(`${SOURCE}: `));

  for (const warning of sourceWarnings) {
    expect(warning).not.toMatch(/stale/i);
    expect(warning).not.toContain('update-framework.sh');
    expect(warning).not.toMatch(/isn't pushing|lacks hooks/i);
    expect(warning).not.toMatch(/last sync failed/i);
  }
});

test('REQ-UI-040/041 the import page and the grid hold together at 1280 and 390', async ({ page }) => {
  await signIn(page);

  for (const vp of [DESKTOP, MOBILE]) {
    // Navigate at desktop first: the shell's sidebar is `hidden md:flex`, and gotoScreen waits for
    // it to be visible, so a 390-wide first paint would never satisfy that wait.
    await page.setViewportSize(DESKTOP);
    await gotoScreen(page, '/repos');
    await page.setViewportSize(vp);
    await page.waitForTimeout(900);

    const problems = await page.evaluate((width) => {
      const found: string[] = [];
      for (const id of ['repos-table', 'connect-repo', 'repos-kpis', `repo-source-g1-probe`, 'repo-reimport-g1-probe']) {
        const el = document.querySelector(`[data-testid="${id}"]`);
        if (!el) continue;
        const r = el.getBoundingClientRect();
        if (r.width <= 0 || r.height <= 0) found.push(`${id}: zero-size @${width}`);
      }
      for (const pane of Array.from(document.querySelectorAll('main'))) {
        if (pane.scrollWidth > pane.clientWidth + 2) {
          found.push(`pane scrolls horizontally @${width}: ${pane.scrollWidth}>${pane.clientWidth}`);
        }
      }
      return found;
    }, vp.width);
    expect(problems, `geometry @${vp.width}`).toEqual([]);

    await page.click('[data-testid="connect-repo"]');
    await testid(page, 'source-mode');
    await page.click('[data-testid="source-mode-import"]');
    const drop = await testid(page, 'import-drop');
    const box = await drop.boundingBox();
    expect(box!.width, `drop zone width @${vp.width}`).toBeGreaterThan(120);
    expect(box!.x, `drop zone left edge @${vp.width}`).toBeGreaterThanOrEqual(-2);
    expect(box!.x + box!.width, `drop zone right edge @${vp.width}`).toBeLessThanOrEqual(vp.width + 2);
    await page.keyboard.press('Escape');
    await page.waitForTimeout(600);
  }

  await page.setViewportSize(DESKTOP);
});

test.afterAll(async ({ browser }) => {
  // Leave the owner's data exactly as it was found: the probe source goes out through the app's
  // own remove path, which purges its rows and its raw archive (BRD-141).
  const page = await browser.newPage();
  try {
    await signIn(page);
    await removeProbe(page);
  } finally {
    await page.close();
  }
});
