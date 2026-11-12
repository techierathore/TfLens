// `/estimates` — black-box acceptance for phase 4: REQ-FN-147, REQ-FN-150, REQ-UI-078, REQ-UI-079,
// REQ-UI-080 and REQ-NFR-027 (BRD-211..BRD-219), over the Sample app test files of BRD §3.1.
//
// The Sample app is made up and exists only as the checked-in files under
// tests/TfLens.Core.Tests/Fixtures/Estimates/. No real repository's data is used. Every expected figure
// below is a line of the BRD §3.1 table, copied as printed there.
//
// Every mutating step signs in as USER2 (the documented second test user) and removes the two sources
// it imports through the app's own Remove path in afterAll, so USER2 ends as it began. USER1 is never
// touched. Nothing here reads the database: the evidence is what the browser shows and what the import
// endpoints the /repos page itself calls answer on the wire.
import { test, Page, Browser } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import { execFileSync } from 'node:child_process';
import { signIn, gotoScreen, testid, expect, USER2, DESKTOP, MOBILE, visualCheck, collectErrors } from './_helpers';

const ROOT = process.cwd();
const FIXTURES = path.join(ROOT, 'tests', 'TfLens.Core.Tests', 'Fixtures', 'Estimates');
const WORK = path.join(ROOT, 'tests', '.artifacts', 'verify-estimates');

/** The two sources this file creates; nothing else is ever removed. */
const OWNER = 'sample';
const APP = 'sample-app';
const EMPTY = 'sample-empty';

/** The import service recognises a stream by its FILE NAME, so each source keeps its own directory. */
const F = {
  appEstimates: path.join(WORK, APP, 'estimates.jsonl'),
  appRuns: path.join(WORK, APP, 'runs.jsonl'),
  emptyEstimates: path.join(WORK, EMPTY, 'docs', 'metrics', 'estimates.jsonl'),
  // The importer refuses a zero-byte upload outright (REQ-NFR-014, BRD-139), so the empty test file is
  // carried unchanged inside a bundle, in the docs/metrics layout a repository's own export has.
  emptyZip: path.join(WORK, `${EMPTY}.zip`),
};

type StreamRow = { stream: string; records?: number; added?: number; presented?: number };
type ImportBody = { accepted: boolean; message?: string; streams?: StreamRow[]; recordsAdded?: number };
type Imported = { preview: ImportBody; commit: ImportBody };

/** What the imports answered, kept for the REQ-FN-147 test. */
const IMPORTED: { app?: Imported; empty?: Imported; again?: Imported } = {};

// Default mode (workers=1 keeps the order): one row's failure must not stop the next row being graded.
test.describe.configure({ mode: 'default' });
test.setTimeout(300_000);

// ---------------------------------------------------------------------------------------------
// Black-box helpers
// ---------------------------------------------------------------------------------------------

const streamOf = (body: ImportBody | undefined, name: string) => (body?.streams ?? []).find(s => s.stream === name);
const squash = (text: string) => text.replace(/\s+/g, ' ').trim();

async function openImportMode(page: Page) {
  await gotoScreen(page, '/repos');
  await page.click('[data-testid="connect-repo"]');
  await testid(page, 'source-mode');
  await page.click('[data-testid="source-mode-import"]');
  await testid(page, 'import-drop');
}

async function choose(page: Page, files: string[]): Promise<ImportBody> {
  const answer = page.waitForResponse(r => r.url().includes('/api/import/preview') && r.request().method() === 'POST',
    { timeout: 90_000 });
  await page.setInputFiles('#tflens-import-file', files);
  const body = (await (await answer).json()) as ImportBody;
  await page.locator('[data-testid="import-preview"], [data-testid="import-refusal"]').first()
    .waitFor({ state: 'visible', timeout: 60_000 });
  return body;
}

async function commit(page: Page): Promise<ImportBody> {
  await expect(page.locator('[data-testid="import-submit"]')).toBeEnabled();
  const answer = page.waitForResponse(r => r.url().includes('/api/import/commit') && r.request().method() === 'POST',
    { timeout: 180_000 });
  await page.click('[data-testid="import-submit"]');
  const body = (await (await answer).json()) as ImportBody;
  if (body.accepted) {
    await page.waitForURL(/\/repos$/, { timeout: 60_000 });
  }
  return body;
}

