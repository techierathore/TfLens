// Puts the running app in the state docs/mockups/estimates.html draws: the Sample app test files
// imported for test user 2 (sample/sample-app with estimates and runs, sample/sample-empty with an
// empty estimate stream). `--remove` takes both out again. Called by tests/verify/seed/estimates.sh.
import { chromium } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';

const BASE = process.env.TF_BASE || process.env.BASE_URL || 'http://localhost:5099';
const USER = { email: 'tflenstest2@techierathore.com', password: 'TfLensTest2!23' };
const ROOT = process.cwd();
const FIX = path.join(ROOT, 'tests', 'TfLens.Core.Tests', 'Fixtures', 'Estimates');
const WORK = path.join(ROOT, 'tests', '.artifacts', 'verify-estimates-seed');
const REMOVE = process.argv.includes('--remove');

async function signIn(page) {
  await page.goto(`${BASE}/login`);
  for (let i = 0; i < 30; i++) {
    await page.fill('[data-testid="login-email"]', USER.email);
    await page.fill('[data-testid="login-pass"]', USER.password);
    if ((await page.inputValue('[data-testid="login-email"]')) === USER.email
      && (await page.inputValue('[data-testid="login-pass"]')) === USER.password) break;
    await page.waitForTimeout(1000);
  }
  await Promise.all([
    page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 45000 }),
    page.click('[data-testid="login-submit"]'),
  ]);
}

async function repos(page) {
  await page.goto(`${BASE}/repos`);
  await page.waitForSelector('[data-testid="connect-repo"]', { timeout: 30000 });
  await page.waitForTimeout(1500);
}

async function has(page, short) {
  await repos(page);
  return (await page.locator(`[data-testid="repo-source-${short}"]`).count()) > 0;
}

async function importSource(page, short, files) {
  await repos(page);
  await page.click('[data-testid="connect-repo"]');
  await page.click('[data-testid="source-mode-import"]');
  await page.waitForSelector('[data-testid="import-drop"]');
  await page.fill('[data-testid="import-name"]', `sample/${short}`);
  const preview = page.waitForResponse(r => r.url().includes('/api/import/preview'), { timeout: 90000 });
  await page.setInputFiles('#tflens-import-file', files);
  await preview;
  await page.waitForSelector('[data-testid="import-submit"]:not([disabled])', { timeout: 60000 });
  const commit = page.waitForResponse(r => r.url().includes('/api/import/commit'), { timeout: 120000 });
  await page.click('[data-testid="import-submit"]');
  const body = await (await commit).json();
  if (!body.accepted) throw new Error(`import of sample/${short} refused: ${body.message}`);
  await page.waitForSelector(`[data-testid="repo-source-${short}"]`, { timeout: 60000 });
}

async function removeSource(page, short) {
  if (!(await has(page, short))) return;
  await page.click(`[data-testid="repo-remove-${short}"]`);
  await page.click('[data-testid="remove-confirm"]');
  await page.waitForTimeout(2500);
}

const browser = await chromium.launch();
try {
  const page = await (await browser.newContext()).newPage();
  await signIn(page);
  if (REMOVE) {
    await removeSource(page, 'sample-app');
    await removeSource(page, 'sample-empty');
    console.log('removed the Sample app sources');
  } else {
    fs.rmSync(WORK, { recursive: true, force: true });
    fs.mkdirSync(path.join(WORK, 'app'), { recursive: true });
    fs.mkdirSync(path.join(WORK, 'empty', 'docs', 'metrics'), { recursive: true });
    for (const f of ['estimates.jsonl', 'runs.jsonl']) {
      fs.copyFileSync(path.join(FIX, 'sample__sample-app', f), path.join(WORK, 'app', f));
    }
    fs.copyFileSync(path.join(FIX, 'sample__sample-empty', 'estimates.jsonl'),
      path.join(WORK, 'empty', 'docs', 'metrics', 'estimates.jsonl'));
    execFileSync('zip', ['-q', '-X', path.join(WORK, 'sample-empty.zip'), 'docs/metrics/estimates.jsonl'],
      { cwd: path.join(WORK, 'empty') });

    if (!(await has(page, 'sample-app'))) {
      await importSource(page, 'sample-app', [path.join(WORK, 'app', 'estimates.jsonl'), path.join(WORK, 'app', 'runs.jsonl')]);
    }
    if (!(await has(page, 'sample-empty'))) {
      await importSource(page, 'sample-empty', [path.join(WORK, 'sample-empty.zip')]);
    }
    console.log('seeded the Sample app sources for test user 2');
  }
} finally {
  await browser.close();
}
