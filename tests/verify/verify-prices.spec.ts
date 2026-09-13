import { test, expect, Page } from '@playwright/test';
import { signIn as helperSignIn, gotoScreen, visualCheck, USER1, USER2, DESKTOP, MOBILE } from './_helpers';

/**
 * REQ-UI-072 / REQ-FN-142 — the /prices screen, black box.
 *
 * The price book is SHARED across users, not per-user: a provider added while signed in as USER2 is
 * seen by USER1 on their own /prices, and the page itself says a change rewrites the one rate card
 * (data/prices.json) every figure is priced from. So every test that changes anything touches ONLY
 * clearly named probe providers (`verify-probe`, `verify-probe-2`) and removes them in a `finally`.
 * The four real providers — Anthropic, OpenAI, OpenCode Go, OpenRouter — are never refreshed,
 * removed or given a rate here, and each mutating test asserts their cards read exactly as they did
 * before it started.
 */
test.use({ viewport: DESKTOP });
// Room for one retried sign-in (up to ~75s) on top of the work itself.
test.beforeEach(({}, testInfo) => testInfo.setTimeout(Math.max(testInfo.timeout, 180_000)));

const REAL = ['anthropic', 'openai', 'opencode-go', 'openrouter'];
const PROBE = 'verify-probe';
const PROBE2 = 'verify-probe-2';
const PROBE_MODEL = 'verify-probe-model';
const TYPED = 'typed from the published page';
const ENDPOINT = 'read from its endpoint';

/**
 * Sign-in is setup, not the thing under test. The shared helper occasionally fills the login form
 * before the Blazor circuit is interactive; the re-render then wipes the fields and the submit goes
 * nowhere (seen twice in this file's runs, on different tests). One retry from a settled page.
 */
async function signIn(page: Page, user = USER1) {
  try {
    await helperSignIn(page, user);
  } catch (e) {
    console.log(`sign-in retry for ${user.email}: ${String(e).split('\n')[0]}`);
    await page.goto('/login');
    await page.waitForLoadState('networkidle').catch(() => {});
    await page.waitForTimeout(1500);
    await helperSignIn(page, user);
  }
}

const tid = (page: Page, id: string) => page.locator(`[data-testid="${id}"]`);

async function providerIds(page: Page): Promise<string[]> {
  return page.$$eval('[data-testid^="prices-provider-"]', els =>
    els.map(e => (e.getAttribute('data-testid') || '').replace(/^prices-provider-/, '')).filter(Boolean));
}

type RateRow = { model: string; cells: string[] };

async function rateRows(page: Page, id: string): Promise<RateRow[]> {
  if ((await tid(page, `prices-table-${id}`).count()) === 0) return [];
  return tid(page, `prices-table-${id}`).evaluate((t: Element) =>
    Array.from(t.querySelectorAll('tbody tr')).map(tr => {
      const tds = Array.from(tr.querySelectorAll('td')).map(td => (td.textContent || '').trim());
      return { model: tds[0] ?? '', cells: tds.slice(1) };
    }));
}

/**
 * A provider's rate table is paged five rates at a time (docs/mockups/prices.html). When it runs past
 * one page — or a filter narrows it — the card's footer says so: "5 of 10 rates shown · page 1 of 2",
 * "5 of 12 rates shown (filtered from 434) · page 1 of 3". A provider whose rates all fit on one page
 * prints no count at all, as the mockup draws OpenAI.
 */
const SHOWN = /(\d[\d,]*) of (\d[\d,]*) rates? shown(?: \(filtered from (\d[\d,]*)\))?(?: · page (\d+) of (\d+))?/;
const num = (s: string | undefined) => Number((s ?? '').replace(/,/g, ''));

type PagerLine = { shown: number; of: number; filteredFrom: number | null; page: number; pages: number };

async function pagerLine(page: Page, id: string): Promise<PagerLine | null> {
  const text = ((await tid(page, `prices-provider-${id}`).innerText()) || '').replace(/\s+/g, ' ');
  const m = SHOWN.exec(text);
  if (!m) return null;
  return {
    shown: num(m[1]),
    of: num(m[2]),
    filteredFrom: m[3] ? num(m[3]) : null,
    page: m[4] ? Number(m[4]) : 1,
    pages: m[5] ? Number(m[5]) : 1,
  };
}

