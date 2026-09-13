// Verify run — `/misses` group: REQ-FN-077, REQ-FN-078, REQ-FN-079, REQ-FN-081, REQ-FN-104, REQ-FN-105.
//
// Black-box only. Every assertion is about what a signed-in user (USER1, the documented demo user) can
// see in the browser. Nothing here reads the database or imports application code, and nothing here
// writes USER1's data: the only state touched is the header Framework switch, which every test that
// moves it puts back on TechieFlow before it ends.
//
// Where an acceptance needs a state today's data does not hold (a populated Playbook miss axis), the
// test skips at runtime and says so, after first proving the page reached the state it did find.
import { test, expect, Page } from '@playwright/test';
import { signIn, gotoScreen, testid, renderCheck, tableCheck, visualCheck, collectErrors, DESKTOP, MOBILE } from './_helpers';

// ─────────────────────────────────────────────────────────────────────────────
// Shared page drivers
// ─────────────────────────────────────────────────────────────────────────────

const SHELL_DESKTOP = ['app-sidebar', 'sidebar-trigger', 'framework-switch', 'sync-now', 'theme-toggle', 'user-menu'];
// At 390px the shell's sidebar is `hidden md:flex` and leaves the DOM; the trigger is how it is reached.
const SHELL_MOBILE = ['sidebar-trigger', 'framework-switch', 'sync-now', 'theme-toggle', 'user-menu'];

/** A key that would name an "unknown" bucket rather than a real model / origin. */
const UNKNOWN_BUCKET = /^(unknown|not named|not recorded|null|none|n\/a|other|unattributed|—|-|\?|inferred)$/i;

const norm = (s: string | null | undefined) => (s || '').replace(/\s+/g, ' ').trim();
const num = (s: string) => Number(s.replace(/,/g, ''));

/** Put the header Framework switch on one axis and wait for the page to re-query. */
async function selectFramework(page: Page, label: 'TechieFlow' | 'Playbook') {
  const trigger = page.locator('[data-testid="framework-switch"] [role="tab"]', { hasText: label }).first();
  await trigger.click();
  await page.waitForTimeout(2000);
}

async function text(page: Page, id: string): Promise<string> {
  return norm(await (await testid(page, id)).innerText());
}

/** The project-type tabs, as the reader sees them: key and the count on the tab's badge. */
async function segments(page: Page): Promise<{ key: string; label: string; count: number }[]> {
  return page.evaluate(() =>
    Array.from(document.querySelectorAll('[data-testid^="miss-type-"]')).map(e => {
      const id = e.getAttribute('data-testid') || '';
      const label = ((e as HTMLElement).innerText || '').replace(/\s+/g, ' ').trim();
      const nums = label.match(/\d[\d,]*/g) || [];
      return { key: id.slice('miss-type-'.length), label, count: Number((nums[nums.length - 1] || 'NaN').replace(/,/g, '')) };
    }));
}

async function selectSegment(page: Page, key: string) {
  await page.locator(`[data-testid="miss-type-${key}"]`).first().click();
  await page.waitForTimeout(1200);
}

async function selectPeriod(page: Page, label: string) {
  await page.locator('[data-testid="misses-period"]').first().click();
  await page.locator('[role="option"]', { hasText: label }).first().click();
  await page.waitForTimeout(1500);
}

type DetailRow = { missId: string; origin: string; confidence: string };

/** Every row of the per-miss detail table for the selected segment, walking its pages. */
async function allDetailRows(page: Page): Promise<DetailRow[]> {
  const out: DetailRow[] = [];
  const table = '[data-testid="miss-detail-table"]';
  const next = page.locator('[data-testid="miss-detail"] button', { hasText: /^\s*Next\s*$/ }).first();
  const first = page.locator('[data-testid="miss-detail"] button', { hasText: 'Go to first page' }).first();
  if ((await first.count()) > 0 && await first.isEnabled().catch(() => false)) {
    await first.click();
    await page.waitForTimeout(700);
  }
  for (let guard = 0; guard < 60; guard++) {
    const rows: DetailRow[] = await page.evaluate((sel) => {
      const root = document.querySelector(sel);
      if (!root) return [];
      const heads = Array.from(root.querySelectorAll('thead th')).map(h => (h.textContent || '').replace(/\s+/g, ' ').trim());
      const originIdx = heads.findIndex(h => h === 'Origin');
      return Array.from(root.querySelectorAll('tbody tr')).map(tr => {
        const tds = Array.from(tr.querySelectorAll('td'));
        const btn = tr.querySelector('[data-testid^="miss-raw-"]');
        const cell = originIdx >= 0 ? tds[originIdx] : undefined;
        const origin = ((cell?.querySelector('.font-mono')?.textContent) || '').replace(/\s+/g, ' ').trim();
        const all = ((cell as HTMLElement | undefined)?.innerText || '').replace(/\s+/g, ' ').trim();
        const confidence = all.startsWith(origin) ? all.slice(origin.length).trim() : all;
        // A record can carry an empty miss_id; it is still a row the reader sees, so it is counted. A
        // single-cell row is the grid's own "no results" placeholder, not a record.
        return { missId: (btn?.getAttribute('data-testid') || '').slice('miss-raw-'.length), origin, confidence, cells: tds.length };
      }).filter(r => r.cells > 1).map(({ cells, ...r }) => r);
    }, table);
    out.push(...rows);
    if ((await next.count()) === 0 || !(await next.isEnabled().catch(() => false))) break;
    const pageLabel = async () => ((await page.locator('[data-testid="miss-detail"]').innerText()).match(/Showing \d+\s*[-–]\s*\d+ of \d+/) || [''])[0];
    const before = await pageLabel();
    await next.click();
    await expect.poll(pageLabel, { timeout: 8000 }).not.toBe(before);
    await page.waitForTimeout(300);
  }
  if ((await first.count()) > 0 && await first.isEnabled().catch(() => false)) {
    await first.click();
    await page.waitForTimeout(700);
  }
  return out;
}

