/**
 * Black-box verification of the Coverage / health landing page (`/`) and the
 * Three questions screen (`/three-questions`).
 *
 * Nothing here touches application source. Every assertion is made against what
 * the running app renders for the canonical demo user.
 *
 * SAFETY: the rebuild flow on `/` is destructive. `rebuild-confirm` is NEVER
 * pressed anywhere in this file — see REQ-UI-017 below.
 */
import { test, expect } from '@playwright/test';
import {
  signIn,
  gotoScreen,
  testid,
  renderCheck,
  tableCheck,
  USER1,
  DESKTOP,
} from './_helpers';

const GATE_ORDER = [
  'build',
  'acceptance',
  'render',
  'visual',
  'perf',
  'standards',
  'escaped',
  'unattributed',
];

/** Reads the `{name}` suffix off every testid carrying a given prefix, in DOM order. */
async function suffixes(page: import('@playwright/test').Page, prefix: string): Promise<string[]> {
  return page.$$eval(
    `[data-testid^="${prefix}"]`,
    (els, p) => els.map(e => (e.getAttribute('data-testid') ?? '').slice((p as string).length)),
    prefix,
  );
}

/** innerText of a testid, or '' when it is absent. */
async function textOf(page: import('@playwright/test').Page, id: string): Promise<string> {
  const loc = page.locator(`[data-testid="${id}"]`).first();
  if ((await page.locator(`[data-testid="${id}"]`).count()) === 0) return '';
  return ((await loc.innerText().catch(() => '')) || '').trim();
}

/** Activates one project_type tab and waits for that tab's KPI row to attach. */
async function activateType(page: import('@playwright/test').Page, type: string) {
  const tab = page.locator(`[data-testid="type-tab-${type}"]`).first();
  await tab.waitFor({ state: 'attached', timeout: 20_000 });
  await tab.click({ timeout: 20_000 });
  // Blazor Server: the tab swap is a round trip, so wait for this type's own tiles.
  await page
    .locator(`[data-testid="kpi-first-pass-${type}"]`)
    .first()
    .waitFor({ state: 'attached', timeout: 20_000 });
  await page.waitForTimeout(400);
}

test.use({ viewport: DESKTOP });

test.beforeEach(async ({ page }) => {
  await signIn(page, USER1);
});

// ---------------------------------------------------------------------------
// SCREEN `/` — Coverage / health
// ---------------------------------------------------------------------------

