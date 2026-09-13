import { test, expect, Page } from '@playwright/test';
import { signIn, gotoScreen, USER1, USER2 } from './_helpers';

/**
 * REQ-UI-072 — the Price providers screen: when each provider's rates were last checked, the endpoint
 * provider labelled apart from the typed ones, and OpenRouter refreshed from its own endpoint.
 * (This file's header used to name REQ-FN-139; that row is the not-money cost figure, and the
 * acceptance exercised here is REQ-UI-072's.)
 *
 * The first test only reads, as USER1, and holds whatever state the book is in — refreshed or not.
 * The second presses OpenRouter's Refresh, which rewrites the shared price book, so it runs as USER2.
 * It reaches the real network, which is the point: a refresh button that has only ever been tested
 * against a canned response has not been tested. It is skipped rather than failed when the network is
 * unavailable — "we could not look" is not the same finding as "the rates were wrong".
 */
const TYPED = 'typed from the published page';
const ENDPOINT = 'read from its endpoint';
const tid = (page: Page, id: string) => page.locator(`[data-testid="${id}"]`);
const todayUtc = () => new Date().toISOString().slice(0, 10);

async function providerIds(page: Page): Promise<string[]> {
  return page.$$eval('[data-testid^="prices-provider-"]', els =>
    els.map(e => (e.getAttribute('data-testid') || '').replace(/^prices-provider-/, '')).filter(Boolean));
}

test('REQ-UI-072 — every provider shows when its rates were last checked, and endpoint and typed providers are labelled differently', async ({ page }) => {
  await signIn(page, USER1);
  await gotoScreen(page, '/prices');

  const ids = await providerIds(page);
  for (const id of ['anthropic', 'openai', 'opencode-go', 'openrouter']) expect(ids, `provider ${id} must be listed`).toContain(id);

  const kinds: Record<string, string> = {};
  for (const id of ids) {
    const kind = ((await tid(page, `prices-kind-${id}`).innerText()) || '').trim();
    const checked = ((await tid(page, `prices-checked-${id}`).innerText()) || '').replace(/\s+/g, ' ').trim();
    const card = ((await tid(page, `prices-provider-${id}`).innerText()) || '').replace(/\s+/g, ' ');
    const holdsRates = (await tid(page, `prices-table-${id}`).locator('tbody tr').count()) > 0;
    kinds[id] = kind;
    console.log(`${id}: "${kind}" · "${checked}" · ${holdsRates ? 'holds rates' : 'no rates'}`);

    // Each provider is one of the two claims, and says which.
    expect([ENDPOINT, TYPED], `${id}: kind badge`).toContain(kind);
    const refresh = tid(page, `prices-refresh-${id}`);
    if (kind === ENDPOINT) {
      await expect(refresh, `${id}: an endpoint provider offers Refresh`).toBeVisible();
      expect(card, `${id}: labelled both ways`).not.toContain(TYPED);
    } else {
      await expect(refresh, `${id}: a typed provider has no endpoint to refresh from`).toHaveCount(0);
      expect(card, `${id}: labelled both ways`).not.toContain(ENDPOINT);
    }

    // When it was last checked: a real date no later than today, or "never checked".
    const m = /^last checked (\d{4}-\d{2}-\d{2})$/.exec(checked);
    if (m) {
      expect(Number.isNaN(Date.parse(`${m[1]}T00:00:00Z`)), `${id}: "${checked}" is not a date`).toBe(false);
      expect(m[1] <= todayUtc(), `${id}: last checked ${m[1]} is in the future`).toBe(true);
    } else {
      expect(checked, `${id}: last-checked badge`).toBe('never checked');
      // "Never checked" is only true of a provider nobody has looked at. An endpoint provider holding
      // rates has read them from its endpoint at some point, so it must carry that date.
      if (kind === ENDPOINT) {
        expect(holdsRates, `${id}: holds rates read from its endpoint yet reads "never checked"`).toBe(false);
        expect(card, `${id}: an endpoint provider with no rates says how to get them`).toContain('No rates yet');
      }
    }
  }

  // The two claims look different on the page, not only in their wording.
  const endpointId = ids.find(id => kinds[id] === ENDPOINT);
  const typedId = ids.find(id => kinds[id] === TYPED);
  expect(endpointId, 'no provider is labelled as read from its endpoint').toBeTruthy();
  expect(typedId, 'no provider is labelled as typed from its published page').toBeTruthy();
  const look = (id: string) =>
    tid(page, `prices-kind-${id}`).evaluate(e => {
      const s = getComputedStyle(e);
      return `${s.backgroundColor} | ${s.color} | ${s.borderColor}`;
    });
  const [endpointLook, typedLook] = [await look(endpointId!), await look(typedId!)];
  console.log(`badge look — endpoint: ${endpointLook}; typed: ${typedLook}`);
  expect(endpointLook, 'the endpoint and typed badges are drawn alike').not.toBe(typedLook);
});

test('REQ-UI-072 — OpenRouter rates are refreshed from its own endpoint and stamped with the day they were checked', async ({ page }) => {
  const reachable = await fetch('https://openrouter.ai/api/v1/models', {
    signal: AbortSignal.timeout(15_000),
  })
    .then(r => r.ok)
    .catch(() => false);

  test.skip(!reachable, 'openrouter.ai is not reachable from here');

  await signIn(page, USER2);
  await gotoScreen(page, '/prices');

  // Whatever it read before — never checked, or an earlier date — a refresh must replace it with today.
  const before = ((await tid(page, 'prices-checked-openrouter').innerText()) || '').trim();
  expect(before, 'last-checked badge before the refresh').toMatch(/^(never checked|last checked \d{4}-\d{2}-\d{2})$/);
  console.log(`prices-checked-openrouter before refresh: "${before}"`);

  await tid(page, 'prices-refresh-openrouter').click();

  // The message says how many rates were read, and the provider's own "last checked" is today — a
  // rate nobody has checked and a rate checked today are different claims.
  const message = tid(page, 'prices-message');
  await expect(message).toContainText('read from its endpoint', { timeout: 60_000 });
  console.log(`prices-message: "${((await message.innerText()) || '').trim()}"`);
  await expect(tid(page, 'prices-checked-openrouter')).toHaveText(`last checked ${todayUtc()}`);
  await expect(tid(page, 'prices-kind-openrouter')).toHaveText(ENDPOINT);
  await expect(tid(page, 'prices-table-openrouter')).toBeVisible();
});