/** Tally a list of keys. */
function tally(keys: string[]): Record<string, number> {
  const m: Record<string, number> = {};
  for (const k of keys) m[k] = (m[k] || 0) + 1;
  return m;
}

// ─────────────────────────────────────────────────────────────────────────────
// REQ-FN-077 — miss figures are live-only and segmented by project type, never pooled.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-FN-077 — miss figures are live-only and segmented by project type, never pooled across them', async ({ page }) => {
  test.setTimeout(300_000);
  await signIn(page);

  // The live miss total and the backfilled count TfLens reports for this framework, from Coverage —
  // the page that states the provenance separation rather than applying it silently.
  await gotoScreen(page, '/');
  const liveTotalText = await text(page, 'miss-quality-total');
  const backfilledText = await text(page, 'miss-backfilled');
  console.log(`Coverage: miss-quality-total = "${liveTotalText}" | miss-backfilled = "${backfilledText.slice(0, 120)}"`);
  const liveTotal = num((liveTotalText.match(/([\d,]+)\s+misses/) || [])[1] || 'NaN');
  const backfilled = num((backfilledText.match(/([\d,]+)\s+misses/) || [])[1] || 'NaN');
  expect(Number.isFinite(liveTotal), `Coverage states no live miss total: "${liveTotalText}"`).toBe(true);
  expect(Number.isFinite(backfilled), `Coverage states no backfilled miss count: "${backfilledText}"`).toBe(true);

  await gotoScreen(page, '/misses');
  const segs = await segments(page);
  console.log(`project-type segments: ${JSON.stringify(segs)}`);
  expect(segs.length, 'no project-type segment tab rendered on /misses').toBeGreaterThan(0);

  // Never pooled: there is no "all types" / total / pooled segment to choose.
  for (const s of segs) {
    expect(s.label, `a segment tab pools project types: "${s.label}"`)
      .not.toMatch(/\b(all|every|total|pooled|combined|overall|any)\b/i);
    expect(Number.isFinite(s.count), `segment ${s.key} shows no count: "${s.label}"`).toBe(true);
  }
  const tabTexts = await page.$$eval('[data-testid="miss-type"] [role="tab"]', els => els.map(e => ((e as HTMLElement).innerText || '').replace(/\s+/g, ' ').trim()));
  expect(tabTexts.length, `the segment strip carries a tab that is not a project type: ${JSON.stringify(tabTexts)}`).toBe(segs.length);

  // Live-only: the segments add up to Coverage's LIVE total — never to live + backfilled.
  const sum = segs.reduce((a, s) => a + s.count, 0);
  console.log(`sum of segment counts = ${sum}; Coverage live total = ${liveTotal}; backfilled held out = ${backfilled}`);
  expect(sum, `the project-type segments add up to ${sum}, but Coverage's live miss total is ${liveTotal}`).toBe(liveTotal);
  if (backfilled > 0) {
    expect(sum, 'backfilled misses reached the /misses segments').not.toBe(liveTotal + backfilled);
  } else {
    test.info().annotations.push({ type: 'note', description: 'Coverage reports 0 backfilled misses today, so the backfilled exclusion is not exercised by the data; the live-total equality still holds.' });
  }

  const nonEmpty = segs.filter(s => s.count > 0).length;

  for (const s of segs) {
    await selectSegment(page, s.key);

    // Every figure on the page states the SELECTED segment's denominator, never the pooled one.
    const period = await text(page, 'kpi-period');
    const periodN = num((period.match(/Misses in all history\s+([\d,]+)/) || [])[1] || 'NaN');
    const taint = await text(page, 'miss-taint-count');
    const taintN = num((taint.match(/[\d,]+ of ([\d,]+) misses excluded/) || [])[1] || 'NaN');
    const obs = await text(page, 'miss-observational');
    const obsN = num((obs.match(/\(([\d,]+) of ([\d,]+)\)/) || [])[2] || 'NaN');
    const design = await text(page, 'kpi-design-share');
    const designN = num((design.match(/over all ([\d,]+) misses/) || [])[1] || 'NaN');
    const escape = await text(page, 'kpi-escape-share');
    const escapeN = num((escape.match(/over ([\d,]+) misses/) || [])[1] || 'NaN');
    const detail = await text(page, 'miss-detail');
    const detailN = num((detail.match(/([\d,]+) records?\b/) || [])[1] || 'NaN');
    console.log(`segment ${s.key} (tab ${s.count}): kpi-period=${periodN} taint-of=${taintN} observational-of=${obsN} design-of=${designN} escape-of=${escapeN} detail=${detailN}`);

    expect(periodN, `kpi-period on ${s.key}: "${period}"`).toBe(s.count);
    expect(taintN, `miss-taint-count on ${s.key}: "${taint}"`).toBe(s.count);
    expect(obsN, `miss-observational on ${s.key}: "${obs}"`).toBe(s.count);
    expect(designN, `kpi-design-share on ${s.key}: "${design}"`).toBe(s.count);
    expect(escapeN, `kpi-escape-share on ${s.key}: "${escape}"`).toBe(s.count);
    expect(detailN, `miss-detail on ${s.key}: "${detail.slice(0, 80)}"`).toBe(s.count);
    if (nonEmpty > 1) {
      expect(periodN, `segment ${s.key} shows the pooled total ${sum}`).not.toBe(sum);
    }

    // The detail table holds exactly this segment's rows ...
    const rows = await allDetailRows(page);
    expect(rows.length, `the detail table on ${s.key} holds ${rows.length} rows, the tab says ${s.count}`).toBe(s.count);

    // ... and a sample of their stored records carry this project type (unclassified is inferred, so
    // it has no project_type to compare against).
    if (s.key !== 'unclassified') {
      const sample = rows.filter(r => r.missId.length > 0).slice(0, 5);
      for (const r of sample) {
        await page.locator(`[data-testid="miss-raw-${r.missId}"]`).first().click();
        await page.waitForTimeout(700);
        const raw = await (await testid(page, 'miss-raw')).innerText();
        const pt = raw.match(/"project_type":\s*(null|"([^"]*)")/);
        const value = pt ? (pt[1] === 'null' ? null : pt[2]) : undefined;
        console.log(`  ${s.key} · ${r.missId} project_type=${JSON.stringify(value)}`);
        expect(value === s.key || (value === null && s.key === 'app'),
          `${r.missId} is listed under ${s.key} but its record says project_type ${JSON.stringify(value)}`).toBe(true);
      }
    }
  }
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-FN-078 — per-origin figures count linked records only, and the exclusion is shown beside them.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-FN-078 — per-origin figures count only linked records and show the excluded count beside them', async ({ page }) => {
  test.setTimeout(300_000);
  await signIn(page);
  await gotoScreen(page, '/misses');

  const segs = await segments(page);
  expect(segs.length, 'no project-type segment rendered').toBeGreaterThan(0);

  for (const s of segs) {
    await selectSegment(page, s.key);

    // The exclusion, stated on the origin card itself.
    const taintLoc = page.locator('[data-testid="miss-origin"] [data-testid="miss-taint-count"]').first();
    expect(await taintLoc.count(), `the excluded count is not on the origin card for ${s.key}`).toBe(1);
    await expect(taintLoc).toBeVisible();
    const taint = norm(await taintLoc.innerText());
    const m = taint.match(/([\d,]+) of ([\d,]+) misses excluded/);
    expect(m, `miss-taint-count does not state "x of N misses excluded": "${taint}"`).not.toBeNull();
    const excluded = num(m![1]);
    const total = num(m![2]);
    const linked = total - excluded;
    expect(taint, 'the exclusion must name the linked-only rule').toMatch(/origin_confidence/);
    expect(taint.toLowerCase()).toContain('linked');

    // The detail table is the ground truth a reader can check by eye: the confidence badge on each row.
    const rows = await allDetailRows(page);
    const linkedRows = rows.filter(r => r.confidence === 'linked');
    console.log(`${s.key}: taint "${taint.slice(0, 90)}" | detail rows ${rows.length}, linked ${linkedRows.length}, ` +
      `other confidences ${JSON.stringify(tally(rows.filter(r => r.confidence !== 'linked').map(r => r.confidence)))}`);
    expect(rows.length, `detail rows on ${s.key}`).toBe(total);
    expect(linkedRows.length, `the excluded count says ${excluded} of ${total}, but ${rows.length - linkedRows.length} detail rows are not linked`).toBe(linked);

    // Origin phase × class: the phase rows total the linked records, phase by phase, and the excluded
    // count sits in its own row beside them.
    const originRows = await page.evaluate(() => {
      const root = document.querySelector('[data-testid="miss-origin"]');
      const heads = Array.from(root?.querySelectorAll('thead th') || []).map(h => (h.textContent || '').trim());
      const totalIdx = heads.indexOf('Total');
      return Array.from(root?.querySelectorAll('tbody tr') || []).map(tr => {
        const tds = Array.from(tr.querySelectorAll('td')).map(td => (td.textContent || '').replace(/\s+/g, ' ').trim());
        return { id: tr.getAttribute('data-testid') || '', phase: tds[0], total: Number((tds[totalIdx] || 'NaN').replace(/,/g, '')) };
      });
    });
    const phaseRows = originRows.filter(r => r.id !== 'miss-origin-unattributed');
    const unattributed = originRows.find(r => r.id === 'miss-origin-unattributed');
    const expectedByPhase = tally(linkedRows.map(r => r.origin.split(' · ')[0]));
    console.log(`${s.key}: origin rows ${JSON.stringify(originRows)} | linked detail by phase ${JSON.stringify(expectedByPhase)}`);

    if (linked === 0) {
      await expect(page.locator('[data-testid="miss-origin-none"]').first(),
        `${s.key} has no linked record but no absence statement`).toBeVisible();
      expect(phaseRows.length, `${s.key}: per-origin rows rendered with no linked record`).toBe(0);
    } else {
      const byPhase: Record<string, number> = {};
      for (const r of phaseRows) byPhase[r.phase] = r.total;
      expect(byPhase, `${s.key}: the origin table's phase totals are not the linked records'`).toEqual(expectedByPhase);
      if (excluded > 0) {
        expect(unattributed, `${s.key}: ${excluded} excluded but no unattributed row beside the figures`).toBeTruthy();
        expect(unattributed!.total, `${s.key}: the unattributed row`).toBe(excluded);
      }
    }

    // Who was running: the same linked-only denominator, stated on the band and on both cards.
    const obs = await text(page, 'miss-observational');
    const om = obs.match(/\(([\d,]+) of ([\d,]+)\)/);
    expect(om, `miss-observational states no "(a of N)": "${obs}"`).not.toBeNull();
    expect(num(om![1]), `${s.key}: the observational band's linked count`).toBe(linked);
    for (const card of ['miss-origin-model', 'miss-origin-agent']) {
      const t = await text(page, card);
      expect(t, `${card} on ${s.key} does not state "${linked} linked"`).toContain(`${linked} linked`);
    }

    // By origin agent: each bucket equals the linked detail rows naming that agent.
    const agentRows = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid="miss-origin-agent-table"] tbody tr')).map(tr => {
        const tds = Array.from(tr.querySelectorAll('td')).map(td => (td.textContent || '').replace(/\s+/g, ' ').trim());
        return { key: tds[0], count: Number((tds[1] || 'NaN').replace(/,/g, '')) };
      }));
    const expectedByAgent = tally(linkedRows.map(r => r.origin.includes(' · ') ? r.origin.split(' · ')[1] : 'not named'));
    const byAgent: Record<string, number> = {};
    for (const r of agentRows) byAgent[r.key] = r.count;
    console.log(`${s.key}: agent buckets ${JSON.stringify(byAgent)} | linked detail by agent ${JSON.stringify(expectedByAgent)}`);
    if (linked > 0 && agentRows.length > 0) {
      expect(byAgent, `${s.key}: the agent buckets are not the linked records'`).toEqual(expectedByAgent);
    }

    // By origin model: no bucket can hold more than the linked records.
    const modelSum = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid="miss-origin-model-table"] tbody tr'))
        .map(tr => Number(((tr.querySelectorAll('td')[1]?.textContent) || '0').replace(/,/g, '')))
        .reduce((a, b) => a + b, 0));
    expect(modelSum, `${s.key}: model buckets hold ${modelSum} misses, only ${linked} are linked`).toBeLessThanOrEqual(linked);
  }
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-FN-079 — measured and apportioned rework cost stay separate; no control renders a blend.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-FN-079 — measured and apportioned rework cost stay separate and no control renders a blended number', async ({ page }) => {
  test.setTimeout(420_000);
  await signIn(page);
  await gotoScreen(page, '/misses');

  // The page's controls: the period select, the project-type tabs, and whatever buttons the page carries.
  // None of them offers a blended / combined cost view.
  await page.locator('[data-testid="misses-period"]').first().click();
  await page.waitForTimeout(500);
  const periodOptions = await page.$$eval('[role="option"]', els => els.map(e => ((e as HTMLElement).innerText || '').replace(/\s+/g, ' ').trim()).filter(Boolean));
  await page.keyboard.press('Escape');
  await page.waitForTimeout(400);
  console.log(`period options: ${JSON.stringify(periodOptions)}`);
  expect(periodOptions.length, 'the period select offered no options').toBeGreaterThan(0);

  const controls = await page.evaluate(() => {
    const root = document.querySelector('[data-testid="misses-page"]');
    return Array.from(root?.querySelectorAll('button, [role="tab"], [role="switch"], [role="checkbox"], [role="radio"], select, option, label') || [])
      .map(e => ((e as HTMLElement).innerText || e.getAttribute('aria-label') || '').replace(/\s+/g, ' ').trim())
      .filter(Boolean);
  });
  const blendControls = [...periodOptions, ...controls].filter(t => /blend|combined|merge|pool|all[- ]in|total cost|sole ?\+ ?shared/i.test(t));
  expect(blendControls, `a control on /misses offers a blended cost view: ${JSON.stringify(blendControls)}`).toEqual([]);

  const checked: string[] = [];
  for (const period of periodOptions) {
    await selectPeriod(page, period);
    if ((await page.locator('[data-testid="miss-cost"]').count()) === 0) {
      // A narrow window with no misses renders the empty state and no cost band — nothing to blend.
      console.log(`period "${period}": no cost band (empty state: ${(await page.locator('[data-testid="misses-empty"]').count()) > 0})`);
      continue;
    }
    for (const s of await segments(page)) {
      await selectSegment(page, s.key);
      const where = `${period} · ${s.key}`;

      // Three cohorts, three distinct cards, none nested in another.
      for (const id of ['miss-cost-measured', 'miss-cost-apportioned', 'miss-cost-unattributable']) {
        expect(await page.locator(`[data-testid="miss-cost"] [data-testid="${id}"]`).count(), `${where}: ${id}`).toBe(1);
      }
      const nested = await page.evaluate(() => {
        const a = document.querySelector('[data-testid="miss-cost-measured"]');
        const b = document.querySelector('[data-testid="miss-cost-apportioned"]');
        return !!a && !!b && (a.contains(b) || b.contains(a));
      });
      expect(nested, `${where}: the measured and apportioned figures share one card`).toBe(false);

      const soleText = norm(await page.locator('[data-testid="miss-cost-sole"]').first().innerText());
      const appText = norm(await page.locator('[data-testid="miss-cost-apportioned-value"]').first().innerText());
      const measuredCard = await text(page, 'miss-cost-measured');
      const apportionedCard = await text(page, 'miss-cost-apportioned');
      const noneCount = num(await text(page, 'miss-cost-none'));
      const soleN = num((measuredCard.match(/over ([\d,]+) fix(?:es)?/) || [])[1] || 'NaN');
      const sharedN = num((apportionedCard.match(/across ([\d,]+) fix(?:es)?/) || [])[1] || 'NaN');
      expect(apportionedCard.toLowerCase(), `${where}: the apportioned card must say it is never summed with measured`).toContain('never summed');
      expect(apportionedCard.toLowerCase()).toContain('apportioned');

      // The KPI headline is the MEASURED figure, not a mix.
      const headline = norm(await page.locator('[data-testid="kpi-rework-tokens-value"]').first().innerText());
      expect(headline, `${where}: the rework-tokens headline is not the measured (sole) figure`).toBe(soleText);
      expect((await text(page, 'kpi-rework-tokens')).toLowerCase(), `${where}: the headline must name its cohort`).toContain('sole');

      const noBlend = (await text(page, 'miss-cost-no-blend')).toLowerCase();
      expect(noBlend, `${where}: the standing no-blend statement`).toContain('no blended figure');

      // No number anywhere on the page equals a blend of the two cohorts.
      const sole = Number(soleText.replace(/,/g, ''));
      const app = Number(appText.replace(/,/g, ''));
      if (!Number.isFinite(sole) || !Number.isFinite(app) || !Number.isFinite(soleN) || !Number.isFinite(sharedN)) {
        console.log(`${where}: a cohort is not a number (measured "${soleText}", apportioned "${appText}") — no blend can be formed from the page`);
        checked.push(`${where}: n/a`);
        continue;
      }
      const candidates: Record<string, number> = {
        'sum of the two per-miss figures': sole + app,
        'mean of the two per-miss figures': (sole + app) / 2,
        'record-weighted mean': (sole * soleN + app * sharedN) / (soleN + sharedN),
      };
      if (Number.isFinite(noneCount) && noneCount > 0) {
        candidates['weighted mean over sole+shared+none'] = (sole * soleN + app * sharedN) / (soleN + sharedN + noneCount);
      }
      if (sole === app) {
        // Identical cohort figures make every blend equal to both; nothing distinguishes a blend.
        checked.push(`${where}: cohorts equal`);
        continue;
      }
      const onPage = await page.evaluate(() => {
        const root = document.querySelector('[data-testid="misses-page"]') as HTMLElement | null;
        const t = (root?.innerText || '');
        return (t.match(/\d[\d,]*(?:\.\d+)?/g) || []).map(s => Number(s.replace(/,/g, ''))).filter(n => n >= 100);
      });
      const hits: string[] = [];
      for (const [name, c] of Object.entries(candidates)) {
        if (Math.abs(c - sole) <= 1 || Math.abs(c - app) <= 1) continue; // indistinguishable from a real cohort
        for (const n of onPage) if (Math.abs(n - c) <= 1) hits.push(`${name} ≈ ${c.toFixed(1)} shown as ${n}`);
      }
      console.log(`${where}: measured ${soleText} over ${soleN} · apportioned ${appText} over ${sharedN} · none ${noneCount} · blends ${JSON.stringify(Object.fromEntries(Object.entries(candidates).map(([k, v]) => [k, Number(v.toFixed(1))])))} · hits ${hits.length}`);
      expect(hits, `${where}: a blended rework figure is rendered on /misses: ${hits.join(' | ')}`).toEqual([]);
      checked.push(where);
    }
  }
  console.log(`checked combinations: ${JSON.stringify(checked)}`);
  expect(checked.length, 'no period × segment combination carried a cost band to check').toBeGreaterThan(0);
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-FN-081 — the render and visual gates on both framework axes, at 1280 and at 390.
// ─────────────────────────────────────────────────────────────────────────────

