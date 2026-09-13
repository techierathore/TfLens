// `/repos` sync and import — black-box acceptance for REQ-FN-065 … REQ-FN-141 (the import-mode rows).
//
// Every mutating test signs in as USER2 (the documented second test user) and removes every source it
// creates through the app's own Remove path, in a `finally`, so USER2 ends as it began. USER1 is never
// touched. Nothing here reads the database or imports application code: the evidence is what the
// browser shows and what the two import endpoints the page itself calls (`/api/import/preview`,
// `/api/import/commit`) answer on the wire.
//
// Fixtures are built into tests/.artifacts/verify-repos-import/ from this repository's own
// docs/metrics streams (real telemetry) or are small hand-written files that follow the real record
// shapes (docs/metrics/runs.jsonl for runs; docs/Phase-Efficiency-TfLens-Contract.md §3 for schema-2
// phase metrics; docs/Miss-Telemetry-TfLens-From-AIFP.md for the Playbook miss export). The import
// service recognises a stream by its FILE NAME, so every loose fixture lives in its own directory
// under the stream's real name (runs.jsonl, phase-metrics.ndjson, playbook-misses.ndjson).
import { test, Page, Response } from '@playwright/test';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { signIn, gotoScreen, testid, expect, USER2 } from './_helpers';

const ROOT = process.cwd();
const WORK = path.join(ROOT, 'tests', '.artifacts', 'verify-repos-import');
const METRICS = path.join(ROOT, 'docs', 'metrics');

/** Every source this file creates is `verifyri/vri-*`, so the sweep can never touch anything else. */
const OWNER = 'verifyri';
const PREFIX = 'vri-';

const F = {
  docsMetricsZip: path.join(WORK, 'docs-metrics.zip'),
  noMissesZip: path.join(WORK, 'no-misses.zip'),
  rollupZip: path.join(WORK, 'rollup-bundle.zip'),
  tflensJsonZip: path.join(WORK, 'tflens-json-bundle.zip'),
  snapshotZip: path.join(WORK, 'exported-snapshot-bundle.zip'),
  notesTxt: path.join(WORK, 'notes.txt'),
  emptyRuns: path.join(WORK, 'empty', 'runs.jsonl'),
  bigRuns: path.join(WORK, 'big', 'runs.jsonl'),
  previewRuns: path.join(WORK, 'preview', 'runs.jsonl'),
  verbatimRuns: path.join(WORK, 'verbatim', 'runs.jsonl'),
  fanoutRuns: path.join(WORK, 'fanout', 'runs.jsonl'),
  twinRuns: path.join(WORK, 'twin', 'runs.jsonl'),
  phaseMetrics: path.join(WORK, 'phase', 'phase-metrics.ndjson'),
  pbMisses: path.join(WORK, 'pbmisses-v1', 'playbook-misses.ndjson'),
  pbMissesV2: path.join(WORK, 'pbmisses-v2', 'playbook-misses.ndjson'),
};

const PHASE_IDS = ['verifyri-phase-0000-0000-000000000001', 'verifyri-phase-0000-0000-000000000002'];
const FANOUT_CMD = 'verifyri-fanout-probe';

// Default mode (workers=1 keeps the order): one row's failure must not stop the next row being graded.
test.describe.configure({ mode: 'default' });
test.setTimeout(420_000);

// ---------------------------------------------------------------------------------------------
// Fixtures
// ---------------------------------------------------------------------------------------------

function write(file: string, text: string) {
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text);
}

function zip(target: string, cwd: string, members: string[]) {
  fs.rmSync(target, { force: true });
  execFileSync('zip', ['-q', '-X', target, ...members], { cwd });
}

/** A real run record from this repository's own stream, with the three fan-out fields removed. */
function baseRun(): Record<string, unknown> {
  const first = fs.readFileSync(path.join(METRICS, 'runs.jsonl'), 'utf8').split('\n').find(l => l.trim())!;
  const run = JSON.parse(first) as Record<string, unknown>;
  delete run.subagent_runs;
  delete run.tokens_out_subagents;
  delete run.model_tokens_out;
  return run;
}

function runLine(overrides: Record<string, unknown>): string {
  return JSON.stringify({ ...baseRun(), app: 'VerifyRi', ...overrides });
}

function phaseMetric(id: string, phase: string, start: string, end: string, elapsedMs: number): string {
  const tokens = { input: 31203, output: 7900, reasoning: 1220, cache_read: 16000, cache_write: 1010 };
  const childTokens = { input: 6100, output: 1510, reasoning: 330, cache_read: 2100, cache_write: 900 };
  return JSON.stringify({
    schema: 2, kind: 'phase-metric', phase_execution_id: id, phase,
    started_at: start, ended_at: end, elapsed_ms: elapsedMs, complete: true, end_reason: 'idle',
    model: 'anthropic/claude-sonnet-5',
    models: [{ model: 'anthropic/claude-sonnet-5', turns: 12, tokens, tokens_in: 48213, tokens_out: 9120,
      cost_usd: 0.41, cost_status: 'complete', active_ms: 78000 }],
    tokens, tokens_in: 48213, tokens_out: 9120, cost_usd: 0.41, attempt: 2, gate_verdict: 'FAIL',
    project_type: 'dotnet-react', timestamp: end, session_id: `ses-${id}`, harness: 'opencode',
    granularity: 'message', turns: 14,
    observed_active_effort: { assistant_elapsed_ms: 78000, tool_elapsed_ms: 31000, observed_active_ms: 84000, coverage: 'complete' },
    data_quality: { valid: true, issues: [], token_status: 'complete', cost_status: 'complete' },
    tokens_scope: 'tree',
    subagents: {
      count: 1, spawned: 1, contributors: 1, tokens: childTokens, tokens_in: 9100, tokens_out: 1840,
      cost_usd: 0.06, cost_status: 'complete',
      sessions: [{ session_id: `child-${id}`, parent_id: `ses-${id}`, started_at: start, ended_at: end,
        elapsed_ms: 60000, complete: true, turns: 3, tokens: childTokens, tokens_in: 9100, tokens_out: 1840,
        cost_usd: 0.06, cost_status: 'complete', models: [] }],
    },
  });
}

