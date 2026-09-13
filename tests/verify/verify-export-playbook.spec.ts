/*
 * Verify run — export, Playbook axis and gate separation.
 * REQ-FN-066, REQ-FN-067, REQ-FN-070, REQ-FN-080, REQ-FN-084, REQ-FN-087, REQ-FN-093.
 *
 * Black-box only. Signed in as the documented demo user (USER1) through the real /login form.
 * Everything asserted here is reachable by that user in a browser: the rendered pages, the header
 * Framework switch, the `export-now` button on /export, and the authenticated
 * `GET /api/export/download` endpoint the Past-snapshots table links to. Nothing here reads the
 * database, imports application code or reads a file under src/.
 *
 * The one write this file performs as USER1 is the one the export acceptance lines name: pressing
 * `export-now` on /export, which writes today's TechieFlow snapshot for the signed-in user. USER1 is
 * never given an import, a removal or a price edit.
 *
 * REQ-FN-067 needs Playbook records, which USER1 does not hold. That test alone signs in as test user 2
 * (USER2), imports the Playbook's own published self-test stream (tests/verify/fixtures/playbook-selftest,
 * copied unmodified — provenance in its README) through the /repos Import screen as a uniquely named
 * `cla-fn067-*` source, measures the populated Playbook axis, and in a `finally` removes that source
 * and puts the Framework switch back on TechieFlow.
 *
 * REQ-FN-093 runs tools/parity-compare.py as a child process on the tflens.json downloaded from
 * /export. The tool is read and run, never edited. The reference document it would normally be fed
 * comes from tf-metrics.sh over the raw streams, which a browser user cannot produce, so the compare
 * is driven with the downloaded document on both sides and with controlled copies of it: that is
 * enough to prove, on the real phases block, that every key is diffed, that null matches null, that
 * null against 0 is a finding and that share strings are compared as strings.
 */
import { test, Page, TestInfo } from '@playwright/test';
import { spawnSync } from 'node:child_process';
import * as fs from 'node:fs';
import * as path from 'node:path';
import { DESKTOP, USER1, USER2, expect, gotoScreen, signIn, testid } from './_helpers';

test.use({ viewport: DESKTOP });

const REPO_ROOT = path.resolve(__dirname, '..', '..');
const PARITY_TOOL = path.join(REPO_ROOT, 'tools', 'parity-compare.py');

type Json = any; // eslint-disable-line @typescript-eslint/no-explicit-any

/* ───────────────────────────── shared helpers ───────────────────────────── */

/** Drives the header Framework switch and returns the label of the trigger that is active after. */
async function switchFramework(page: Page, label: 'TechieFlow' | 'Playbook'): Promise<string> {
  const sw = await testid(page, 'framework-switch');
  const trigger = sw.locator('[role="tab"]').filter({ hasText: label }).first();
  if ((await trigger.count()) === 0) {
    await sw.getByText(label, { exact: false }).first().click();
  } else {
    await trigger.click();
  }
  await page.waitForTimeout(2_500);
  return page.evaluate(() => {
    const root = document.querySelector('[data-testid="framework-switch"]');
    if (!root) return '';
    const active = Array.from(root.querySelectorAll('[role="tab"]')).find(
      t => t.getAttribute('aria-selected') === 'true' || t.getAttribute('data-state') === 'active',
    );
    return (active?.textContent || '').replace(/\s+/g, ' ').trim();
  });
}

/** The switch's own labels, e.g. "TechieFlow 5 | Playbook 0" — used in skip reasons. */
async function switchLabels(page: Page): Promise<string> {
  return page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid="framework-switch"] [role="tab"]'))
      .map(t => (t.textContent || '').replace(/\s+/g, ' ').trim())
      .join(' | '),
  );
}

async function exists(page: Page, id: string): Promise<boolean> {
  return (await page.locator(`[data-testid="${id}"]`).count()) > 0;
}

async function textOf(page: Page, id: string): Promise<string> {
  if (!(await exists(page, id))) return '';
  const t = await page.locator(`[data-testid="${id}"]`).first().innerText().catch(() => '');
  return (t || '').replace(/\s+/g, ' ').trim();
}

/** Every data-testid in the DOM, minus the shell chrome. */
async function contentTestIds(page: Page): Promise<string[]> {
  return page.evaluate(() => {
    const chrome = /^(app-|nav-|sidebar-|framework-|user-|theme-|breadcrumb-|sync-|login-|toast|last-sync)/;
    return Array.from(document.querySelectorAll('[data-testid]'))
      .map(e => e.getAttribute('data-testid') || '')
      .filter(id => id.length > 0 && !chrome.test(id));
  });
}

/** GET an export file through the session; JSON is re-parsed after a second if a concurrent writer is mid-write. */
async function download(page: Page, href: string): Promise<{ status: number; body: string }> {
  const res = await page.request.get(href);
  return { status: res.status(), body: await res.text() };
}

async function downloadJson(page: Page, href: string): Promise<Json> {
  let lastError = '';
  for (let attempt = 0; attempt < 6; attempt++) {
    const { status, body } = await download(page, href);
    if (status === 200) {
      try {
        return JSON.parse(body);
      } catch (e) {
        lastError = `JSON.parse failed (${String(e).slice(0, 120)})`;
      }
    } else {
      lastError = `HTTP ${status}`;
    }
    await page.waitForTimeout(1_000);
  }
  throw new Error(`${href}: could not read a complete JSON document — ${lastError}`);
}

/** The anchor hrefs of the Past-snapshots table, as { date, framework, file, href }. */
async function snapshotLinks(page: Page): Promise<{ date: string; framework: string; file: string; href: string }[]> {
  const hrefs = await page
    .locator('a[href*="/api/export/download"]')
    .evaluateAll(as => as.map(a => (a as HTMLAnchorElement).getAttribute('href') || ''));
  return hrefs
    .map(href => {
      const q = new URL(href, 'http://x').searchParams;
      return { date: q.get('date') || '', framework: q.get('framework') || '', file: q.get('file') || '', href };
    })
    .filter(l => l.date && l.framework && l.file);
}

/** The newest Past-snapshots row for a framework; must be called on the TechieFlow-axis /export. */
async function latestSnapshot(page: Page, framework: 'techieflow' | 'playbook') {
  // The table renders after the circuit lists the folders; read it only once it (or its empty state) is there.
  await page
    .locator('[data-testid="snapshots-table"] a[href*="/api/export/download"], [data-testid="snapshots-empty"]')
    .first()
    .waitFor({ state: 'attached', timeout: 30_000 });
  const links = (await snapshotLinks(page)).filter(l => l.framework === framework);
  if (links.length === 0) return null;
  const date = links.map(l => l.date).sort().reverse()[0];
  const json = links.find(l => l.date === date && l.file === 'tflens.json');
  const md = links.find(l => l.date === date && l.file === 'snapshot.md');
  return { date, jsonHref: json?.href ?? null, mdHref: md?.href ?? null };
}

interface FreshExport {
  date: string;
  json: Json;
  md: string;
  jsonHref: string;
  mdHref: string;
}

let freshTechieFlow: FreshExport | null = null;

/**
 * Writes today's TechieFlow snapshot from /export (`export-now`), waits for today's row in Past
 * snapshots, and reads both files back through the authenticated download endpoint. The document read
 * back must have been generated at or after the press, so a stale file is never mistaken for ours.
 * Cached for the file: the suite runs on one worker, and one export per run is all the rows need.
 */