/**
 * Every rate a provider holds, read page by page through its own Next button, together with the total
 * its footer states (the M of "N of M rates shown"; the rows themselves when everything fits on one
 * page and no count is printed). Each page is checked to show at most five rates and to say how many
 * it shows. Must be called on a freshly loaded /prices, where every provider is on page 1, unfiltered.
 */
async function allRateRows(page: Page, id: string): Promise<{ rows: RateRow[]; total: number; pages: number }> {
  const first = await rateRows(page, id);
  const line = await pagerLine(page, id);
  if (!line) {
    // No count printed: the whole list is on one page, and there is no pager to turn.
    expect(first.length, `${id}: ${first.length} rates on screen with no "N of M rates shown" line`).toBeLessThanOrEqual(5);
    await expect(tid(page, `prices-page-next-${id}`), `${id}: a pager with nothing to page`).toHaveCount(0);
    return { rows: first, total: first.length, pages: 1 };
  }
  expect(line.filteredFrom, `${id}: a fresh /prices must not be filtered`).toBeNull();
  expect(line.page, `${id}: a fresh /prices opens on page 1`).toBe(1);
  expect(line.pages, `${id}: page count for ${line.of} rates, five a page`).toBe(Math.max(1, Math.ceil(line.of / 5)));

  const card = tid(page, `prices-provider-${id}`);
  const rows = [...first];
  let onPage = first.length;
  expect(onPage, `${id} page 1: the footer says ${line.shown} shown`).toBe(line.shown);
  for (let p = 2; p <= line.pages; p++) {
    await tid(page, `prices-page-next-${id}`).click();
    await expect(card).toContainText(new RegExp(`rates? shown · page ${p} of ${line.pages}\\b`));
    const next = await rateRows(page, id);
    const now = await pagerLine(page, id);
    onPage = next.length;
    expect(onPage, `${id} page ${p}: at most five rates a page`).toBeLessThanOrEqual(5);
    expect(onPage, `${id} page ${p}: the footer says ${now?.shown} shown`).toBe(now?.shown);
    rows.push(...next);
  }
  if (line.pages > 1) {
    await expect(tid(page, `prices-page-next-${id}`), `${id}: Next on the last page`).toBeDisabled();
  }
  return { rows, total: line.of, pages: line.pages };
}

/** The visible text of each real provider card — what "left untouched" is checked against. */
async function realFingerprint(page: Page): Promise<Record<string, string>> {
  const out: Record<string, string> = {};
  for (const id of REAL) {
    out[id] = ((await tid(page, `prices-provider-${id}`).innerText().catch(() => 'ABSENT')) || '')
      .replace(/\s+/g, ' ').trim();
  }
  return out;
}

async function addProvider(page: Page, name: string, source: string, api = '') {
  await tid(page, 'prices-new-name').fill(name);
  await tid(page, 'prices-new-source').fill(source);
  await tid(page, 'prices-new-api').fill(api);
  await tid(page, 'prices-add').click();
  await expect(tid(page, `prices-provider-${name}`)).toBeVisible();
}

async function saveRate(page: Page, provider: string, model: string, input: string, output: string) {
  await tid(page, 'prices-rate-provider').selectOption(provider);
  await tid(page, 'prices-rate-model').fill(model);
  await tid(page, 'prices-rate-in').fill(input);
  await tid(page, 'prices-rate-out').fill(output);
  await tid(page, 'prices-save-rate').click();
  await expect(tid(page, `prices-table-${provider}`)).toContainText(model);
}

/** Remove every probe provider still present. Never touches a real provider. */
async function removeProbes(page: Page) {
  await gotoScreen(page, '/prices');
  for (const id of [PROBE2, PROBE]) {
    const btn = tid(page, `prices-remove-${id}`);
    if ((await btn.count()) > 0) {
      await btn.click();
      await expect(tid(page, `prices-provider-${id}`)).toHaveCount(0);
    }
  }
}

/* ─────────────────────────── REQ-UI-072 ─────────────────────────── */

