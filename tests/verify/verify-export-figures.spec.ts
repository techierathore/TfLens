/**
 * Snapshot export figures — REQ-FN-131 … REQ-FN-134, REQ-FN-136 … REQ-FN-140, REQ-UI-071.
 *
 * Black-box acceptance tests against the running app, signed in as the documented demo user.
 * The snapshot is WRITTEN once from `/export` (press `export-now` with the header Framework switch
 * on TechieFlow), then read back through the authenticated download endpoint the Past-snapshots
 * table links to (`/api/export/download?date=…&framework=…&file=tflens.json|snapshot.md`).
 * Every assertion below is made on what that document — or a screen a signed-in user can open —
 * actually says. Nothing here reads the database or the application source.
 *
 * Pressing Export writes snapshot.md + tflens.json for today; it imports, removes and edits nothing.
 */
import { test, expect, Page, Browser } from '@playwright/test';
import { signIn, gotoScreen, testid, DESKTOP } from './_helpers';

test.use({ viewport: DESKTOP });

type J = any;

/** The written snapshot, shared by every test in this file (one export per worker). */
let SNAP: { json: J; md: string; date: string; framework: string } | null = null;

const FIVE_MODES_PLUS_UNKNOWN = ['subscription', 'metered', 'plan', 'local', 'mixed', 'unknown'];
const LEGAL_BILLING_KEYS = [...FIVE_MODES_PLUS_UNKNOWN, 'unrecorded'];

const isInt = (v: unknown) => typeof v === 'number' && Number.isInteger(v);
const isNum = (v: unknown) => typeof v === 'number' && Number.isFinite(v);
const sumValues = (o: Record<string, number> | null | undefined) =>
  Object.values(o ?? {}).reduce((a, b) => a + (typeof b === 'number' ? b : 0), 0);

/** Make sure the header Framework switch is on TechieFlow before exporting. */
async function ensureTechieFlow(page: Page): Promise<void> {
  const sw = await testid(page, 'framework-switch');
  const active = async () =>
    page.evaluate(() => {
      const root = document.querySelector('[data-testid="framework-switch"]');
      const t = Array.from(root?.querySelectorAll('[role="tab"]') ?? []).find(
        x => x.getAttribute('data-state') === 'active' || x.getAttribute('aria-selected') === 'true',
      );
      return (t?.textContent || '').trim();
    });
  if (!/TechieFlow/i.test(await active())) {
    await sw.locator('[role="tab"]').filter({ hasText: 'TechieFlow' }).first().click();
    await page.waitForTimeout(2_000);
    await gotoScreen(page, '/export');
  }
  expect(await active(), 'the header Framework switch must be on TechieFlow').toMatch(/TechieFlow/i);
}

/** GET a download link with the session; retry while a concurrent export may be mid-write. */
async function fetchText(page: Page, href: string, parseJson: boolean): Promise<string> {
  let last = '';
  for (let i = 0; i < 20; i++) {
    const r = await page.request.get(href);
    last = `${r.status()}`;
    if (r.ok()) {
      const text = await r.text();
      if (!parseJson) {
        if (text.trim().length > 0) return text;
      } else {
        try {
          JSON.parse(text);
          return text;
        } catch {
          last = 'JSON.parse failed (concurrent write?)';
        }
      }
    }
    await page.waitForTimeout(1_000);
  }
  throw new Error(`could not fetch ${href}: ${last}`);
}

