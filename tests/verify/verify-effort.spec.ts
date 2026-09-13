/*
 * Verify run — `/effort` functional rows REQ-FN-089 … REQ-FN-092, REQ-FN-096 … REQ-FN-102 and REQ-FN-112.
 *
 * Black-box only. Every assertion is about what the signed-in demo user can see in the browser, or what the
 * authenticated snapshot download hands back after `export-now` is pressed on `/export`. Nothing here reads
 * the database, imports app code, or presses a control that changes the user's telemetry.
 *
 * The TechieFlow axis carries real data, so its rows are graded arithmetically: the numbers the page prints
 * must add up the way the acceptance line says they do (a mean that was averaged over zeros does not multiply
 * back to its own total; two exclusions that were pooled do not sum to the population).
 *
 * The Playbook axis has no phase-execution data today and renders its HarnessUnsupported state. Each schema-2
 * row (FN-096..FN-102) is therefore split in two: the invariant as it is observable in the current state
 * (the state is named, nothing numeric is published in its place, no zero stands in for a gap), and the
 * populated clause, which skips at runtime — truthfully — while no execution exists to grade.
 */
import { test, Page } from '@playwright/test';
import { signIn, gotoScreen, testid, collectErrors, USER1, DESKTOP, expect } from './_helpers';

const EM_DASH = '—';
const INSUFFICIENT = /^insufficient data \(n=(\d+)\)$/;

let objErrors: string[] = [];

test.beforeEach(async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  objErrors = collectErrors(page);
  await signIn(page, USER1);
});

test.afterEach(async () => {
  const real = objErrors.filter(e => !/favicon|websocket/i.test(e));
  if (real.length > 0) console.log(`CONSOLE-ERRORS (${real.length}): ${real.slice(0, 8).join(' | ')}`);
});

/* ───────────────────────────── local helpers ───────────────────────────── */

async function textOf(page: Page, id: string): Promise<string | null> {
  if ((await page.locator(`[data-testid="${id}"]`).count()) === 0) return null;
  const t = await page.locator(`[data-testid="${id}"]`).first().innerText().catch(() => '');
  return (t || '').replace(/\s+/g, ' ').trim();
}

async function exists(page: Page, id: string): Promise<boolean> {
  return (await page.locator(`[data-testid="${id}"]`).count()) > 0;
}

/** "1,234" → 1234. */
function num(s: string | undefined | null): number {
  return Number(String(s ?? '').replace(/,/g, ''));
}

/** First integer captured by `re` in `text`, or NaN. */
function grab(text: string, re: RegExp): number {
  const m = text.match(re);
  return m ? num(m[1]) : NaN;
}

/**
 * Reads an abbreviated token figure ("17.5M", "669k", "304", "0") into a value and the width of one unit of
 * its last printed digit — the rounding a reader has to allow when adding printed figures back up.
 */
function abbrev(s: string): { value: number; unit: number } | null {
  const m = s.trim().match(/^([\d.,]+)\s*([kMB])?$/);
  if (!m) return null;
  const mult = m[2] === 'k' ? 1e3 : m[2] === 'M' ? 1e6 : m[2] === 'B' ? 1e9 : 1;
  const raw = m[1].replace(/,/g, '');
  const decimals = raw.includes('.') ? raw.split('.')[1].length : 0;
  return { value: Number(raw) * mult, unit: Math.pow(10, -decimals) * mult };
}

type PhaseRow = {
  key: string; cmd: string; runs: number; shareTime: string; tokens: string; shareOut: string;
  measured: number; measuredOf: number; fanout: string;
};

/** The command-phase table, read as the reader sees it. */
async function phaseRows(page: Page): Promise<PhaseRow[]> {
  const raw = await page.evaluate(() => {
    const root = document.querySelector('[data-testid="effort-phase-table"]');
    const heads = Array.from(root?.querySelectorAll('thead th') || []).map(h => (h.textContent || '').trim().toLowerCase());
    const col = (n: string) => heads.indexOf(n);
    return {
      heads,
      rows: Array.from(root?.querySelectorAll('tbody tr') || []).map(tr => {
        const tds = Array.from(tr.querySelectorAll('td')).map(td => (td.textContent || '').replace(/\s+/g, ' ').trim());
        const key = (tr.querySelector('[data-testid^="phase-cmd-"]')?.getAttribute('data-testid') || '').replace(/^phase-cmd-/, '');
        return {
          key,
          cmd: tds[col('command phase')], runs: tds[col('runs')], shareTime: tds[col('share of time')],
          tokens: tds[col('output tokens')], shareOut: tds[col('share of output')],
          measured: tds[col('measured')], fanout: tds[col('fan-out')],
        };
      }),
    };
  });
  return raw.rows.map(r => {
    const m = (r.measured || '').match(/^(\d[\d,]*) of (\d[\d,]*)$/) || ['', 'NaN', 'NaN'];
    return {
      key: r.key, cmd: r.cmd, runs: num(r.runs), shareTime: r.shareTime, tokens: r.tokens, shareOut: r.shareOut,
      measured: num(m[1]), measuredOf: num(m[2]), fanout: r.fanout,
    };
  });
}

/**
 * Opens every per-phase disclosure that is not already open.
 *
 * Openness is judged by VISIBILITY, never by presence: a closed `CollapsibleContent` still renders
 * its subtree behind `hidden` + `display:none`, so a presence probe would find a body on every phase
 * and never press a trigger.
 */
async function openAllPhases(page: Page, keys: string[]): Promise<void> {
  for (const key of keys) {
    const open = await page.evaluate((k: string) => {
      const el = document.querySelector(`[data-testid="effort-detail-${k}"] .tflens-detail-body`);
      return !!el && (el as HTMLElement).getClientRects().length > 0;
    }, key);
    if (!open) {
      await page.locator(`[data-testid="effort-detail-trigger-${key}"]`).first().click();
      await page.waitForTimeout(700);
    }
  }
}

type Band = { text: string; rows: string[][] };

/** One band (0 time, 1 tokens, 2 by model, 3 fan-out) of one phase's detail, as visible text and table rows. */
async function band(page: Page, key: string, index: number): Promise<Band> {
  return page.evaluate(({ k, i }) => {
    const card = document.querySelector(`[data-testid="effort-detail-${k}"]`);
    const p = Array.from(card?.querySelectorAll('.tflens-panel') || [])[i] as HTMLElement | undefined;
    return {
      text: (p?.innerText || '').replace(/\s+/g, ' ').trim(),
      rows: Array.from(p?.querySelectorAll('tbody tr') || []).map(tr =>
        Array.from(tr.querySelectorAll('td')).map(td => (td.textContent || '').replace(/\s+/g, ' ').trim())),
    };
  }, { k: key, i: index });
}