test('REQ-UI-072 — every provider shows its rates, source page and last-checked date', async ({ page }) => {
  await signIn(page, USER1);
  await gotoScreen(page, '/prices');

  const ids = await providerIds(page);
  console.log(`providers on /prices: ${ids.join(', ')}`);
  for (const id of REAL) expect(ids, `provider ${id} must be listed`).toContain(id);

  let totalRates = 0;
  const perProvider: Record<string, number> = {};
  for (const id of ids) {
    const card = tid(page, `prices-provider-${id}`);
    await expect(card).toBeVisible();

    // The page a person can check the rates against.
    const link = card.locator('a[href^="http"]').first();
    await expect(link, `${id}: must link the page its rates can be checked against`).toBeVisible();
    const href = (await link.getAttribute('href')) || '';
    expect(href, `${id}: source page link`).toMatch(/^https?:\/\/\S+/);

    // When the rates were last checked — a date, or an explicit "never".
    const checked = ((await tid(page, `prices-checked-${id}`).innerText()) || '').trim();
    expect(checked, `${id}: last-checked badge`).toMatch(/^(never checked|last checked \d{4}-\d{2}-\d{2})/);

    // Its rates, every page of them: every row names a model and four dollar rates. A negative or
    // blank rate (an endpoint's -1 coerced into the table) is exactly what this must never show.
    const { rows, total, pages } = await allRateRows(page, id);
    const cardText = (await card.innerText()).replace(/\s+/g, ' ');
    console.log(`${id}: ${rows.length} rate(s) read over ${pages} page(s), footer total ${total} · source ${href} · "${checked}"`);
    if (rows.length === 0) {
      // A provider with no rates must say so, and must not be read as priced at zero.
      expect(cardText, `${id}: no rates, and the card must say so`).toContain('No rates yet');
      expect(cardText, `${id}: no rates must never be read as zero`).toContain('never priced at zero');
    }
    for (const r of rows) {
      expect(r.model, `${id}: every rate row names a model`).not.toBe('');
      expect(r.cells.length, `${id} ${r.model}: input, output, cache read, cache write`).toBe(4);
      for (const c of r.cells) expect(c, `${id} ${r.model}: rate cell`).toMatch(/^\$\d+(\.\d+)?$/);
    }
    // The footer's "of M" is the rates the pages actually hold — each once, none lost between pages.
    expect(rows.length, `${id}: rows read across every page vs the footer's total`).toBe(total);
    expect(new Set(rows.map(r => r.model)).size, `${id}: a model listed on two pages`).toBe(rows.length);
    perProvider[id] = total;
    totalRates += total;
  }

  // The three providers typed from a published page are seeded with rates; the book is not empty.
  for (const id of ['anthropic', 'openai', 'opencode-go']) {
    expect(perProvider[id], `${id} must carry at least one rate`).toBeGreaterThan(0);
  }

  // The header badge: "447 rates over 4 providers" — the sum of every provider's own total.
  const count = (await tid(page, 'prices-model-count').innerText()).replace(/\s+/g, ' ').trim();
  console.log(`prices-model-count = "${count}" · per-provider totals = ${JSON.stringify(perProvider)} · sum = ${totalRates}`);
  const m = /^(\d[\d,]*) (rates?) over (\d+) (providers?)$/.exec(count);
  expect(m, `the rate count badge reads "${count}", not "N rates over N providers"`).not.toBeNull();
  expect(num(m![1]), 'the rate count badge agrees with the per-provider "of M rates" totals').toBe(totalRates);
  expect(Number(m![3]), 'the rate count badge counts every provider listed').toBe(ids.length);
  expect(m![2], 'singular only for one rate').toBe(totalRates === 1 ? 'rate' : 'rates');
  expect(m![4], 'singular only for one provider').toBe(ids.length === 1 ? 'provider' : 'providers');
});

test('REQ-UI-072 — a provider refreshed from its endpoint is labelled differently from one typed from its published page', async ({ page }) => {
  await signIn(page, USER1);
  await gotoScreen(page, '/prices');

  // OpenRouter publishes its rates as data; it is the endpoint provider. Its Refresh is never pressed.
  const or = (await tid(page, 'prices-provider-openrouter').innerText()).replace(/\s+/g, ' ');
  expect(or, 'OpenRouter is labelled as read from its endpoint').toContain(ENDPOINT);
  expect(or, 'OpenRouter is not also labelled as typed').not.toContain(TYPED);
  await expect(tid(page, 'prices-refresh-openrouter'), 'an endpoint provider offers Refresh').toBeVisible();

  for (const id of ['anthropic', 'openai', 'opencode-go']) {
    const t = (await tid(page, `prices-provider-${id}`).innerText()).replace(/\s+/g, ' ');
    expect(t, `${id} is labelled as typed from its published page`).toContain(TYPED);
    expect(t, `${id} is not labelled as read from an endpoint`).not.toContain(ENDPOINT);
    await expect(tid(page, `prices-refresh-${id}`), `${id} has no endpoint, so no Refresh`).toHaveCount(0);
  }
});