/** Every control the TechieFlow axis renders on a dataset holding live misses. */
const TF_REQUIRED = [
  'misses-page', 'misses-period', 'misses-period-label', 'miss-escape-note', 'miss-type',
  'miss-kpis', 'kpi-open', 'kpi-wontfix', 'kpi-period', 'kpi-median-close', 'kpi-design-share', 'kpi-escape-share',
  'kpi-rework-tokens', 'kpi-rework-usd', 'kpi-rework-usd-estimate',
  'miss-origin', 'miss-taint-count', 'miss-whymissed', 'miss-whymissed-denominator', 'miss-whymissed-eligibility',
  'miss-sort', 'miss-sort-denominator', 'miss-sort-predates', 'miss-sort-amended', 'miss-sort-words',
  'miss-observational', 'miss-origin-model', 'miss-origin-agent',
  'miss-cost', 'miss-cost-measured', 'miss-cost-apportioned', 'miss-cost-unattributable', 'miss-cost-none',
  'miss-cost-attribution-missing', 'miss-cost-no-blend',
  'miss-review-note', 'miss-review-cost', 'miss-review-notavailable', 'miss-review-phases',
  'miss-detail', 'miss-what-note', 'miss-raw-trigger',
];
const TF_TABLES = ['miss-detail-table', 'miss-whymissed-table', 'miss-sort-table'];
/** Either the grid or its stated absence must render — never neither. */
const TF_EITHER: [string, string][] = [
  ['miss-origin-model-table', 'miss-origin-model-none'],
  ['miss-origin-agent-table', 'miss-origin-agent-none'],
];