test('REQ-UI-014 Coverage landing page states a verdict, four KPIs, and one card per repo with linked SHA and stream table', async ({
  page,
}) => {
  await gotoScreen(page, '/');
  await (await testid(page, 'coverage-status')).waitFor({ state: 'attached', timeout: 20_000 });

  // The one-line verdict for the whole workspace.
  const status = await textOf(page, 'coverage-status');
  expect(status, `coverage-status text: ${JSON.stringify(status)}`).toMatch(/^(GREEN|CHECK)\b/);

  // The parser / repo-count chip is the page's provenance stamp.
  expect((await renderCheck(page, 'coverage-parser')).verdict).toBe('RENDERS');

  // The four KPI tiles all render non-empty text.
  for (const kpi of ['kpi-repos-synced', 'kpi-gate-records', 'kpi-newest-age', 'kpi-sync-errors']) {
    const r = await renderCheck(page, kpi);
    expect(r.verdict, `${kpi} -> ${r.detail}`).toBe('RENDERS');
    expect((await textOf(page, kpi)).length, `${kpi} has no text`).toBeGreaterThan(0);
  }

  // Discover the repo names — never hardcoded.
  const repos = await suffixes(page, 'repo-card-');
  console.log(`BRANCH: REQ-UI-014 discovered repo cards = ${JSON.stringify(repos)}`);
  expect(repos.length, 'no repo-card-* rendered').toBeGreaterThan(0);

  let shaCards = 0;
  let fourStreamTables = 0;

  for (const name of repos) {
    // --- SHA badge links at the commit it synced ------------------------------
    const shaLoc = page.locator(`[data-testid="repo-sha-${name}"]`).first();
    const hasSha = (await page.locator(`[data-testid="repo-sha-${name}"]`).count()) > 0;
    if (hasSha) {
      shaCards++;
      const href = await shaLoc.evaluate(el => {
        const a = el instanceof HTMLAnchorElement ? el : el.closest('a');
        return a ? a.getAttribute('href') : null;
      });
      expect(href, `repo-sha-${name} is not inside an anchor`).not.toBeNull();
      expect(href!, `repo-sha-${name} href`).toMatch(
        /^https:\/\/github\.com\/[^/]+\/[^/]+\/commit\/.+/,
      );
      expect((await shaLoc.innerText()).trim().length, `repo-sha-${name} is blank`).toBeGreaterThan(0);
    } else {
      // A card may legitimately carry no SHA when the repo has no commit SHA recorded in
      // SyncState. What must never happen is the SHA going missing *silently*: the state
      // badge has to say something. (The demo account also holds `tflenstest/Store*` rows
      // left behind by a build-phase harness — repos that do not exist on GitHub, so they
      // have a sync timestamp but no SHA. That is DB pollution, reported separately, not a
      // page defect: the page renders faithfully what the store holds.)
      const state = await textOf(page, `repo-state-${name}`);
      console.log(`BRANCH: REQ-UI-014 ${name} has no repo-sha badge; repo-state reads "${state}"`);
      expect(state.trim().length, `repo-card-${name} has no SHA and no state badge either`).toBeGreaterThan(0);
    }

    // --- stream table renders rows with content ------------------------------
    const t = await tableCheck(page, `repo-streams-${name}`);
    expect(t.verdict, `repo-streams-${name} -> ${t.detail}`).toBe('RENDERS');

    // First column = stream label. Collect them for the framework-stream check.
    const labels = await page.$$eval(
      `[data-testid="repo-streams-${name}"] tbody tr td:first-child`,
      tds => tds.map(td => ((td as HTMLElement).innerText || '').trim().split(/\s+/)[0]),
    );
    console.log(`BRANCH: REQ-UI-014 ${name} streams = ${JSON.stringify(labels)}`);
    expect(labels.length, `repo-streams-${name} has no rows`).toBeGreaterThan(0);
    for (const l of labels) expect(l.length, `blank stream label in ${name}`).toBeGreaterThan(0);

    if (['runs', 'gates', 'sessions', 'commits'].every(s => labels.includes(s))) {
      fourStreamTables++;
    }
  }

  expect(shaCards, 'no repo card carried a repo-sha-* commit link').toBeGreaterThan(0);
  // A TechieFlow repo reports four streams; the header switch defaults to TechieFlow.
  expect(
    fourStreamTables,
    'no repo stream table listed all four TechieFlow streams (runs, gates, sessions, commits)',
  ).toBeGreaterThan(0);
});

test('REQ-UI-015 Staleness warning appears with prose when and only when a stream is stale', async ({
  page,
}) => {
  await gotoScreen(page, '/');
  await (await testid(page, 'coverage-status')).waitFor({ state: 'attached', timeout: 20_000 });

  const staleBadges = await suffixes(page, 'stale-');
  const staleAlerts = await suffixes(page, 'repo-stale-');

  if (staleBadges.length > 0) {
    console.log(
      `BRANCH: REQ-UI-015 STALE branch exercised — badges=${JSON.stringify(staleBadges)}, alerts=${JSON.stringify(staleAlerts)}`,
    );
    expect(staleAlerts.length, 'stale badge rendered with no repo-stale-* alert').toBeGreaterThan(0);

    for (const name of staleAlerts) {
      const prose = await textOf(page, `repo-stale-${name}`);
      console.log(`BRANCH: REQ-UI-015 repo-stale-${name} = ${JSON.stringify(prose)}`);
      expect(prose.toLowerCase(), `repo-stale-${name} does not say "stale"`).toContain('stale');
      // It must name a day threshold as a number...
      expect(prose, `repo-stale-${name} names no day threshold`).toMatch(/\d+\s*day/i);
      // ...and hand the reader the fix.
      expect(prose, `repo-stale-${name} omits the remediation`).toContain('update-framework.sh');
      // ...and name the stream(s) it is complaining about.
      const named = ['runs', 'gates', 'sessions', 'commits'].some(s => prose.includes(s));
      expect(named, `repo-stale-${name} names no stream`).toBe(true);
    }
  } else {
    console.log('BRANCH: REQ-UI-015 NOT-STALE branch exercised — no stale-* badge on the page');
    // The warning must not appear without cause.
    expect(staleAlerts.length, 'repo-stale-* alert rendered with no stale stream').toBe(0);
  }
});