/** Puts the header Framework switch on one axis and waits for the page to re-query. */
async function switchFramework(page: Page, label: 'TechieFlow' | 'Playbook'): Promise<void> {
  const sw = await testid(page, 'framework-switch');
  const trigger = sw.locator('[role="tab"]').filter({ hasText: label }).first();
  if ((await trigger.count()) === 0) await sw.getByText(label, { exact: false }).first().click();
  else await trigger.click();
  await page.waitForTimeout(2_500);
}

type PbState = { active: string; listed: string[]; populated: boolean; unsupported: boolean; empty: boolean; surface: string };

/** Opens `/effort` on the Playbook axis and reports which of its states it is in. */
async function openPlaybook(page: Page): Promise<PbState> {
  await gotoScreen(page, '/effort');
  await switchFramework(page, 'Playbook');
  await (await testid(page, 'pb-effort-states')).waitFor({ state: 'attached' });
  const listed = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid^="pb-effort-state-"]'))
      .map(e => (e.getAttribute('data-testid') || '').replace(/^pb-effort-state-/, ''))
      .filter(n => n !== 'active'));
  const state: PbState = {
    active: (await textOf(page, 'pb-effort-state-active')) || '',
    listed,
    populated: await exists(page, 'pb-effort-summary'),
    unsupported: await exists(page, 'pb-effort-unsupported'),
    empty: await exists(page, 'pb-effort-empty'),
    surface: (await page.locator('[data-testid="effort-page"]').first().innerText()).replace(/\s+/g, ' '),
  };
  console.log(`PLAYBOOK STATE: active="${state.active}" populated=${state.populated} unsupported=${state.unsupported} empty=${state.empty}`);
  return state;
}

async function backToTechieFlow(page: Page): Promise<void> {
  await gotoScreen(page, '/effort').catch(() => {});
  await switchFramework(page, 'TechieFlow').catch(() => {});
}

/**
 * The invariant every schema-2 row shares in the current state: the page names which state it is in, and
 * publishes no numeric body — and no zero, no empty measured figure — in place of the data it does not have.
 */
async function assertPlaybookStateNamedAndNothingZeroed(page: Page, s: PbState): Promise<void> {
  expect(await exists(page, 'pb-effort-error'), 'the Playbook effort surface rendered its error alert').toBe(false);
  expect(s.active.length, 'no Playbook state is named as active').toBeGreaterThan(0);
  expect(s.listed, `the active state "${s.active}" is not one of the listed states`).toContain(s.active);
  if (s.populated) return;
  expect(s.unsupported || s.empty,
    'the Playbook surface has no numeric body and names neither the unsupported banner nor the empty state').toBe(true);
  const bare = await page.evaluate(() => {
    const out: { id: string; text: string }[] = [];
    for (const el of Array.from(document.querySelectorAll('[data-testid^="pb-"]'))) {
      if (el.querySelector('[data-testid]')) continue;
      const t = ((el as HTMLElement).innerText || '').trim();
      if (/^(\$?0(\.0+)?%?|\$0\.00|free)$/i.test(t) || t === '') out.push({ id: el.getAttribute('data-testid') || '', text: t });
    }
    return out;
  });
  expect(bare, `the Playbook surface renders a zero or an empty measured figure: ${JSON.stringify(bare)}`).toEqual([]);
}

/* ─────────────────────────────── REQ-FN-089 ─────────────────────────────── */

test('REQ-FN-089 — runs with no computable token window are excluded, counted, and never averaged in as zero', async ({ page }) => {
  test.setTimeout(180_000);
  await gotoScreen(page, '/effort');

  const runs = grab((await textOf(page, 'kpi-runs')) || '', /(\d[\d,]*)/);
  const tile = (await textOf(page, 'kpi-tokens-out')) || '';
  console.log(`kpi-runs=${runs} · kpi-tokens-out="${tile}"`);
  const measured = grab(tile, /measured on (\d[\d,]*) of/);
  const of = grab(tile, /measured on \d[\d,]* of (\d[\d,]*) runs/);
  const excluded = grab(tile, /(\d[\d,]*) runs carry no token window/);
  expect(of, 'the token tile\'s denominator is not the run population').toBe(runs);
  expect(tile, 'the token tile must say the unmeasured runs are excluded and never averaged in as zero')
    .toMatch(/excluded, never averaged in as zero/);
  expect(measured + excluded, `measured ${measured} + excluded ${excluded} must account for every one of ${runs} runs`).toBe(runs);

  // The excluded runs are exactly the two no-window buckets, counted rather than dropped.
  const scope: Record<string, number> = {};
  for (const b of ['tree', 'conversation', 'main', 'none', 'absent']) scope[b] = num(await textOf(page, `scope-${b}`));
  console.log(`token-window buckets: ${JSON.stringify(scope)}`);
  expect(scope.none + scope.absent, 'tokens_scope none + absent must equal the excluded count on the tile').toBe(excluded);
  expect(scope.tree + scope.conversation + scope.main, 'tree + conversation + main must equal the measured count').toBe(measured);

  // Per phase: the Measured column adds back to the tile, and a phase measured on nothing shows no token zero.
  const rows = await phaseRows(page);
  expect(rows.length, 'the command-phase table rendered no rows').toBeGreaterThan(0);
  expect(rows.reduce((a, r) => a + r.measured, 0), 'Σ Measured over phases does not equal the tile\'s measured count').toBe(measured);
  expect(rows.reduce((a, r) => a + r.runs, 0), 'Σ Runs over phases does not equal the run population').toBe(runs);

  // A mean that averaged unmeasured runs in as zero would be total ÷ runs; the page's mean must be total ÷ measured.
  await openAllPhases(page, rows.map(r => r.key));
  let graded = 0;
  for (const r of rows) {
    const tokens = await band(page, r.key, 1);
    const badge = (await textOf(page, `tokens-measured-${r.key}`)) || '';
    expect(badge, `tokens band of ${r.cmd} must carry its own denominator`).toBe(`measured on ${r.measured} of ${r.runs} runs`);
    const gap = r.runs - r.measured;
    if (gap > 0) {
      expect(grab(tokens.text, /(\d[\d,]*) runs are excluded from every figure in this band/),
        `tokens band of ${r.cmd} must count its ${gap} excluded runs`).toBe(gap);
    } else {
      expect(tokens.text, `tokens band of ${r.cmd} must say every run carried a window`).toContain('Every run in this phase carried a token window');
    }
    const mean = (await textOf(page, `tokens-mean-${r.key}`)) || '';
    expect(mean, `${r.cmd}: the per-run figure rendered a bare zero`).not.toMatch(/^0(\.0+)?$/);
    const out = tokens.rows.find(c => c[0] === 'Output');
    const total = out ? abbrev(out[1]) : null;
    if (INSUFFICIENT.test(mean) || !total) {
      console.log(`BRANCH ${r.cmd}: per-run figure "${mean}" (measured ${r.measured}) — no multiply-back check`);
      continue;
    }
    const implied = num(mean) * r.measured;
    console.log(`${r.cmd}: mean ${mean} × measured ${r.measured} = ${Math.round(implied)} vs printed total ${out![1]} (runs ${r.runs})`);
    expect(Math.abs(implied - total.value),
      `${r.cmd}: mean ${mean} × ${r.measured} measured runs = ${Math.round(implied)}, but the printed total is ${out![1]} — ` +
      `the mean was not taken over the measured runs only`).toBeLessThanOrEqual(total.unit + r.measured);
    graded++;
  }
  expect(graded, 'no phase carried a numeric per-run figure to grade').toBeGreaterThan(0);
});