const WINDOW = '"source_window":{"complete":true,"valid":true},"data_quality":{"valid":true,"cost_status":"complete"}';
const PB_MISSES_V1 = [
  `{"kind":"miss","ts":"2026-08-30T09:00:00Z","miss_id":"VRI-PB-1","why_missed":"missing-checklist-item","miss_class":"wrong-behaviour","found_phase_gate":"verify","item_id":"VRI-ITEM-1",${WINDOW}}`,
  `{"kind":"miss","ts":"2026-08-30T09:05:00Z","miss_id":"VRI-PB-2","miss_class":"missing-feature","found_phase_gate":"plan-review","item_id":"VRI-ITEM-2",${WINDOW}}`,
  `{"kind":"miss-fix","ts":"2026-08-30T10:00:00Z","miss_id":"VRI-PB-1","fix_run_id":"vri-run-1","cost_attribution":"sole","tokens_out":100,${WINDOW}}`,
  `{"kind":"miss-amend","ts":"2026-08-30T11:00:00Z","miss_id":"VRI-PB-2","field":"why_missed","value":"ambiguous-acceptance"}`,
];
// The superset a later export would carry: one wholly new miss, and one new source line that repeats
// an EXISTING miss_id with different content. On a miss_id key that second line would collapse; on the
// Playbook's own source-line identity it is a new record.
const PB_MISSES_V2 = [
  ...PB_MISSES_V1,
  `{"kind":"miss","ts":"2026-08-30T12:00:00Z","miss_id":"VRI-PB-1","why_missed":"missing-checklist-item","miss_class":"wrong-behaviour","found_phase_gate":"gap-report","item_id":"VRI-ITEM-1",${WINDOW}}`,
  `{"kind":"miss","ts":"2026-08-30T13:00:00Z","miss_id":"VRI-PB-3","miss_class":"wrong-behaviour","found_phase_gate":"verify","item_id":"VRI-ITEM-3",${WINDOW}}`,
];

test.beforeAll(() => {
  fs.rmSync(WORK, { recursive: true, force: true });
  fs.mkdirSync(WORK, { recursive: true });

  // Real telemetry: this repository's own five streams, and the same bundle without misses.jsonl.
  zip(F.docsMetricsZip, METRICS, ['runs.jsonl', 'gates.jsonl', 'sessions.jsonl', 'commits.jsonl', 'misses.jsonl']);
  zip(F.noMissesZip, METRICS, ['runs.jsonl', 'gates.jsonl', 'sessions.jsonl', 'commits.jsonl']);

  // The three precomputed shapes BRD-140 refuses: a tf-metrics rollup, a tflens.json, an exported snapshot.
  write(path.join(WORK, 'rollup', 'rollup.json'), JSON.stringify({
    generated_at: '2026-08-28T10:00:00Z', framework: 'techieflow',
    totals: { runs: 11, gates: 225, sessions: 7, commits: 10 }, first_pass_rate: 0.82, escape_rate: 0.04,
  }, null, 2));
  zip(F.rollupZip, path.join(WORK, 'rollup'), ['rollup.json']);
  const snapshotJson = JSON.stringify({
    per_repo: [{ repo: 'acme/app', app: 'app', project_type: 'app', gates: 214, runs: 41, sessions: 21, commits: 101,
      misses: 0, framework: 'techieflow', source_sha: '30e66161343661d94b8bd4b01e97c63a30b1c579', source_kind: 'api' }],
    live: { app: { records: 835, first_pass_rate: '50%', escape_rate: '23%' } },
    pooled: { runs_total: 300, tokens_total: 30065520, commits: 274 },
    misses: { misses_total: 244 }, phases: { runs_live: 300 }, parity: {},
  }, null, 2);
  write(path.join(WORK, 'tflens', 'tflens.json'), snapshotJson);
  zip(F.tflensJsonZip, path.join(WORK, 'tflens'), ['tflens.json']);
  write(path.join(WORK, 'snapshot', 'tflens.json'), snapshotJson);
  write(path.join(WORK, 'snapshot', 'snapshot.md'),
    '# TfLens snapshot — 2026-09-11\n\nParser 1.2.0 · techieflow\n\n| Repo | Runs | Gates |\n|---|---|---|\n| acme/app | 41 | 214 |\n');
  zip(F.snapshotZip, path.join(WORK, 'snapshot'), ['snapshot.md', 'tflens.json']);

  // Type and size gates.
  write(F.notesTxt, 'these are notes, not telemetry\n');
  write(F.emptyRuns, '');
  {
    const line = runLine({ cmd: 'build-phase', ts: '2026-09-05T10:00:00Z' }) + '\n';
    const copies = Math.ceil((26 * 1024 * 1024) / Buffer.byteLength(line));
    write(F.bigRuns, line.repeat(copies));
  }

  write(F.previewRuns, [
    runLine({ cmd: 'build-phase', started: '2026-09-05T08:00:00Z', ended: '2026-09-05T08:10:00Z', duration_s: 600, ts: '2026-09-05T08:10:05Z' }),
    runLine({ cmd: 'verify-phase', started: '2026-09-05T09:00:00Z', ended: '2026-09-05T09:10:00Z', duration_s: 600, ts: '2026-09-05T09:10:05Z' }),
  ].join('\n') + '\n');

  // Two valid runs and one malformed line — the malformed line only survives a replay if the raw
  // archive holds the uploaded bytes verbatim rather than the parsed records.
  write(F.verbatimRuns, [
    runLine({ cmd: 'build-phase', started: '2026-09-06T08:00:00Z', ended: '2026-09-06T08:20:00Z', duration_s: 1200, ts: '2026-09-06T08:20:05Z' }),
    'this line is not json and must be counted, skipped and archived verbatim',
    runLine({ cmd: 'verify-phase', started: '2026-09-06T09:00:00Z', ended: '2026-09-06T09:20:00Z', duration_s: 1200, ts: '2026-09-06T09:20:05Z' }),
  ].join('\n') + '\n');

  // BRD-145 — one run with none of the three fan-out fields and an unknown field; one with
  // subagent_runs: 0; one with a two-model split. All tree-scoped, so the fan-out band can see them.
  write(F.fanoutRuns, [
    runLine({ cmd: FANOUT_CMD, started: '2026-09-05T11:00:00Z', ended: '2026-09-05T11:30:00Z', duration_s: 1800, ts: '2026-09-05T11:30:05Z', tokens_scope: 'tree', verifyri_unknown_probe: 'kept' }),
    runLine({ cmd: FANOUT_CMD, started: '2026-09-05T12:00:00Z', ended: '2026-09-05T12:30:00Z', duration_s: 1800, ts: '2026-09-05T12:30:05Z', tokens_scope: 'tree', subagent_runs: 0, tokens_out_subagents: 0, model_tokens_out: { 'claude-opus-5': 1000 } }),
    runLine({ cmd: FANOUT_CMD, started: '2026-09-05T13:00:00Z', ended: '2026-09-05T13:30:00Z', duration_s: 1800, ts: '2026-09-05T13:30:05Z', tokens_scope: 'tree', subagent_runs: 2, tokens_out_subagents: 500, model_tokens_out: { 'claude-opus-5': 1500, 'claude-sonnet-5': 500 } }),
  ].join('\n') + '\n');

  // BRD-199 — two byte-identical run lines.
  {
    const twin = runLine({ cmd: 'log-miss', started: '2026-09-07T10:00:00Z', ended: '2026-09-07T10:01:00Z', duration_s: 60, ts: '2026-09-07T10:01:02Z' });
    write(F.twinRuns, `${twin}\n${twin}\n`);
  }

  write(F.phaseMetrics, [
    phaseMetric(PHASE_IDS[0], 'verify', '2026-08-31T09:10:00.000Z', '2026-08-31T09:12:00.000Z', 120000),
    '{not json — a malformed phase line is counted and skipped, never a zero-valued run',
    phaseMetric(PHASE_IDS[1], 'implement', '2026-08-31T10:00:00.000Z', '2026-08-31T10:05:00.000Z', 300000),
  ].join('\n') + '\n');

  write(F.pbMisses, PB_MISSES_V1.join('\n') + '\n');
  write(F.pbMissesV2, PB_MISSES_V2.join('\n') + '\n');
});