test('REQ-UI-016 Unknown-fields panel lists names only, counts honestly, and leaks no payload', async ({
  page,
}) => {
  await gotoScreen(page, '/');
  const panel = await testid(page, 'unknown-fields');
  await panel.waitFor({ state: 'attached', timeout: 20_000 });

  const hasTrigger = (await page.locator('[data-testid="unknown-fields-trigger"]').count()) > 0;
  const hasNone = (await page.locator('[data-testid="unknown-fields-none"]').count()) > 0;
  expect(
    hasTrigger || hasNone,
    'neither unknown-fields-trigger nor unknown-fields-none rendered',
  ).toBe(true);

  if (hasTrigger) {
    const triggerText = await textOf(page, 'unknown-fields-trigger');
    console.log(`BRANCH: REQ-UI-016 trigger = ${JSON.stringify(triggerText)}`);
    const m = triggerText.match(/(\d+)/);
    expect(m, `unknown-fields-trigger carries no count: ${triggerText}`).not.toBeNull();
    const declared = Number(m![1]);

    // Exercise the disclosure. The panel starts open, so a click closes it —
    // click again to land open before counting.
    await page.locator('[data-testid="unknown-fields-trigger"]').first().click({ timeout: 20_000 });
    await page.waitForTimeout(600);
    let bodyVisible =
      (await page.locator('[data-testid="unknown-fields-none"]').isVisible().catch(() => false)) ||
      (await page.locator('[data-testid^="unknown-group-"]').first().isVisible().catch(() => false));
    if (!bodyVisible) {
      await page.locator('[data-testid="unknown-fields-trigger"]').first().click({ timeout: 20_000 });
      await page.waitForTimeout(600);
      bodyVisible =
        (await page.locator('[data-testid="unknown-fields-none"]').isVisible().catch(() => false)) ||
        (await page.locator('[data-testid^="unknown-group-"]').first().isVisible().catch(() => false));
    }
    expect(bodyVisible, 'unknown-fields panel body never became visible after toggling').toBe(true);

    if (declared === 0) {
      expect(
        await page.locator('[data-testid="unknown-fields-none"]').count(),
        'trigger says 0 undocumented fields but unknown-fields-none is absent',
      ).toBeGreaterThan(0);
      expect((await textOf(page, 'unknown-fields-none')).toLowerCase()).toContain('none');
      console.log('BRANCH: REQ-UI-016 ZERO-unknown-fields branch exercised');
    } else {
      // Field-name badges live inside the per-repo/stream groups.
      const names = await page.$$eval(
        '[data-testid^="unknown-group-"]',
        groups =>
          groups.flatMap(g =>
            Array.from(g.querySelectorAll('span.font-mono')).map(
              s => ((s as HTMLElement).innerText || '').trim(),
            ),
          ),
      );
      const distinct = Array.from(new Set(names.filter(n => n.length > 0)));
      console.log(
        `BRANCH: REQ-UI-016 declared=${declared} badges=${names.length} distinct=${distinct.length} -> ${JSON.stringify(distinct)}`,
      );
      // The trigger counts DISTINCT undocumented field names; the same name may be
      // badged once per repo/stream group it was observed in.
      expect(distinct.length, `panel shows ${distinct.length} distinct names, trigger says ${declared}`).toBe(
        declared,
      );
      for (const n of distinct) expect(n.length).toBeGreaterThan(0);
    }

    // CRITICAL PRIVACY ASSERTION — names only, never a stored payload.
    const panelText = await panel.innerText();
    expect(panelText, 'unknown-fields panel rendered a `{`').not.toContain('{');
    expect(panelText, 'unknown-fields panel rendered a `}`').not.toContain('}');
    expect(panelText, 'unknown-fields panel rendered a JSON key/value pair').not.toContain('":"');
  } else {
    console.log('BRANCH: REQ-UI-016 none-only branch exercised (no trigger)');
    expect((await textOf(page, 'unknown-fields-none')).toLowerCase()).toContain('none');
  }

  // The v > 1 alert, when the store holds records from a newer schema.
  const versionAlerts = await page.locator('[data-testid="schema-version-alert"]').count();
  if (versionAlerts > 0) {
    console.log(`BRANCH: REQ-UI-016 schema-version-alert present (${versionAlerts})`);
    const repoNames = (await suffixes(page, 'repo-card-')).map(n => n.toLowerCase());
    for (let i = 0; i < versionAlerts; i++) {
      const t = ((await page.locator('[data-testid="schema-version-alert"]').nth(i).innerText()) || '')
        .trim()
        .toLowerCase();
      console.log(`BRANCH: REQ-UI-016 schema-version-alert[${i}] = ${JSON.stringify(t)}`);
      const namesRepo = repoNames.some(r => r.length > 0 && t.includes(r));
      const namesStream = ['runs', 'gates', 'sessions', 'commits'].some(s => t.includes(s));
      expect(namesRepo, `schema-version-alert[${i}] names no repo`).toBe(true);
      expect(namesStream, `schema-version-alert[${i}] names no stream`).toBe(true);
    }
  } else {
    console.log('BRANCH: REQ-UI-016 no schema-version-alert on this data');
  }
});

