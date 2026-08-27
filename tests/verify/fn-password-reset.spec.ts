// REQ-FN-003 / REQ-UI-003 / REQ-UI-004 (BRD-92) — password reset, end to end in a browser.
//
// Black-box: every assertion is made through the browser against the running app at `baseURL`.
// Nothing here writes to the app. `/forgot-password` is submitted twice, which asks AppManager to
// send a reset mail — for the demo account that is the documented, idempotent behaviour of the
// feature under test, and for the invalid address it cannot reach anyone.
import { test, expect, Page } from '@playwright/test';
import { visualCheck, collectErrors, DESKTOP, MOBILE, USER1 } from './_helpers';

// A token distinctive enough that a leak anywhere — DOM, URL, log — is unmistakable.
const CANARY = 'rst-CANARY-9f3ac71e-do-not-log';

// An address that does not exist, and must be indistinguishable from one that does.
const UNKNOWN = 'nobody.at.all@techierathore.invalid';

// The one sentence every unusable link produces, whatever AppManager's reason was.
const DEAD_LINK = /invalid or has expired/i;

// AppManager error codes and scoping codes that must never reach the browser.
const APP_MANAGER_CODES = /INVALID_RESET_TOKEN|APP_ID_MISMATCH|APPLICATION_ID_REQUIRED|INVALID_API_KEY/;

test.describe.configure({ timeout: 120_000 });

/**
 * Opens an anonymous auth screen and waits until it is genuinely interactive.
 *
 * Waiting for the anchor alone is not enough and quietly produces a false pass: the control is in the
 * DOM from the prerender, seconds before the circuit is up, so a click lands on markup with no handler
 * attached and simply does nothing. These pages submit on the circuit, so every interaction here has to
 * wait for `window.Blazor` and let the connection settle first.
 */
async function gotoAnonymous(page: Page, route: string, anchorTestId: string): Promise<void> {
  await page.goto(route);
  await page.locator(`[data-testid="${anchorTestId}"]`).first().waitFor({ state: 'visible', timeout: 30_000 });
  await page.waitForFunction(() => 'Blazor' in window, null, { timeout: 30_000 });
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(1_500);
}

/** Submits one address on /forgot-password and returns the outcome the browser can observe. */
async function submitForgot(page: Page, email: string) {
  await gotoAnonymous(page, '/forgot-password', 'forgot-email');
  await page.fill('[data-testid="forgot-email"]', email);
  await page.click('[data-testid="forgot-submit"]');
  await page.locator('[data-testid="forgot-sent"]').waitFor({ state: 'visible', timeout: 45_000 });

  return {
    // The whole card, normalised: wording, structure and control set all in one comparison.
    card: (await page.locator('[data-testid="forgot-sent"]').innerText()).replace(/\s+/g, ' ').trim(),
    // The form must be gone, not merely covered: a surviving form is a second submit path.
    formCount: await page.locator('[data-testid="forgot-email"]').count(),
    url: new URL(page.url()).pathname + new URL(page.url()).search,
    controls: await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid]'))
        .map(el => el.getAttribute('data-testid'))
        .sort()
        .join(','),
    ),
  };
}

// ─────────────────────────────────────────────────────────────────────────────
// Enumeration safety — the acceptance's first clause.
// ─────────────────────────────────────────────────────────────────────────────

test('REQ-FN-003 /forgot-password answers identically for a known and an unknown address', async ({ page }) => {
  const errors = collectErrors(page);

  const known = await submitForgot(page, USER1.email);
  const unknown = await submitForgot(page, UNKNOWN);

  expect(unknown.card, 'the wording must not differ').toBe(known.card);
  expect(unknown.controls, 'the control set must not differ').toBe(known.controls);
  expect(unknown.formCount, 'the form must be replaced for both').toBe(known.formCount);
  expect(unknown.url, 'the URL must not differ').toBe(known.url);

  expect(known.card).toMatch(/if that address exists/i);
  expect(known.formCount).toBe(0);

  // Neither address may appear anywhere in the resulting page.
  const html = await page.content();
  expect(html).not.toContain(USER1.email);
  expect(html).not.toContain(UNKNOWN);
  expect(html).not.toMatch(APP_MANAGER_CODES);

  expect(errors.filter(e => !/favicon|blazor.*reconnect/i.test(e))).toEqual([]);
});