async function importNew(page: Page, short: string, files: string[]): Promise<Imported> {
  await openImportMode(page);
  await page.fill('[data-testid="import-name"]', `${OWNER}/${short}`);
  const preview = await choose(page, files);
  expect(preview.accepted, `preview refused: ${preview.message}`).toBe(true);
  const committed = await commit(page);
  expect(committed.accepted, `commit refused: ${committed.message}`).toBe(true);
  await testid(page, `repo-source-${short}`, 60_000);
  return { preview, commit: committed };
}

async function reimport(page: Page, short: string, files: string[]): Promise<Imported> {
  await gotoScreen(page, '/repos');
  await page.click(`[data-testid="repo-reimport-${short}"]`);
  await testid(page, 'import-drop');
  const preview = await choose(page, files);
  expect(preview.accepted, `re-import preview refused: ${preview.message}`).toBe(true);
  return { preview, commit: await commit(page) };
}

/** Removes a source through the app's own Remove route, if it is there. */
async function removeSource(page: Page, short: string) {
  await gotoScreen(page, '/repos');
  const remove = page.locator(`[data-testid="repo-remove-${short}"]`);
  if ((await remove.count()) === 0) return;
  await remove.first().click();
  await page.locator('[data-testid="remove-confirm"]').click();
  await page.waitForURL(/\/repos$/, { timeout: 60_000 }).catch(() => {});
  await page.waitForTimeout(1500);
  await gotoScreen(page, '/repos');
  await expect(page.locator(`[data-testid="repo-source-${short}"]`)).toHaveCount(0);
}

async function ensureFramework(page: Page, label: 'TechieFlow' | 'Playbook') {
  const sw = await testid(page, 'framework-switch');
  const tab = sw.locator('[role="tab"]').filter({ hasText: label }).first();
  if ((await tab.count()) > 0) await tab.click();
  else await sw.getByText(label, { exact: false }).first().click();
  await page.waitForTimeout(2500);
}

/** Opens /estimates on TechieFlow with the picker on the one source whose option names `short`. */
async function openEstimates(page: Page, short: string) {
  await gotoScreen(page, '/estimates');
  await ensureFramework(page, 'TechieFlow');
  await (await testid(page, 'repo-picker')).click();
  const option = page.locator('[role="option"]').filter({ hasText: new RegExp(short.replace('-', '[ -]'), 'i') }).first();
  await option.waitFor({ state: 'visible', timeout: 10_000 });
  await option.click();
  await page.waitForTimeout(1500);
  await testid(page, 'estimates-page');
}

/** The card whose head line names `model` and whose title names `phase`. */
async function card(page: Page, phase: number, model: string) {
  // A card and its By step / By row disclosures stand together in one phase block.
  const vCard = page.locator('[data-testid="est-phase"]')
    .filter({ has: page.locator('[data-testid="est-card"]', { hasText: `SampleApp · phase ${phase}` }) })
    .filter({ has: page.locator('[data-testid="est-card-head"]', { hasText: model }) });
  await expect(vCard, `one card for phase ${phase} · ${model}`).toHaveCount(1);
  return vCard;
}

async function rowText(page: Page, phase: number, model: string, part: string): Promise<string> {
  const vCard = await card(page, phase, model);
  return squash(await vCard.locator(`[data-testid="est-part-${part}"]`).innerText());
}

function expectAll(text: string, fragments: string[], where: string) {
  for (const fragment of fragments) {
    expect(text, `${where} shows "${fragment}"`).toContain(fragment);
  }
}

async function freshSignedInPage(browser: Browser): Promise<Page> {
  const page = await (await browser.newContext()).newPage();
  await signIn(page, USER2);
  return page;
}

// ---------------------------------------------------------------------------------------------
// Fixtures in, fixtures out
// ---------------------------------------------------------------------------------------------