async function exportTechieFlowNow(page: Page): Promise<FreshExport> {
  if (freshTechieFlow) return freshTechieFlow;

  await gotoScreen(page, '/export');
  const active = await switchFramework(page, 'TechieFlow');
  expect(active, `the Framework switch did not settle on TechieFlow (reads "${active}")`).toMatch(/techieflow/i);
  await gotoScreen(page, '/export');

  const target = await textOf(page, 'export-target');
  const m = target.match(/(\d{4}-\d{2}-\d{2})\/techieflow\//);
  expect(m, `export-target does not name today's techieflow folder: "${target}"`).not.toBeNull();
  const date = m![1];

  const pressedAt = Date.now();
  const button = await testid(page, 'export-now');
  await expect(button, 'export-now must be enabled on the TechieFlow axis').toBeEnabled({ timeout: 20_000 });
  await button.click();

  const jsonLink = page.locator(`[data-testid="snapshot-json-${date}-techieflow"]`).first();
  await jsonLink.waitFor({ state: 'attached', timeout: 45_000 });
  const mdLink = page.locator(`[data-testid="snapshot-md-${date}-techieflow"]`).first();
  await mdLink.waitFor({ state: 'attached', timeout: 15_000 });
  const jsonHref = (await jsonLink.getAttribute('href')) || '';
  const mdHref = (await mdLink.getAttribute('href')) || '';
  expect(jsonHref).toContain('/api/export/download');
  expect(jsonHref).toContain('file=tflens.json');
  expect(mdHref).toContain('file=snapshot.md');

  // Poll until the document on disk is the one this press (or a concurrent later one) wrote.
  let json: Json = null;
  let generated = '';
  for (let i = 0; i < 30; i++) {
    json = await downloadJson(page, jsonHref);
    generated = json?.parity?.generated_ts ?? '';
    if (generated && Date.parse(generated) >= pressedAt - 5_000) break;
    await page.waitForTimeout(1_000);
  }
  expect(
    Date.parse(generated),
    `tflens.json was not rewritten by export-now: generated_ts=${generated}, pressed at ${new Date(pressedAt).toISOString()}`,
  ).toBeGreaterThanOrEqual(pressedAt - 5_000);

  const md = await download(page, mdHref);
  expect(md.status, `snapshot.md download returned HTTP ${md.status}`).toBe(200);

  console.log(`EXPORT techieflow ${date}: generated_ts=${generated}, ${md.body.length} chars of snapshot.md`);
  freshTechieFlow = { date, json, md: md.body, jsonHref, mdHref };
  return freshTechieFlow;
}

/** Every key path in a JSON document, as [dotted path, key, value]. */
function keyPaths(o: Json, p = ''): [string, string, Json][] {
  const out: [string, string, Json][] = [];
  if (o && typeof o === 'object' && !Array.isArray(o)) {
    for (const [k, v] of Object.entries(o)) {
      const q = p ? `${p}.${k}` : k;
      out.push([q, k, v]);
      out.push(...keyPaths(v, q));
    }
  } else if (Array.isArray(o)) {
    o.forEach((v, i) => out.push(...keyPaths(v, `${p}[${i}]`)));
  }
  return out;
}

/** A value carrying no data at all: null, zero, an empty map/list, or a map/list of only such values. */
function carriesNoData(v: Json): boolean {
  if (v === null || v === 0 || v === '' || v === false) return true;
  if (typeof v === 'string') return /\(n=0\)|^—$/.test(v);
  if (Array.isArray(v)) return v.every(carriesNoData);
  if (typeof v === 'object') return Object.values(v).every(carriesNoData);
  return false;
}

/** /repos: repo short name → { kind (techieflow|playbook), source badge text }. */
async function reposInventory(page: Page): Promise<Record<string, { full: string; kind: string; source: string }>> {
  await gotoScreen(page, '/repos');
  await testid(page, 'repos-table');
  const rows = await page.evaluate(() => {
    const table = document.querySelector('[data-testid="repos-table"]');
    if (!table) return [];
    const heads = Array.from(table.querySelectorAll('thead th')).map(th => (th.textContent || '').trim().toLowerCase());
    const ki = heads.indexOf('kind');
    const si = heads.indexOf('source');
    return Array.from(table.querySelectorAll('tbody tr')).map(tr => {
      const tds = Array.from(tr.querySelectorAll('td')).map(td => (td.textContent || '').replace(/\s+/g, ' ').trim());
      return { full: tds[0] || '', kind: ki >= 0 ? tds[ki] || '' : '', source: si >= 0 ? tds[si] || '' : '' };
    });
  });
  const out: Record<string, { full: string; kind: string; source: string }> = {};
  for (const r of rows) {
    const m = r.full.match(/[\w.-]+\/([\w.-]+)/);
    if (m) out[m[1]] = { full: m[0], kind: r.kind.toLowerCase(), source: r.source };
  }
  return out;
}

/** Runs tools/parity-compare.py and returns its exit code, output and parsed FAIL/INFO lines. */
function runCompare(args: string[]) {
  const r = spawnSync('python3', [PARITY_TOOL, ...args], { cwd: REPO_ROOT, encoding: 'utf8', timeout: 120_000 });
  const out = `${r.stdout || ''}${r.stderr || ''}`;
  const lines = out.split('\n');
  const fails: { kind: string; path: string; detail: string }[] = [];
  const infos: { kind: string; path: string; detail: string }[] = [];
  for (let i = 0; i < lines.length; i++) {
    const m = lines[i].match(/^ {2}(FAIL|INFO) {2}(\S+)\s+(.*)$/);
    if (!m) continue;
    const detail: string[] = [];
    for (let j = i + 1; j < lines.length && /^ {9}/.test(lines[j]); j++) detail.push(lines[j].trim());
    const entry = { kind: m[2], path: m[3].trim(), detail: detail.join(' ') };
    (m[1] === 'FAIL' ? fails : infos).push(entry);
  }
  return { code: r.status, out, fails, infos };
}

function writeJson(info: TestInfo, name: string, doc: Json): string {
  const p = info.outputPath(name);
  fs.mkdirSync(path.dirname(p), { recursive: true });
  fs.writeFileSync(p, JSON.stringify(doc, null, 2), 'utf8');
  return p;
}

/** The TechieFlow `gate`-axis surfaces of /gate-outcomes, by test-id prefix. */
const TF_GATE_ID =
  /^(schema-note$|type-tabs$|type-tab-|kpis-|kpi-first-pass-|kpi-escape-|kpi-failures-|live-|backfilled-|segment-facts-|gate-dist-|late-gate|taint-|gate-outcomes-empty)/;
/** The Playbook `phase_gate`-axis surfaces. */
const PB_ID = /^(pb-|playbook-)/;
/** A key that names TechieFlow `gate` data (and not Playbook `phase_gate` data). */
const isTfGateKey = (k: string) => /gate/i.test(k) && !/phase_gate/i.test(k);

/* ─────────────────────────────── REQ-FN-066 ─────────────────────────────── */

test('REQ-FN-066 — on /gate-outcomes the Playbook phase_gate axis and the TechieFlow gate axis never share a table or chart', async ({
  page,
}) => {
  test.setTimeout(180_000);
  await signIn(page, USER1);

  // TechieFlow axis: the gate surfaces render, and nothing Playbook-keyed is on the page.
  await gotoScreen(page, '/gate-outcomes');
  await switchFramework(page, 'TechieFlow');
  await gotoScreen(page, '/gate-outcomes');
  await testid(page, 'type-tabs');
  const tfIds = await contentTestIds(page);
  const tfGateIds = tfIds.filter(id => TF_GATE_ID.test(id));
  const pbOnTf = tfIds.filter(id => PB_ID.test(id));
  console.log(`TF /gate-outcomes: ${tfGateIds.length} gate-axis controls; pb-/playbook- controls: ${JSON.stringify(pbOnTf)}`);
  expect(tfGateIds.length, 'the TechieFlow gate surfaces did not render').toBeGreaterThan(0);
  expect(pbOnTf, 'Playbook phase_gate surfaces are rendered on the TechieFlow axis').toEqual([]);

  // Playbook axis: its own surface, and none of the TechieFlow gate tables, tabs or charts.
  const active = await switchFramework(page, 'Playbook');
  expect(active, `the Framework switch did not settle on Playbook (reads "${active}")`).toMatch(/playbook/i);
  await gotoScreen(page, '/gate-outcomes');
  await testid(page, 'playbook-axis-note');
  const pbIds = await contentTestIds(page);
  const tfOnPb = pbIds.filter(id => TF_GATE_ID.test(id));
  console.log(`PB /gate-outcomes ids: ${JSON.stringify(pbIds.slice(0, 40))}`);
  expect(tfOnPb, 'TechieFlow gate tables/tabs/charts are rendered on the Playbook axis').toEqual([]);
  expect(pbIds.filter(id => PB_ID.test(id)).length, 'the Playbook axis rendered nothing of its own').toBeGreaterThan(0);

  // Structural: no Playbook control contains a TechieFlow gate control (a shared table or chart).
  const nested = await page.evaluate(
    ({ tf }) => {
      const re = new RegExp(tf);
      const bad: string[] = [];
      for (const el of Array.from(document.querySelectorAll('[data-testid^="pb-"]'))) {
        for (const inner of Array.from(el.querySelectorAll('[data-testid]'))) {
          const id = inner.getAttribute('data-testid') || '';
          if (re.test(id)) bad.push(`${el.getAttribute('data-testid')} ⊃ ${id}`);
        }
      }
      return bad;
    },
    { tf: TF_GATE_ID.source },
  );
  expect(nested, 'a Playbook table/chart contains a TechieFlow gate control').toEqual([]);

  // The standing note states the separation in words beside whatever the axis shows.
  const note = (await textOf(page, 'playbook-axis-note')).toLowerCase();
  expect(note).toContain('phase_gate');
  expect(note).toMatch(/never share a chart|different axes/);

  // Back to TechieFlow for the next test's context hygiene.
  await switchFramework(page, 'TechieFlow');
});

test('REQ-FN-066 — the exports keep the axes apart: no phase_gate in the TechieFlow snapshot, no TechieFlow gate data in the Playbook one', async ({
  page,
}) => {
  test.setTimeout(240_000);
  await signIn(page, USER1);
  const tf = await exportTechieFlowNow(page);

  // TechieFlow export: nothing Playbook-keyed anywhere.
  const tfPhaseGate = keyPaths(tf.json).filter(([, k]) => /phase_gate/i.test(k)).map(([p]) => p);
  expect(tfPhaseGate, 'the TechieFlow export carries phase_gate keys').toEqual([]);
  expect(Object.keys(tf.json), 'the TechieFlow export carries a playbook block').not.toContain('playbook');
  expect(tf.md.toLowerCase(), 'the TechieFlow snapshot.md mentions phase_gate data').not.toMatch(/##.*phase_gate/);

  // Playbook export: the newest one in Past snapshots (the Playbook axis offers no export-now today).
  await gotoScreen(page, '/export');
  const pb = await latestSnapshot(page, 'playbook');
  test.skip(!pb || !pb.jsonHref, 'no Playbook snapshot row exists in Past snapshots for USER1, so there is no Playbook export to read');
  const pbJson = await downloadJson(page, pb!.jsonHref!);
  console.log(`PLAYBOOK snapshot read: ${pb!.date} (parity.framework=${pbJson?.parity?.framework})`);
  expect(pbJson?.parity?.framework, 'the Playbook snapshot does not say it is the Playbook framework').toBe('playbook');

  // phase_gate data lives only inside the `playbook` block, and that block names no TechieFlow gate key.
  const pbPaths = keyPaths(pbJson);
  const phaseGateOutside = pbPaths.filter(([p, k]) => /phase_gate/i.test(k) && !p.startsWith('playbook.') && p !== 'playbook');
  expect(phaseGateOutside.map(([p]) => p), 'phase_gate data outside the playbook block').toEqual([]);
  const tfKeyInsidePb = pbPaths.filter(([p, k]) => p.startsWith('playbook.') && isTfGateKey(k)).map(([p]) => p);
  expect(tfKeyInsidePb, 'the playbook block carries a TechieFlow gate key').toEqual([]);

  // Outside the playbook block, no key derived from TechieFlow gate records carries any data.
  const tfGateKeysInPb = pbPaths.filter(([p, k]) => !p.startsWith('playbook') && isTfGateKey(k));
  const withData = tfGateKeysInPb.filter(([, , v]) => !carriesNoData(v)).map(([p, , v]) => `${p}=${JSON.stringify(v).slice(0, 60)}`);
  console.log(
    `PLAYBOOK snapshot ${pb!.date}: TechieFlow-gate-named keys present outside the playbook block ` +
      `(all must be empty): ${JSON.stringify(tfGateKeysInPb.map(([p, , v]) => `${p}=${JSON.stringify(v)}`))}`,
  );
  expect(withData, 'the Playbook export carries TechieFlow gate data').toEqual([]);
});

/* ─────────────────────────────── REQ-FN-067 ─────────────────────────────── */

/**
 * The Playbook's own published events stream — `techierathore/AI-First-Playbook`,
 * `.miss-selftest/verification/telemetry/events.ndjson` — copied unmodified (provenance in the README
 * beside it). It is only ever imported as USER2, and removed again after the run.
 */
const PB_SELFTEST = path.join(__dirname, 'fixtures', 'playbook-selftest', 'events.ndjson');
/** Every source this test imports is named `verify/cla-fn067-<run>`; a leftover from an aborted run is swept by prefix. */
const PB_SOURCE_OWNER = 'verify';
const PB_SOURCE_PREFIX = 'cla-fn067-';

type ImportStream = { stream: string; records?: number; presented?: number; added?: number; duplicatesCollapsed?: number; invalidLines?: number };
type ImportBody = { accepted: boolean; message?: string; framework?: string; streams?: ImportStream[]; recordsAdded?: number };

/** /repos → Add source → Import: names the source, chooses the file, reads the preview, commits it. */
async function importSource(page: Page, short: string, file: string): Promise<{ preview: ImportBody; committed: ImportBody }> {
  await gotoScreen(page, '/repos');
  await page.click('[data-testid="connect-repo"], [data-testid="repos-empty-connect"]');
  await testid(page, 'source-mode');
  await page.click('[data-testid="source-mode-import"]');
  await testid(page, 'import-drop');
  await page.fill('[data-testid="import-name"]', `${PB_SOURCE_OWNER}/${short}`);

  const previewAnswer = page.waitForResponse(
    r => r.url().includes('/api/import/preview') && r.request().method() === 'POST',
    { timeout: 90_000 },
  );
  await page.setInputFiles('#tflens-import-file', [file]);
  const preview = (await (await previewAnswer).json()) as ImportBody;
  await page
    .locator('[data-testid="import-preview"], [data-testid="import-refusal"]')
    .first()
    .waitFor({ state: 'visible', timeout: 60_000 });
  expect(preview.accepted, `the import preview refused the Playbook stream: ${preview.message}`).toBe(true);

  const commitAnswer = page.waitForResponse(
    r => r.url().includes('/api/import/commit') && r.request().method() === 'POST',
    { timeout: 180_000 },
  );
  await page.click('[data-testid="import-submit"]');
  const committed = (await (await commitAnswer).json()) as ImportBody;
  expect(committed.accepted, `the import commit refused the Playbook stream: ${committed.message}`).toBe(true);
  // The import finishes on the circuit; leaving before the row appears would cut it off mid-stream.
  await page.waitForURL(/\/repos$/, { timeout: 60_000 });
  await testid(page, `repo-source-${short}`, 60_000);
  return { preview, committed };
}

/** Removes a source through /repos → Remove → confirm, if it is still listed. */
async function removeSource(page: Page, short: string) {
  await gotoScreen(page, '/repos');
  const remove = page.locator(`[data-testid="repo-remove-${short}"]`);
  if ((await remove.count()) === 0) return;
  await remove.first().click();
  await page.locator('[data-testid="remove-confirm"]').click();
  await page.waitForURL(/\/repos$/, { timeout: 60_000 }).catch(() => {});
  await page.waitForTimeout(1_500);
}

/** The short names of every source this test's prefix still has on /repos (a previous aborted run's). */
async function leftoverSources(page: Page): Promise<string[]> {
  await gotoScreen(page, '/repos');
  return page.evaluate(
    prefix =>
      Array.from(document.querySelectorAll(`[data-testid^="repo-remove-${prefix}"]`)).map(e =>
        (e.getAttribute('data-testid') || '').replace(/^repo-remove-/, ''),
      ),
    PB_SOURCE_PREFIX,
  );
}

test('REQ-FN-067 — on the Playbook axis /gate-outcomes renders the three questions and phase totals from Playbook records', async ({
  page,
}) => {
  test.setTimeout(420_000);

  // The phase_gates the imported records name: the `command` of each phase-start line in the file.
  const lines = fs.readFileSync(PB_SELFTEST, 'utf8').split('\n').filter(l => l.trim().length > 0);
  const records = lines.map(l => JSON.parse(l) as Json);
  const fileGates = [...new Set(records.filter(r => r.kind === 'phase-start').map(r => String(r.command)))].sort();
  expect(fileGates.length, 'the fixture names no phase_gate — it is not the Playbook self-test stream').toBeGreaterThan(0);

  await signIn(page, USER2);

  // A previous run that died before its finally may have left its source behind; take it away first.
  for (const stale of await leftoverSources(page)) {
    console.log(`removing a leftover source from an earlier run: ${stale}`);
    await removeSource(page, stale);
  }

  const short = `${PB_SOURCE_PREFIX}${Date.now().toString(36)}`;
  try {
    const { preview, committed } = await importSource(page, short, PB_SELFTEST);
    console.log(
      `imported ${PB_SOURCE_OWNER}/${short}: framework=${preview.framework}, recordsAdded=${committed.recordsAdded}, ` +
        `streams=${JSON.stringify(committed.streams)}`,
    );
    expect(preview.framework, 'the importer did not read the stream as a Playbook one').toBe('playbook');
    expect(committed.recordsAdded ?? 0, 'the import added no Playbook record').toBeGreaterThan(0);
    const badge = (await textOf(page, `repo-source-${short}`)).toLowerCase();
    expect(badge, `/repos: the imported source's badge reads "${badge}"`).toMatch(/import/);

    // The user switches to Playbook and opens /gate-outcomes.
    await gotoScreen(page, '/gate-outcomes');
    const active = await switchFramework(page, 'Playbook');
    expect(active, `the Framework switch did not settle on Playbook (reads "${active}")`).toMatch(/playbook/i);
    await gotoScreen(page, '/gate-outcomes');
    await testid(page, 'playbook-axis-note');
    console.log(`Framework switch after import: "${await switchLabels(page)}"`);

    // The populated axis, never the empty state and never the TechieFlow gate surface in its place.
    expect(await exists(page, 'playbook-empty'), 'the Playbook axis still shows its empty state after the import').toBe(false);
    const ids = await contentTestIds(page);
    expect(ids.filter(id => TF_GATE_ID.test(id)), 'TechieFlow gate surfaces on the Playbook axis').toEqual([]);

    // Phase totals over every phase_gate, and one tab per phase_gate the imported records name.
    const allRows = await page.locator('[data-testid="pb-phases-all"] tbody tr').allInnerTexts();
    console.log(`pb-phases-all: ${JSON.stringify(allRows.map(r => r.replace(/\s+/g, ' ')))}`);
    expect(allRows.length, 'pb-phases-all: the every-phase_gate totals table has no rows').toBeGreaterThan(0);
    const gates = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid^="pb-gate-tab-"]')).map(e =>
        (e.getAttribute('data-testid') || '').replace('pb-gate-tab-', ''),
      ),
    );
    console.log(`phase_gate tabs: ${JSON.stringify(gates)}; phase_gates in the imported file: ${JSON.stringify(fileGates)}`);
    expect(gates.length, 'no phase_gate tab rendered on the Playbook axis').toBeGreaterThan(0);
    for (const g of fileGates) {
      expect(gates, `the imported records' phase_gate "${g}" has no tab`).toContain(g);
      const inAll = allRows.some(r => r.replace(/\s+/g, ' ').trim().startsWith(`${g} `));
      expect(inAll, `the imported records' phase_gate "${g}" has no row in pb-phases-all`).toBe(true);
    }

    // Each phase_gate: its own phase totals table, and the three questions — each a figure, or the
    // not-computable dash with the reason printed beside it, never a blank.
    for (const g of gates) {
      await page.locator(`[data-testid="pb-gate-tab-${g}"]`).first().click();
      await page.waitForTimeout(700);
      const rows = await page.locator(`[data-testid="pb-phases-${g}"] tbody tr`).allInnerTexts();
      expect(rows.length, `pb-phases-${g}: the phase totals table has no rows`).toBeGreaterThan(0);
      const q: Record<string, string> = {};
      for (const id of [`pb-first-pass-${g}`, `pb-catch-share-${g}`, `pb-escape-rate-${g}`]) {
        q[id] = await textOf(page, id);
        expect(q[id].length, `${id} is blank`).toBeGreaterThan(0);
      }
      if (Object.values(q).some(v => /^—/.test(v))) {
        const reason = await textOf(page, `pb-unavailable-${g}`);
        expect(reason, `${g}: a question reads "—" with no reason printed beside it`).toMatch(/not computable/i);
      }
      console.log(`${g}: rows=${JSON.stringify(rows.map(r => r.replace(/\s+/g, ' ')))} questions=${JSON.stringify(q)}`);
    }
  } finally {
    // Leave USER2 as it was: the framework back on TechieFlow, and the imported source removed.
    await gotoScreen(page, '/gate-outcomes').catch(() => {});
    await switchFramework(page, 'TechieFlow').catch(e => console.log(`switch back failed: ${String(e).split('\n')[0]}`));
    await removeSource(page, short).catch(e => console.log(`removal failed: ${String(e).split('\n')[0]}`));
  }

  // The clean-up itself is checked: nothing this test imported is left, and the switch is on TechieFlow.
  await gotoScreen(page, '/repos');
  await expect(page.locator(`[data-testid="repo-source-${short}"]`), `${short} was not removed`).toHaveCount(0);
  await gotoScreen(page, '/gate-outcomes');
  const after = await page.evaluate(() => {
    const root = document.querySelector('[data-testid="framework-switch"]');
    const active = Array.from(root?.querySelectorAll('[role="tab"]') || []).find(
      t => t.getAttribute('aria-selected') === 'true' || t.getAttribute('data-state') === 'active',
    );
    return (active?.textContent || '').replace(/\s+/g, ' ').trim();
  });
  expect(after, 'the Framework switch was not put back on TechieFlow').toMatch(/techieflow/i);
});