const PB_EMPTY = ['misses-page', 'misses-period', 'misses-period-label', 'playbook-axis-note', 'pb-misses-surface',
  'pb-miss-ingest-note', 'playbook-empty', 'playbook-empty-connect', 'misses-playbook-plan',
  'misses-playbook-zero-note', 'pb-stream-health', 'pb-stream-health-note'];
const PB_POPULATED = ['misses-page', 'misses-period', 'misses-period-label', 'playbook-axis-note', 'pb-misses-surface',
  'pb-miss-ingest-note', 'miss-kpis', 'kpi-open', 'kpi-closed', 'kpi-reopened', 'kpi-time-to-close',
  'kpi-rework-incidence', 'kpi-rework-tokens', 'pb-miss-guard-note', 'miss-origin', 'miss-taint-count',
  'miss-whymissed', 'miss-whymissed-denominator', 'miss-whymissed-eligibility', 'miss-found-axes',
  'miss-found-axes-note', 'miss-id-axes', 'miss-id-axes-note', 'pb-miss-observational', 'miss-origin-model',
  'miss-origin-model-note', 'miss-origin-tier', 'miss-cost', 'miss-cost-measured', 'miss-cost-apportioned',
  'miss-cost-unattributable', 'miss-cost-no-blend', 'miss-amend-diagnostics', 'pb-stream-health', 'miss-detail'];