test('REQ-FN-089 — a phase measured on no run shows no token figure as zero, share included', async ({ page }) => {
  test.setTimeout(120_000);
  await gotoScreen(page, '/effort');
  const rows = await phaseRows(page);
  const unmeasured = rows.filter(r => r.measured === 0);
  console.log(`phases measured on 0 runs: ${JSON.stringify(unmeasured.map(r => [r.cmd, r.tokens, r.shareOut]))}`);
  test.skip(unmeasured.length === 0, 'every command phase has at least one run with a token window today — no unmeasured phase to read');
  for (const r of unmeasured) {
    expect(r.tokens, `${r.cmd} is measured on 0 of ${r.runs} runs but its Output tokens reads "${r.tokens}"`).toBe(EM_DASH);
    expect(r.shareOut,
      `${r.cmd} is measured on 0 of ${r.runs} runs but its Share of output reads "${r.shareOut}" — ` +
      `a share computed for a phase with no token window is a zero standing in for "not measured"`).toBe(EM_DASH);
  }
});

/* ─────────────────────────────── REQ-FN-090 ─────────────────────────────── */

test('REQ-FN-090 — only tree-scope runs with a recorded count feed fan-out, and the two exclusions stay two counts', async ({ page }) => {
  test.setTimeout(180_000);
  await gotoScreen(page, '/effort');

  const runs = grab((await textOf(page, 'kpi-runs')) || '', /(\d[\d,]*)/);
  const observed = grab((await textOf(page, 'fanout-observed')) || '', /^(\d[\d,]*)/);
  const notTree = grab((await textOf(page, 'fanout-not-tree')) || '', /^(\d[\d,]*)/);
  const predates = grab((await textOf(page, 'fanout-predates-field')) || '', /^(\d[\d,]*)/);
  const tree = num(await textOf(page, 'scope-tree'));
  const nonTree = ['conversation', 'main', 'none', 'absent'];
  let nonTreeSum = 0;
  for (const b of nonTree) nonTreeSum += num(await textOf(page, `scope-${b}`));
  console.log(`fan-out: observed=${observed} not_tree=${notTree} predates=${predates} · tree=${tree} non-tree=${nonTreeSum} · runs=${runs}`);

  const table = (await textOf(page, 'effort-fanout-table')) || '';
  expect(table, 'the observation predicate must be printed').toContain('tokens_scope == "tree" and subagent_runs != null');
  expect(table, 'unobserved_not_tree must be its own row').toContain('unobserved_not_tree');
  expect(table, 'unobserved_predates_field must be its own row').toContain('unobserved_predates_field');
  expect(observed + notTree + predates, 'observed + the two exclusions must partition the run population').toBe(runs);
  expect(notTree, 'unobserved_not_tree must be exactly the non-tree windows').toBe(nonTreeSum);
  expect(observed + predates, 'every tree-scope run is either observed or predates the field').toBe(tree);
  expect(grab((await textOf(page, 'kpi-fanout-coverage')) || '', /(\d[\d,]*) of/), 'the coverage tile does not quote the observed count').toBe(observed);
  expect((await textOf(page, 'effort-fanout-exclusions')) || '', 'the card must refuse to pool the two exclusions').toMatch(/never pooled|Pooling these two would be the defect/);

  // Per phase: the same partition, the two exclusions still two numbers, and every figure computed over observed runs only.
  const rows = await phaseRows(page);
  await openAllPhases(page, rows.map(r => r.key));
  let sumObs = 0, sumNot = 0, sumPre = 0;
  for (const r of rows) {
    const alert = (await textOf(page, `fanout-alert-${r.key}`)) || '';
    const x = grab(alert, /^(\d[\d,]*) of \d[\d,]* runs observed/);
    const a = grab(alert, /(\d[\d,]*) unobserved_not_tree/);
    const b = grab(alert, /(\d[\d,]*) unobserved_predates_field/);
    expect(grab(alert, /^\d[\d,]* of (\d[\d,]*) runs observed/), `${r.cmd}: the alert's denominator is not the phase's runs`).toBe(r.runs);
    expect(x + a + b, `${r.cmd}: observed ${x} + not_tree ${a} + predates ${b} ≠ ${r.runs} runs`).toBe(r.runs);
    expect(r.fanout, `${r.cmd}: the table's Fan-out cell disagrees with its band`).toBe(x === 0 ? 'not observed' : `${x} observed`);
    if (x > 0) {
      const fan = await band(page, r.key, 3);
      const across = grab(fan.text, /across the (\d[\d,]*) observed runs/);
      const spawned = fan.text.match(/Observed runs that spawned anything (\d[\d,]*) of (\d[\d,]*)/);
      expect(across, `${r.cmd}: the spawn total is not computed over the ${x} observed runs`).toBe(x);
      expect(spawned && num(spawned[2]), `${r.cmd}: runs_with_fanout is not "n of ${x}"`).toBe(x);
      expect(num(spawned![1]), `${r.cmd}: more runs spawned than were observed`).toBeLessThanOrEqual(x);
    }
    sumObs += x; sumNot += a; sumPre += b;
  }
  expect([sumObs, sumNot, sumPre], 'the per-phase counts do not add up to the page-level observed / not_tree / predates')
    .toEqual([observed, notTree, predates]);
});