/* ─────────────────────────────── REQ-FN-070 ─────────────────────────────── */

test('REQ-FN-070 — on the Playbook axis /export shows no TechieFlow figure and the snapshots pool nothing across frameworks', async ({
  page,
}) => {
  test.setTimeout(240_000);
  await signIn(page, USER1);
  const inventory = await reposInventory(page);
  const tfRepos = Object.entries(inventory).filter(([, r]) => r.kind === 'techieflow').map(([n]) => n);
  const pbRepos = Object.entries(inventory).filter(([, r]) => r.kind === 'playbook').map(([n]) => n);
  console.log(`/repos kinds — techieflow: ${JSON.stringify(tfRepos)}; playbook: ${JSON.stringify(pbRepos)}`);

  const tf = await exportTechieFlowNow(page);

  // The Playbook-axis /export: the axis note, and no TechieFlow repository or figure on the page.
  await gotoScreen(page, '/export');
  await switchFramework(page, 'Playbook');
  await gotoScreen(page, '/export');
  await testid(page, 'playbook-axis-note');
  // Repositories are named owner/name on this page (dataset SHAs); the short name alone would also
  // match the header's "TechieFlow" switch label and the "TfLens Demo" user chip.
  const main = (await page.locator('main').first().innerText()).replace(/\s+/g, ' ');
  const tfNamed = tfRepos.filter(n => main.includes(inventory[n].full));
  expect(tfNamed, 'the Playbook-axis /export names TechieFlow repositories').toEqual([]);
  if (pbRepos.length === 0) {
    // With no Playbook repository there is nothing the axis could list: a dataset-SHA table here
    // could only be carrying TechieFlow repositories.
    expect(await exists(page, 'dataset-shas-table'), 'a dataset-SHA table is on the Playbook axis with no Playbook repo').toBe(false);
  }
  const pbAxisShas = await page
    .locator('[data-testid="dataset-shas-table"] tbody tr')
    .allInnerTexts()
    .catch(() => [] as string[]);
  for (const row of pbAxisShas) {
    const name = (row.match(/[\w.-]+\/([\w.-]+)/) || [])[1];
    if (name) expect(inventory[name]?.kind, `dataset SHA row "${row.trim()}" on the Playbook axis`).toBe('playbook');
  }
  await switchFramework(page, 'TechieFlow');

  // The TechieFlow snapshot: every repository is TechieFlow, and each pooled total is exactly the
  // sum of its own repositories — nothing from the other framework was added in.
  expect(tf.json.parity.framework).toBe('techieflow');
  for (const r of tf.json.per_repo) expect(r.framework, `per_repo ${r.repo}`).toBe('techieflow');
  const sum = (doc: Json, k: string) => (doc.per_repo as Json[]).reduce((a, r) => a + (r[k] ?? 0), 0);
  for (const [pooledKey, repoKey] of [['runs_total', 'runs'], ['sessions', 'sessions'], ['commits', 'commits']]) {
    expect(tf.json.pooled[pooledKey], `techieflow pooled.${pooledKey} vs Σ per_repo.${repoKey}`).toBe(sum(tf.json, repoKey));
  }
  const pbNamedInTf = pbRepos.filter(n => (tf.json.per_repo as Json[]).some(r => String(r.repo).endsWith(`/${n}`)));
  expect(pbNamedInTf, 'Playbook repositories inside the TechieFlow snapshot').toEqual([]);

  // The newest Playbook snapshot: the same no-pool invariants from the other side.
  await gotoScreen(page, '/export');
  const pb = await latestSnapshot(page, 'playbook');
  test.skip(!pb || !pb.jsonHref, 'no Playbook snapshot row exists in Past snapshots for USER1');
  const pbJson = await downloadJson(page, pb!.jsonHref!);
  expect(pbJson.parity.framework).toBe('playbook');
  for (const r of pbJson.per_repo) expect(r.framework, `per_repo ${r.repo}`).toBe('playbook');
  for (const [pooledKey, repoKey] of [['runs_total', 'runs'], ['sessions', 'sessions'], ['commits', 'commits']]) {
    expect(pbJson.pooled[pooledKey], `playbook pooled.${pooledKey} vs Σ per_repo.${repoKey}`).toBe(sum(pbJson, repoKey));
  }
  const { parity: _parity, ...pbFigures } = pbJson;
  const pbText = JSON.stringify(pbFigures);
  const tfInPb = tfRepos.filter(n => pbText.includes(`"${inventory[n].full}"`) || pbText.includes(`"app":"${n}"`));
  console.log(`PLAYBOOK snapshot ${pb!.date}: per_repo=${pbJson.per_repo.length}, events_total=${pbJson.playbook?.events_total}`);
  expect(tfInPb, 'TechieFlow repositories inside the Playbook snapshot figures').toEqual([]);
});