test('REQ-UI-017 Rebuild is guarded by a confirmation that can be cancelled without running', async ({
  page,
}) => {
  await gotoScreen(page, '/');
  await (await testid(page, 'rebuild-card')).waitFor({ state: 'attached', timeout: 20_000 });
  const button = await testid(page, 'rebuild');

  // The rebuild has not run before we touch anything.
  expect(await page.locator('[data-testid="rebuild-progress"]').count()).toBe(0);
  expect(await page.locator('[data-testid="rebuild-per-stream"]').count()).toBe(0);
  const reportBefore = await textOf(page, 'rebuild-report');
  expect(reportBefore, 'a rebuild report was already on screen').not.toContain('Files replayed');

  // Opening the dialog must drop nothing.
  await button.click({ timeout: 20_000 });
  const title = await testid(page, 'rebuild-title');
  await expect(title).toBeVisible({ timeout: 20_000 });
  await expect(page.locator('[data-testid="rebuild-cancel"]').first()).toBeVisible({ timeout: 20_000 });
  await expect(page.locator('[data-testid="rebuild-confirm"]').first()).toBeVisible({ timeout: 20_000 });
  console.log(`BRANCH: REQ-UI-017 dialog opened with title ${JSON.stringify(await title.innerText())}`);

  // Still nothing has run just from opening it.
  expect(await page.locator('[data-testid="rebuild-progress"]').count()).toBe(0);

  // NEVER press `rebuild-confirm` — it drops and re-derives every parsed row for
  // this user. This test only ever presses Cancel; the confirm path is
  // deliberately left unexercised because it is destructive.
  await page.locator('[data-testid="rebuild-cancel"]').first().click({ timeout: 20_000 });
  await page.waitForTimeout(1000);

  // The dialog closed...
  await expect(page.locator('[data-testid="rebuild-title"]').first()).toBeHidden({ timeout: 20_000 });
  // ...and no rebuild ran.
  expect(await page.locator('[data-testid="rebuild-progress"]').count(), 'rebuild-progress appeared after Cancel').toBe(0);
  expect(await page.locator('[data-testid="rebuild-per-stream"]').count(), 'a rebuild report was produced after Cancel').toBe(0);
  const reportAfter = await textOf(page, 'rebuild-report');
  expect(reportAfter, 'Cancel produced a rebuild report').not.toContain('Files replayed');
  console.log(`BRANCH: REQ-UI-017 cancelled; rebuild-report reads ${JSON.stringify(reportAfter)}`);
});

// ---------------------------------------------------------------------------
// SCREEN `/three-questions`
// ---------------------------------------------------------------------------