/* ─────────────────────────────── REQ-FN-091 ─────────────────────────────── */

test('REQ-FN-091 — a run predating the fan-out fields leaves those denominators and is reported as predating the field', async ({ page }) => {
  test.setTimeout(180_000);
  await gotoScreen(page, '/effort');

  const predatesCell = (await textOf(page, 'fanout-predates-field')) || '';
  const predates = grab(predatesCell, /^(\d[\d,]*)/);
  const table = (await textOf(page, 'effort-fanout-table')) || '';
  const card = (await textOf(page, 'effort-fanout-exclusions')) || '';
  console.log(`unobserved_predates_field = "${predatesCell}"`);
  test.skip(!Number.isFinite(predates), 'the page publishes no unobserved_predates_field count to read');

  expect(table, 'the predating row must say the run was written before subagent_runs existed, with the date')
    .toMatch(/written before subagent_runs existed \(2026-08-31\)/);
  for (const field of ['subagent_runs', 'tokens_out_subagents', 'model_tokens_out']) {
    expect(card, `the FIELD_SINCE note must carry ${field}`).toContain(field);
  }
  expect(card, 'the FIELD_SINCE floor must be dated 2026-08-31').toMatch(/FIELD_SINCE floor carries[^.]*2026-08-31/);

  // The predating runs are outside the observed denominator on the KPI row …
  const runs = grab((await textOf(page, 'kpi-runs')) || '', /(\d[\d,]*)/);
  const cov = (await textOf(page, 'kpi-fanout-coverage')) || '';
  const observed = grab((await textOf(page, 'fanout-observed')) || '', /^(\d[\d,]*)/);
  expect(grab(cov, /(\d[\d,]*) of/), 'the coverage numerator includes runs that could not have been observed').toBe(observed);
  expect(observed + predates, 'observed + predating exceeds the run population').toBeLessThanOrEqual(runs);

  // … and in every phase: a phase with predating runs names them and computes its figures over observed runs only.
  const rows = await phaseRows(page);
  await openAllPhases(page, rows.map(r => r.key));
  let withPredating = 0;
  for (const r of rows) {
    const alert = (await textOf(page, `fanout-alert-${r.key}`)) || '';
    const x = grab(alert, /^(\d[\d,]*) of/);
    const b = grab(alert, /(\d[\d,]*) unobserved_predates_field/);
    if (b > 0) {
      withPredating++;
      expect(alert, `${r.cmd}: its ${b} predating runs must be reported as not contributing a zero`)
        .toMatch(/none of them contributes a zero|not a measurement of zero/);
      if (x > 0) expect(alert, `${r.cmd}: the figures must be over the ${x} observed runs only`).toContain(`computed over those ${x} runs only`);
    }
    // model_tokens_out predates 2026-08-31 too: runs without the split are counted apart, not blended.
    const models = await band(page, r.key, 2);
    const label = grab(models.text, /and (\d[\d,]*) from a dominant model label/);
    if (label > 0) expect(models.text, `${r.cmd}: label-only contributions must be counted apart`).toContain('counted apart');
  }
  console.log(`phases carrying predating runs: ${withPredating}`);
  expect(withPredating > 0 || predates === 0, 'the page reports predating runs but no phase names any').toBe(true);
});

/* ─────────────────────────────── REQ-FN-092 ─────────────────────────────── */

test('REQ-FN-092 — per-model effort comes from the per-model split, never the dominant label, under observational copy', async ({ page }) => {
  test.setTimeout(180_000);
  await gotoScreen(page, '/effort');
  const rows = await phaseRows(page);
  await openAllPhases(page, rows.map(r => r.key));

  let graded = 0, exceeding = 0;
  for (const r of rows) {
    if (await exists(page, `models-none-${r.key}`)) {
      const none = (await textOf(page, `models-none-${r.key}`)) || '';
      expect(none, `${r.cmd}: the empty model band must call it not observed`).toMatch(/Not observed, rather than a phase that ran on no model/);
      continue;
    }
    const m = await band(page, r.key, 2);
    const tokens = await band(page, r.key, 1);
    expect(m.text, `${r.cmd}: the model band must name the split it reads`).toContain('Computed from model_tokens_out');
    expect(m.text, `${r.cmd}: the model band must refuse the dominant-label attribution`).toContain("never attributed to its dominant model");
    expect(m.text, `${r.cmd}: the model band must carry the observational copy`).toContain('observational, not causal');

    const all = m.text.match(/All (\d[\d,]*) contributions here come from a measured split/);
    const mixed = m.text.match(/(\d[\d,]*) contributions come from a measured split and (\d[\d,]*) from a dominant model label/);
    expect(!!all || !!mixed, `${r.cmd}: the band does not state where its contributions came from: "${m.text.slice(0, 300)}"`).toBe(true);
    const split = all ? num(all[1]) : num(mixed![1]);
    const label = all ? 0 : num(mixed![2]);
    const modelRuns = m.rows.reduce((a, c) => a + num(c[1]), 0);
    expect(modelRuns, `${r.cmd}: Σ model runs ${modelRuns} ≠ ${split} split + ${label} label contributions`).toBe(split + label);
    if (modelRuns > r.measured) exceeding++;

    // Each model contributes its own tokens: the per-model figures add back to the phase's output, nothing filed twice.
    const out = tokens.rows.find(c => c[0] === 'Output');
    const total = out ? abbrev(out[1]) : null;
    const parts = m.rows.map(c => abbrev(c[2]));
    if (total && parts.every(p => p)) {
      const sum = parts.reduce((a, p) => a + p!.value, 0);
      const tol = total.unit + parts.reduce((a, p) => a + p!.unit, 0);
      console.log(`${r.cmd}: Σ per-model output ${Math.round(sum)} vs phase output ${out![1]} (±${Math.round(tol)}) · split ${split} / label ${label}`);
      expect(Math.abs(sum - total.value), `${r.cmd}: per-model output does not add back to the phase output ${out![1]}`).toBeLessThanOrEqual(tol);
    }
    graded++;
  }
  console.log(`BRANCH REQ-FN-092: ${graded} phases graded · ${exceeding} show more model contributions than measured runs (a mixed-model window split)`);
  expect(graded, 'no phase rendered a per-model band').toBeGreaterThan(0);
});