// ─────────────────────────────────────────────────────────────────────────────
// The reset token never appears anywhere the user or an operator can read it.
// ─────────────────────────────────────────────────────────────────────────────

test('REQ-FN-003 /reset-password never echoes its token into the DOM', async ({ page }) => {
  await gotoAnonymous(page, `/reset-password?token=${CANARY}`, 'reset-pass');

  // The rendered source, the serialised DOM and every attribute value — the token is in the query
  // string and must stay there. An interactive Blazor form with no `action` of its own is rendered
  // carrying the request URL, which is exactly how this leaked before.
  expect(await page.content(), 'page source').not.toContain(CANARY);
  expect(
    await page.evaluate(() => document.documentElement.outerHTML),
    'serialised DOM',
  ).not.toContain(CANARY);

  const attributes = await page.evaluate(() =>
    Array.from(document.querySelectorAll('*'))
      .flatMap(el => Array.from(el.attributes).map(a => `${a.name}=${a.value}`))
      .join('\n'),
  );
  expect(attributes, 'no attribute may carry the token').not.toContain(CANARY);
});

// ─────────────────────────────────────────────────────────────────────────────
// Both dead-link reasons surface as one outcome — the acceptance's second clause.
// ─────────────────────────────────────────────────────────────────────────────

test('REQ-FN-003 every unusable reset link produces the same "invalid or expired" outcome', async ({ page }) => {
  // The endpoint collapses INVALID_RESET_TOKEN and APP_ID_MISMATCH onto one reason word before the
  // browser ever sees them, so `error=expired` is the single observable both codes arrive as.
  const outcomes: string[] = [];

  for (const route of [
    `/reset-password?token=${CANARY}&error=expired`,
    '/reset-password',
    '/reset-password?token=',
  ]) {
    await gotoAnonymous(page, route, 'reset-invalid');
    const text = (await page.locator('[data-testid="reset-invalid"]').innerText()).replace(/\s+/g, ' ').trim();

    expect(text, route).toMatch(DEAD_LINK);
    expect(await page.content(), route).not.toMatch(APP_MANAGER_CODES);
    expect(await page.locator('[data-testid="reset-request-new"]').isVisible()).toBe(true);
    expect(await page.locator('[data-testid="reset-pass"]').count(), 'no form on a dead link').toBe(0);

    outcomes.push(text);
  }

  expect(new Set(outcomes).size, `one outcome only, saw: ${JSON.stringify(outcomes)}`).toBe(1);
});

// ─────────────────────────────────────────────────────────────────────────────
// Visual truth — §4b, both viewports, both screens.
// ─────────────────────────────────────────────────────────────────────────────

const FORGOT_IDS = ['forgot-email', 'forgot-submit', 'forgot-back'];
const RESET_IDS = ['reset-pass', 'reset-confirm', 'reset-submit', 'reset-back'];

for (const [label, viewport] of [['1280', DESKTOP], ['390', MOBILE]] as const) {
  test(`REQ-UI-003 /forgot-password renders clean at ${label}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await gotoAnonymous(page, '/forgot-password', 'forgot-email');

    expect(await visualCheck(page, FORGOT_IDS, viewport.width)).toEqual([]);
    await page.screenshot({ path: `tests/.artifacts/forgot-password-${label}.png`, fullPage: true });
  });

  test(`REQ-UI-004 /reset-password renders clean at ${label}`, async ({ page }) => {
    await page.setViewportSize(viewport);
    await gotoAnonymous(page, `/reset-password?token=${CANARY}`, 'reset-pass');

    expect(await visualCheck(page, RESET_IDS, viewport.width)).toEqual([]);
    await page.screenshot({ path: `tests/.artifacts/reset-password-${label}.png`, fullPage: true });
  });
}
