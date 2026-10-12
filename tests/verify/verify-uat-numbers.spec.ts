/**
 * UAT numbers and the parity record (2026-10-12).
 *
 * REQ-FN-143..146 import one small fixture (tests/verify/fixtures/uat-numbers/) as USER2, write a
 * snapshot from /export and read it back through the download endpoint. Every expected value below
 * is what the reference, `tf-metrics.sh --rollup`, printed for the same three files — not a number
 * derived by hand. USER2 holds no source before or after.
 *
 * REQ-FN-063 and REQ-NFR-019 read USER1's /export as the owner sees it, the recorded
 * data/parity-last.json and DECISIONS.md, and the `provenance-check` verb.
 *
 * REQ-NFR-020 plants one badge inside an anchored cell of a copy of the harness mockup and proves the
 * mockup-parity gate reports it at 1280 and at 390.
 */
import { test, expect, Page, Browser } from '@playwright/test';
import { execFileSync, spawnSync } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';
import { signIn, gotoScreen, testid, USER1, USER2 } from './_helpers';

const ROOT = path.resolve(__dirname, '..', '..');
const FIXTURE = path.join(ROOT, 'tests', 'verify', 'fixtures', 'uat-numbers');
const SHORT = 'vri-uat-numbers';
const SOURCE = `verifyri/${SHORT}`;
const ARTIFACTS = path.join(ROOT, 'tests', '.artifacts', 'verify', 'uat-numbers');
const DOTNET_ENV = { ...process.env, PATH: `${process.env.HOME}/.dotnet:${process.env.PATH}`, DOTNET_ROOT: `${process.env.HOME}/.dotnet` };

async function ensureTechieFlow(page: Page) {
  const sw = page.locator('[data-testid="framework-switch"]');
  if ((await sw.count()) === 0) return;
  const tab = sw.locator('[role="tab"]').filter({ hasText: 'TechieFlow' }).first();
  if ((await tab.count()) > 0) await tab.click();
  else await sw.getByText('TechieFlow', { exact: false }).first().click();
  await page.waitForTimeout(1500);
}

async function sourceShorts(page: Page): Promise<string[]> {
  await gotoScreen(page, '/repos');
  return page.$$eval('[data-testid^="repo-source-"]',
    els => els.map(e => (e.getAttribute('data-testid') || '').replace(/^repo-source-/, '')));
}

async function removeSource(page: Page) {
  await gotoScreen(page, '/repos');
  const remove = page.locator(`[data-testid="repo-remove-${SHORT}"]`);
  if ((await remove.count()) === 0) return;
  await remove.first().click();
  await page.locator('[data-testid="remove-confirm"]').click();
  await page.waitForURL(/\/repos$/, { timeout: 60_000 }).catch(() => {});
  await gotoScreen(page, '/repos');
  await expect(page.locator(`[data-testid="repo-source-${SHORT}"]`)).toHaveCount(0);
}

type Imported = { preview: any; snapshot: any; missesText: string; coverageText: string; reportDir: string | null };
let imported: Imported | null = null;