test('REQ-UI-018 One tab per project_type, no "all" tab, no total, standing SCHEMA note', async ({
  page,
}) => {
  await gotoScreen(page, '/three-questions');
  const note = await testid(page, 'schema-note');
  await expect(note).toBeVisible({ timeout: 20_000 });
  const noteText = (await note.innerText()).trim();
  console.log(`BRANCH: REQ-UI-018 schema-note = ${JSON.stringify(noteText)}`);
  expect(noteText, 'schema-note does not mention project_type').toContain('project_type');
  expect(noteText, 'schema-note does not cite SCHEMA.md').toContain('SCHEMA.md');

  await (await testid(page, 'type-tabs')).waitFor({ state: 'attached', timeout: 20_000 });
  const types = await suffixes(page, 'type-tab-');
  console.log(`BRANCH: REQ-UI-018 discovered project types = ${JSON.stringify(types)}`);
  expect(types.length, 'no type-tab-* rendered').toBeGreaterThan(0);

  // There is no "all types" tab.
  expect(await page.locator('[data-testid="type-tab-all"]').count(), 'a type-tab-all exists').toBe(0);
  for (const t of types) {
    const label = (await textOf(page, `type-tab-${t}`)).trim();
    expect(label, `tab "${t}" is labelled as an all-types tab: ${label}`).not.toMatch(/^\s*all\b/i);
  }

  // There is no total row anywhere inside the tabs region.
  const tabsText = ((await (await testid(page, 'type-tabs')).innerText()) || '').trim();
  const totalLines = tabsText.split('\n').filter(l => /\btotal\b/i.test(l));
  expect(
    totalLines,
    `the tabs region renders a total: ${JSON.stringify(totalLines)}`,
  ).toEqual([]);

  for (const type of types) {
    await activateType(page, type);
    for (const kpi of [`kpi-first-pass-${type}`, `kpi-escape-${type}`, `kpi-failures-${type}`]) {
      const r = await renderCheck(page, kpi);
      expect(r.verdict, `${kpi} -> ${r.detail}`).toBe('RENDERS');
      const t = await textOf(page, kpi);
      expect(t.length, `${kpi} rendered no text`).toBeGreaterThan(0);
      // A figure below the floor must refuse to be a number, and say n.
      if (/insufficient data/i.test(t)) {
        console.log(`BRANCH: REQ-UI-018 ${kpi} below the floor -> ${JSON.stringify(t)}`);
        expect(t, `${kpi} says "insufficient data" without (n=`).toContain('(n=');
      }
    }
  }
});