test('REQ-UI-072 — a user can add a provider and a rate, change the rate, and remove the provider', async ({ page }) => {
  await signIn(page, USER2);
  await gotoScreen(page, '/prices');
  await removeProbes(page); // a previous aborted run must not leave a probe behind
  const before = await realFingerprint(page);

  try {
    await addProvider(page, PROBE, 'https://example.invalid/verify-probe');
    const card = tid(page, `prices-provider-${PROBE}`);
    await expect(card).toContainText('https://example.invalid/verify-probe');
    await expect(card).toContainText(TYPED);
    await expect(card, 'a new provider prices nothing until it has a rate').toContainText('No rates yet');
    await expect(tid(page, 'prices-message')).toContainText(`${PROBE} added`);

    await saveRate(page, PROBE, PROBE_MODEL, '2', '8');
    let rows = (await rateRows(page, PROBE)).filter(r => r.model === PROBE_MODEL);
    console.log(`after add: ${JSON.stringify(rows)}`);
    expect(rows.length).toBe(1);
    expect(rows[0].cells.slice(0, 2)).toEqual(['$2', '$8']);

    // Changing the rate replaces it, never adds a second row for the same model.
    await saveRate(page, PROBE, PROBE_MODEL, '3', '9');
    await expect(tid(page, `prices-table-${PROBE}`)).toContainText('$9');
    rows = (await rateRows(page, PROBE)).filter(r => r.model === PROBE_MODEL);
    console.log(`after change: ${JSON.stringify(rows)}`);
    expect(rows.length, 'one row per model after a change').toBe(1);
    expect(rows[0].cells.slice(0, 2)).toEqual(['$3', '$9']);

    // It survives a reload — the change was stored, not just drawn.
    await gotoScreen(page, '/prices');
    rows = (await rateRows(page, PROBE)).filter(r => r.model === PROBE_MODEL);
    expect(rows.length, 'rate persists across a reload').toBe(1);
    expect(rows[0].cells.slice(0, 2)).toEqual(['$3', '$9']);

    await tid(page, `prices-remove-${PROBE}`).click();
    await expect(tid(page, `prices-provider-${PROBE}`)).toHaveCount(0);
    await expect(tid(page, 'prices-message')).toContainText(`${PROBE} removed`);
    const opts = await page.$$eval('[data-testid="prices-rate-provider"] option', o => o.map(x => (x as HTMLOptionElement).value));
    expect(opts, 'a removed provider can no longer be given a rate').not.toContain(PROBE);
  } finally {
    await removeProbes(page);
  }

  expect(await realFingerprint(page), 'the four real providers are exactly as they were').toEqual(before);
});

test('REQ-UI-072 — two providers pricing the same model raise the clash notice, never an average', async ({ page }) => {
  await signIn(page, USER2);
  await gotoScreen(page, '/prices');
  await removeProbes(page);
  const before = await realFingerprint(page);
  await expect(tid(page, 'prices-clashes'), 'no clash before the probe').toHaveCount(0);

  try {
    await addProvider(page, PROBE, 'https://example.invalid/verify-probe');
    await saveRate(page, PROBE, PROBE_MODEL, '2', '8');
    await addProvider(page, PROBE2, 'https://example.invalid/verify-probe-2');
    await saveRate(page, PROBE2, PROBE_MODEL, '4', '16');

    const clash = tid(page, 'prices-clashes');
    await expect(clash, 'two providers pricing one model must raise the clash notice').toBeVisible();
    const text = (await clash.innerText()).replace(/\s+/g, ' ');
    console.log(`prices-clashes = "${text}"`);
    expect(text).toContain(PROBE_MODEL);
    expect(text.toLowerCase(), 'the notice says the clash is never averaged').toContain('never averaged');
    // The mean of $2/$8 and $4/$16 is $3/$12 — a number nobody publishes. It must appear nowhere.
    expect(text).not.toMatch(/\$3\b|\$12\b/);

    // Each provider still shows its own published rate, unblended.
    const a = (await rateRows(page, PROBE)).find(r => r.model === PROBE_MODEL);
    const b = (await rateRows(page, PROBE2)).find(r => r.model === PROBE_MODEL);
    expect(a?.cells.slice(0, 2)).toEqual(['$2', '$8']);
    expect(b?.cells.slice(0, 2)).toEqual(['$4', '$16']);

    // The clash is a stored fact, not a transient message.
    await gotoScreen(page, '/prices');
    await expect(tid(page, 'prices-clashes'), 'clash still reported after a reload').toContainText(PROBE_MODEL);

    // Take the duplicate away and the notice goes with it.
    await tid(page, `prices-remove-${PROBE2}`).click();
    await expect(tid(page, `prices-provider-${PROBE2}`)).toHaveCount(0);
    await expect(tid(page, 'prices-clashes'), 'no clash once one provider owns the model').toHaveCount(0);
  } finally {
    await removeProbes(page);
  }

  await expect(tid(page, 'prices-clashes')).toHaveCount(0);
  expect(await realFingerprint(page), 'the four real providers are exactly as they were').toEqual(before);
});