test.describe.serial('UAT numbers — one fixture imported as user 2', () => {
  test.beforeAll(async ({ browser }: { browser: Browser }) => {
    test.setTimeout(300_000);
    const page = await browser.newPage();
    let reportDir: string | null = null;
    try {
      await signIn(page, USER2);
      expect(await sourceShorts(page), 'user 2 must hold no source before this test').toEqual([]);
      await gotoScreen(page, '/export');
      await ensureTechieFlow(page);

      await gotoScreen(page, '/repos');
      await page.click('[data-testid="connect-repo"]');
      await testid(page, 'source-mode');
      await page.click('[data-testid="source-mode-import"]');
      await testid(page, 'import-drop');
      await page.fill('[data-testid="import-name"]', SOURCE);
      const previewAnswer = page.waitForResponse(r => r.url().includes('/api/import/preview') && r.request().method() === 'POST', { timeout: 90_000 });
      await page.setInputFiles('#tflens-import-file', ['gates.jsonl', 'sessions.jsonl', 'misses.jsonl'].map(f => path.join(FIXTURE, f)));
      const preview = await (await previewAnswer).json();
      await expect(page.locator('[data-testid="import-submit"]')).toBeEnabled({ timeout: 60_000 });
      const commitAnswer = page.waitForResponse(r => r.url().includes('/api/import/commit') && r.request().method() === 'POST', { timeout: 180_000 });
      await page.click('[data-testid="import-submit"]');
      const commit = await (await commitAnswer).json();
      expect(commit.accepted, `commit refused: ${commit.message}`).toBe(true);
      await page.waitForURL(/\/repos$/, { timeout: 60_000 });

      await gotoScreen(page, '/misses');
      const missesText = (await page.locator('main').innerText()).replace(/\s+/g, ' ');
      await gotoScreen(page, '/');
      const coverageText = (await page.locator('main').innerText()).replace(/\s+/g, ' ');

      await gotoScreen(page, '/export');
      const target = (await (await testid(page, 'export-target')).innerText()).trim();
      const m = target.match(/data\/reports\/([^/]+)\/(\d{4}-\d{2}-\d{2})\/([a-z0-9-]+)\//i)!;
      const [, user, date, framework] = m;
      const contentRootDir = path.join(ROOT, 'src', 'TfLens', 'data', 'reports', user, date);
      reportDir = fs.existsSync(contentRootDir) ? null : contentRootDir;
      await (await testid(page, 'export-now')).click();
      const href = `/api/export/download?date=${date}&framework=${framework}&file=tflens.json`;
      await expect(page.locator(`[data-testid="snapshots-table"] a[href="${href}"]`).first()).toBeAttached({ timeout: 30_000 });
      const snapshot = JSON.parse(await (await page.request.get(href)).text());
      imported = { preview, snapshot, missesText, coverageText, reportDir };
    } finally {
      await removeSource(page);
      expect(await sourceShorts(page), 'user 2 must hold no source after this test').toEqual([]);
      if (reportDir && fs.existsSync(reportDir)) fs.rmSync(reportDir, { recursive: true, force: true });
      await page.close();
    }
  });

  test('REQ-FN-143 a withdrawn miss leaves every figure and the void line is not invalid', async () => {
    const s = imported!.snapshot;
    console.log(`preview invalid=${imported!.preview.totalInvalidLines} misses=${JSON.stringify({
      total: s.misses.misses_total, voided: s.misses.misses_voided_n, orphaned: s.misses.miss_voids_orphaned_n })}`);
    expect(imported!.preview.totalInvalidLines ?? 0, 'a miss-void line must not be counted invalid').toBe(0);
    expect(s.misses.misses_total).toBe(2);
    expect(s.misses.open_misses).toBe(1);
    expect(s.misses.resolved_misses).toBe(1);
    expect(s.misses.misses_voided_n).toBe(1);
    expect(s.misses.misses_voided).toEqual(['MISS-VerifyUat-20261010-03 — fixture: logged twice']);
    expect(s.misses.miss_voids_orphaned_n).toBe(1);
    expect(imported!.missesText, '/misses must not show the withdrawn miss').not.toContain('MISS-VerifyUat-20261010-03');
    expect(imported!.coverageText.toLowerCase(), 'Coverage must say a miss was withdrawn').toContain('withdrawn');
  });

  test('REQ-FN-144 a second fix attempt and a second review correction both count', async () => {
    const s = imported!.snapshot;
    expect(s.misses.miss_fixes_total).toBe(2);
    expect(s.misses.reviews_n).toBe(2);
    expect(s.misses.review_corrections).toBe(4);
  });

  test('REQ-FN-145 a gate record with no req_id is listed as malformed and scored nowhere', async () => {
    const s = imported!.snapshot;
    const repo = s.per_repo.find((r: any) => r.repo === SOURCE);
    expect(s.gates_malformed_n).toBe(1);
    expect(s.gates_malformed).toEqual([{ app: 'VerifyUat', ts: '2026-10-10T09:12:00Z', gate: 'verify-suite', result: 'pass' }]);
    expect(repo.gates).toBe(2);
    expect(repo.gates_malformed).toBe(1);
  });

  test('REQ-FN-146 an import records the session duplicates it collapsed', async () => {
    expect(imported!.snapshot.pooled.session_duplicates_collapsed).toBe(1);
  });
});

test('REQ-FN-063 the parity record names its date, dataset SHAs, script hash and parser version, and DECISIONS.md records the pass', async ({ page }) => {
  const record = JSON.parse(fs.readFileSync(path.join(ROOT, 'src', 'TfLens', 'data', 'parity-last.json'), 'utf-8'));
  console.log(`parity-last: date=${record.date} passed=${record.passed} parser=${record.parser_version} shas=${Object.keys(record.dataset_shas || {}).length}`);
  expect(record.passed).toBe(true);
  expect(record.date).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  expect(record.parser_version).toMatch(/^\d+\.\d+\.\d+$/);
  expect(record.script_hash).toMatch(/^sha256:[0-9a-f]{64}$/);
  expect(Object.keys(record.dataset_shas || {}).length, 'the record must pin the dataset it was run on').toBeGreaterThan(0);

  const decisions = fs.readFileSync(path.join(ROOT, 'DECISIONS.md'), 'utf-8');
  expect(decisions, 'DECISIONS.md must record this pass').toContain(`parity pass ${record.date} — parser ${record.parser_version}`);

  await signIn(page, USER1);
  await gotoScreen(page, '/export');
  await ensureTechieFlow(page);
  const facts = (await (await testid(page, 'parity-facts')).innerText()).replace(/\s+/g, ' ');
  expect(facts).toContain(record.date);
  expect(facts).toContain(record.parser_version);
  expect(facts.toLowerCase()).toContain('script hash');
  const shas = (await (await testid(page, 'dataset-shas')).innerText()).replace(/\s+/g, ' ');
  for (const repo of Object.keys(record.dataset_shas)) expect(shas, `dataset table must list ${repo}`).toContain(repo);
});

test('REQ-UI-001 the sign-in fields cannot be typed into before the page is interactive, so nothing typed is blanked', async ({ page }) => {
  // The pre-rendered HTML — what a browser shows before the circuit attaches — carries the boxes disabled.
  const html = await (await page.request.get('/login')).text();
  const email = /<input[^>]*data-testid="login-email"[^>]*>/.exec(html)?.[0] || '';
  const pass = /<input[^>]*data-testid="login-pass"[^>]*>/.exec(html)?.[0] || '';
  console.log(`pre-rendered: ${email.slice(0, 160)}`);
  expect(email, 'pre-rendered email box must be disabled').toMatch(/\sdisabled/);
  expect(pass, 'pre-rendered password box must be disabled').toMatch(/\sdisabled/);

  // Interactive: the boxes open, and a single fill-and-submit with no retry signs in.
  await page.goto('/login');
  await expect(page.locator('[data-testid="login-email"]')).toBeEnabled({ timeout: 30_000 });
  await page.fill('[data-testid="login-email"]', USER1.email);
  await page.fill('[data-testid="login-pass"]', USER1.password);
  await Promise.all([
    page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 45_000 }),
    page.click('[data-testid="login-submit"]'),
  ]);
});