// ---------------------------------------------------------------------------------------------
// Black-box helpers
// ---------------------------------------------------------------------------------------------

type StreamRow = { stream: string; records?: number; invalidLines?: number; duplicatesCollapsed?: number;
  presented?: number; added?: number };
type ImportBody = { accepted: boolean; reason?: string; message?: string; bundleSha?: string; framework?: string;
  totalRecords?: number; totalInvalidLines?: number; unknownFields?: string[]; streams?: StreamRow[];
  recordsAdded?: number; duplicatesCollapsed?: number };

const sha256 = (file: string) => crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex');
const source = (short: string) => `${OWNER}/${short}`;
const streamOf = (body: ImportBody, name: string) => (body.streams ?? []).find(s => s.stream === name);

/** Relative times tick between two reads of the same page; nothing else may change. */
const normalise = (text: string) => text
  .replace(/\b(just now|today|yesterday)\b/gi, '<rel>')
  .replace(/\b\d+\s*(seconds?|secs?|minutes?|mins?|hours?|hrs?|days?|[smhd])\b( ago)?/gi, '<rel>')
  .replace(/\s+/g, ' ')
  .trim();

async function sourceShorts(page: Page): Promise<string[]> {
  await gotoScreen(page, '/repos');
  return page.$$eval('[data-testid^="repo-source-"]',
    els => els.map(e => (e.getAttribute('data-testid') || '').replace(/^repo-source-/, '')));
}

/** True when USER2 holds nothing but the given sources — only then is a pooled figure this test's own. */
async function onlyMine(page: Page, shorts: string[]): Promise<boolean> {
  const all = await sourceShorts(page);
  return all.length === shorts.length && all.every(s => shorts.includes(s));
}

async function openImportMode(page: Page) {
  await gotoScreen(page, '/repos');
  await page.click('[data-testid="connect-repo"]');
  await testid(page, 'source-mode');
  await page.click('[data-testid="source-mode-import"]');
  await testid(page, 'import-drop');
}

/** Chooses files in the drop zone and returns what the preview endpoint answered. */
async function choose(page: Page, files: string[]): Promise<{ status: number; body: ImportBody }> {
  const answer = page.waitForResponse(r => r.url().includes('/api/import/preview') && r.request().method() === 'POST',
    { timeout: 90_000 });
  await page.setInputFiles('#tflens-import-file', files);
  const res: Response = await answer;
  const body = (await res.json()) as ImportBody;
  await page.locator('[data-testid="import-preview"], [data-testid="import-refusal"]').first()
    .waitFor({ state: 'visible', timeout: 60_000 });
  return { status: res.status(), body };
}

/** Presses Import and returns what the commit endpoint answered, once the grid is back. */
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

async function importNew(page: Page, short: string, files: string[]) {
  await openImportMode(page);
  await page.fill('[data-testid="import-name"]', source(short));
  const preview = await choose(page, files);
  expect(preview.body.accepted, `preview refused: ${preview.body.message}`).toBe(true);
  const committed = await commit(page);
  expect(committed.accepted, `commit refused: ${committed.message}`).toBe(true);
  await testid(page, `repo-source-${short}`, 60_000);
  return { preview: preview.body, commit: committed };
}