test('REQ-UI-072 — a refresh that fails leaves the stored rates unchanged and says so', async ({ page }) => {
  await signIn(page, USER2);
  await gotoScreen(page, '/prices');
  await removeProbes(page);
  const before = await realFingerprint(page);

  try {
    // A probe provider with an endpoint that cannot resolve (.invalid is reserved and never resolves).
    await addProvider(page, PROBE, 'https://example.invalid/verify-probe', 'https://example.invalid/verify-probe/models.json');
    await expect(tid(page, `prices-provider-${PROBE}`), 'a provider given an endpoint is labelled as such').toContainText(ENDPOINT);
    await saveRate(page, PROBE, PROBE_MODEL, '2', '8');
    const rowsBefore = await rateRows(page, PROBE);

    await tid(page, `prices-refresh-${PROBE}`).click();
    const msg = tid(page, 'prices-message');
    await expect(msg).toContainText('unchanged', { timeout: 60_000 });
    console.log(`prices-message after failed refresh = "${(await msg.innerText()).trim()}"`);
    expect(await rateRows(page, PROBE), 'stored rates unchanged by a failed refresh').toEqual(rowsBefore);
  } finally {
    await removeProbes(page);
  }

  expect(await realFingerprint(page), 'the four real providers are exactly as they were').toEqual(before);
});

/* ─────────────────────────── REQ-FN-142 ─────────────────────────── */

/**
 * "Superseded", read from the page alone: a model id of the form `<family>-<n>[-<n>…]` (an 8-digit
 * snapshot-date suffix ignored) is superseded when the same family carries a higher version on
 * /prices — claude-opus-4-8 by claude-opus-5, claude-fable-5 by claude-fable-5-1.
 */
function supersededModels(models: string[]): Map<string, string> {
  const parse = (m: string) => {
    const base = m.replace(/-\d{8}$/, '');
    const g = /^(.*[a-z])-(\d+(?:-\d+)*)$/.exec(base);
    return g ? { family: g[1], ver: g[2].split('-').map(Number) } : null;
  };
  const cmp = (a: number[], b: number[]) => {
    for (let i = 0; i < Math.max(a.length, b.length); i++) {
      const d = (a[i] ?? -1) - (b[i] ?? -1);
      if (d !== 0) return d;
    }
    return 0;
  };
  const out = new Map<string, string>();
  for (const m of models) {
    const p = parse(m);
    if (!p) continue;
    for (const n of models) {
      const q = parse(n);
      if (q && q.family === p.family && cmp(q.ver, p.ver) > 0) { out.set(m, n); break; }
    }
  }
  return out;
}

/** Every rate on /prices, every page of every provider (the tables show five at a time). */
async function allRates(page: Page): Promise<Map<string, { provider: string; cells: string[] }>> {
  const out = new Map<string, { provider: string; cells: string[] }>();
  for (const id of await providerIds(page)) {
    for (const r of (await allRateRows(page, id)).rows) if (!out.has(r.model)) out.set(r.model, { provider: id, cells: r.cells });
  }
  return out;
}

test('REQ-FN-142 — a superseded model on /prices still carries its rate', async ({ page }) => {
  await signIn(page, USER1);
  await gotoScreen(page, '/prices');

  const rates = await allRates(page);
  const sup = supersededModels([...rates.keys()]);
  console.log(`superseded on /prices: ${[...sup].map(([o, n]) => `${o} (by ${n})`).join(', ') || '(none)'}`);
  test.skip(sup.size === 0, 'no model on /prices has a newer version of itself listed, so no superseded model can be observed');

  for (const [old, by] of sup) {
    const r = rates.get(old)!;
    expect(r.cells.length, `${old} (superseded by ${by}) keeps a full rate row`).toBe(4);
    const [input, output] = r.cells;
    expect(input, `${old}: input rate`).toMatch(/^\$\d+(\.\d+)?$/);
    expect(output, `${old}: output rate`).toMatch(/^\$\d+(\.\d+)?$/);
    expect(parseFloat(output.slice(1)), `${old}: a superseded model is never priced at zero`).toBeGreaterThan(0);
  }
});