/* ─────────────────────────────── REQ-FN-096 ─────────────────────────────── */

test('REQ-FN-096 — the Playbook axis names its state and publishes no aggregate a quarantined row could enter', async ({ page }) => {
  test.setTimeout(150_000);
  try {
    const s = await openPlaybook(page);
    await assertPlaybookStateNamedAndNothingZeroed(page, s);
    const states = (await textOf(page, 'pb-effort-states-table')) || '';
    expect(states, 'the NoEligibleRows state must promise the count and reason, and no aggregate from zero rows')
      .toMatch(/quarantined — the count and the reason are shown, and no aggregate is printed from zero rows/);
    if (!s.populated) {
      for (const id of ['pb-effort-tile-quarantined', 'pb-effort-tile-completed', 'pb-effort-tile-tokens']) {
        expect(await exists(page, id), `${id} is rendered while the surface has no executions`).toBe(false);
      }
    }
  } finally { await backToTechieFlow(page); }
});

test('REQ-FN-096 — populated: a quarantined schema-2 row is shown with its reason and enters no numeric aggregate', async ({ page }) => {
  test.setTimeout(150_000);
  try {
    const s = await openPlaybook(page);
    test.skip(!s.populated, `no schema-2 phase execution exists to quarantine — the Playbook axis is in state "${s.active}"`);
    const tile = (await textOf(page, 'pb-effort-tile-quarantined')) || '';
    const q = grab(tile, /(\d[\d,]*)/);
    expect(tile, 'the Quarantined tile must carry its own cohort').toMatch(/\d[\d,]* of \d[\d,]*/);
    test.skip(q === 0, 'the populated surface holds no quarantined row today');
    const rowsText = (await textOf(page, 'pb-executions-table')) || '';
    expect(rowsText.toLowerCase(), 'a quarantined execution is not visible in the execution table').toContain('quarantin');
  } finally { await backToTechieFlow(page); }
});

/* ─────────────────────────────── REQ-FN-097 ─────────────────────────────── */

test('REQ-FN-097 — wall clock, observed active time and human effort stay distinct, and no diagnostic sum is added', async ({ page }) => {
  test.setTimeout(180_000);
  await gotoScreen(page, '/effort');
  const wall = (await textOf(page, 'kpi-wallclock')) || '';
  expect(wall, 'the wall-clock tile must deny being human effort').toContain('never human effort');
  const rows = await phaseRows(page);
  await openAllPhases(page, rows.map(r => r.key));
  for (const r of rows) {
    const t = await band(page, r.key, 0);
    expect(t.text, `${r.cmd}: the time band must say wall clock is not active time and never human effort`)
      .toMatch(/It is not active time and it is never human effort/);
  }
  const body = (await page.locator('[data-testid="effort-page"]').first().innerText()).replace(/\s+/g, ' ');
  const claims = body.match(/[^.]*\bhuman effort\b[^.]*/gi) || [];
  for (const c of claims) {
    expect(/\b(never|not|no)\b/i.test(c), `"human effort" appears as something the page measures: "${c.trim()}"`).toBe(true);
  }
  expect(/assistant_elapsed_ms\s*\+\s*tool_elapsed_ms/.test(body), 'the page adds the two diagnostic sums').toBe(false);

  try {
    const s = await openPlaybook(page);
    await assertPlaybookStateNamedAndNothingZeroed(page, s);
    // A timing figure may only appear with the three-concepts note beside it.
    const timingFigures = (await exists(page, 'pb-effort-tile-wallclock')) || (await exists(page, 'pb-effort-tile-active'));
    const note = await exists(page, 'pb-effort-timing-note');
    console.log(`Playbook: timing figures=${timingFigures} · timing note=${note}`);
    expect(!timingFigures || note, 'a Playbook timing figure is rendered without the three-timing-concepts note').toBe(true);
    if (note) {
      const n = ((await textOf(page, 'pb-effort-timing-note')) || '').toLowerCase();
      expect(n).toContain('never added');
      expect(n).toContain('human effort is never captured');
    }
  } finally { await backToTechieFlow(page); }
});

test('REQ-FN-097 — populated: observed active time is never labelled human effort and the two diagnostic sums are never added', async ({ page }) => {
  test.setTimeout(150_000);
  try {
    const s = await openPlaybook(page);
    test.skip(!s.populated, `no schema-2 execution carries active or elapsed time — the Playbook axis is in state "${s.active}"`);
    const note = ((await textOf(page, 'pb-effort-timing-note')) || '').toLowerCase();
    expect(note, 'the timing note must say the diagnostic sums are never added').toContain('never added');
    expect(note, 'the timing note must deny active time is human effort').toContain('not human effort');
    const active = ((await textOf(page, 'pb-effort-tile-active')) || '').toLowerCase();
    expect(active, 'the active-time tile calls itself human effort').not.toMatch(/(?<!not |never )human effort/);
    const wall = (await textOf(page, 'pb-effort-tile-wallclock')) || '';
    expect(wall.length, 'the wall-clock tile is blank').toBeGreaterThan(0);
  } finally { await backToTechieFlow(page); }
});

/* ─────────────────────────────── REQ-FN-098 ─────────────────────────────── */