async function reimport(page: Page, short: string, files: string[]) {
  await gotoScreen(page, '/repos');
  await page.click(`[data-testid="repo-reimport-${short}"]`);
  await testid(page, 'import-drop');
  expect(await page.inputValue('[data-testid="import-name"]')).toBe(source(short));
  const preview = await choose(page, files);
  expect(preview.body.accepted, `re-import preview refused: ${preview.body.message}`).toBe(true);
  const committed = await commit(page);
  expect(committed.accepted, `re-import commit refused: ${committed.message}`).toBe(true);
  return { preview: preview.body, commit: committed };
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

async function rowText(page: Page, short: string): Promise<string> {
  await gotoScreen(page, '/repos');
  return page.locator(`[data-testid="repo-source-${short}"]`)
    .evaluate(el => (el.closest('tr') as HTMLElement).innerText.replace(/\s+/g, ' ').trim());
}

/** The Records cell of the source's grid row. */
async function gridRecords(page: Page, short: string): Promise<number> {
  await gotoScreen(page, '/repos');
  const headers = await page.$$eval('[data-testid="repos-table"] thead th', ths => ths.map(t => (t as HTMLElement).innerText.trim()));
  const col = headers.indexOf('Records');
  expect(col, 'the grid has a Records column').toBeGreaterThanOrEqual(0);
  const cell = await page.locator(`[data-testid="repo-source-${short}"]`)
    .evaluate((el, c) => ((el.closest('tr') as HTMLElement).querySelectorAll('td')[c] as HTMLElement).innerText, col);
  return Number(cell.replace(/[^\d]/g, ''));
}

/** Coverage's per-source stream table: the Records figure for one stream, or null when it has no row. */
async function coverageStream(page: Page, short: string, stream: string): Promise<number | null> {
  const loc = page.locator(`[data-testid="stream-name-${short}-${stream}"]`);
  if ((await loc.count()) === 0) return null;
  const cells = await loc.first().evaluate(el =>
    Array.from((el.closest('tr') as HTMLElement).querySelectorAll('td')).map(td => (td as HTMLElement).innerText.trim()));
  return Number((cells[1] ?? '').replace(/[^\d]/g, ''));
}

async function switchFramework(page: Page, label: 'TechieFlow' | 'Playbook') {
  const sw = await testid(page, 'framework-switch');
  const tab = sw.locator('[role="tab"]').filter({ hasText: label }).first();
  if ((await tab.count()) > 0) await tab.click();
  else await sw.getByText(label, { exact: false }).first().click();
  await page.waitForTimeout(2500);
}

async function mainText(page: Page, route: string): Promise<string> {
  await gotoScreen(page, route);
  await page.waitForTimeout(800);
  return normalise(await page.locator('main').first().innerText());
}

// ---------------------------------------------------------------------------------------------
// REQ-FN-065 / 068 / 069 — the Playbook SYNC rows. Not producible black-box.
// ---------------------------------------------------------------------------------------------

test('REQ-FN-065 — syncing a Playbook repo fetches, archives raw and parses events.ndjson into Playbook-only tables', async () => {
  test.skip(true,
    'Needs a connected public Playbook repo that currently commits verification/telemetry/events.ndjson, synced from ' +
    'GitHub. No such repo exists (the file is transient and rotates, BRD-73 as amended), and a GitHub fetch cannot be ' +
    'produced black-box from this harness. The Import path is not the acceptance\'s trigger and is not substituted for it.');
});

test('REQ-FN-068 — the Playbook adapter\'s first run records the observed field names before any column is fixed', async () => {
  test.skip(true,
    'The trigger is the Playbook adapter\'s FIRST run from a /repos sync, and the property is an ordering in project ' +
    'history (the DECISIONS.md entry predates the schema commit). Neither a first adapter run against a real Playbook ' +
    'repo nor that history is observable through the browser.');
});

test('REQ-FN-069 — a Playbook repo emitting schema-v1 streams syncs through the same parser and engine, tagged Playbook', async () => {
  test.skip(true,
    'Needs a Playbook-tagged repo that emits docs/metrics/*.jsonl, connected and synced from GitHub. None is available, ' +
    'a GitHub fetch cannot be produced black-box, and Import mode cannot stand in: it tags schema-v1 files by their ' +
    'layout, so a docs/metrics bundle is always recorded as techieflow, never as a Playbook repo.');
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-071 — the miss stream, present and absent
// ---------------------------------------------------------------------------------------------

test('REQ-FN-071 — a bundle with misses.jsonl previews and parses the miss stream; one without it imports with an empty miss stream and no error', async ({ page }) => {
  const WITH = `${PREFIX}misses-present`;
  const WITHOUT = `${PREFIX}misses-absent`;
  await signIn(page, USER2);
  try {
    // With: the fifth stream is recognised in the preview and parsed on commit.
    const withMisses = await importNew(page, WITH, [F.docsMetricsZip]);
    const previewMisses = streamOf(withMisses.preview, 'misses');
    expect(previewMisses, 'the preview lists no misses stream for a bundle carrying misses.jsonl').toBeTruthy();
    expect(previewMisses!.records!, 'the misses stream previews with no records').toBeGreaterThan(0);
    expect(previewMisses!.invalidLines, 'the real misses stream previews with invalid lines').toBe(0);
    const committedMisses = streamOf(withMisses.commit, 'misses');
    expect(committedMisses?.added, 'the misses stream was not parsed into rows on commit').toBe(previewMisses!.records);

    await gotoScreen(page, '/');
    expect(await coverageStream(page, WITH, 'misses'), 'Coverage does not show the parsed miss stream for the source')
      .toBe(previewMisses!.records);

    // Without: accepted, previewed and imported — no refusal, no error, and no miss records.
    await openImportMode(page);
    await page.fill('[data-testid="import-name"]', source(WITHOUT));
    const preview = await choose(page, [F.noMissesZip]);
    expect(preview.status).toBe(200);
    expect(preview.body.accepted, `a bundle without misses.jsonl was refused: ${preview.body.message}`).toBe(true);
    await expect(page.locator('[data-testid="import-refusal"]')).toHaveCount(0);
    expect(streamOf(preview.body, 'misses')?.records ?? 0, 'an absent miss stream previewed as records').toBe(0);
    expect(streamOf(preview.body, 'misses')?.invalidLines ?? 0, 'an absent miss stream previewed as invalid lines').toBe(0);
    const committed = await commit(page);
    expect(committed.accepted, `a bundle without misses.jsonl failed to import: ${committed.message}`).toBe(true);
    expect(streamOf(committed, 'misses')?.added ?? 0).toBe(0);

    const badge = (await (await testid(page, `repo-status-${WITHOUT}`, 60_000)).innerText()).trim();
    expect(badge.toLowerCase(), 'the source without a miss stream reads as an error').not.toBe('error');
    await gotoScreen(page, '/');
    expect(await coverageStream(page, WITHOUT, 'misses') ?? 0, 'Coverage shows miss records for a source with none').toBe(0);
    await expect(page.locator(`[data-testid="repo-card-${WITHOUT}"]`)).toHaveCount(1);

    // The miss surface renders for the source-without, rather than failing. Pooled, so only read on a
    // quiet account.
    await removeSource(page, WITH);
    if (await onlyMine(page, [WITHOUT])) {
      await gotoScreen(page, '/misses');
      await switchFramework(page, 'TechieFlow');
      const text = (await page.locator('main').first().innerText()).replace(/\s+/g, ' ');
      expect(text, 'the miss page did not state an empty miss stream').toMatch(/No misses recorded yet/i);
      expect(text).not.toMatch(/something went wrong|unhandled|exception/i);
    } else {
      test.info().annotations.push({ type: 'note', description: 'USER2 held other sources; the pooled /misses empty-state check was not graded.' });
    }
  } finally {
    await removeSource(page, WITH);
    await removeSource(page, WITHOUT);
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-082 — size- and type-checked, previewed before anything is written
// ---------------------------------------------------------------------------------------------

test('REQ-FN-082 — an upload is size- and type-checked and previewed before anything is written', async ({ page }) => {
  const SHORT = `${PREFIX}preview-first`;
  await signIn(page, USER2);
  try {
    const before = await sourceShorts(page);

    // Import cannot be pressed before a preview has landed.
    await openImportMode(page);
    await page.fill('[data-testid="import-name"]', source(SHORT));
    await expect(page.locator('[data-testid="import-submit"]')).toBeDisabled();

    // Type gate.
    const typed = await choose(page, [F.notesTxt]);
    expect(typed.body.accepted).toBe(false);
    expect(typed.body.reason).toBe('UnsupportedExtension');
    expect((await page.locator('[data-testid="import-refusal"]').innerText())).toMatch(/\.jsonl|\.zip/);
    await expect(page.locator('[data-testid="import-preview"]')).toHaveCount(0);
    await expect(page.locator('[data-testid="import-submit"]')).toBeDisabled();

    // Size gate — a 26 MB stream file is over the 25 MB limit.
    expect(fs.statSync(F.bigRuns).size).toBeGreaterThan(25 * 1024 * 1024);
    const big = await choose(page, [F.bigRuns]);
    expect(big.body.accepted).toBe(false);
    expect(big.body.reason).toBe('TooLarge');
    expect(big.status).toBe(413);
    expect((await page.locator('[data-testid="import-refusal"]').innerText())).toMatch(/25 MB/);
    await expect(page.locator('[data-testid="import-submit"]')).toBeDisabled();

    // An empty stream file is reported, not partially ingested.
    const empty = await choose(page, [F.emptyRuns]);
    expect(empty.body.accepted).toBe(false);
    expect(empty.body.reason).toBe('Empty');
    await expect(page.locator('[data-testid="import-submit"]')).toBeDisabled();

    // A valid file previews: records per stream, date range, invalid lines — and a preview writes nothing.
    const ok = await choose(page, [F.previewRuns]);
    expect(ok.body.accepted).toBe(true);
    expect(streamOf(ok.body, 'runs')?.records).toBe(2);
    const summary = (await page.locator('[data-testid="import-preview-summary"]').innerText()).trim();
    expect(summary).toMatch(/2 records across 1 stream/);
    expect(summary).toMatch(/date range 2026-09-05/);
    expect(summary).toMatch(/0 invalid lines/);
    await expect(page.locator('[data-testid="import-submit"]')).toBeEnabled();

    await page.click('[data-testid="add-source-cancel"]');
    await page.waitForURL(/\/repos$/, { timeout: 20_000 });
    expect(await sourceShorts(page), 'an abandoned preview left a source behind').toEqual(before);

    // The strongest form of "wrote nothing": importing the very bytes that were previewed adds every
    // one of them — no row was left behind by the preview for the commit to collapse onto.
    const { commit: committed } = await importNew(page, SHORT, [F.previewRuns]);
    expect(committed.recordsAdded, 'the preview had already written rows').toBe(2);
    expect(committed.duplicatesCollapsed).toBe(0);
  } finally {
    await removeSource(page, SHORT);
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-083 — archived verbatim before parsing, then the shared parser
// ---------------------------------------------------------------------------------------------

test('REQ-FN-083 — a committed import archives the recognised bytes verbatim and replays them through the same parser', async ({ page }) => {
  const SHORT = `${PREFIX}verbatim`;
  const fileSha = sha256(F.verbatimRuns);
  await signIn(page, USER2);
  try {
    const { preview, commit: committed } = await importNew(page, SHORT, [F.verbatimRuns]);

    // The dataset identity is the sha256 of the uploaded bytes themselves — not of a re-serialisation.
    expect(preview.bundleSha, 'the preview sha is not the sha256 of the uploaded bytes').toBe(fileSha);
    expect(committed.bundleSha, 'the committed sha is not the sha256 of the uploaded bytes').toBe(fileSha);
    expect(streamOf(committed, 'runs')?.added).toBe(2);
    expect(streamOf(committed, 'runs')?.invalidLines).toBe(1);

    await gotoScreen(page, '/');
    const shaBadge = (await (await testid(page, `repo-sha-${SHORT}`, 30_000)).innerText()).trim();
    expect(shaBadge).toBe(`sha256 ${fileSha.slice(0, 7)}`);
    expect(await coverageStream(page, SHORT, 'runs')).toBe(2);

    // Rebuild from raw replays the archive through the parser. It drops and re-derives every parsed row
    // of THIS user, so it is only pressed when USER2 holds nothing but this source.
    if (!(await onlyMine(page, [SHORT]))) {
      test.info().annotations.push({ type: 'note', description: 'USER2 held other sources; the rebuild replay was not pressed.' });
      return;
    }
    await gotoScreen(page, '/');
    await (await testid(page, 'rebuild')).click();
    await (await testid(page, 'rebuild-confirm')).click();
    const perStream = await testid(page, 'rebuild-per-stream', 180_000);
    await page.waitForTimeout(1500);
    const report = (await page.locator('[data-testid="rebuild-report"]').innerText()).replace(/\s+/g, ' ');
    // One archived file; the malformed line is still in it (so the archive is the bytes, not the parse),
    // and replaying it through the parser reproduces the import's own counts.
    expect(report).toMatch(/Files replayed:\s*1\b/);
    expect(report).toMatch(/Records stored:\s*2\b/);
    expect(report, 'the malformed line did not survive in the raw archive').toMatch(/Invalid lines skipped:\s*1\b/);
    expect((await perStream.innerText()).replace(/\s+/g, ' ')).toMatch(/runs\s*2\b/);
    await gotoScreen(page, '/');
    expect(await coverageStream(page, SHORT, 'runs'), 'the replay did not reproduce the imported count').toBe(2);
  } finally {
    await removeSource(page, SHORT);
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-085 — the same bundle twice
// ---------------------------------------------------------------------------------------------

test('REQ-FN-085 — importing the same bundle twice collapses on the natural keys and no figure double-counts', async ({ page }) => {
  const SHORT = `${PREFIX}twice`;
  const POOLED = ['/', '/gate-outcomes', '/harness', '/routing', '/misses', '/effort'];
  await signIn(page, USER2);
  try {
    const first = await importNew(page, SHORT, [F.docsMetricsZip]);
    expect(first.commit.recordsAdded!).toBeGreaterThan(0);

    const quiet = await onlyMine(page, [SHORT]);
    const rowBefore = normalise(await rowText(page, SHORT));
    const recordsBefore = await gridRecords(page, SHORT);
    await gotoScreen(page, '/');
    const cardBefore = normalise(await page.locator(`[data-testid="repo-card-${SHORT}"]`).innerText());
    const pagesBefore: Record<string, string> = {};
    if (quiet) {
      await switchFramework(page, 'TechieFlow');
      for (const r of POOLED) pagesBefore[r] = await mainText(page, r);
    }

    const second = await reimport(page, SHORT, [F.docsMetricsZip]);
    expect(second.commit.bundleSha).toBe(first.commit.bundleSha);
    expect(second.commit.recordsAdded, 'the second import of the same bundle added records').toBe(0);
    for (const s of second.commit.streams ?? []) {
      expect(s.added, `${s.stream}: the second import added rows`).toBe(0);
      expect(s.duplicatesCollapsed, `${s.stream}: not every presented record collapsed`).toBe(s.presented);
    }

    expect(normalise(await rowText(page, SHORT)), 'the grid row changed after an identical re-import').toBe(rowBefore);
    expect(await gridRecords(page, SHORT)).toBe(recordsBefore);
    await gotoScreen(page, '/');
    expect(normalise(await page.locator(`[data-testid="repo-card-${SHORT}"]`).innerText()),
      'the Coverage card changed after an identical re-import').toBe(cardBefore);

    if (quiet && (await onlyMine(page, [SHORT]))) {
      for (const r of POOLED) {
        expect(await mainText(page, r), `${r}: a figure moved after importing the same bundle again`).toBe(pagesBefore[r]);
      }
    } else {
      test.info().annotations.push({ type: 'note', description: 'USER2 held other sources; the pooled-page comparison was not graded.' });
    }
  } finally {
    await removeSource(page, SHORT);
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-086 — precomputed rollups refused
// ---------------------------------------------------------------------------------------------

test('REQ-FN-086 — a precomputed rollup, a tflens.json and an exported snapshot are refused with a message naming what to upload', async ({ page }) => {
  const SHORT = `${PREFIX}rollup`;
  await signIn(page, USER2);
  try {
    const before = await sourceShorts(page);
    for (const [label, bundle] of [['rollup', F.rollupZip], ['tflens.json', F.tflensJsonZip], ['exported snapshot', F.snapshotZip]] as const) {
      await openImportMode(page);
      await page.fill('[data-testid="import-name"]', source(SHORT));
      const res = await choose(page, [bundle]);
      expect(res.body.accepted, `${label} was accepted`).toBe(false);
      expect(res.body.reason, `${label} was not refused as a precomputed rollup`).toBe('PrecomputedRollup');
      const text = (await page.locator('[data-testid="import-refusal"]').innerText()).replace(/\s+/g, ' ');
      expect(text, `${label}: the refusal does not name the telemetry directory to upload`).toContain('docs/metrics/');
      expect(text, `${label}: the refusal does not name the stream files`).toContain('runs.jsonl');
      await expect(page.locator('[data-testid="import-preview"]')).toHaveCount(0);
      await expect(page.locator('[data-testid="import-submit"]')).toBeDisabled();
      await page.click('[data-testid="add-source-cancel"]');
      await page.waitForURL(/\/repos$/, { timeout: 20_000 });
    }
    expect(await sourceShorts(page), 'a refused rollup left a source behind').toEqual(before);
  } finally {
    await removeSource(page, SHORT);
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-088 — fan-out fields nullable, unknown field to overflow
// ---------------------------------------------------------------------------------------------

test('REQ-FN-088 — a run with an unknown field previews with 0 invalid lines and the field listed as unknown; fan-out fields stay nullable', async ({ page }) => {
  const SHORT = `${PREFIX}fanout`;
  await signIn(page, USER2);
  try {
    await openImportMode(page);
    await page.fill('[data-testid="import-name"]', source(SHORT));
    const { body: preview } = await choose(page, [F.fanoutRuns]);
    expect(preview.accepted).toBe(true);
    const runs = streamOf(preview, 'runs');
    expect(runs?.records, 'not every run line parsed').toBe(3);
    expect(runs?.invalidLines, 'an unknown field made a run line invalid').toBe(0);
    expect(preview.totalInvalidLines).toBe(0);
    expect(preview.unknownFields, 'the unknown field is not listed').toContain('verifyri_unknown_probe');
    for (const known of ['subagent_runs', 'tokens_out_subagents', 'model_tokens_out']) {
      expect(preview.unknownFields, `${known} is treated as unknown`).not.toContain(known);
    }
    const summary = (await page.locator('[data-testid="import-preview-summary"]').innerText()).replace(/\s+/g, ' ');
    expect(summary).toMatch(/0 invalid lines/);
    expect(summary).toMatch(/verifyri_unknown_probe\) — kept in Overflow/);

    const committed = await commit(page);
    expect(committed.accepted).toBe(true);
    expect(streamOf(committed, 'runs')?.added).toBe(3);
    expect(streamOf(committed, 'runs')?.invalidLines).toBe(0);

    // Nullable, not defaulted: the run without the fields is NOT observed, while subagent_runs: 0 IS —
    // so 2 of this command's 3 runs are observed, and the absent one contributes no zero.
    await gotoScreen(page, '/effort');
    await switchFramework(page, 'TechieFlow');
    const fan = (await (await testid(page, `phase-fanout-${FANOUT_CMD}`, 30_000)).innerText()).replace(/\s+/g, ' ');
    expect(fan, 'absent fan-out fields were stored as zeros (or the zero as absent)').toMatch(/\b2 observed\b/);
    const alert = page.locator(`[data-testid="fanout-alert-${FANOUT_CMD}"]`);
    if ((await alert.count()) > 0) {
      expect((await alert.first().innerText()).replace(/\s+/g, ' ')).toMatch(/2 of 3 runs observed/);
    }
  } finally {
    await removeSource(page, SHORT);
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-094 / 095 — schema-2 phase metrics
// ---------------------------------------------------------------------------------------------

test('REQ-FN-094 — a schema-2 phase-metric bundle flows through the existing Import mode, with no second ingest path', async ({ page }) => {
  const SHORT = `${PREFIX}phase-import`;
  await signIn(page, USER2);
  try {
    // The same Add source → Import metric files mode every other bundle uses.
    await openImportMode(page);
    await page.fill('[data-testid="import-name"]', source(SHORT));
    const { body: preview } = await choose(page, [F.phaseMetrics]);
    expect(preview.accepted, `phase-metrics.ndjson was refused: ${preview.message}`).toBe(true);
    expect(preview.bundleSha, 'the dataset identity is not the bundle sha256').toBe(sha256(F.phaseMetrics));
    const streams = await page.$$eval('[data-testid="import-preview-streams"] tbody tr',
      rs => rs.map(r => (r.querySelector('td')?.textContent ?? '').trim()));
    expect(streams).toEqual(['phasemetrics']);
    const phase = streamOf(preview, 'phasemetrics')!;
    expect(phase.records, 'the two phase executions previewed no rows').toBeGreaterThan(0);
    expect(phase.invalidLines, 'the malformed phase line was not counted as invalid').toBe(1);

    const committed = await commit(page);
    expect(committed.accepted).toBe(true);
    expect(streamOf(committed, 'phasemetrics')?.added).toBe(phase.records);

    // It is an imported source like any other: Imported, Re-import, never Sync.
    await gotoScreen(page, '/repos');
    expect((await page.locator(`[data-testid="repo-source-${SHORT}"]`).innerText()).trim()).toBe('Imported');
    await expect(page.locator(`[data-testid="repo-reimport-${SHORT}"]`)).toHaveCount(1);
    await expect(page.locator(`[data-testid="repo-sync-${SHORT}"]`)).toHaveCount(0);

    // Re-import of the same NDJSON adds nothing and reports the duplicates collapsed.
    const again = await reimport(page, SHORT, [F.phaseMetrics]);
    expect(again.commit.recordsAdded, 're-importing the same phase NDJSON added rows').toBe(0);
    expect(streamOf(again.commit, 'phasemetrics')?.duplicatesCollapsed).toBe(phase.records);

    // Exactly the two executions reach the Playbook effort surface — the malformed line is not a
    // zero-valued run.
    await gotoScreen(page, '/effort');
    await switchFramework(page, 'Playbook');
    for (const id of PHASE_IDS) {
      await expect(page.locator(`[data-testid="pb-execution-open-${id}"]`), `${id} is not on the effort surface`).toHaveCount(1);
    }
    const ours = await page.$$eval('[data-testid^="pb-execution-open-verifyri-"]', els => els.length);
    expect(ours, 'a malformed phase line became an execution').toBe(PHASE_IDS.length);
  } finally {
    await removeSource(page, SHORT);
  }
});

test('REQ-FN-095 — schema-2 phase data occupies three tables, and removing the repo purges all three', async ({ page }) => {
  const SHORT = `${PREFIX}phase-purge`;
  await signIn(page, USER2);
  try {
    const first = await importNew(page, SHORT, [F.phaseMetrics]);
    const stored = streamOf(first.commit, 'phasemetrics')!;
    // Two executions, each with one model-usage row and one sub-agent row: three rows per execution,
    // one in each of the three tables.
    expect(stored.added, 'two executions did not store one row in each of three tables').toBe(PHASE_IDS.length * 3);

    // All three are read back: the execution, its per-model usage and its sub-agent.
    await gotoScreen(page, '/effort');
    await switchFramework(page, 'Playbook');
    await page.click(`[data-testid="pb-execution-open-${PHASE_IDS[0]}"]`);
    await page.waitForTimeout(1500);
    const models = (await (await testid(page, 'pb-detail-models')).innerText()).replace(/\s+/g, ' ');
    expect(models, 'the per-model usage row is not read back').toContain('anthropic/claude-sonnet-5');
    const subagents = (await (await testid(page, 'pb-detail-subagents')).innerText()).replace(/\s+/g, ' ');
    expect(subagents, 'the sub-agent row is not read back').toContain(`child-${PHASE_IDS[0]}`);

    // Remove, then prove the purge from the outside: nothing is left on the surface, and importing the
    // identical bytes under the same name adds EVERY row again. A single surviving row in any of the
    // three tables would have collapsed onto its upsert key and shown up as a duplicate.
    await removeSource(page, SHORT);
    await gotoScreen(page, '/effort');
    if ((await page.locator('[data-testid="framework-switch"]').count()) > 0) {
      await switchFramework(page, 'Playbook');
    }
    for (const id of PHASE_IDS) {
      await expect(page.locator(`[data-testid="pb-execution-open-${id}"]`), `${id} survived the removal`).toHaveCount(0);
    }
    const again = await importNew(page, SHORT, [F.phaseMetrics]);
    const after = streamOf(again.commit, 'phasemetrics')!;
    expect(after.duplicatesCollapsed, 'rows survived the removal in at least one of the three tables').toBe(0);
    expect(after.added).toBe(PHASE_IDS.length * 3);
  } finally {
    await removeSource(page, SHORT);
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-103 — the Playbook miss export
// ---------------------------------------------------------------------------------------------

test('REQ-FN-103 — the Playbook miss export lands in the existing miss tables, keyed on its own source-line identity', async ({ page }) => {
  const SHORT = `${PREFIX}pb-misses`;
  await signIn(page, USER2);
  try {
    const first = await importNew(page, SHORT, [F.pbMisses]);
    expect(first.preview.framework).toBe('playbook');
    const s1 = streamOf(first.commit, 'playbookmisses');
    expect(s1?.added, 'not every lifecycle line was stored').toBe(PB_MISSES_V1.length);
    expect(s1?.invalidLines).toBe(0);

    // The existing miss surface reads them on the Playbook axis, with the amendment folded.
    await gotoScreen(page, '/misses');
    await switchFramework(page, 'Playbook');
    await testid(page, 'pb-misses-surface', 30_000);
    await expect(page.locator('[data-testid="miss-item-VRI-ITEM-1"]')).toHaveCount(1);
    await expect(page.locator('[data-testid="miss-item-VRI-ITEM-2"]')).toHaveCount(1);
    await expect(page.locator('[data-testid="miss-whymissed-ambiguous-acceptance"]'),
      'the miss-amend was not folded into why_missed').toHaveCount(1);

    // Re-importing the same export duplicates no lifecycle record.
    const again = await reimport(page, SHORT, [F.pbMisses]);
    expect(again.commit.recordsAdded, 're-importing the same export added records').toBe(0);
    expect(streamOf(again.commit, 'playbookmisses')?.duplicatesCollapsed).toBe(PB_MISSES_V1.length);

    // Source-line identity, not miss_id: a later export adds exactly its two new lines — including the
    // one that repeats an existing miss_id with different content, which a miss_id key would collapse.
    const later = await reimport(page, SHORT, [F.pbMissesV2]);
    const s3 = streamOf(later.commit, 'playbookmisses');
    expect(streamOf(later.preview, 'playbookmisses')?.records, 'the later export does not preview as six distinct lines').toBe(PB_MISSES_V2.length);
    expect(s3?.added,
      `the new source lines were not keyed on their own identity (commit: presented ${s3?.presented}, added ${s3?.added}, ` +
      `collapsed ${s3?.duplicatesCollapsed}) — a new line that repeats miss_id VRI-PB-1 collapsed onto the existing one`)
      .toBe(PB_MISSES_V2.length - PB_MISSES_V1.length);
    expect(s3?.duplicatesCollapsed, 'the unchanged lines were not recognised as the same records').toBe(PB_MISSES_V1.length);
  } finally {
    await removeSource(page, SHORT);
  }
});

// ---------------------------------------------------------------------------------------------
// REQ-FN-141 — two byte-identical run lines
// ---------------------------------------------------------------------------------------------

test('REQ-FN-141 — a runs file with two byte-identical lines counts both as runs, and importing it again adds none', async ({ page }) => {
  const SHORT = `${PREFIX}twin-runs`;
  await signIn(page, USER2);
  try {
    const lines = fs.readFileSync(F.twinRuns, 'utf8').split('\n').filter(l => l.length > 0);
    expect(lines.length).toBe(2);
    expect(lines[0]).toBe(lines[1]);

    const first = await importNew(page, SHORT, [F.twinRuns]);
    expect(streamOf(first.preview, 'runs')?.records, 'the preview collapsed two byte-identical run lines').toBe(2);
    expect(streamOf(first.commit, 'runs')?.added, 'the commit did not store both byte-identical run lines').toBe(2);
    expect(await gridRecords(page, SHORT)).toBe(2);
    await gotoScreen(page, '/');
    expect(await coverageStream(page, SHORT, 'runs'), 'Coverage does not count both lines as runs').toBe(2);

    const second = await reimport(page, SHORT, [F.twinRuns]);
    expect(second.commit.recordsAdded, 'the second import of the same file added runs').toBe(0);
    expect(streamOf(second.commit, 'runs')?.duplicatesCollapsed).toBe(2);
    expect(await gridRecords(page, SHORT)).toBe(2);
    await gotoScreen(page, '/');
    expect(await coverageStream(page, SHORT, 'runs')).toBe(2);
  } finally {
    await removeSource(page, SHORT);
  }
});

// ---------------------------------------------------------------------------------------------
// Sweep: USER2 ends as it began.
// ---------------------------------------------------------------------------------------------

test.afterAll(async ({ browser }) => {
  const page = await browser.newPage();
  try {
    await signIn(page, USER2);
    for (const short of await sourceShorts(page)) {
      if (short.startsWith(PREFIX)) await removeSource(page, short);
    }
  } finally {
    await page.close();
  }
});