/** Write a snapshot from /export and read both files back. */
async function writeAndReadSnapshot(browser: Browser) {
  const ctx = await browser.newContext({ viewport: DESKTOP, baseURL: test.info().project.use.baseURL });
  const page = await ctx.newPage();
  try {
    await signIn(page);
    await gotoScreen(page, '/export');
    await ensureTechieFlow(page);

    // The export target names the folder the button writes: data/reports/<user>/<date>/<framework>/.
    const target = (await (await testid(page, 'export-target')).innerText()).trim();
    const m = target.match(/data\/reports\/[^/]+\/(\d{4}-\d{2}-\d{2})\/([a-z0-9-]+)\//i);
    expect(m, `export-target must name the folder it writes; read "${target}"`).not.toBeNull();
    const [, date, framework] = m!;
    expect(framework).toBe('techieflow');
    const jsonHref = `/api/export/download?date=${date}&framework=${framework}&file=tflens.json`;
    const mdHref = `/api/export/download?date=${date}&framework=${framework}&file=snapshot.md`;

    // What today's snapshot said before this press (if one exists), so a fresh write is provable.
    let before: string | null = null;
    const pre = await page.request.get(jsonHref);
    if (pre.ok()) {
      try { before = JSON.parse(await pre.text())?.parity?.generated_ts ?? null; } catch { before = null; }
    }

    const exportNow = await testid(page, 'export-now');
    await expect(exportNow).toBeEnabled({ timeout: 20_000 });
    await exportNow.click();

    // The Past-snapshots table must show today's snapshot with both download links.
    await expect(page.locator(`[data-testid="snapshots-table"] a[href="${jsonHref}"]`).first()).toBeAttached({
      timeout: 30_000,
    });
    await expect(page.locator(`[data-testid="snapshots-table"] a[href="${mdHref}"]`).first()).toBeAttached();

    // Wait until the document on disk is the one this press wrote (another writer may press too —
    // any newer write is still a snapshot written from /export).
    let jsonText = '';
    for (let i = 0; i < 30; i++) {
      jsonText = await fetchText(page, jsonHref, true);
      const ts = JSON.parse(jsonText)?.parity?.generated_ts ?? null;
      if (before === null || (ts !== null && ts !== before)) break;
      await page.waitForTimeout(1_000);
    }
    const json = JSON.parse(jsonText);
    const md = await fetchText(page, mdHref, false);
    console.log(
      `SNAPSHOT ${date}/${framework}: generated_ts before=${before} after=${json?.parity?.generated_ts}; ` +
        `tflens.json ${jsonText.length} bytes, snapshot.md ${md.length} bytes`,
    );
    return { json, md, date, framework };
  } finally {
    await ctx.close();
  }
}

test.beforeAll(async ({ browser }) => {
  test.setTimeout(180_000);
  if (!SNAP) SNAP = await writeAndReadSnapshot(browser);
});

function phasesBlock(): J {
  const ph = SNAP!.json.phases;
  expect(ph, 'the snapshot must carry a `phases` block').toBeTruthy();
  expect(typeof ph.phases, '`phases.phases` must hold one entry per command phase').toBe('object');
  return ph;
}

function eachPhase(): [string, J][] {
  return Object.entries(phasesBlock().phases as Record<string, J>);
}

/* ─────────────────────────────── durations ─────────────────────────────── */

test('REQ-FN-131 — duration_measured_n sits beside the duration totals and never exceeds the live run count', async () => {
  const ph = phasesBlock();
  console.log(
    `phases: runs_live=${ph.runs_live} duration_s_total=${ph.duration_s_total} duration_measured_n=${ph.duration_measured_n}`,
  );

  // Beside the totals: the same object that carries duration_s_total carries its denominator.
  expect(Object.keys(ph), '`phases` must carry duration_s_total').toContain('duration_s_total');
  expect(Object.keys(ph), '`phases` must carry duration_measured_n beside it').toContain('duration_measured_n');
  expect(isInt(ph.duration_measured_n), 'duration_measured_n must be a whole count').toBe(true);
  expect(ph.duration_measured_n).toBeGreaterThanOrEqual(0);

  // Never larger than the live run count as the export states it.
  expect(isInt(ph.runs_live), '`phases.runs_live` must be a whole count').toBe(true);
  expect(ph.duration_measured_n, 'duration_measured_n must not exceed runs_live').toBeLessThanOrEqual(ph.runs_live);

  // It is the total's denominator: the per-phase counts of timed runs add up to it, each phase's count
  // never exceeds that phase's runs, and the per-phase totals add up to duration_s_total.
  let nSum = 0;
  let totalSum = 0;
  for (const [name, p] of eachPhase()) {
    expect(isInt(p.duration_s?.n), `${name}: duration_s.n must be a whole count`).toBe(true);
    expect(p.duration_s.n, `${name}: timed runs must not exceed runs`).toBeLessThanOrEqual(p.runs);
    nSum += p.duration_s.n;
    totalSum += p.duration_s.total ?? 0;
  }
  expect(nSum, 'Σ per-phase duration_s.n must equal duration_measured_n').toBe(ph.duration_measured_n);
  expect(totalSum, 'Σ per-phase duration_s.total must equal duration_s_total').toBe(ph.duration_s_total);
});

test('REQ-FN-132 — duration_impossible_n is published and an ended-before-started run adds nothing to any duration total', async () => {
  const ph = phasesBlock();
  expect(Object.keys(ph), '`phases` must carry duration_impossible_n').toContain('duration_impossible_n');
  expect(isInt(ph.duration_impossible_n), 'duration_impossible_n must be a whole count').toBe(true);
  expect(ph.duration_impossible_n).toBeGreaterThanOrEqual(0);

  // Black-box proof of the second half. The document does not name the individual records, so what it
  // lets us assert is the reconciliation: every live run is exactly one of measured, impossible or
  // absent. If an impossible run were contributing a duration it would have to be counted as measured
  // too (or double-counted), and this partition would not hold. Together with the REQ-FN-131 check that
  // the per-phase totals are summed over exactly duration_measured_n runs, an impossible run cannot be
  // inside any duration total.
  const measured = ph.duration_measured_n;
  const impossible = ph.duration_impossible_n;
  const absent = ph.duration_absent_n;
  console.log(`measured ${measured} + impossible ${impossible} + absent ${absent} vs runs_live ${ph.runs_live}`);
  expect(isInt(absent), 'duration_absent_n must be present to reconcile').toBe(true);
  expect(measured + impossible + absent, 'measured + impossible + absent must reconcile to runs_live').toBe(
    ph.runs_live,
  );

  let nSum = 0;
  for (const [, p] of eachPhase()) nSum += p.duration_s?.n ?? 0;
  expect(nSum, 'the duration totals are taken over the measured runs only').toBe(measured);
  expect(nSum + impossible, 'an impossible run is never among the timed runs').toBeLessThanOrEqual(ph.runs_live);
});

test('REQ-FN-133 — duration_absent_n is published and neither the snapshot nor /effort calls those runs corrupt, impossible or failing', async ({
  page,
}) => {
  test.setTimeout(150_000);
  const ph = phasesBlock();
  expect(Object.keys(ph), '`phases` must carry duration_absent_n').toContain('duration_absent_n');
  expect(isInt(ph.duration_absent_n), 'duration_absent_n must be a whole count').toBe(true);
  expect(ph.duration_absent_n).toBeGreaterThanOrEqual(0);
  console.log(`duration_absent_n = ${ph.duration_absent_n}`);

  // The snapshot's own labels: no duration key names these runs corrupt / failing / invalid.
  const durationKeys = Object.keys(ph).filter(k => k.startsWith('duration'));
  console.log(`duration keys in phases: ${durationKeys.join(', ')}`);
  for (const k of durationKeys) {
    expect(k, `phases key "${k}" must not label runs corrupt or failing`).not.toMatch(/corrupt|fail|invalid|broken|bad/i);
  }

  // Any sentence in the snapshot (tflens.json strings and snapshot.md) that speaks about runs recording no
  // elapsed time must not call them corrupt, impossible or failing.
  const strings: string[] = [];
  const walk = (o: J) => {
    if (typeof o === 'string') strings.push(o);
    else if (Array.isArray(o)) o.forEach(walk);
    else if (o && typeof o === 'object') Object.values(o).forEach(walk);
  };
  walk(SNAP!.json);
  strings.push(...SNAP!.md.split(/\n/));
  const aboutAbsent = strings
    .flatMap(s => s.split(/(?<=[.;])\s+/))
    .filter(s => /duration_absent|no elapsed time|records? no elapsed|same second|carr(y|ies) no duration/i.test(s));
  console.log(`snapshot sentences about runs recording no elapsed time: ${aboutAbsent.length}`);
  for (const s of aboutAbsent) {
    console.log(`  · ${s.slice(0, 200)}`);
    expect(s, 'the snapshot must not call a no-elapsed-time run corrupt, impossible or failing').not.toMatch(
      /corrupt|impossib|\bfail/i,
    );
  }

  // /effort — the wall-clock KPI and its derived-duration note, and every phase's TIME note, which is where
  // untimed runs are described.
  await signIn(page);
  await gotoScreen(page, '/effort');
  const wall = (await (await testid(page, 'kpi-wallclock')).innerText()).replace(/\s+/g, ' ').trim();
  const derived = (await (await testid(page, 'kpi-wallclock-derived')).innerText()).replace(/\s+/g, ' ').trim();
  console.log(`/effort kpi-wallclock = "${wall}"`);
  console.log(`/effort kpi-wallclock-derived = "${derived}"`);
  for (const t of [wall, derived]) {
    expect(t, '/effort wall-clock KPI must not call untimed runs corrupt, impossible or failing').not.toMatch(
      /corrupt|impossib|\bfail/i,
    );
  }

  const triggers = await page.locator('[data-testid^="effort-detail-trigger-"]').all();
  expect(triggers.length, '/effort must list its per-phase details').toBeGreaterThan(0);
  for (const trig of triggers) {
    const tid = (await trig.getAttribute('data-testid'))!;
    const name = tid.replace('effort-detail-trigger-', '');
    const detail = page.locator(`[data-testid="effort-detail-${name}"]`).first();
    if (!/Wall-clock elapsed/.test(await detail.innerText().catch(() => ''))) {
      await trig.click();
      await expect(detail).toContainText('Wall-clock elapsed', { timeout: 10_000 });
    }
    const timeNote = (await detail.innerText())
      .split(/\n/)
      .filter(l => /Wall-clock elapsed|timed|duration/i.test(l))
      .join(' ');
    console.log(`/effort ${name} TIME: ${timeNote.replace(/^.*?(All \d+ runs|Timed on)/, '$1').slice(0, 220)}`);
    expect(timeNote, `/effort ${name}: untimed runs must not be called corrupt, impossible or failing`).not.toMatch(
      /corrupt|impossib|\bfail/i,
    );
  }
});

test('REQ-FN-134 — duration_recomputed_n sits beside the duration totals', async () => {
  const ph = phasesBlock();
  expect(Object.keys(ph), '`phases` must carry duration_s_total').toContain('duration_s_total');
  expect(Object.keys(ph), '`phases` must carry duration_recomputed_n beside it').toContain('duration_recomputed_n');
  expect(isInt(ph.duration_recomputed_n), 'duration_recomputed_n must be a whole count').toBe(true);
  expect(ph.duration_recomputed_n).toBeGreaterThanOrEqual(0);
  // A recomputed record is one whose own timestamps produced its duration, so it is among the measured runs.
  expect(ph.duration_recomputed_n, 'recomputed runs are a subset of measured runs').toBeLessThanOrEqual(
    ph.duration_measured_n,
  );
  console.log(`duration_recomputed_n = ${ph.duration_recomputed_n} of ${ph.duration_measured_n} measured`);
});

/* ─────────────────────────────── billing & money ─────────────────────────────── */

test('REQ-FN-136 — run records are counted by billing_mode and a record written before 2026-09-10 is counted as unrecorded', async () => {
  const phases = eachPhase();
  expect(phases.length).toBeGreaterThan(0);

  const agg: Record<string, number> = {};
  for (const [name, p] of phases) {
    expect(p.billing_modes, `${name}: billing_modes must be present`).toBeTruthy();
    expect(typeof p.billing_modes, `${name}: billing_modes must be a count map`).toBe('object');
    for (const [k, v] of Object.entries(p.billing_modes as Record<string, number>)) {
      expect(LEGAL_BILLING_KEYS, `${name}: billing mode "${k}" must be one of the five, unknown or unrecorded`).toContain(k);
      expect(isInt(v) && v >= 0, `${name}: billing_modes.${k} must be a whole count`).toBe(true);
      agg[k] = (agg[k] ?? 0) + v;
    }
    expect(sumValues(p.billing_modes), `${name}: billing-mode counts must not exceed the phase's runs`).toBeLessThanOrEqual(
      p.runs,
    );
  }
  const counted = sumValues(agg);
  console.log(`billing_modes over all phases: ${JSON.stringify(agg)} — ${counted} of ${phasesBlock().runs_live} live runs`);

  // Second half, black box. The snapshot's routing list (extras.routing.drift) carries each routed run's
  // `ts` and `cmd`. Where that list covers EVERY live run of a phase and the latest of them was written
  // before 2026-09-10, every one of that phase's records predates billing_mode — so the phase may count
  // them only as `unrecorded`, never as one of the five modes or unknown.
  const drift: { ts: string; cmd: string }[] = SNAP!.json?.extras?.routing?.drift ?? [];
  const byCmd = new Map<string, string[]>();
  for (const d of drift) {
    if (!d?.cmd || !d?.ts) continue;
    byCmd.set(d.cmd, [...(byCmd.get(d.cmd) ?? []), d.ts]);
  }
  const allBefore = phases.filter(([name, p]) => {
    const ts = byCmd.get(name) ?? [];
    return ts.length === p.runs && ts.length > 0 && ts.every(t => t < '2026-09-10');
  });
  console.log(
    `phases whose every live run is dated before 2026-09-10 in the snapshot: ${allBefore.map(([n]) => n).join(', ') || '(none)'}`,
  );
  test.skip(
    allBefore.length === 0,
    'no phase in today\'s snapshot has every run dated (extras.routing.drift) and all before 2026-09-10',
  );
  for (const [name, p] of allBefore) {
    const recorded = Object.keys(p.billing_modes).filter(k => k !== 'unrecorded' && p.billing_modes[k] > 0);
    expect(recorded, `${name}: every record predates 2026-09-10, so none may be counted as ${recorded.join(', ')}`).toEqual([]);
  }
});

test('REQ-FN-137 — money_usd, plan_allowance_usd and money_mixed_usd are separate figures, each with its own record count, never added together', async () => {
  const COMBINED = /((money|allowance|spend|mixed).*(total|sum|combined|all|grand))|((total|sum|combined|grand).*(money|allowance|spend))/i;
  for (const [name, p] of eachPhase()) {
    const figures: [string, string][] = [
      ['money_usd', 'money_records'],
      ['plan_allowance_usd', 'plan_allowance_records'],
      ['money_mixed_usd', 'money_mixed_records'],
    ];
    for (const [usd, n] of figures) {
      expect(Object.keys(p), `${name}: must carry ${usd}`).toContain(usd);
      expect(Object.keys(p), `${name}: must carry ${n} beside ${usd}`).toContain(n);
      expect(p[usd] === null || isNum(p[usd]), `${name}: ${usd} must be a number`).toBe(true);
      expect(isInt(p[n]) && p[n] >= 0, `${name}: ${n} must be a whole count`).toBe(true);
    }

    // Each figure is taken over its own billing mode only — so a subscription's zero can never be spend.
    const bm = p.billing_modes ?? {};
    expect(p.money_records, `${name}: money_records counts metered records only`).toBeLessThanOrEqual(bm.metered ?? 0);
    expect(p.plan_allowance_records, `${name}: plan_allowance_records counts plan records only`).toBeLessThanOrEqual(
      bm.plan ?? 0,
    );
    expect(p.money_mixed_records, `${name}: money_mixed_records counts mixed records only`).toBeLessThanOrEqual(
      bm.mixed ?? 0,
    );

    // No key adds them together — neither by name…
    const combinedKeys = Object.keys(p).filter(k => COMBINED.test(k));
    expect(combinedKeys, `${name}: no key may combine the three money figures`).toEqual([]);
    // …nor by value: where at least two are non-zero, no other figure in the phase equals their sum.
    const vals = [p.money_usd ?? 0, p.plan_allowance_usd ?? 0, p.money_mixed_usd ?? 0];
    if (vals.filter(v => v !== 0).length >= 2) {
      const s = vals.reduce((a, b) => a + b, 0);
      const hits = Object.entries(p).filter(
        ([k, v]) => isNum(v) && !['money_usd', 'plan_allowance_usd', 'money_mixed_usd'].includes(k) && Math.abs((v as number) - s) < 1e-6,
      );
      expect(hits.map(([k]) => k), `${name}: no figure may equal money + allowance + mixed`).toEqual([]);
    }
    console.log(
      `${name}: money ${p.money_usd}/${p.money_records} · allowance ${p.plan_allowance_usd}/${p.plan_allowance_records} · ` +
        `mixed ${p.money_mixed_usd}/${p.money_mixed_records} · billing ${JSON.stringify(bm)}`,
    );
  }
  // Nor at the pooled level.
  const pooledCombined = Object.keys(SNAP!.json.pooled ?? {}).filter(k => COMBINED.test(k));
  expect(pooledCombined, 'no pooled key may combine the three money figures').toEqual([]);
});

test('REQ-UI-071 — each phase carries list_usd with list_usd_records and list_usd_unpriced_n beside it', async () => {
  for (const [name, p] of eachPhase()) {
    for (const k of ['list_usd', 'list_usd_records', 'list_usd_unpriced_n']) {
      expect(Object.keys(p), `${name}: must carry ${k}`).toContain(k);
    }
    expect(p.list_usd === null || isNum(p.list_usd), `${name}: list_usd must be a number`).toBe(true);
    expect(isInt(p.list_usd_records) && p.list_usd_records >= 0, `${name}: list_usd_records must be a whole count`).toBe(true);
    expect(isInt(p.list_usd_unpriced_n) && p.list_usd_unpriced_n >= 0, `${name}: list_usd_unpriced_n must be a whole count`).toBe(
      true,
    );

    // A list price is computed only for runs whose tokens were measured, and every measured run is either
    // priced or counted as unpriced — never silently dropped.
    const measured = p.tokens_measured_n;
    expect(p.list_usd_records, `${name}: priced records cannot exceed runs with measured tokens`).toBeLessThanOrEqual(measured);
    expect(
      p.list_usd_records + p.list_usd_unpriced_n,
      `${name}: every measured run must be priced or counted as unpriced`,
    ).toBeGreaterThanOrEqual(measured);

    // A phase that ran only unpriced models must not read as free.
    if (p.list_usd_records === 0 && p.list_usd_unpriced_n > 0) {
      expect(p.list_usd, `${name}: only unpriced models ran — list_usd must not read 0`).not.toBe(0);
    }
    // No model that produced output is priced at zero.
    for (const [model, m] of Object.entries((p.models ?? {}) as Record<string, J>)) {
      if ((m?.tokens_out ?? 0) > 0 && 'list_usd' in (m ?? {})) {
        expect(m.list_usd, `${name}/${model}: a model with output must never be priced at $0`).not.toBe(0);
      }
    }
    console.log(
      `${name}: list_usd ${p.list_usd} over ${p.list_usd_records} records · unpriced ${p.list_usd_unpriced_n} · measured ${measured}`,
    );
  }
});

test('REQ-UI-071 — a model with no rate is counted as unpriced, never priced at zero', async () => {
  const phases = eachPhase();
  const unpricedPhases = phases.filter(([, p]) => (p.list_usd_unpriced_n ?? 0) > 0);
  const missing: string[] = SNAP!.json?.extras?.repricing?.missing_price_models ?? [];
  console.log(
    `phases with unpriced runs: ${unpricedPhases.map(([n, p]) => `${n}=${p.list_usd_unpriced_n}`).join(', ') || '(none)'}; ` +
      `extras.repricing.missing_price_models: ${JSON.stringify(missing)}`,
  );
  test.skip(
    unpricedPhases.length === 0 && missing.length === 0,
    'today\'s snapshot has no model the rate card cannot price (every list_usd_unpriced_n is 0 and missing_price_models is empty)',
  );
  for (const [name, p] of unpricedPhases) {
    if (p.list_usd_records === 0) {
      expect(p.list_usd, `${name}: ran only unpriced models — list_usd must not read 0`).not.toBe(0);
    }
  }
  for (const model of missing) {
    for (const [name, p] of phases) {
      const m = p.models?.[model];
      if (m && 'list_usd' in m) {
        expect(m.list_usd, `${name}/${model}: a model with no rate must never be priced at $0`).not.toBe(0);
      }
    }
  }
});

/* ─────────────────────────────── reviews & misses ─────────────────────────────── */

test('REQ-FN-138 — reviews_n, review_corrections and reviews_by_phase are published and no review reaches a miss count', async ({
  page,
}) => {
  const mi = SNAP!.json.misses;
  expect(mi, 'the snapshot must carry a misses section').toBeTruthy();
  for (const k of ['reviews_n', 'review_corrections', 'reviews_by_phase']) {
    expect(Object.keys(mi), `misses must carry ${k}`).toContain(k);
  }
  expect(isInt(mi.reviews_n) && mi.reviews_n >= 0, 'reviews_n must be a whole count').toBe(true);
  expect(isNum(mi.review_corrections), 'review_corrections must be a number').toBe(true);
  expect(typeof mi.reviews_by_phase).toBe('object');
  expect(sumValues(mi.reviews_by_phase), 'Σ reviews_by_phase must equal reviews_n').toBe(mi.reviews_n);
  console.log(
    `reviews_n=${mi.reviews_n} review_corrections=${mi.review_corrections} reviews_by_phase=${JSON.stringify(mi.reviews_by_phase)} · misses_total=${mi.misses_total}`,
  );

  // Every miss count partitions misses_total — a review record slipped in would have to land in one of these.
  expect(mi.open_misses + mi.wont_fix + mi.resolved_misses, 'open + wont_fix + resolved = misses_total').toBe(mi.misses_total);
  expect(sumValues(mi.class_distribution), 'Σ class_distribution = misses_total').toBe(mi.misses_total);
  expect(sumValues(mi.found_by), 'Σ found_by = misses_total').toBe(mi.misses_total);
  // No review phase appears as a bucket of any miss distribution.
  const reviewPhases = Object.keys(mi.reviews_by_phase ?? {});
  for (const dist of ['class_distribution', 'found_by', 'why_missed', 'sort', 'by_origin_phase']) {
    const keys = Object.keys(mi[dist] ?? {});
    for (const rp of reviewPhases) {
      expect(keys, `misses.${dist} must not carry the review phase "${rp}"`).not.toContain(rp);
    }
  }

  // Cross-check against the Coverage screen, which states the miss count and the review-record count apart:
  // the snapshot's miss count must be the screen's, with the review records outside it.
  await signIn(page);
  await gotoScreen(page, '/');
  const body = (await page.locator('main').first().innerText()).replace(/\s+/g, ' ');
  const missesOnScreen = body.match(/(\d[\d,]*) misses · (\d[\d,]*) fixes/);
  const reviewsOnScreen = body.match(/review records read \(not misses\):\s*(\d[\d,]*) records?/i);
  console.log(`Coverage: "${missesOnScreen?.[0]}" · "${reviewsOnScreen?.[0]}"`);
  expect(missesOnScreen, 'Coverage must state the miss count').not.toBeNull();
  expect(Number(missesOnScreen![1].replace(/,/g, '')), 'snapshot misses_total = Coverage miss count').toBe(mi.misses_total);
  expect(reviewsOnScreen, 'Coverage must state the review records apart from the misses').not.toBeNull();
  expect(Number(reviewsOnScreen![1].replace(/,/g, '')), 'snapshot reviews_n = Coverage review-record count').toBe(mi.reviews_n);
});

test('REQ-FN-139 — cost_usd_excluded_not_money_n sits beside the measured-dollars figure, never inside it, with cost_list_usd_per_miss', async () => {
  const mi = SNAP!.json.misses;
  for (const k of [
    'cost_usd_per_miss_measured',
    'cost_usd_records',
    'cost_usd_excluded_not_money_n',
    'cost_list_usd_per_miss',
    'cost_list_usd_records',
  ]) {
    expect(Object.keys(mi), `misses must carry ${k}`).toContain(k);
  }
  console.log(
    `cost_usd_per_miss_measured=${mi.cost_usd_per_miss_measured} over ${mi.cost_usd_records} · ` +
      `cost_usd_excluded_not_money_n=${mi.cost_usd_excluded_not_money_n} · ` +
      `cost_list_usd_per_miss=${mi.cost_list_usd_per_miss} over ${mi.cost_list_usd_records}`,
  );
  expect(isInt(mi.cost_usd_excluded_not_money_n) && mi.cost_usd_excluded_not_money_n >= 0).toBe(true);
  expect(isInt(mi.cost_usd_records) && mi.cost_usd_records >= 0).toBe(true);

  // Never inside it: the measured-dollars figure is taken over its own measuring records only. With none,
  // it is absent (null), never a zero — a zero there is exactly a not-money zero leaking in.
  if (mi.cost_usd_records === 0) {
    expect(mi.cost_usd_per_miss_measured, 'no measuring record → the measured figure is absent, not $0').toBeNull();
  } else {
    expect(isNum(mi.cost_usd_per_miss_measured), 'measured dollars per miss must be a number').toBe(true);
    expect(mi.cost_usd_per_miss_measured, 'a subscription zero must not sit inside measured dollars').not.toBe(0);
  }

  // The list-price figure is money-shaped on every harness and carries its own count.
  expect(isInt(mi.cost_list_usd_records) && mi.cost_list_usd_records >= 0).toBe(true);
  if (mi.cost_list_usd_records > 0) {
    expect(isNum(mi.cost_list_usd_per_miss), 'cost_list_usd_per_miss must be a number when records exist').toBe(true);
  } else {
    expect(mi.cost_list_usd_per_miss, 'with no priced record, cost_list_usd_per_miss is absent, not $0').toBeNull();
  }
  expect(mi.cost_list_usd_records, 'priced repairs cannot exceed miss fixes').toBeLessThanOrEqual(mi.miss_fixes_total);
});

/* ─────────────────────────────── run voids ─────────────────────────────── */

test('REQ-FN-140 — runs_voided_n, runs_voided and run_voids_orphaned_n are published and no void is counted as a live run', async () => {
  const j = SNAP!.json;
  for (const k of ['runs_voided_n', 'runs_voided', 'run_voids_orphaned_n']) {
    expect(Object.keys(j), `the snapshot must carry ${k}`).toContain(k);
  }
  expect(isInt(j.runs_voided_n) && j.runs_voided_n >= 0).toBe(true);
  expect(Array.isArray(j.runs_voided), 'runs_voided must list the reasons').toBe(true);
  expect(j.runs_voided.length, 'runs_voided lists one reason per voided run').toBe(j.runs_voided_n);
  for (const r of j.runs_voided) expect(String(r).trim().length, 'each void reason must say why').toBeGreaterThan(0);
  expect(isInt(j.run_voids_orphaned_n) && j.run_voids_orphaned_n >= 0).toBe(true);
  console.log(`runs_voided_n=${j.runs_voided_n} run_voids_orphaned_n=${j.run_voids_orphaned_n}`);
  for (const r of j.runs_voided) console.log(`  · ${String(r).slice(0, 160)}`);

  // The live run count the export states is one number everywhere it is stated as such.
  const ph = phasesBlock();
  let phaseRuns = 0;
  for (const [, p] of eachPhase()) phaseRuns += p.runs;
  const perRepo = (j.per_repo ?? []).reduce((a: number, r: J) => a + (r.runs ?? 0), 0);
  console.log(
    `live runs: phases.runs_live=${ph.runs_live} Σphase.runs=${phaseRuns} pooled.runs_total=${j.pooled?.runs_total} ` +
      `Σpooled.runs_by_cmd=${sumValues(j.pooled?.runs_by_cmd)} Σper_repo.runs=${perRepo}`,
  );
  expect(phaseRuns, 'Σ phase runs = runs_live').toBe(ph.runs_live);
  expect(j.pooled.runs_total, 'pooled.runs_total = runs_live').toBe(ph.runs_live);
  expect(sumValues(j.pooled.runs_by_cmd), 'Σ pooled.runs_by_cmd = runs_live').toBe(ph.runs_live);
  expect(perRepo, 'Σ per_repo.runs = runs_live').toBe(ph.runs_live);
});

test('REQ-FN-140 — no other run count in the snapshot or on /effort counts a voided run or a void record', async ({ page }) => {
  test.setTimeout(120_000);
  const j = SNAP!.json;
  const live = phasesBlock().runs_live as number;
  test.skip(
    (j.runs_voided_n ?? 0) === 0,
    'today\'s snapshot names no voided run, so there is no void to leak into another count',
  );

  // The harness comparison counts runs per harness. Voids honoured, those counts can never exceed the live
  // run count, and per command they can never exceed that command's live runs.
  const cols: J[] = j.extras?.harness?.columns ?? [];
  const harnessRuns = cols.reduce((a, c) => a + (c.runs ?? 0), 0);
  const byCmd: Record<string, number> = {};
  for (const c of cols) for (const [cmd, n] of Object.entries((c.runs_by_cmd ?? {}) as Record<string, number>)) byCmd[cmd] = (byCmd[cmd] ?? 0) + n;
  const over = Object.entries(byCmd)
    .map(([cmd, n]) => [cmd, n, j.phases.phases[cmd]?.runs ?? 0] as [string, number, number])
    .filter(([, n, lv]) => n > lv);
  console.log(
    `snapshot harness columns: ${cols.map(c => `${c.harness}=${c.runs}`).join(', ')} (Σ ${harnessRuns}) vs runs_live ${live}; ` +
      `commands over their live runs: ${over.map(([c, n, lv]) => `${c} ${n}>${lv}`).join(', ') || '(none)'}`,
  );

  // /effort states its run count as "live records".
  await signIn(page);
  await gotoScreen(page, '/effort');
  const kpi = (await (await testid(page, 'kpi-runs')).innerText()).replace(/\s+/g, ' ').trim();
  const effortRuns = Number((kpi.match(/(\d[\d,]*)/)?.[1] ?? 'NaN').replace(/,/g, ''));
  console.log(`/effort kpi-runs = "${kpi}" → ${effortRuns} vs snapshot runs_live ${live}`);

  expect.soft(harnessRuns, 'Σ snapshot harness-column runs must not exceed runs_live').toBeLessThanOrEqual(live);
  expect.soft(over, 'no command may have more harness-column runs than live runs').toEqual([]);
  expect.soft(effortRuns, '/effort "Runs recorded" must equal the snapshot\'s live run count').toBe(live);
});