test('REQ-UI-019 Backfilled figures are labelled beside the live ones and never summed', async ({
  page,
}) => {
  await gotoScreen(page, '/three-questions');
  await (await testid(page, 'type-tabs')).waitFor({ state: 'attached', timeout: 20_000 });
  const types = await suffixes(page, 'type-tab-');
  expect(types.length).toBeGreaterThan(0);

  for (const type of types) {
    await activateType(page, type);

    for (const metric of ['first-pass', 'escape', 'failures']) {
      const liveId = `live-${metric}-${type}`;
      const backId = `backfilled-${metric}-${type}`;

      expect(await page.locator(`[data-testid="${liveId}"]`).count(), `${liveId} missing`).toBeGreaterThan(0);
      expect(await page.locator(`[data-testid="${backId}"]`).count(), `${backId} missing`).toBeGreaterThan(0);

      const liveText = await textOf(page, liveId);
      const backText = await textOf(page, backId);
      expect(liveText.length, `${liveId} is blank`).toBeGreaterThan(0);
      expect(backText.length, `${backId} is blank`).toBeGreaterThan(0);

      // The backfilled figure carries the word "backfilled" on itself or on the
      // element that immediately contains it.
      const labelled = await page.locator(`[data-testid="${backId}"]`).first().evaluate(el => {
        const own = ((el as HTMLElement).innerText || '').toLowerCase();
        const parent = el.parentElement ? ((el.parentElement as HTMLElement).innerText || '').toLowerCase() : '';
        return own.includes('backfilled') || parent.includes('backfilled');
      });
      expect(labelled, `${backId} is not labelled "backfilled"`).toBe(true);

      console.log(`BRANCH: REQ-UI-019 ${type}/${metric} live=${JSON.stringify(liveText)} backfilled=${JSON.stringify(backText)}`);
    }
  }

  // Nothing anywhere on the page adds the two provenances together.
  const body = ((await page.locator('body').innerText()) || '').trim();
  // NB: the page's own standing note legitimately contains the word "combined" in the
  // negative ("never combined across project_type or across live/backfilled"). Matching a
  // bare "combined" therefore flags the disclaimer that proves the rule, not a breach of it.
  // Only wording that asserts an actual merged value counts.
  const combined = body
    .split('\n')
    .filter(l => /live *\+ *backfilled|total \(live|combined (total|figure|value|rate)/i.test(l));
  expect(combined, `the page combines provenances: ${JSON.stringify(combined)}`).toEqual([]);
});

test('REQ-UI-020 Gate catch distribution renders the whole gate order with its caveat badges', async ({
  page,
}) => {
  await gotoScreen(page, '/three-questions');
  await (await testid(page, 'type-tabs')).waitFor({ state: 'attached', timeout: 20_000 });
  const types = await suffixes(page, 'type-tab-');
  expect(types.length).toBeGreaterThan(0);

  for (const type of types) {
    await activateType(page, type);
    const id = `gate-dist-${type}`;
    await page.locator(`[data-testid="${id}"]`).first().waitFor({ state: 'attached', timeout: 20_000 });

    const t = await tableCheck(page, id);
    expect(t.verdict, `${id} -> ${t.detail}`).toBe('RENDERS');

    // First column, in DOM order. The gate name is the first token of the cell —
    // the escaped and perf rows carry a trailing badge in the same cell.
    const rows = await page.$$eval(`[data-testid="${id}"] tbody tr`, trs =>
      trs.map(tr => {
        const td = tr.querySelector('td');
        const text = td ? ((td as HTMLElement).innerText || '').trim() : '';
        return { gate: text.split(/\s+/)[0] ?? '', cell: text };
      }),
    );
    const gates = rows.map(r => r.gate);
    console.log(`BRANCH: REQ-UI-020 ${type} gate order = ${JSON.stringify(gates)}`);
    expect(gates, `${id} gate order`).toEqual(GATE_ORDER);

    const escaped = rows.find(r => r.gate === 'escaped')!;
    expect(escaped.cell.toLowerCase(), `${id} escaped row lacks its badge`).toContain(
      'no gate caught it',
    );

    const perf = rows.find(r => r.gate === 'perf')!;
    expect(perf.cell.toLowerCase(), `${id} perf row lacks its late-gate badge`).toContain(
      'see coverage',
    );
  }
});

test('REQ-UI-021 Tainted-REQ list names every REQ excluded from the live first-pass rate', async ({
  page,
}) => {
  await gotoScreen(page, '/three-questions');
  const list = await testid(page, 'taint-list');
  await list.waitFor({ state: 'attached', timeout: 20_000 });

  const hasTrigger = (await page.locator('[data-testid="taint-trigger"]').count()) > 0;
  let declared: number | null = null;

  if (hasTrigger) {
    const triggerText = await textOf(page, 'taint-trigger');
    console.log(`BRANCH: REQ-UI-021 trigger = ${JSON.stringify(triggerText)}`);
    const m = triggerText.match(/(\d+)/);
    expect(m, `taint-trigger carries no count: ${triggerText}`).not.toBeNull();
    expect(triggerText.toLowerCase(), 'taint-trigger does not say "excluded"').toContain('excluded');
    declared = Number(m![1]);

    // The disclosure starts open; click, and click again if that closed it.
    await page.locator('[data-testid="taint-trigger"]').first().click({ timeout: 20_000 });
    await page.waitForTimeout(600);
    if (!(await list.isVisible().catch(() => false))) {
      await page.locator('[data-testid="taint-trigger"]').first().click({ timeout: 20_000 });
      await page.waitForTimeout(600);
    }
    await expect(list).toBeVisible({ timeout: 20_000 });
  } else {
    console.log('BRANCH: REQ-UI-021 no taint-trigger rendered');
  }

  const listText = ((await list.innerText()) || '').trim();
  const reqs = (listText.match(/REQ-[A-Z]+-\d+/g) ?? []).filter((v, i, a) => a.indexOf(v) === i);
  console.log(`BRANCH: REQ-UI-021 declared=${declared} listed=${reqs.length} -> ${JSON.stringify(reqs)}`);

  if (declared !== null && declared > 0) {
    expect(reqs.length, `taint-list shows ${reqs.length} REQ badges, trigger says ${declared}`).toBe(
      declared,
    );
    // Every badge is a well-formed REQ id and nothing else.
    const badgeTexts = await page.$$eval('[data-testid="taint-list"] > *', els =>
      els.map(e => ((e as HTMLElement).innerText || '').trim()).filter(s => s.length > 0),
    );
    for (const b of badgeTexts) {
      expect(b, `taint-list badge is not a REQ id: ${b}`).toMatch(/^REQ-[A-Z]+-\d+$/);
    }
  } else {
    console.log('BRANCH: REQ-UI-021 EMPTY branch exercised — no tainted REQs');
    expect(listText.toLowerCase(), `empty taint-list should read "none", got ${listText}`).toContain(
      'none',
    );
    expect(reqs.length, 'an empty taint-list still listed REQ ids').toBe(0);
  }

  // SOFT: cross-check against the per-type segment-facts wording. The trigger
  // counts REQs across every type, so the per-type figures should sum to it.
  const types = await suffixes(page, 'type-tab-');
  let summed = 0;
  let parsedAny = false;
  for (const type of types) {
    const facts = await textOf(page, `segment-facts-${type}`);
    const m = facts.match(/(\d+)\s+excluded/i);
    if (m) {
      parsedAny = true;
      summed += Number(m[1]);
      console.log(`BRANCH: REQ-UI-021 segment-facts-${type} excluded=${m[1]} (${JSON.stringify(facts)})`);
    } else {
      console.log(`BRANCH: REQ-UI-021 segment-facts-${type} unparseable: ${JSON.stringify(facts)}`);
    }
  }
  if (parsedAny && declared !== null && summed !== declared) {
    console.log(
      `BRANCH: REQ-UI-021 SOFT MISMATCH — segment-facts sum to ${summed} excluded REQs, taint-trigger says ${declared}`,
    );
  } else if (parsedAny) {
    console.log(`BRANCH: REQ-UI-021 segment-facts sum (${summed}) agrees with taint-trigger (${declared})`);
  }
});

test('REQ-UI-022 Late-gate lines report ran beside caught, never a share as a catch rate', async ({
  page,
}) => {
  await gotoScreen(page, '/three-questions');
  await (await testid(page, 'type-tabs')).waitFor({ state: 'attached', timeout: 20_000 });

  expect(
    await page.locator('[data-testid^="late-gate-"]').count(),
    'no late-gate-* element on the page',
  ).toBeGreaterThan(0);

  const types = await suffixes(page, 'type-tab-');
  let lines = 0;

  for (const type of types) {
    await activateType(page, type);
    await page.locator(`[data-testid="late-gate-${type}"]`).first().waitFor({ state: 'attached', timeout: 20_000 });

    // Per-gate lines only: `late-gate-rate-*` sits under a different prefix.
    const sel = `[data-testid^="late-gate-${type}-"]`;
    const texts = await page.$$eval(sel, els =>
      els.map(e => ((e as HTMLElement).innerText || '').replace(/\s+/g, ' ').trim()),
    );

    if (texts.length === 0) {
      const card = await textOf(page, `late-gate-${type}`);
      console.log(`BRANCH: REQ-UI-022 ${type} has no late gate — card reads ${JSON.stringify(card)}`);
      expect(card.toLowerCase(), `late-gate-${type} neither lists a gate nor says none applies`).toContain(
        'no late-added gate',
      );
      continue;
    }

    for (const text of texts) {
      lines++;
      console.log(`BRANCH: REQ-UI-022 ${type} line = ${JSON.stringify(text)}`);
      const ran = /ran on \d+ records, caught \d+/.test(text);
      const insufficient = /insufficient data \(n=\d+\)/.test(text);
      const notRun = /not yet run on this data \(gate added \d{4}-\d{2}-\d{2}\)/.test(text);
      expect(
        ran || insufficient || notRun,
        `late-gate line matches none of the three permitted forms: ${text}`,
      ).toBe(true);

      // CRITICAL: a distribution share must never stand in for a catch rate.
      if (/ran on/.test(text)) {
        expect(text, `a late-gate line states "ran on" without "caught": ${text}`).toContain('caught');
      }
    }
  }

  console.log(`BRANCH: REQ-UI-022 checked ${lines} late-gate line(s) across ${types.length} type(s)`);
});