test('REQ-FN-142 — runs naming a superseded model are priced in list_usd, not counted in list_usd_unpriced_n', async ({ page }) => {
  test.setTimeout(180_000);
  await signIn(page, USER1);
  await gotoScreen(page, '/prices');
  const sup = supersededModels([...(await allRates(page)).keys()]);

  // A fresh snapshot, so the figures are priced from the rate card as it stands now.
  await gotoScreen(page, '/export');
  const exportNow = tid(page, 'export-now');
  await expect(exportNow).toBeEnabled({ timeout: 20_000 });
  await exportNow.click();
  await page.waitForTimeout(1000);
  await expect(exportNow).toBeEnabled({ timeout: 120_000 });
  await gotoScreen(page, '/export');

  const links = page.locator('[data-testid^="snapshot-json-"]');
  await expect(links.first(), 'the Past-snapshots table links a tflens.json').toBeVisible({ timeout: 30_000 });
  const testids = await links.evaluateAll(els => els.map(e => e.getAttribute('data-testid') || ''));
  const newest = testids.map(t => t.replace('snapshot-json-', '').slice(0, 10)).sort().reverse()[0];
  const today = testids.filter(t => t.startsWith(`snapshot-json-${newest}`));
  console.log(`snapshot links for ${newest}: ${today.join(', ')}`);

  const seen = new Set<string>();
  const snapshots: any[] = [];
  for (const t of today) {
    const href = (await tid(page, t).getAttribute('href')) || '';
    expect(href).toContain('/api/export/download');
    const res = await page.request.get(href);
    expect(res.status(), `${href} downloads`).toBe(200);
    const json = await res.json();
    snapshots.push(json);
    for (const p of Object.values<any>(json?.phases?.phases ?? {})) for (const m of Object.keys(p?.models ?? {})) seen.add(m);
    for (const r of json?.extras?.routing?.tokens_by_model ?? []) if (r?.model) seen.add(r.model);
    for (const m of Object.keys(json?.misses?.by_origin_model ?? {})) seen.add(m);
  }
  const named = [...seen].filter(m => sup.has(m));
  console.log(`models in the snapshot's per-model figures: ${[...seen].join(', ')}`);
  console.log(`of those, superseded on /prices: ${named.join(', ') || '(none)'}`);
  test.skip(named.length === 0,
    `no superseded model appears in the data — the snapshot names only ${[...seen].join(', ')}, all current on /prices`);

  for (const json of snapshots) {
    const missing: string[] = json?.extras?.repricing?.missing_price_models ?? [];
    for (const m of named) expect(missing, `${m} must not be reported as an unpriced model`).not.toContain(m);
    for (const [phase, p] of Object.entries<any>(json?.phases?.phases ?? {})) {
      for (const m of named) {
        const f = p?.models?.[m];
        if (!f || !(f.tokens_out > 0)) continue;
        expect(typeof f.list_usd, `${phase} · ${m}: priced in list_usd`).toBe('number');
        expect(f.list_usd, `${phase} · ${m}: list_usd above zero`).toBeGreaterThan(0);
      }
    }
  }
});

/* ─────────────────────────── visual ─────────────────────────── */

test('REQ-UI-072 — /prices is clean at 1280 and 390', async ({ page }) => {
  await signIn(page, USER1);
  await gotoScreen(page, '/prices');

  const ids = Array.from(new Set(await page.$$eval('[data-testid^="prices-"]', els =>
    els.map(e => e.getAttribute('data-testid') || '').filter(Boolean))));
  console.log(`measuring ${ids.length} controls`);
  expect(ids.length).toBeGreaterThan(10);

  const problems: string[] = [];
  for (const vp of [DESKTOP, MOBILE]) {
    await page.setViewportSize(vp);
    await page.waitForTimeout(900);
    const p = await visualCheck(page, ids, vp.width);
    console.log(`@${vp.width}: ${p.length ? p.join(' | ') : 'clean'}`);
    problems.push(...p);
  }
  await page.setViewportSize(DESKTOP);
  expect(problems, 'no overlap, zero-size or horizontal scroll on /prices').toEqual([]);
});