test('REQ-UI-032 the dataset table names every source by the identity its figures were computed from', async ({ page }) => {
  const record = JSON.parse(fs.readFileSync(path.join(ROOT, 'src', 'TfLens', 'data', 'parity-last.json'), 'utf-8'));
  await signIn(page, USER1);
  await gotoScreen(page, '/export');
  await ensureTechieFlow(page);
  const rows = (await page.locator('[data-testid="dataset-shas-table"] tbody tr').allInnerTexts()).map(r => r.replace(/\s+/g, ' '));
  console.log(rows.join('\n'));
  for (const [repo, sha] of Object.entries<string>(record.dataset_shas)) {
    const row = rows.find(r => r.includes(repo));
    expect(row, `dataset table must list ${repo}`).toBeTruthy();
    const shown = sha.length === 64 ? `sha256 ${sha.slice(0, 8)}` : sha.slice(0, 7);
    expect(row!, `${repo} must show ${shown}, never a dash`).toContain(shown);
  }
});

test('REQ-NFR-019 no stored row carries an unaccounted source SHA, and the banner agrees', async ({ page }) => {
  test.setTimeout(240_000);
  const out = execFileSync('dotnet', ['run', '--project', 'src/TfLens', '-c', 'Release', '--no-build', '--', 'provenance-check', '--user', '2'],
    { cwd: ROOT, env: DOTNET_ENV, encoding: 'utf-8', timeout: 200_000 });
  const line = out.split('\n').find(l => l.startsWith('provenance-check:')) || '';
  console.log(line);
  expect(line).toMatch(/0 unaccounted source\(s\), 0 row\(s\)/);

  await signIn(page, USER1);
  await gotoScreen(page, '/export');
  await ensureTechieFlow(page);
  const banner = (await (await testid(page, 'quotable-banner')).innerText()).replace(/\s+/g, ' ');
  console.log(`quotable-banner = "${banner.slice(0, 200)}"`);
  expect(banner).not.toContain('no sync and no import ever obtained');
});

test('REQ-NFR-020 a structural difference from the approved mockup fails the parity gate at 1280 and 390', async () => {
  test.setTimeout(300_000);
  fs.mkdirSync(path.join(ARTIFACTS, 'mockups'), { recursive: true });
  const src = fs.readFileSync(path.join(ROOT, 'docs', 'mockups', 'harness.html'), 'utf-8');
  const anchor = /<[^>]*data-testid="harness-claude-code-runs"[^>]*>/.exec(src)!;
  const planted = src.slice(0, anchor.index + anchor[0].length) + '<span class="badge">planted</span>' + src.slice(anchor.index + anchor[0].length);
  fs.writeFileSync(path.join(ARTIFACTS, 'mockups', 'harness.html'), planted);
  const out = path.join(ARTIFACTS, 'parity-planted.json');
  const run = spawnSync('node', [path.join(ROOT, '.tfcore', 'utils', 'tf-mockup-parity.mjs'),
    '--base', test.info().project.use.baseURL as string, '--mockups', path.join(ARTIFACTS, 'mockups'),
    '--screen', 'harness=/harness', '--login-path', '/login', '--user', USER1.email, '--password', USER1.password,
    '--json-out', out], { cwd: ROOT, encoding: 'utf-8', timeout: 280_000 });
  console.log(`tf-mockup-parity exit ${run.status}`);
  expect(run.status, 'a planted structural difference must fail the gate').toBe(5);
  const findings = JSON.parse(fs.readFileSync(out, 'utf-8')).screens[0].findings
    .filter((f: any) => f.key === 'harness-claude-code-runs > span[0]' && f.class === 'missing');
  expect(findings.map((f: any) => f.width).sort()).toEqual([1280, 390]);
});