test('REQ-FN-070 — on the Playbook axis /export renders the full report set from Playbook records', async ({ page }) => {
  test.setTimeout(180_000);
  await signIn(page, USER1);
  await gotoScreen(page, '/export');
  await switchFramework(page, 'Playbook');
  await gotoScreen(page, '/export');
  await testid(page, 'playbook-axis-note');

  const empty = await exists(page, 'playbook-empty');
  const hasExport = await exists(page, 'export-now');
  const labels = await switchLabels(page);
  const emptyText = await textOf(page, 'playbook-empty');
  await switchFramework(page, 'TechieFlow');
  test.skip(
    empty,
    `no Playbook records exist for USER1: the switch reads "${labels}", the Playbook axis on /export shows its empty ` +
      `state ("${emptyText.slice(0, 140)}") and export-now is ${hasExport ? 'present' : 'not offered'} — the populated report set cannot be exercised`,
  );

  // Populated branch: the Playbook export surface writes a Playbook snapshot built from Playbook records.
  await switchFramework(page, 'Playbook');
  await gotoScreen(page, '/export');
  const target = await textOf(page, 'export-target');
  const m = target.match(/(\d{4}-\d{2}-\d{2})\/playbook\//);
  expect(m, `export-target does not name a playbook folder: "${target}"`).not.toBeNull();
  const pressedAt = Date.now();
  await (await testid(page, 'export-now')).click();
  const link = page.locator(`[data-testid="snapshot-json-${m![1]}-playbook"]`).first();
  await link.waitFor({ state: 'attached', timeout: 45_000 });
  const doc = await downloadJson(page, (await link.getAttribute('href')) || '');
  expect(Date.parse(doc.parity.generated_ts)).toBeGreaterThanOrEqual(pressedAt - 5_000);
  expect(doc.parity.framework).toBe('playbook');
  expect(doc.playbook.events_total, 'the Playbook snapshot was built from no events').toBeGreaterThan(0);
  for (const r of doc.per_repo) expect(r.framework).toBe('playbook');
  await switchFramework(page, 'TechieFlow');
});

/* ─────────────────────────────── REQ-FN-080 ─────────────────────────────── */

test('REQ-FN-080 — the snapshot written from /export carries a misses section with three distinct attribution keys', async ({
  page,
}) => {
  test.setTimeout(240_000);
  await signIn(page, USER1);
  const tf = await exportTechieFlowNow(page);
  const misses = tf.json.misses;

  expect(misses && typeof misses === 'object' && !Array.isArray(misses), 'tflens.json has no misses object').toBe(true);

  // The attribution split survives as three distinct keys — each its own map, none collapsed.
  const three = ['by_origin_phase', 'by_origin_model', 'by_origin_agent'];
  for (const k of three) {
    expect(Object.keys(misses), `misses.${k} is missing`).toContain(k);
    const v = misses[k];
    expect(v !== null && typeof v === 'object' && !Array.isArray(v), `misses.${k} is not a map: ${JSON.stringify(v)}`).toBe(true);
  }
  const collapsed = Object.keys(misses).filter(k => /^by_origin/.test(k) && !three.includes(k));
  expect(collapsed, 'an extra by_origin* key suggests the split was collapsed or restructured').toEqual([]);
  const keySets = three.map(k => JSON.stringify(Object.keys(misses[k]).sort()));
  if (three.every(k => Object.keys(misses[k]).length > 0)) {
    expect(new Set(keySets).size, `the three attribution maps carry identical keys: ${keySets.join(' / ')}`).toBe(3);
  }
  console.log(
    `misses attribution: phase=${JSON.stringify(misses.by_origin_phase)} model=${JSON.stringify(misses.by_origin_model)} ` +
      `agent=${JSON.stringify(misses.by_origin_agent)}`,
  );

  // Both export files carry the section: snapshot.md has a misses heading and the three rows.
  expect(tf.md, 'snapshot.md has no misses section').toMatch(/^##\s+Misses/m);
  for (const row of ['By origin phase', 'By origin model', 'By origin agent']) {
    expect(tf.md, `snapshot.md misses section lacks the "${row}" row`).toContain(row);
  }

  // Every figure the export itself labels as a rate-card estimate carries `_usd_estimate`; a
  // measured dollar figure never does.
  const estimateBlocks: [string, Json][] = [];
  const walk = (o: Json, p: string) => {
    if (Array.isArray(o)) return o.forEach((v, i) => walk(v, `${p}[${i}]`));
    if (!o || typeof o !== 'object') return;
    const isEstimate =
      o.estimate === true || typeof o.rate_card_path === 'string' || (typeof o.estimate_label === 'string' && o.measured !== true);
    if (isEstimate) estimateBlocks.push([p, o]);
    for (const [k, v] of Object.entries(o)) walk(v, p ? `${p}.${k}` : k);
  };
  walk(tf.json, '');
  expect(estimateBlocks.length, 'the export labels no rate-card estimate block at all').toBeGreaterThan(0);
  const badEstimate: string[] = [];
  for (const [p, o] of estimateBlocks) {
    for (const [k, v] of Object.entries(o)) {
      if (/usd/i.test(k) && (typeof v === 'number' || v === null) && !k.endsWith('_usd_estimate')) badEstimate.push(`${p}.${k}`);
    }
  }
  expect(badEstimate, 'rate-card estimate figures whose key does not end _usd_estimate').toEqual([]);

  const all = keyPaths(tf.json);
  const measuredWithEstimateKey: string[] = [];
  const walkMeasured = (o: Json, p: string) => {
    if (Array.isArray(o)) return o.forEach((v, i) => walkMeasured(v, `${p}[${i}]`));
    if (!o || typeof o !== 'object') return;
    for (const [k, v] of Object.entries(o)) {
      if (o.measured === true && k.endsWith('_usd_estimate') && v !== null) measuredWithEstimateKey.push(`${p}.${k}`);
      walkMeasured(v, p ? `${p}.${k}` : k);
    }
  };
  walkMeasured(tf.json, '');
  expect(measuredWithEstimateKey, 'a measured dollar figure carries the _usd_estimate suffix').toEqual([]);
  const measuredKeys = all.filter(([, k]) => /_measured$/.test(k) && /usd/.test(k)).map(([p]) => p);
  expect(measuredKeys.filter(p => p.endsWith('_usd_estimate')), 'measured dollar keys').toEqual([]);
  expect(Object.keys(misses)).toContain('cost_usd_per_miss_measured');

  // Observation only (not asserted): BRD-195 / BRD-197 name the list-price figures `list_usd*` and
  // `cost_list_usd_per_miss` explicitly; they are rate-card products without the suffix.
  const listPrice = [...new Set(all.filter(([, k]) => /list_usd/.test(k)).map(([p]) => p.replace(/phases\.phases\.[^.]+/, 'phases.phases.<cmd>').replace(/models\.[^.]+(\.[^.]+)*\.list_usd$/, 'models.<model>.list_usd')))];
  test.info().annotations.push({
    type: 'observation',
    description: `rate-card list-price keys named per BRD-195/197 without _usd_estimate: ${listPrice.join(', ')}`,
  });
  console.log(`OBSERVATION list-price keys (BRD-195/197 naming): ${JSON.stringify(listPrice)}`);
  console.log(`estimate blocks: ${JSON.stringify(estimateBlocks.map(([p]) => p))}`);
});

/* ─────────────────────────────── REQ-FN-084 ─────────────────────────────── */

test('REQ-FN-084 — on / an imported source shows its bundle hash where a fetched source shows its commit SHA', async ({ page }) => {
  test.setTimeout(240_000);
  await signIn(page, USER1);
  const tf = await exportTechieFlowNow(page);

  await gotoScreen(page, '/');
  await switchFramework(page, 'TechieFlow');
  await gotoScreen(page, '/');
  await testid(page, 'coverage-kpis');
  const cards = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid^="repo-card-"]')).map(card => {
      const name = (card.getAttribute('data-testid') || '').replace('repo-card-', '');
      const badge = document.querySelector(`[data-testid="repo-source-badge-${name}"]`);
      const sha = document.querySelector(`[data-testid="repo-sha-${name}"]`);
      return {
        name,
        badge: (badge?.textContent || '').replace(/\s+/g, ' ').trim(),
        sha: (sha?.textContent || '').replace(/\s+/g, ' ').trim(),
        shaHref: sha?.getAttribute('href') || '',
      };
    }),
  );
  console.log(`Coverage cards: ${JSON.stringify(cards)}`);
  expect(cards.length, 'the Coverage page rendered no repository card').toBeGreaterThan(0);

  const perRepo = (name: string) => (tf.json.per_repo as Json[]).find(r => String(r.repo).split('/').pop() === name);

  // Fetched sources: the SHA slot is the commit the streams were read at, matching the export.
  const fetched = cards.filter(c => !/import/i.test(c.badge));
  for (const c of fetched) {
    const r = perRepo(c.name);
    expect(r, `${c.name}: no per_repo entry in the export`).toBeTruthy();
    expect(r.source_kind, `${c.name}: source_kind`).toBe('api');
    expect(r.source_sha, `${c.name}: source_sha is not a 40-hex commit SHA`).toMatch(/^[0-9a-f]{40}$/);
    expect(c.sha, `${c.name}: the Coverage SHA slot is not a commit-SHA prefix`).toMatch(/^[0-9a-f]{7,40}$/);
    expect(r.source_sha.startsWith(c.sha), `${c.name}: Coverage shows ${c.sha}, export says ${r.source_sha}`).toBe(true);
  }

  // Imported sources: the same slot, on / and in the export's per_repo block, holds the bundle hash.
  const imported = cards.filter(c => /import/i.test(c.badge));
  const importedInExport = (tf.json.per_repo as Json[]).filter(r => r.source_kind === 'import');
  const reposKpi = await (async () => {
    await gotoScreen(page, '/repos');
    return textOf(page, 'kpi-repos');
  })();
  test.skip(
    imported.length === 0 && importedInExport.length === 0,
    `USER1 has no imported source: all ${cards.length} Coverage badges read ` +
      `${JSON.stringify([...new Set(cards.map(c => c.badge))])}, /repos reads "${reposKpi}", and every export ` +
      `per_repo source_kind is ${JSON.stringify([...new Set((tf.json.per_repo as Json[]).map(r => r.source_kind))])}`,
  );

  for (const c of imported) {
    const r = perRepo(c.name);
    expect(r, `${c.name}: no per_repo entry in the export`).toBeTruthy();
    expect(r.source_kind).toBe('import');
    expect(r.source_sha, `${c.name}: per_repo.source_sha is not a sha256 bundle hash`).toMatch(/^(sha256:)?[0-9a-f]{64}$/);
    expect(c.sha.length, `${c.name}: the Coverage SHA slot is blank for an imported source`).toBeGreaterThan(0);
    const hex = String(r.source_sha).replace(/^sha256:/, '');
    const shown = c.sha.replace(/^sha256:/, '').replace(/[^0-9a-f]/g, '');
    expect(shown.length, `${c.name}: Coverage shows "${c.sha}"`).toBeGreaterThanOrEqual(7);
    expect(hex.startsWith(shown), `${c.name}: Coverage shows ${c.sha}, the bundle hash is ${r.source_sha}`).toBe(true);
    expect(c.shaHref, `${c.name}: an imported bundle hash must not link to a GitHub commit`).not.toContain('/commit/');
  }
});