test.beforeAll(async ({ browser }) => {
  test.setTimeout(420_000);
  fs.rmSync(WORK, { recursive: true, force: true });
  for (const [vTarget, vSource] of [
    [F.appEstimates, path.join(FIXTURES, 'sample__sample-app', 'estimates.jsonl')],
    [F.appRuns, path.join(FIXTURES, 'sample__sample-app', 'runs.jsonl')],
    [F.emptyEstimates, path.join(FIXTURES, 'sample__sample-empty', 'estimates.jsonl')],
  ]) {
    fs.mkdirSync(path.dirname(vTarget), { recursive: true });
    fs.copyFileSync(vSource, vTarget);
  }
  execFileSync('zip', ['-q', '-X', F.emptyZip, 'docs/metrics/estimates.jsonl'], { cwd: path.join(WORK, EMPTY) });

  const page = await freshSignedInPage(browser);
  // A run that died half way leaves its sources behind; start from none.
  await removeSource(page, APP);
  await removeSource(page, EMPTY);

  IMPORTED.app = await importNew(page, APP, [F.appEstimates, F.appRuns]);
  IMPORTED.empty = await importNew(page, EMPTY, [F.emptyZip]);
  IMPORTED.again = await reimport(page, APP, [F.appEstimates, F.appRuns]);
  fs.writeFileSync(path.join(WORK, 'import-answers.json'), JSON.stringify(IMPORTED, null, 2));
  await page.context().close();
});