async function paneOverflow(page: Page): Promise<number> {
  return page.evaluate(() => {
    const p = document.querySelector('.tflens-page') as HTMLElement | null;
    return p ? p.scrollWidth - p.clientWidth : 0;
  });
}

for (const viewport of [DESKTOP, MOBILE]) {
  test(`REQ-FN-081 — render and visual gates on the TechieFlow axis of /misses @${viewport.width}`, async ({ page }) => {
    test.setTimeout(180_000);
    const errors = collectErrors(page);
    await signIn(page);
    await gotoScreen(page, '/misses');
    await page.setViewportSize(viewport);
    await page.waitForTimeout(1200);

    const shell = viewport.width < 768 ? SHELL_MOBILE : SHELL_DESKTOP;
    const failures: string[] = [];
    const ids: string[] = [...shell, 'framework-switch', ...TF_REQUIRED];

    for (const id of [...shell, ...TF_REQUIRED]) {
      const r = await renderCheck(page, id);
      if (r.verdict !== 'RENDERS') failures.push(`${id}: ${r.verdict} (${r.detail})`);
    }
    for (const id of TF_TABLES) {
      const r = await tableCheck(page, id);
      ids.push(id);
      if (r.verdict !== 'RENDERS') failures.push(`${id}: ${r.verdict} (${r.detail})`);
    }
    for (const [grid, none] of TF_EITHER) {
      const hasGrid = (await page.locator(`[data-testid="${grid}"]`).count()) > 0;
      const r = hasGrid ? await tableCheck(page, grid) : await renderCheck(page, none);
      ids.push(hasGrid ? grid : none);
      if (r.verdict !== 'RENDERS') failures.push(`${hasGrid ? grid : none}: ${r.verdict} (${r.detail})`);
    }
    // The origin cross-tab: phase rows or its stated absence.
    const originIds: string[] = await page.$$eval('[data-testid^="miss-origin-"]', els =>
      els.map(e => e.getAttribute('data-testid') || '').filter(id => !/^miss-origin-(model|agent)/.test(id)));
    expect(originIds.length, 'the origin card rendered neither rows nor its absence statement').toBeGreaterThan(0);
    for (const id of Array.from(new Set(originIds))) {
      const r = await renderCheck(page, id);
      ids.push(id);
      if (r.verdict !== 'RENDERS') failures.push(`${id}: ${r.verdict} (${r.detail})`);
    }
    // Each project-type tab.
    const typeIds: string[] = await page.$$eval('[data-testid^="miss-type-"]', els => els.map(e => e.getAttribute('data-testid') || ''));
    expect(typeIds.length, 'no project-type tab rendered').toBeGreaterThan(0);
    for (const id of typeIds) {
      const r = await renderCheck(page, id);
      ids.push(id);
      if (r.verdict !== 'RENDERS') failures.push(`${id}: ${r.verdict} (${r.detail})`);
    }
    console.log(`TechieFlow @${viewport.width}: ${ids.length} controls checked, ${failures.length} not rendering`);
    expect(failures, `controls not rendering their data on the TechieFlow axis @${viewport.width}: ${failures.join(' | ')}`).toEqual([]);

    const problems = await visualCheck(page, Array.from(new Set(ids)), viewport.width);
    await page.screenshot({ path: `tests/.artifacts/verify-misses/techieflow-${viewport.width}.png`, fullPage: true }).catch(() => {});
    expect(problems, `TechieFlow axis @${viewport.width}: ${problems.join(' | ')}`).toEqual([]);
    const pane = await paneOverflow(page);
    expect(pane, `the page pane scrolls sideways by ${pane}px @${viewport.width}`).toBeLessThanOrEqual(2);

    const real = errors.filter(e => !/favicon|websocket/i.test(e));
    expect(real, `console errors on /misses: ${real.join(' | ')}`).toEqual([]);
  });

  test(`REQ-FN-081 — render and visual gates on the Playbook axis of /misses @${viewport.width}`, async ({ page }) => {
    test.setTimeout(180_000);
    const errors = collectErrors(page);
    try {
      await signIn(page);
      await gotoScreen(page, '/misses');
      await selectFramework(page, 'Playbook');
      await page.setViewportSize(viewport);
      await page.waitForTimeout(1200);

      await expect(page.locator('[data-testid="pb-misses-surface"]').first(), 'the Playbook miss surface did not render').toBeVisible();
      const populated = (await page.locator('[data-testid="pb-misses-surface"] [data-testid="miss-kpis"]').count()) > 0;
      console.log(`Playbook axis @${viewport.width}: ${populated ? 'POPULATED' : 'empty state (no Playbook miss records held)'}`);

      const shell = viewport.width < 768 ? SHELL_MOBILE : SHELL_DESKTOP;
      const required = [...shell, ...(populated ? PB_POPULATED : PB_EMPTY)];
      const failures: string[] = [];
      for (const id of required) {
        const r = await renderCheck(page, id);
        if (r.verdict !== 'RENDERS') failures.push(`${id}: ${r.verdict} (${r.detail})`);
      }
      const ids = [...required];
      if (populated) {
        const r = await tableCheck(page, 'miss-detail-table');
        ids.push('miss-detail-table');
        if (r.verdict !== 'RENDERS') failures.push(`miss-detail-table: ${r.verdict} (${r.detail})`);
      } else {
        // The stream-health rows: each states a count and a state, never a blank cell.
        const streams = await page.evaluate(() =>
          Array.from(document.querySelectorAll('[data-testid^="pb-stream-"]'))
            .filter(e => !/^pb-stream-health/.test(e.getAttribute('data-testid') || ''))
            .map(e => ({ id: e.getAttribute('data-testid') || '', cells: Array.from(e.querySelectorAll('td')).map(td => (td.textContent || '').replace(/\s+/g, ' ').trim()) })));
        console.log(`Playbook stream rows: ${JSON.stringify(streams)}`);
        if (streams.length === 0) failures.push('pb-stream-*: no stream row rendered');
        for (const s of streams) {
          ids.push(s.id);
          if (s.cells.some(c => c.length === 0)) failures.push(`${s.id}: a blank cell ${JSON.stringify(s.cells)}`);
        }
      }
      expect(failures, `controls not rendering their data on the Playbook axis @${viewport.width}: ${failures.join(' | ')}`).toEqual([]);

      const problems = await visualCheck(page, ids, viewport.width);
      await page.screenshot({ path: `tests/.artifacts/verify-misses/playbook-${viewport.width}.png`, fullPage: true }).catch(() => {});
      expect(problems, `Playbook axis @${viewport.width}: ${problems.join(' | ')}`).toEqual([]);
      const pane = await paneOverflow(page);
      expect(pane, `the page pane scrolls sideways by ${pane}px @${viewport.width}`).toBeLessThanOrEqual(2);

      const real = errors.filter(e => !/favicon|websocket/i.test(e));
      expect(real, `console errors on /misses (Playbook): ${real.join(' | ')}`).toEqual([]);
    } finally {
      await page.setViewportSize(DESKTOP);
      await gotoScreen(page, '/misses').catch(() => {});
      await selectFramework(page, 'TechieFlow').catch(() => {});
    }
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// REQ-FN-104 — cross-edition axes: item id beside req id; process and assertion gates never share a column.
// ─────────────────────────────────────────────────────────────────────────────

/** The header cells of a table under a testid, as the reader sees them. */
async function headers(page: Page, id: string): Promise<string[]> {
  return page.evaluate((tid) => {
    const root = document.querySelector(`[data-testid="${tid}"]`);
    return Array.from(root?.querySelectorAll('thead th') || []).map(h => (h.textContent || '').replace(/\s+/g, ' ').trim());
  }, id);
}

test('REQ-FN-104 — on the Playbook axis item_id sits beside req_id and found_phase_gate never shares a column with found_gate', async ({ page }) => {
  test.setTimeout(180_000);
  try {
    await signIn(page);
    await gotoScreen(page, '/misses');
    await selectFramework(page, 'Playbook');

    await expect(page.locator('[data-testid="pb-misses-surface"]').first(), 'the Playbook miss surface did not render').toBeVisible();
    const populated = (await page.locator('[data-testid="pb-misses-surface"] [data-testid="miss-kpis"]').count()) > 0;
    const zero = populated ? '' : norm(await page.locator('[data-testid="misses-playbook-zero-note"]').first().innerText().catch(() => ''));
    const held = (zero.match(/holds (\d[\d,]*) Playbook miss records/) || [])[1];
    test.skip(!populated,
      `The Playbook axis holds no miss records today (${held !== undefined ? `page states it holds ${held}` : 'empty state shown'}), so the cross-edition axes (miss-found-axes, miss-id-axes) are not rendered and cannot be read.`);

    // miss-id-axes: item_id and req_id are two adjacent columns, never one "ID" column.
    const idHeads = await headers(page, 'miss-id-axes');
    console.log(`miss-id-axes headers: ${JSON.stringify(idHeads)}`);
    const itemIdx = idHeads.findIndex(h => /\bitem_id\b/.test(h));
    const reqIdx = idHeads.findIndex(h => /\breq_id\b/.test(h));
    expect(itemIdx, 'item_id is not a column on miss-id-axes').toBeGreaterThanOrEqual(0);
    expect(reqIdx, 'req_id is not a column on miss-id-axes').toBeGreaterThanOrEqual(0);
    expect(itemIdx, 'item_id and req_id are collapsed into one column').not.toBe(reqIdx);
    expect(Math.abs(itemIdx - reqIdx), `item_id (col ${itemIdx}) does not sit beside req_id (col ${reqIdx})`).toBe(1);

    // miss-found-axes: the process gate and the assertion gate are separate columns.
    const foundHeads = await headers(page, 'miss-found-axes');
    console.log(`miss-found-axes headers: ${JSON.stringify(foundHeads)}`);
    const pgIdx = foundHeads.findIndex(h => /\bfound_phase_gate\b/.test(h));
    const gIdx = foundHeads.findIndex(h => /\bfound_gate\b/.test(h));
    expect(pgIdx, 'found_phase_gate is not a column').toBeGreaterThanOrEqual(0);
    expect(gIdx, 'found_gate is not a column').toBeGreaterThanOrEqual(0);
    expect(pgIdx, 'found_phase_gate and found_gate share one column').not.toBe(gIdx);
    expect(foundHeads.filter(h => /found_phase_gate/.test(h) && /\bfound_gate\b/.test(h)), 'a header names both gates').toEqual([]);
    expect(foundHeads.filter(h => /\b(total|sum|combined|all gates)\b/i.test(h)), 'a column totals the two gate axes').toEqual([]);
    expect((await text(page, 'miss-found-axes-note')).toLowerCase(), 'the note must say the two axes are never summed').toMatch(/never summed/);

    // The per-miss detail table keeps the same split.
    const detailHeads = await headers(page, 'miss-detail-table');
    console.log(`Playbook miss-detail-table headers: ${JSON.stringify(detailHeads)}`);
    const dItem = detailHeads.indexOf('item_id');
    const dReq = detailHeads.indexOf('req_id');
    expect(dItem, 'item_id is not a detail column').toBeGreaterThanOrEqual(0);
    expect(dReq, 'req_id is not a detail column').toBeGreaterThanOrEqual(0);
    expect(Math.abs(dItem - dReq), 'item_id does not sit beside req_id in the detail table').toBe(1);
    expect(detailHeads.filter(h => /found_phase_gate/.test(h) && /\bfound_gate\b/.test(h)), 'a detail header merges the two gates').toEqual([]);
  } finally {
    await gotoScreen(page, '/misses').catch(() => {});
    await selectFramework(page, 'TechieFlow').catch(() => {});
  }
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-FN-105 — Playbook guards are stricter; an unknown origin never lands in a model-named bucket.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-FN-105 — on the Playbook axis the stricter guards apply and no unknown origin lands in a model-named bucket', async ({ page }) => {
  test.setTimeout(180_000);
  try {
    await signIn(page);
    await gotoScreen(page, '/misses');
    await selectFramework(page, 'Playbook');

    await expect(page.locator('[data-testid="pb-misses-surface"]').first(), 'the Playbook miss surface did not render').toBeVisible();
    const populated = (await page.locator('[data-testid="pb-misses-surface"] [data-testid="miss-kpis"]').count()) > 0;
    if (!populated) {
      // Today's state: no band exists, so nothing can be bucketed at all.
      expect(await page.locator('[data-testid^="miss-model-"]').count(), 'model buckets rendered with no Playbook records held').toBe(0);
    }
    const zero = populated ? '' : norm(await page.locator('[data-testid="misses-playbook-zero-note"]').first().innerText().catch(() => ''));
    const held = (zero.match(/holds (\d[\d,]*) Playbook miss records/) || [])[1];
    test.skip(!populated,
      `The Playbook axis holds no miss records today (${held !== undefined ? `page states it holds ${held}` : 'empty state shown'}), so no Playbook miss figure exists to apply the stricter guards to.`);

    const guard = await text(page, 'pb-miss-guard-note');
    console.log(`pb-miss-guard-note = "${guard.slice(0, 300)}"`);
    expect(guard.toLowerCase(), 'the guard note must say the Playbook guards are stricter').toContain('stricter');
    expect(guard.toLowerCase(), 'the guard note must say they are not relaxed').toContain('not relaxed');
    expect(guard.toLowerCase(), 'the guard note must refuse the unknown-model bucket').toMatch(/never placed in an .unknown model. bucket/);

    const taint = await text(page, 'miss-taint-count');
    const tm = taint.match(/([\d,]+) of ([\d,]+) misses excluded/);
    expect(tm, `miss-taint-count: "${taint}"`).not.toBeNull();
    const refused = num(tm![1]);
    const total = num(tm![2]);
    const obs = await text(page, 'pb-miss-observational');
    const attributed = num((obs.match(/([\d,]+) records/) || [])[1] || 'NaN');
    console.log(`Playbook: refused ${refused} of ${total}; attributed ${attributed}`);
    expect(attributed + refused, 'attributed + refused does not account for every miss').toBe(total);

    const models = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid^="miss-model-"]')).map(tr => {
        const tds = Array.from(tr.querySelectorAll('td')).map(td => (td.textContent || '').replace(/\s+/g, ' ').trim());
        return { key: tds[0] || '', count: Number((tds[1] || '0').replace(/,/g, '')) };
      }));
    console.log(`Playbook model buckets: ${JSON.stringify(models)}`);
    expect(models.filter(m => UNKNOWN_BUCKET.test(m.key) || /unknown/i.test(m.key)),
      'a model-named bucket holds unknown-origin records').toEqual([]);
    const modelSum = models.reduce((a, m) => a + m.count, 0);
    expect(modelSum, `model buckets hold ${modelSum}, only ${attributed} records cleared the guard`).toBeLessThanOrEqual(attributed);
    expect((await text(page, 'miss-origin-model-note')).toLowerCase()).toMatch(/no .unknown model. row/);
  } finally {
    await gotoScreen(page, '/misses').catch(() => {});
    await selectFramework(page, 'TechieFlow').catch(() => {});
  }
});