test('REQ-FN-098 — the phase dimension is labelled Command phase and no window is split between conceptual phases', async ({ page }) => {
  test.setTimeout(150_000);
  await gotoScreen(page, '/effort');
  const heads = await page.evaluate(() =>
    Array.from(document.querySelector('[data-testid="effort-phase-table"]')?.querySelectorAll('thead th') || [])
      .map(h => (h.textContent || '').trim()));
  expect(heads[0], 'the phase table\'s dimension column is not labelled "Command phase"').toBe('Command phase');
  const card = (await textOf(page, 'effort-phases')) || '';
  expect(card, 'the page must state a window is never split between conceptual phases')
    .toContain('A window is never split between conceptual phases');
  expect(card, 'the page must say a whole-task total needs an explicit cohort').toMatch(/whole-task total is unavailable without an explicit cohort/);

  // Each run sits in exactly one command phase: the rows partition the population, shares close to 100%.
  const runs = grab((await textOf(page, 'kpi-runs')) || '', /(\d[\d,]*)/);
  const rows = await phaseRows(page);
  expect(rows.reduce((a, r) => a + r.runs, 0), 'Σ phase runs ≠ runs recorded — a run was split or dropped').toBe(runs);
  const share = rows.reduce((a, r) => a + num((r.shareTime.match(/(\d+)%/) || ['', 'NaN'])[1]), 0);
  console.log(`Σ share of time = ${share}% over ${rows.length} phases`);
  expect(Math.abs(share - 100), `the per-phase shares of time sum to ${share}%`).toBeLessThanOrEqual(rows.length);
  const kpiOut = abbrev(((await textOf(page, 'kpi-tokens-out')) || '').match(/Output tokens ([\d.,]+[kMB]?)/)?.[1] || '');
  const parts = rows.map(r => abbrev(r.tokens)).filter(Boolean) as { value: number; unit: number }[];
  if (kpiOut) {
    const sum = parts.reduce((a, p) => a + p.value, 0);
    expect(Math.abs(sum - kpiOut.value), `Σ phase output ${Math.round(sum)} ≠ the page's output total`)
      .toBeLessThanOrEqual(kpiOut.unit + parts.reduce((a, p) => a + p.unit, 0));
  }

  await page.locator('[data-testid="effort-routing-byphase-trigger"]').first().click();
  await page.waitForTimeout(1_000);
  const byPhase = await page.evaluate(() =>
    (document.querySelector('[data-testid="effort-routing-byphase"] thead th')?.textContent || '').trim());
  expect(byPhase, 'the by-phase routing table does not label its dimension "Command phase"').toBe('Command phase');

  try {
    const s = await openPlaybook(page);
    await assertPlaybookStateNamedAndNothingZeroed(page, s);
    expect(s.surface, 'the Playbook filters must offer the dimension as command phases').toContain('All command phases');
  } finally { await backToTechieFlow(page); }
});

test('REQ-FN-098 — populated: the schema-2 phase table is labelled Command phase', async ({ page }) => {
  test.setTimeout(150_000);
  try {
    const s = await openPlaybook(page);
    test.skip(!s.populated, `no schema-2 phase execution exists to group — the Playbook axis is in state "${s.active}"`);
    const head = await page.evaluate(() =>
      (document.querySelector('[data-testid="pb-effort-command-phases"] thead th')?.textContent || '').trim());
    expect(head, 'the schema-2 phase table is not labelled "Command phase"').toBe('Command phase');
  } finally { await backToTechieFlow(page); }
});

/* ─────────────────────────────── REQ-FN-099 ─────────────────────────────── */

test('REQ-FN-099 — the Playbook axis names its state, offers the model filter and publishes no model aggregate without executions', async ({ page }) => {
  test.setTimeout(150_000);
  try {
    const s = await openPlaybook(page);
    await assertPlaybookStateNamedAndNothingZeroed(page, s);
    expect(await exists(page, 'pb-effort-filter-model'), 'the model filter is missing').toBe(true);
    if (!s.populated) {
      for (const id of ['pb-effort-model-mix', 'pb-model-tokens', 'pb-chart-tokens']) {
        expect(await exists(page, id), `${id} is rendered while the surface has no executions`).toBe(false);
      }
    }
  } finally { await backToTechieFlow(page); }
});

test('REQ-FN-099 — populated: each model of a mixed-model execution contributes its own tokens and the model filter matches any member', async ({ page }) => {
  test.setTimeout(240_000);
  try {
    const s = await openPlaybook(page);
    test.skip(!s.populated, `no schema-2 execution exists to carry a models[] list — the Playbook axis is in state "${s.active}"`);
    const models = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid^="pb-model-tokens-"]'))
        .map(e => (e.getAttribute('data-testid') || '').replace(/^pb-model-tokens-/, '')));
    test.skip(models.length < 2, 'no execution today ran on more than one model');
    // A model that contributed tokens must match at least one execution when filtered on — even the quieter one.
    for (const model of models) {
      await page.locator('[data-testid="pb-effort-filter-model"]').first().click();
      await page.waitForTimeout(500);
      await page.locator('[role="option"]').filter({ hasText: model }).first().click();
      await page.waitForTimeout(1_500);
      const count = (await textOf(page, 'pb-executions-count')) || '';
      expect(grab(count, /^(\d[\d,]*) of/), `filtering on ${model} matched no execution: "${count}"`).toBeGreaterThan(0);
    }
  } finally { await backToTechieFlow(page); }
});

/* ─────────────────────────────── REQ-FN-100 ─────────────────────────────── */

test('REQ-FN-100 — fan-out is never inferred: the Playbook axis names its state and the TechieFlow bands count observed runs once', async ({ page }) => {
  test.setTimeout(180_000);
  await gotoScreen(page, '/effort');
  const rows = await phaseRows(page);
  await openAllPhases(page, rows.map(r => r.key));
  for (const r of rows) {
    const fan = await band(page, r.key, 3);
    expect(fan.text.toLowerCase(), `${r.cmd}: the fan-out band infers a failure`).not.toMatch(/\bfail(ed|ure)?\b/);
    const spawned = fan.text.match(/Observed runs that spawned anything (\d[\d,]*) of (\d[\d,]*)/);
    if (spawned) expect(num(spawned[1]), `${r.cmd}: more runs spawned than were observed`).toBeLessThanOrEqual(num(spawned[2]));
  }
  try {
    const s = await openPlaybook(page);
    await assertPlaybookStateNamedAndNothingZeroed(page, s);
    if (!s.populated) {
      expect(await exists(page, 'pb-effort-tile-fanout'), 'a sub-agent figure is rendered with no executions').toBe(false);
      expect(/\b0 \/ 0\b/.test(s.surface), 'the Playbook surface renders "0 / 0" contributors/spawned').toBe(false);
    }
  } finally { await backToTechieFlow(page); }
});