test.afterAll(async ({ browser }) => {
  const page = await freshSignedInPage(browser);
  await removeSource(page, APP);
  await removeSource(page, EMPTY);
  await page.context().close();
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-147 — the stream is imported into its own table, and a re-import adds nothing
// ---------------------------------------------------------------------------------------------

test('REQ-FN-147 — importing the three Sample app test files on /repos stores 44 Sample app estimate rows and 0 Sample empty rows; re-importing adds none', async () => {
  // The file is recognised as the sixth stream by its name, and every line is a typed record.
  const appPreview = streamOf(IMPORTED.app?.preview, 'estimates');
  expect(appPreview?.records, 'Sample app estimates.jsonl is read as the estimates stream').toBe(44);
  expect(streamOf(IMPORTED.app?.preview, 'runs')?.records, 'Sample app runs.jsonl beside it').toBe(15);

  // Stored: 44 Sample app rows, 0 Sample empty rows — an empty stream, never an error.
  expect(streamOf(IMPORTED.app?.commit, 'estimates')?.added, 'Sample app estimate rows stored').toBe(44);
  expect(IMPORTED.empty?.commit.accepted, 'Sample empty is accepted, not refused').toBe(true);
  expect(streamOf(IMPORTED.empty?.preview, 'estimates'), 'Sample empty file is read as the estimates stream').toBeTruthy();
  expect(streamOf(IMPORTED.empty?.commit, 'estimates')?.added, 'Sample empty estimate rows stored').toBe(0);

  // Re-importing the same files adds none: every estimate collapses on the dedupe key, first wins.
  expect(IMPORTED.again?.commit.accepted, `re-import refused: ${IMPORTED.again?.commit.message}`).toBe(true);
  expect(streamOf(IMPORTED.again?.commit, 'estimates')?.added, 're-import adds no estimate row').toBe(0);
  expect(IMPORTED.again?.commit.recordsAdded, 're-import adds no record at all').toBe(0);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-078 / REQ-FN-150 / REQ-NFR-027 — each card's lines read exactly as BRD §3.1
// ---------------------------------------------------------------------------------------------

const OPUS = 'claude-opus-5-5';
const SONNET = 'claude-sonnet-5-5';

/** BRD §3.1, Expected figures — tokens, then time; estimate mid (low–high) beside actual. */
const EXPECTED = [
  {
    phase: 1, model: OPUS,
    build: ['42.0k tok (28.0k–56.0k)', '38m20s (23m20s–53m20s)', '52.0k tok', '50m00s', 'apportioned', '3 · 1 apportioned'],
    qa: ['13.5k tok (7.5k–19.5k)', '35m00s (20m00s–50m00s)', '24.0k tok', '55m00s'],
    total: ['55.5k tok (35.5k–75.5k)', '1h13m (43m20s–1h43m)', '76.0k tok', '1h45m', 'apportioned', '8 · 1 apportioned'],
  },
  {
    phase: 1, model: SONNET,
    build: ['70.0k tok (55.0k–85.0k)', '56m40s (43m20s–1h10m)', '52.0k tok', '50m00s', 'apportioned', '3 · 1 apportioned'],
    qa: ['13.5k tok (7.5k–19.5k)', '35m00s (20m00s–50m00s)', '24.0k tok', '55m00s'],
    total: ['83.5k tok (62.5k–104.5k)', '1h31m (1h03m–2h00m)', '76.0k tok', '1h45m', 'apportioned', '8 · 1 apportioned'],
  },
  {
    phase: 2, model: OPUS,
    build: ['45.0k tok (30.0k–60.0k)', '45m00s (30m00s–1h00m)', '65.0k tok', '1h05m', 'apportioned', '3 · 1 apportioned'],
    qa: ['13.0k tok (8.0k–18.0k)', '33m20s (23m20s–43m20s)', '13.0k tok', '30m00s'],
    total: ['58.0k tok (38.0k–78.0k)', '1h18m (53m20s–1h43m)', '78.0k tok', '1h35m', 'apportioned', '7 · 1 apportioned'],
  },
];

test('REQ-UI-078 — each Sample app card shows exactly the Build, Machine QA, After handoff and Owner UAT figures of BRD §3.1', async ({ page }) => {
  await signIn(page, USER2);
  await openEstimates(page, APP);

  await expect(page.locator('[data-testid="est-card"]'), 'one card per (repo, phase, estimating model)').toHaveCount(3);
  await expect(await testid(page, 'est-separation-note')).toContainText('Two columns, never one number');

  for (const e of EXPECTED) {
    const where = `phase ${e.phase} · ${e.model}`;
    const vCard = await card(page, e.phase, e.model);
    const vRowsN = e.phase === 1 ? 3 : 2;
    await expect(vCard.locator('[data-testid="est-card-head"]')).toContainText(`${vRowsN} rows`);

    expectAll(await rowText(page, e.phase, e.model, 'build'), e.build, `${where} Build`);

    const qa = await rowText(page, e.phase, e.model, 'qa');
    expectAll(qa, e.qa, `${where} Machine QA`);
    expect(qa, `${where} Machine QA counted no run as apportioned`).not.toContain('apportioned');

    expectAll(await rowText(page, e.phase, e.model, 'handoff'),
      ['no precedent', 'insufficient data (n=0)', `0/${vRowsN}`], `${where} After handoff`);
    expectAll(await rowText(page, e.phase, e.model, 'uat'), ['Owner UAT time', 'not measured'], `${where} Owner UAT`);

    await expect(vCard.locator('[data-testid="est-rowless"]'), `${where} runs that named no row`)
      .toContainText('build 0 · verify 0 · fix 1 · after handoff 0');
  }
});

test('REQ-FN-150 — the three Totals read 55.5k, 83.5k and 58.0k beside 76.0k, 76.0k and 78.0k, labelled as having no oracle', async ({ page }) => {
  await signIn(page, USER2);
  await openEstimates(page, APP);

  for (const e of EXPECTED) {
    const total = await rowText(page, e.phase, e.model, 'total');
    expectAll(total, [...e.total, 'no oracle', 'after handoff never inside'], `phase ${e.phase} · ${e.model} Total`);
  }
});

test('REQ-UI-078 — By step gives each step with where the actual falls: within, below, above, or a dash under insufficient data', async ({ page }) => {
  await signIn(page, USER2);
  await openEstimates(page, APP);

  const steps = async (phase: number, model: string) => {
    const vCard = await card(page, phase, model);
    await vCard.locator('[data-testid="est-by-step-trigger"]').click();
    const table = vCard.locator('[data-testid="est-step-table"]');
    await expect(table).toBeVisible();
    const rows = await table.locator('tbody tr').allInnerTexts();
    return rows.map(squash);
  };

  const p1Opus = await steps(1, OPUS);
  expectAll(p1Opus[0], ['build-phase', 'within'], 'phase 1 · Opus build');
  expectAll(p1Opus[1], ['verify-phase', '6.0k tok (3.0k–9.0k)', '15.0k tok', 'above'], 'phase 1 · Opus verify');
  expectAll(p1Opus[2], ['fix-issues', 'insufficient data (n=2)'], 'phase 1 · Opus fix');
  expectAll(p1Opus[3], ['after handoff', 'no precedent', 'insufficient data (n=0)'], 'phase 1 · Opus after handoff');

  const p1Sonnet = await steps(1, SONNET);
  expectAll(p1Sonnet[0], ['build-phase', 'below'], 'phase 1 · Sonnet build');

  const p2Opus = await steps(2, OPUS);
  expectAll(p2Opus[0], ['build-phase', 'above'], 'phase 2 · Opus build');
  expectAll(p2Opus[1], ['verify-phase', '9.0k tok (6.0k–12.0k)', '10.0k tok', 'within'], 'phase 2 · Opus verify');
  expectAll(p2Opus[2], ['fix-issues', 'insufficient data (n=1)'], 'phase 2 · Opus fix');
});

test('REQ-UI-078 — the Estimates page lays out at 1280 and 390 with nothing overlapping, no sideways page scroll and no page error', async ({ page }) => {
  const errors = collectErrors(page);
  await signIn(page, USER2);
  const ids = ['repo-picker', 'est-separation-note', 'est-card', 'est-card-head', 'est-parts', 'est-rowless',
    'est-by-step', 'est-by-row'];

  for (const [size, name] of [[DESKTOP, 'desktop'], [MOBILE, 'mobile']] as const) {
    await page.setViewportSize(size);
    await openEstimates(page, APP);
    const vCard = await card(page, 1, OPUS);
    await vCard.locator('[data-testid="est-by-row-trigger"]').click();
    await expect(vCard.locator('[data-testid="est-row-table"]')).toBeVisible();
    await page.screenshot({ path: path.join(WORK, `estimates-${name}.png`) });
    await vCard.scrollIntoViewIfNeeded();
    await vCard.screenshot({ path: path.join(WORK, `estimates-${name}-card.png`) });
    expect(await visualCheck(page, ids, size.width), `layout at ${size.width}`).toEqual([]);

    // The drill-down sits inside the card: its first column starts inside the card's left edge, so
    // nothing of a row id is cut off ("EQ-FN-001" for REQ-FN-001 was the defect this guards).
    const cardBox = (await vCard.boundingBox())!;
    const firstHead = (await vCard.locator('[data-testid="est-row-table"] th').first().boundingBox())!;
    expect(firstHead.x, `By row's first column starts inside the card at ${size.width}`).toBeGreaterThanOrEqual(cardBox.x);
  }
  expect(errors, 'no page or console error').toEqual([]);
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-079 — the per-row drill-down
// ---------------------------------------------------------------------------------------------

test('REQ-UI-079 — By row on the Sample app phase 1 Opus card shows exactly the per-row figures of BRD §3.1', async ({ page }) => {
  await signIn(page, USER2);
  await openEstimates(page, APP);

  const vCard = await card(page, 1, OPUS);
  await vCard.locator('[data-testid="est-by-row-trigger"]').click();
  const table = vCard.locator('[data-testid="est-row-table"]');
  await expect(table).toBeVisible();

  const lines = (await table.locator('tbody tr').allInnerTexts()).map(squash);
  expect(lines, 'one line per checklist row of the batch').toHaveLength(3);

  const line = (id: string) => {
    const found = lines.find(l => l.startsWith(id));
    expect(found, `${id} has a line`).toBeTruthy();
    return found!;
  };

  // After handoff has no precedent on every row: the basis reads `none` alone, as the mockup draws it.
  const handoff = ['no precedent', 'none · actual: insufficient data (n=0)'];
  for (const l of lines) {
    expect(l, 'no run count beside a no-precedent basis').not.toMatch(/none n=/);
  }
  expectAll(line('REQ-FN-001'), ['15.0k tok (10.0k–20.0k)', 'insufficient data (n=2)', '4.5k tok (2.5k–6.5k)', ...handoff], 'REQ-FN-001');
  expectAll(line('REQ-FN-002'), ['15.0k tok (10.0k–20.0k)', 'insufficient data (n=1)', '4.5k tok (2.5k–6.5k)',
    '11.0k tok · 25m00s', 'apportioned', ...handoff], 'REQ-FN-002');
  expectAll(line('REQ-UI-001'), ['12.0k tok (8.0k–16.0k)', 'insufficient data (n=2)', '4.5k tok (2.5k–6.5k)',
    '9.0k tok · 20m00s', 'apportioned', ...handoff], 'REQ-UI-001');

  // Nothing on a row line is summed into a per-row total.
  const headers = (await table.locator('thead th').allInnerTexts()).map(squash);
  expect(headers.join(' | ').toLowerCase(), 'no per-row total column').not.toContain('total');
});

// ---------------------------------------------------------------------------------------------
// REQ-UI-080 — empty and partial states in words
// ---------------------------------------------------------------------------------------------

test('REQ-UI-080 — with the Sample empty test file loaded, the page shows no estimate yet and no card, table, number or 0', async ({ page }) => {
  await signIn(page, USER2);
  await openEstimates(page, EMPTY);

  await expect(await testid(page, 'est-empty')).toContainText(/no estimate yet/i);
  await expect(page.locator('[data-testid="est-card"]')).toHaveCount(0);
  await expect(page.locator('[data-testid="estimates-page"] table')).toHaveCount(0);

  // The page body below its header (title, picker and scope line) carries no digit at all.
  const body = squash(await page.locator('[data-testid="est-empty"]').innerText());
  expect(body, 'the empty state holds no number and no 0').not.toMatch(/\d/);
});

test('REQ-UI-080 — with All repos, one card per repository with estimates and a line naming the repository with none', async ({ page }) => {
  await signIn(page, USER2);
  await gotoScreen(page, '/estimates');
  await ensureFramework(page, 'TechieFlow');
  await (await testid(page, 'repo-picker')).click();
  await page.locator('[role="option"]').filter({ hasText: /^\s*All repos\s*$/ }).first().click();
  await page.waitForTimeout(1500);

  // Sample app's three cards, never one that adds the two repositories together.
  await expect(page.locator('[data-testid="est-card"]')).toHaveCount(3);
  await expect(page.locator('[data-testid="est-card"]').filter({ hasNotText: 'SampleApp' })).toHaveCount(0);
  await expect(await testid(page, 'est-repos-without')).toContainText(/no estimate yet/i);
  await expect(await testid(page, 'est-repos-without')).toContainText(/sample[ -]empty/i);
});

test('REQ-UI-080 — the Playbook state reads no estimate yet, with no card', async ({ page }) => {
  await signIn(page, USER2);
  await gotoScreen(page, '/estimates');
  await ensureFramework(page, 'Playbook');
  try {
    await expect(await testid(page, 'est-empty')).toContainText(/no estimate yet/i);
    await expect(await testid(page, 'est-empty')).toContainText('The Playbook writes no estimate stream');
    await expect(page.locator('[data-testid="est-card"]')).toHaveCount(0);
  } finally {
    await ensureFramework(page, 'TechieFlow');
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-NFR-027 — Owner UAT time is fixed text; nothing on the page accepts a value for it
// ---------------------------------------------------------------------------------------------

test('REQ-NFR-027 — on /estimates Owner UAT time reads exactly not measured and the page has no input that takes a value', async ({ page }) => {
  await signIn(page, USER2);
  await openEstimates(page, APP);

  const uat = page.locator('[data-testid="est-part-uat"]');
  await expect(uat).toHaveCount(3);
  for (const text of await uat.allInnerTexts()) {
    const cells = squash(text);
    expect(cells, 'Owner UAT time reads not measured').toContain('not measured');
    expect(cells.replace('Owner UAT time', ''), 'no figure is printed for Owner UAT time').not.toMatch(/\d/);
  }
  await expect(page.locator('[data-testid="estimates-page"] input, [data-testid="estimates-page"] textarea'))
    .toHaveCount(0);
});