test('REQ-FN-105 — on the TechieFlow axis the who-was-running band has no unknown model bucket and holds only linked origins', async ({ page }) => {
  test.setTimeout(240_000);
  await signIn(page);
  await gotoScreen(page, '/misses');

  const segs = await segments(page);
  expect(segs.length, 'no project-type segment rendered').toBeGreaterThan(0);
  for (const s of segs) {
    await selectSegment(page, s.key);
    const obs = await text(page, 'miss-observational');
    const om = obs.match(/\(([\d,]+) of ([\d,]+)\)/);
    expect(om, `miss-observational: "${obs}"`).not.toBeNull();
    const linked = num(om![1]);

    const band = await page.evaluate(() => {
      const read = (id: string) => Array.from(document.querySelectorAll(`[data-testid="${id}"] tbody tr`)).map(tr => {
        const tds = Array.from(tr.querySelectorAll('td')).map(td => (td.textContent || '').replace(/\s+/g, ' ').trim());
        return { key: tds[0] || '', count: Number((tds[1] || '0').replace(/,/g, '')) };
      });
      return { models: read('miss-origin-model-table'), agents: read('miss-origin-agent-table') };
    });
    const hasModelNone = (await page.locator('[data-testid="miss-origin-model-none"]').count()) > 0;
    console.log(`${s.key}: linked ${linked} · model buckets ${JSON.stringify(band.models)} · model-none ${hasModelNone} · agent buckets ${JSON.stringify(band.agents)}`);

    // A model bucket is a claim about a model. None may be named for an unknown origin.
    expect(band.models.filter(m => UNKNOWN_BUCKET.test(m.key) || /unknown/i.test(m.key)),
      `${s.key}: the model band carries an unknown bucket`).toEqual([]);
    expect(band.agents.filter(m => /unknown/i.test(m.key)), `${s.key}: the agent band carries an "unknown" bucket`).toEqual([]);
    expect(band.models.length > 0 || hasModelNone, `${s.key}: the model band shows neither buckets nor its absence`).toBe(true);

    // Unknown- and inferred-origin records are held out: the model buckets never exceed the linked count.
    const modelSum = band.models.reduce((a, m) => a + m.count, 0);
    expect(modelSum, `${s.key}: model buckets hold ${modelSum}, only ${linked} records are linked`).toBeLessThanOrEqual(linked);
    const agentSum = band.agents.reduce((a, m) => a + m.count, 0);
    expect(agentSum, `${s.key}: agent buckets hold ${agentSum}, only ${linked} records are linked`).toBeLessThanOrEqual(linked);
  }
});