test('REQ-FN-100 — populated: fan-out reads contributors over spawned and a recursive child is counted exactly once', async ({ page }) => {
  test.setTimeout(150_000);
  try {
    const s = await openPlaybook(page);
    test.skip(!s.populated, `no schema-2 execution carries a sub-agent tree — the Playbook axis is in state "${s.active}"`);
    const tile = (await textOf(page, 'pb-effort-tile-fanout')) || '';
    const m = tile.match(/(\d[\d,]*) \/ (\d[\d,]*)/);
    expect(m, `the fan-out tile does not read "contributors / spawned": "${tile}"`).not.toBeNull();
    expect(num(m![1]), 'contributors exceed spawned').toBeLessThanOrEqual(num(m![2]));
    expect(tile.toLowerCase(), 'the fan-out tile infers a failure').not.toMatch(/\bfailure\b/);
  } finally { await backToTechieFlow(page); }
});

/* ─────────────────────────────── REQ-FN-101 ─────────────────────────────── */

test('REQ-FN-101 — an unverified or absent provider cost is never shown as free or as a measured zero', async ({ page }) => {
  test.setTimeout(150_000);
  await gotoScreen(page, '/effort');
  const cells = await page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid^="cost-"]'))
      .map(e => ({ id: e.getAttribute('data-testid') || '', text: (e.textContent || '').trim() })));
  console.log(`measured spend cells: ${JSON.stringify(cells)}`);
  expect(cells.length, 'the measured-spend card rendered no cells').toBeGreaterThan(0);
  for (const c of cells) {
    expect(c.text === EM_DASH || /^\$\d/.test(c.text), `${c.id} reads "${c.text}"`).toBe(true);
    expect(c.text, `${c.id} shows a measured zero`).not.toMatch(/^\$0(\.0+)?$/);
    expect(c.text.toLowerCase(), `${c.id} shows "free"`).not.toContain('free');
  }
  const card = (await textOf(page, 'effort-cost')) || '';
  expect(card, 'the card must say a dash is never $0.00 and never free').toMatch(/never \$0\.00 and never “free”/);

  try {
    const s = await openPlaybook(page);
    await assertPlaybookStateNamedAndNothingZeroed(page, s);
    if (!s.populated) {
      expect(await exists(page, 'pb-cost-measured'), 'a measured-cost tile is rendered with no executions').toBe(false);
      expect(/\$0(\.00)?\b/.test(s.surface), 'the Playbook surface prints a zero-dollar figure').toBe(false);
    }
  } finally { await backToTechieFlow(page); }
});

test('REQ-FN-101 — populated: a zero-unverified cost is excluded with its status', async ({ page }) => {
  test.setTimeout(150_000);
  try {
    const s = await openPlaybook(page);
    test.skip(!s.populated, `no schema-2 execution carries a provider cost status — the Playbook axis is in state "${s.active}"`);
    const measured = (await textOf(page, 'pb-cost-measured-value')) || '';
    expect(measured, 'the measured-cost tile shows a zero').not.toMatch(/^\$0(\.0+)?$/);
    expect(measured.toLowerCase(), 'the measured-cost tile shows "free"').not.toContain('free');
    const costs = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid^="pb-execution-cost-"]')).map(e => (e.textContent || '').trim()));
    for (const c of costs) {
      expect(c, `an execution cost reads "${c}"`).not.toMatch(/^\$0(\.0+)?$|^free$/i);
    }
    test.skip(!costs.some(c => /zero-unverified/.test(c)), 'no execution carries a zero-unverified cost today');
  } finally { await backToTechieFlow(page); }
});

/* ─────────────────────────────── REQ-FN-102 ─────────────────────────────── */

test('REQ-FN-102 — each cohort figure carries its n and exclusions beside it, and a cohort under three renders insufficient data', async ({ page }) => {
  test.setTimeout(180_000);
  await gotoScreen(page, '/effort');
  const rows = await phaseRows(page);
  await openAllPhases(page, rows.map(r => r.key));
  let below = 0;
  for (const r of rows) {
    expect((await textOf(page, `tokens-measured-${r.key}`)) || '', `${r.cmd}: the tokens band has no n beside it`)
      .toBe(`measured on ${r.measured} of ${r.runs} runs`);
    expect((await textOf(page, `fanout-alert-${r.key}`)) || '', `${r.cmd}: the fan-out band does not lead with its n`)
      .toMatch(/^\d[\d,]* of \d[\d,]* runs observed/);
    const mean = (await textOf(page, `tokens-mean-${r.key}`)) || '';
    if (r.measured < 3) {
      below++;
      expect(mean, `${r.cmd}: a per-run figure over ${r.measured} runs must render insufficient data`)
        .toBe(`insufficient data (n=${r.measured})`);
    } else {
      expect(mean, `${r.cmd}: a per-run figure over ${r.measured} runs renders "${mean}"`).toMatch(/^[\d,]+(\.\d+)?$/);
    }
  }
  console.log(`BRANCH REQ-FN-102: ${below} phases sit below MIN_N=3 on the TechieFlow axis`);

  try {
    const s = await openPlaybook(page);
    await assertPlaybookStateNamedAndNothingZeroed(page, s);
    expect(s.surface, 'the Playbook axis must state its cohort floor').toMatch(/Any comparative cohort below 3 records renders insufficient data \(n=…\)/);
    expect((await textOf(page, 'pb-effort-filter-scope')) || '', 'the filter note must say every figure keeps its own exclusion counts')
      .toContain('with their own exclusion counts');
  } finally { await backToTechieFlow(page); }
});

test('REQ-FN-102 — populated: every schema-2 cohort figure shows n of N eligible and a cohort under three renders insufficient data', async ({ page }) => {
  test.setTimeout(150_000);
  try {
    const s = await openPlaybook(page);
    test.skip(!s.populated, `no schema-2 execution exists to form a cohort — the Playbook axis is in state "${s.active}"`);
    for (const id of ['pb-effort-tile-completed', 'pb-effort-tile-wallclock', 'pb-effort-tile-active', 'pb-effort-tile-fanout',
      'pb-effort-tile-tokens', 'pb-cost-measured', 'pb-effort-tile-incomplete', 'pb-effort-tile-quarantined']) {
      expect((await textOf(page, id)) || '', `${id} does not carry its own cohort`).toMatch(/\d[\d,]* of \d[\d,]*/);
    }
    const figs = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid^="pb-active-active-"], [data-testid^="pb-phase-elapsed-"]'))
        .map(e => (e.textContent || '').trim()));
    for (const f of figs) expect(f, 'a comparative schema-2 figure rendered a bare zero').not.toBe('0');
  } finally { await backToTechieFlow(page); }
});

/* ─────────────────────────────── REQ-FN-112 ─────────────────────────────── */