/* ─────────────────────────────── REQ-FN-087 ─────────────────────────────── */

test('REQ-FN-087 — /repos and / display each source origin, the export carries source_kind, and nothing segments on it', async ({
  page,
}) => {
  test.setTimeout(300_000);
  await signIn(page, USER1);
  const tf = await exportTechieFlowNow(page);

  // /repos: a Source column and a badge on every row.
  const inventory = await reposInventory(page);
  const heads = await page.locator('[data-testid="repos-table"] thead th').allInnerTexts();
  expect(heads.map(h => h.trim()), '/repos has no Source column').toContain('Source');
  const names = Object.keys(inventory);
  expect(names.length, '/repos lists no source').toBeGreaterThan(0);
  for (const n of names) {
    const badge = await textOf(page, `repo-source-${n}`);
    expect(badge, `/repos: ${n} has no source badge`).toMatch(/\S/);
  }

  // Coverage `/`: a source badge on every repository card.
  await gotoScreen(page, '/');
  await testid(page, 'coverage-kpis');
  const cardBadges = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid^="repo-card-"]')).map(card => {
      const name = (card.getAttribute('data-testid') || '').replace('repo-card-', '');
      const b = document.querySelector(`[data-testid="repo-source-badge-${name}"]`);
      return { name, badge: (b?.textContent || '').replace(/\s+/g, ' ').trim() };
    }),
  );
  expect(cardBadges.length).toBeGreaterThan(0);
  for (const c of cardBadges) expect(c.badge, `Coverage: ${c.name} has no source badge`).toMatch(/\S/);

  // The export: every per_repo entry carries source_kind, consistent with the badge.
  for (const r of tf.json.per_repo as Json[]) {
    expect(['api', 'import'], `per_repo ${r.repo}: source_kind=${JSON.stringify(r.source_kind)}`).toContain(r.source_kind);
    const name = String(r.repo).split('/').pop()!;
    const badge = cardBadges.find(c => c.name === name)?.badge ?? '';
    if (r.source_kind === 'api') expect(badge, `${name}: api source badge`).not.toMatch(/import/i);
    else expect(badge, `${name}: import source badge`).toMatch(/import/i);
  }

  // No figure segments or pools on origin — in the export document…
  const SOURCE_WORD = /^(api|import|imported|synced|fetched|upload|uploaded)$/i;
  const segKeys = keyPaths(tf.json)
    .filter(([p, k]) => (/source_kind|by_source|origin_kind|source_origin/i.test(k) && !/^per_repo\[\d+\]\.source_kind$/.test(p)) || SOURCE_WORD.test(k))
    .map(([p]) => p);
  expect(segKeys, 'export keys segmented by source origin').toEqual([]);
  const mdHeadings = tf.md.split('\n').filter(l => /^#/.test(l));
  expect(
    mdHeadings.filter(h => /\b(imported|synced|fetched|by source|source kind)\b/i.test(h)),
    'snapshot.md sections segmented by source origin',
  ).toEqual([]);

  // …and on every report page: no tab, select option, radio or chart series is named by origin.
  const ORIGIN_LABEL = /\b(api|imported?|synced|fetched|uploaded|source[ _-]?kind|by source|origin kind)\b/i;
  const findings: string[] = [];
  for (const route of ['/', '/gate-outcomes', '/harness', '/routing', '/misses', '/effort', '/export']) {
    await gotoScreen(page, route);
    const labels = await page.evaluate(() => {
      const t = (s: string | null) => (s || '').replace(/\s+/g, ' ').trim();
      const sel = [
        'main [role="tab"]',
        'main option',
        'main [role="option"]',
        'main [role="radio"]',
        'main select',
        'main [role="combobox"]',
        'main .apexcharts-legend-text',
        'main [data-testid*="segment"]',
        'main [data-testid*="filter"]',
      ];
      return sel.flatMap(s =>
        Array.from(document.querySelectorAll(s)).map(e => `${s.replace('main ', '')}: ${t(e.textContent).slice(0, 80)}`),
      );
    });
    for (const l of labels) if (ORIGIN_LABEL.test(l.split(': ').slice(1).join(': '))) findings.push(`${route} ${l}`);
    console.log(`${route}: ${labels.length} tab/option/legend/segment labels checked`);
  }
  expect(findings, 'report controls segmented by source origin').toEqual([]);
});

/* ─────────────────────────────── REQ-FN-093 ─────────────────────────────── */

test('REQ-FN-093 — the compare script diffs every key of the /export phases block, null matching null and share strings as strings', async ({
  page,
}, info) => {
  test.setTimeout(300_000);
  await signIn(page, USER1);

  // The page's own parity report: the recorded compare covered the phases block with zero findings.
  await gotoScreen(page, '/export');
  await switchFramework(page, 'TechieFlow');
  await gotoScreen(page, '/export');
  // The status cell is filled after the circuit reads the parity record; an early read can catch the
  // pre-load placeholder, so the assertion waits for the settled value (and fails if it never settles).
  const statusCell = await testid(page, 'export-parity-status');
  const early = (await statusCell.innerText().catch(() => '')).trim();
  await expect(statusCell, 'export-parity-status').toHaveText(/^\s*QUOTABLE\s*$/, { timeout: 30_000 });
  const status = await textOf(page, 'export-parity-status');
  await testid(page, 'parity-output');
  const output = ((await page.locator('[data-testid="parity-output"]').first().innerText().catch(() => '')) || '').trim();
  console.log(
    `export-parity-status: first read "${early}", settled "${status}"; parity-output tail: ${output.replace(/\s+/g, ' ').slice(-160)}`,
  );
  expect(output, 'the recorded compare did not cover the phases block').toMatch(/INFO\s+COVERED\s+phases/);
  expect(output, 'the recorded compare left a phases figure uncovered').not.toMatch(/UNCOVERED\s+phases/);
  expect(output, 'the recorded compare had findings').toMatch(/\b0 finding\(s\)/);

  // The tool's CLI takes a reference document and the tflens.json /export writes.
  const help = runCompare(['--help']);
  expect(help.code, help.out).toBe(0);
  expect(help.out).toMatch(/reference/);
  expect(help.out).toMatch(/tflens/);

  // The document under test: today's snapshot, written from /export and read back through the session.
  const tf = await exportTechieFlowNow(page);
  const doc = tf.json;
  expect(doc.phases && typeof doc.phases === 'object', 'tflens.json has no phases block').toBe(true);
  const original = writeJson(info, 'tflens.json', doc);

  // 1. Same document both sides: every BRD-152 phases figure is present (no UNCOVERED), nothing differs,
  //    and every null in the block matches its null.
  const self = runCompare([original, original]);
  console.log(self.out.split('\n').filter(l => /COVERED|finding/.test(l)).join('\n'));
  expect(self.fails, `self-compare findings:\n${self.out.slice(-2000)}`).toEqual([]);
  expect(self.code).toBe(0);
  expect(self.infos.some(i => i.kind === 'COVERED' && i.path === 'phases'), 'phases block reported COVERED').toBe(true);

  // Enumerate the phases block's scalar leaves exactly as the tool spells paths.
  const leaves: { path: string; value: Json; set: (v: Json) => void }[] = [];
  const collect = (o: Json, p: string) => {
    if (Array.isArray(o)) {
      o.forEach((v, i) => (v !== null && typeof v === 'object' ? collect(v, `${p}[${i}]`) : leaves.push({ path: `${p}[${i}]`, value: v, set: x => (o[i] = x) })));
      return;
    }
    for (const [k, v] of Object.entries(o)) {
      const q = `${p}.${k}`;
      if (v !== null && typeof v === 'object') collect(v, q);
      else leaves.push({ path: q, value: v, set: x => (o[k] = x) });
    }
  };
  const mutated = JSON.parse(JSON.stringify(doc));
  collect(mutated.phases, 'phases');
  const nulls = leaves.filter(l => l.value === null);
  const shares = leaves.filter(l => typeof l.value === 'string' && /^\d+(\.\d+)?%$/.test(l.value));
  console.log(`phases block: ${leaves.length} scalar leaves, ${nulls.length} null, ${shares.length} share strings`);
  expect(leaves.length).toBeGreaterThan(0);

  // 2. Change every scalar leaf of the phases block in the copy: null → 0, a number by one, a share
  //    string to the same number written differently ("44%" → "44.0%"), any other string by a suffix,
  //    a boolean flipped. Every one of them must come back as its own DIFF finding.
  const expected = new Map<string, string>();
  for (const l of leaves) {
    const v = l.value;
    let next: Json;
    if (v === null) next = 0;
    else if (typeof v === 'number') next = v + 1;
    else if (typeof v === 'boolean') next = !v;
    else if (typeof v === 'string' && /^\d+%$/.test(v)) next = v.replace('%', '.0%');
    else if (typeof v === 'string' && /^\d+\.\d+%$/.test(v)) next = v.replace('%', '0%');
    else next = `${v}·changed`;
    l.set(next);
    expected.set(l.path, JSON.stringify(v));
  }
  const mutatedPath = writeJson(info, 'tflens.mutated.json', mutated);
  const diff = runCompare([original, mutatedPath]);
  expect(diff.code, 'the compare exited 0 on a changed phases block').not.toBe(0);

  const prose = new Set(['phases.note']);
  const reported = new Map(diff.fails.map(f => [f.path, f]));
  const notDiffed = [...expected.keys()].filter(p => !prose.has(p) && reported.get(p)?.kind !== 'DIFF');
  expect(notDiffed, `phases keys the compare did not diff (of ${expected.size})`).toEqual([]);
  const outsidePhases = diff.fails.filter(f => !f.path.startsWith('phases'));
  expect(outsidePhases, 'findings outside the phases block').toEqual([]);
  for (const p of prose) {
    if (expected.has(p)) expect(diff.infos.some(i => i.kind === 'PROSE-OK' && i.path === p), `${p} reported as prose`).toBe(true);
  }

  // Null against 0 is a finding, reported with both values.
  for (const l of nulls) {
    const f = reported.get(l.path)!;
    expect(f.detail, `${l.path}: null vs 0`).toContain('reference=null');
    expect(f.detail, `${l.path}: null vs 0`).toContain('tflens=0');
  }
  // Share strings are compared as strings: the same percentage written differently is a finding.
  for (const l of shares) {
    const f = reported.get(l.path)!;
    expect(f?.kind, `${l.path}: "${l.value}" reformatted was not a DIFF`).toBe('DIFF');
    expect(f.detail).toContain(`reference=${JSON.stringify(l.value)}`);
  }

  // 3. Null matching null: a null placed at the same phases keys on BOTH sides is agreement; the same
  //    null against a 0 is a finding. Run on copies of the real document.
  const bothNull = JSON.parse(JSON.stringify(doc));
  bothNull.phases.runs_live = null;
  bothNull.phases.tokens_out_total = null;
  const bothNullPath = writeJson(info, 'tflens.both-null.json', bothNull);
  const nn = runCompare([bothNullPath, bothNullPath]);
  expect(nn.fails, 'null against null produced a finding').toEqual([]);
  const zeroSide = JSON.parse(JSON.stringify(bothNull));
  zeroSide.phases.runs_live = 0;
  const zeroSidePath = writeJson(info, 'tflens.zero-side.json', zeroSide);
  const nz = runCompare([bothNullPath, zeroSidePath]);
  expect(nz.fails.map(f => `${f.kind} ${f.path}`), 'null against 0').toEqual(['DIFF phases.runs_live']);

  // 4. A phases key missing on one side is a finding (MISSING, and UNCOVERED for a BRD-152 key).
  const dropped = JSON.parse(JSON.stringify(doc));
  delete dropped.phases.duration_s_total;
  const droppedPath = writeJson(info, 'tflens.dropped.json', dropped);
  const dr = runCompare([original, droppedPath]);
  const drKinds = dr.fails.map(f => `${f.kind} ${f.path}`);
  expect(drKinds).toContain('MISSING phases.duration_s_total');
  expect(drKinds).toContain('UNCOVERED phases.duration_s_total');

  console.log(
    `COMPARE on /export phases block: ${expected.size} keys changed → ${diff.fails.length} findings ` +
      `(${diff.fails.filter(f => f.kind === 'DIFF').length} DIFF); nulls=${nulls.length}, share strings=${shares.length}`,
  );
});