type Phases = {
  runs_live: number; duration_measured_n: number; duration_impossible_n: number;
  duration_absent_n: number; duration_recomputed_n: number;
  phases: Record<string, { runs: number; duration_s: { n: number; derived_n: number } }>;
};

/** Presses export-now on /export (TechieFlow axis) and downloads the snapshot it wrote. */
async function freshSnapshot(page: Page): Promise<Phases> {
  await gotoScreen(page, '/export');
  const started = Date.now();
  await (await testid(page, 'export-now')).click();
  await page.waitForTimeout(6_000);
  const hrefs = await page.evaluate(() =>
    Array.from(document.querySelectorAll('a[href*="/api/export/download"]')).map(a => a.getAttribute('href') || ''));
  const md = hrefs.find(h => h.includes('framework=techieflow') && h.includes('file=snapshot.md'));
  const js = hrefs.find(h => h.includes('framework=techieflow') && h.includes('file=tflens.json'));
  expect(md && js, `the Past snapshots table links no TechieFlow snapshot: ${JSON.stringify(hrefs.slice(0, 4))}`).toBeTruthy();
  const mdRes = await page.request.get(md!);
  expect(mdRes.status(), 'snapshot.md download failed').toBe(200);
  const generated = ((await mdRes.text()).match(/\*\*Generated:\*\* (\S+)/) || [])[1] || '';
  console.log(`snapshot ${js} generated ${generated}`);
  expect(Date.parse(generated), 'the downloaded snapshot was not written by this export-now press')
    .toBeGreaterThanOrEqual(started - 120_000);
  const res = await page.request.get(js!);
  expect(res.status(), 'tflens.json download failed').toBe(200);
  return (await res.json()).phases as Phases;
}

test('REQ-FN-112 — the timestamps win: recomputed and impossible durations are counted beside the time figure and match the export', async ({ page }) => {
  test.setTimeout(240_000);
  await gotoScreen(page, '/effort');

  const wall = (await textOf(page, 'kpi-wallclock')) || '';
  const derivedNote = (await textOf(page, 'kpi-wallclock-derived')) || '';
  const runs = grab((await textOf(page, 'kpi-runs')) || '', /(\d[\d,]*)/);
  console.log(`kpi-wallclock = "${wall}" · kpi-wallclock-derived = "${derivedNote}" · kpi-runs = ${runs}`);

  // The count sits beside the time figure — inside the same tile, as visible text.
  const inside = await page.evaluate(() => {
    const tile = document.querySelector('[data-testid="kpi-wallclock"]');
    return !!tile?.querySelector('[data-testid="kpi-wallclock-derived"]');
  });
  expect(inside, 'kpi-wallclock-derived does not sit inside the wall-clock tile').toBe(true);
  expect(derivedNote, 'the derived note carries no count').toMatch(/\d|every duration read from the record/);
  const timed = grab(wall, /over (\d[\d,]*) timed runs/);
  const recomputedOnPage = Number.isFinite(grab(wall, /(\d[\d,]*) (?:recomputed|overridden)/))
    ? grab(wall, /(\d[\d,]*) (?:recomputed|overridden)/)
    : grab(derivedNote, /(\d[\d,]*)/);

  // Per phase, as the page prints it: a phase can never have more derived durations than timed ones.
  const rows = await phaseRows(page);
  await openAllPhases(page, rows.map(r => r.key));
  const pagePhase: Record<string, { timed: number; derived: number }> = {};
  for (const r of rows) {
    const t = (await band(page, r.key, 0)).text;
    const all = grab(t, /All (\d[\d,]*) runs were timed/);
    const some = grab(t, /Timed on (\d[\d,]*) of \d[\d,]* runs/);
    const ptimed = Number.isFinite(all) ? all : some;
    const pderived = Number.isFinite(grab(t, /(\d[\d,]*) of those durations were derived/))
      ? grab(t, /(\d[\d,]*) of those durations were derived/) : 0;
    pagePhase[r.cmd] = { timed: ptimed, derived: pderived };
    expect.soft(pderived, `${r.cmd}: the time band says ${pderived} of ${ptimed} timed durations were derived`)
      .toBeLessThanOrEqual(ptimed);
  }
  console.log(`page per-phase timed/derived: ${JSON.stringify(pagePhase)}`);

  // The export written now, from the same account, on the same axis.
  const p = await freshSnapshot(page);
  console.log(`export phases: runs_live=${p.runs_live} measured=${p.duration_measured_n} impossible=${p.duration_impossible_n} ` +
    `absent=${p.duration_absent_n} recomputed=${p.duration_recomputed_n}`);
  for (const k of ['duration_measured_n', 'duration_impossible_n', 'duration_absent_n', 'duration_recomputed_n'] as const) {
    expect(Number.isInteger(p[k]), `the export publishes no ${k}`).toBe(true);
  }
  expect(p.duration_measured_n + p.duration_impossible_n + p.duration_absent_n,
    'measured + impossible + absent must partition the live runs in the export').toBe(p.runs_live);
  expect(p.duration_recomputed_n, 'recomputed must be a subset of measured').toBeLessThanOrEqual(p.duration_measured_n);

  // Consistency between what the reader sees and what they would quote.
  expect.soft(runs, `the page reads ${runs} runs; the export's runs_live is ${p.runs_live}`).toBe(p.runs_live);
  expect.soft(timed, `the page sums duration over ${timed} timed runs; the export's duration_measured_n is ${p.duration_measured_n}`)
    .toBe(p.duration_measured_n);
  expect.soft(runs - timed, `the page leaves ${runs - timed} runs untimed; the export's impossible + absent is ` +
    `${p.duration_impossible_n + p.duration_absent_n}`).toBe(p.duration_impossible_n + p.duration_absent_n);
  expect.soft(recomputedOnPage, `the page states ${recomputedOnPage} recomputed/derived durations beside the time figure ` +
    `("${derivedNote}"); the export's duration_recomputed_n is ${p.duration_recomputed_n}`).toBe(p.duration_recomputed_n);
  for (const [cmd, v] of Object.entries(pagePhase)) {
    const e = p.phases[cmd];
    if (!e) { expect.soft(e, `the export has no phase "${cmd}"`).toBeTruthy(); continue; }
    expect.soft(v.timed, `${cmd}: the page times ${v.timed} runs; the export's duration_s.n is ${e.duration_s.n}`).toBe(e.duration_s.n);
  }
});
