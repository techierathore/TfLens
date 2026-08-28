// REQ-UI-001 .. REQ-UI-013 — auth screens and the app shell.
//
// Black-box: every assertion is made through the browser against the running app at `baseURL`.
// Nothing here writes to the app: no account is registered, no password is changed, no repo is
// connected or removed. The destructive controls (connect-submit, remove-confirm, sync-now) are
// asserted on presence/enabled state only and are never pressed.
import { test, expect, Page } from '@playwright/test';
import { signIn, gotoScreen, testid, renderCheck, tableCheck, USER1, DESKTOP } from './_helpers';

test.use({ viewport: DESKTOP });

// Blazor Server + a real AppManager round trip + a real GitHub read: the default 60s is not enough for
// the multi-route tests, so every test in this file gets a generous budget.
test.describe.configure({ timeout: 180_000 });

/** AppManager error codes that must never reach the browser. */
const APP_MANAGER_CODES = /INVALID_CREDENTIALS|DECRYPTION_FAILED|NO_APP_ACCESS/;

/** Opens an anonymous auth screen and waits for Blazor to render the control the test needs. */
async function gotoAnonymous(page: Page, route: string, anchorTestId: string): Promise<void> {
  await page.goto(route);
  await page.locator(`[data-testid="${anchorTestId}"]`).first().waitFor({ state: 'attached', timeout: 20_000 });
}

/** Every `button`/`a` on the page whose visible text or accessible name mentions GitHub. */
async function githubControls(page: Page): Promise<string[]> {
  return await page.evaluate(() =>
    Array.from(document.querySelectorAll('button, a'))
      .map(el => `${(el as HTMLElement).innerText ?? ''} ${el.getAttribute('aria-label') ?? ''} ${el.getAttribute('title') ?? ''}`)
      .filter(text => /github/i.test(text))
      .map(text => text.replace(/\s+/g, ' ').trim()),
  );
}

/** The document order of a list of testids; -1 for any that is absent. */
async function domOrder(page: Page, ids: string[]): Promise<number[]> {
  return await page.evaluate(wanted => {
    const all = Array.from(document.querySelectorAll('*'));
    return wanted.map(id => all.findIndex(el => el.getAttribute('data-testid') === id));
  }, ids);
}

/** The Framework switch's currently selected trigger, plus a DOM dump for a useful failure message. */
async function frameworkSelection(page: Page): Promise<{ label: string; dump: string }> {
  return await page.evaluate(() => {
    const sw = document.querySelector('[data-testid="framework-switch"]');
    if (!sw) return { label: '', dump: 'framework-switch absent' };
    const selected = sw.querySelector(
      '[data-state="active"], [aria-selected="true"], [aria-checked="true"], [data-selected="true"], .active',
    );
    return {
      label: selected ? ((selected as HTMLElement).innerText || selected.textContent || '').replace(/\s+/g, ' ').trim() : '',
      dump: sw.innerHTML.replace(/\s+/g, ' ').slice(0, 900),
    };
  });
}

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-001 — /login, anonymous
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-001 login page renders anonymously, Enter submits, a bad password is reported generically and no GitHub button exists', async ({ page }) => {
  await gotoAnonymous(page, '/login', 'login-email');

  await expect(page.locator('[data-testid="login-email"]')).toBeVisible();
  await expect(page.locator('[data-testid="login-pass"]')).toBeVisible();
  await expect(page.locator('[data-testid="login-submit"]')).toBeVisible();
  await expect(page.locator('[data-testid="login-register-link"]')).toBeVisible();

  // No GitHub / SSO control. The muted "GitHub sign-in: coming in a later release" line is TEXT, not a
  // button or an anchor, so the assertion is scoped to `button, a` only.
  expect(await githubControls(page), 'a GitHub/SSO button or link is present in the DOM').toEqual([]);

  // Enter in the password field must submit the form — no click on login-submit here.
  await page.fill('[data-testid="login-email"]', USER1.email);
  await page.fill('[data-testid="login-pass"]', 'WrongPass!23');
  await page.locator('[data-testid="login-pass"]').press('Enter');

  const error = page.locator('[data-testid="login-error"]');
  await error.waitFor({ state: 'attached', timeout: 30_000 });
  await expect(error).toContainText('Sign-in failed. Check your email and password.', { timeout: 20_000 });

  // Still on /login — the Enter submit did not navigate away.
  expect(new URL(page.url()).pathname).toBe('/login');

  // The entered email is kept.
  await expect(page.locator('[data-testid="login-email"]')).toHaveValue(USER1.email);

  // No AppManager error code ever reaches the browser.
  const bodyText = await page.evaluate(() => document.body.innerText);
  expect(bodyText, 'an AppManager error code leaked into the page').not.toMatch(APP_MANAGER_CODES);
  expect(await page.content(), 'an AppManager error code leaked into the markup').not.toMatch(APP_MANAGER_CODES);
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-002 — /register, anonymous. Never creates an account.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-002 register page renders every field, the strength meter and the Manager note, and enforces the password rules locally', async ({ page }) => {
  const requests: string[] = [];
  page.on('request', r => requests.push(r.method() + ' ' + r.url()));

  await gotoAnonymous(page, '/register', 'reg-email');

  for (const id of ['reg-first', 'reg-last', 'reg-email', 'reg-pass', 'reg-confirm', 'reg-submit']) {
    await expect(page.locator(`[data-testid="${id}"]`), `${id} missing`).toBeVisible();
  }
  await expect(page.locator('[data-testid="password-strength"]')).toBeVisible();

  // BRD-95 — always on screen, never conditional.
  const managerNote = page.locator('[data-testid="reg-manager-note"]');
  await expect(managerNote).toBeVisible();
  await expect(managerNote).toContainText('Manager');

  // The email deliberately belongs to the EXISTING canonical test user, so this can never create an
  // account even if the local rules were absent. The password is weak and the confirmation differs, so
  // the local rules short-circuit before anything leaves the browser.
  await page.fill('[data-testid="reg-first"]', 'Verify');
  await page.fill('[data-testid="reg-last"]', 'Run');
  await page.fill('[data-testid="reg-email"]', USER1.email);
  await page.fill('[data-testid="reg-pass"]', 'abc');
  await page.locator('[data-testid="reg-pass"]').blur();
  await page.fill('[data-testid="reg-confirm"]', 'DifferentPass!23');
  await page.locator('[data-testid="reg-confirm"]').blur();
  await page.waitForTimeout(1000);

  const passError = page.locator('[data-testid="reg-pass-error"]');
  if ((await passError.count()) === 0) {
    // This build surfaces the per-rule errors when the form is submitted rather than on blur. The submit
    // is still purely local: `abc` fails the rules, so no request is made and no account is created.
    await page.click('[data-testid="reg-submit"]');
  }

  await passError.waitFor({ state: 'attached', timeout: 20_000 });
  await expect(passError).not.toHaveText('');

  const confirmError = page.locator('[data-testid="reg-confirm-error"]');
  await confirmError.waitFor({ state: 'attached', timeout: 20_000 });
  await expect(confirmError).toContainText(/differ/i);

  // Nothing reached the AppManager API, and nothing reached the account-creating endpoint either.
  const offending = requests.filter(r => /AuthSvc\/register/i.test(r) || /\/auth\/register/i.test(r));
  expect(offending, `a network request escaped for a locally-predictable violation: ${offending.join(', ')}`).toEqual([]);
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-003 — /forgot-password, anonymous
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-003 forgot-password answers an unknown address with the enumeration-safe wording and replaces the form', async ({ page }) => {
  await gotoAnonymous(page, '/forgot-password', 'forgot-email');

  await expect(page.locator('[data-testid="forgot-email"]')).toBeVisible();
  await expect(page.locator('[data-testid="forgot-submit"]')).toBeVisible();
  await expect(page.locator('[data-testid="forgot-back"]')).toBeVisible();

  await page.fill('[data-testid="forgot-email"]', 'no-such-user-9f2a@example.invalid');
  await page.click('[data-testid="forgot-submit"]');

  const sent = page.locator('[data-testid="forgot-sent"]');
  await sent.waitFor({ state: 'attached', timeout: 30_000 });
  await expect(sent).toContainText('If that address exists', { timeout: 20_000 });

  // The form must be REPLACED, not merely disabled.
  await expect(page.locator('[data-testid="forgot-email"]')).toHaveCount(0, { timeout: 20_000 });
  await expect(page.locator('[data-testid="forgot-submit"]')).toHaveCount(0);
  await expect(page.locator('[data-testid="forgot-back"]')).toBeVisible();
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-004 — /reset-password?token=…, anonymous
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-004 reset-password never echoes the token and enforces the password rules locally', async ({ page }) => {
  const token = 'deadbeefdeadbeef';
  await page.goto(`/reset-password?token=${token}`);
  await page.locator('[data-testid="reset-back"]').first().waitFor({ state: 'attached', timeout: 20_000 });

  // The page may render the form, or treat an unknown token as dead up front. Both are acceptable.
  const formCount = await page.locator('[data-testid="reset-pass"]').count();
  const deadCount = await page.locator('[data-testid="reset-invalid"]').count();
  const branch = formCount > 0 ? 'form' : deadCount > 0 ? 'reset-invalid' : 'neither';
  test.info().annotations.push({ type: 'REQ-UI-004 branch', description: branch });
  expect.soft(branch, 'reset-password rendered neither the form nor reset-invalid').not.toBe('neither');
  expect(formCount + deadCount, 'reset-password rendered neither the form nor reset-invalid').toBeGreaterThan(0);

  // The token must never be visible anywhere, nor sit in any input's value.
  const leak = await page.evaluate(theToken => {
    const bodyText = document.body.innerText || '';
    const inputs = Array.from(document.querySelectorAll('input, textarea')).map(el => (el as HTMLInputElement).value ?? '');
    return {
      inText: bodyText.includes(theToken),
      inInputs: inputs.some(v => v.includes(theToken)),
    };
  }, token);
  expect(leak.inText, 'the reset token appears in the rendered page text').toBe(false);
  expect(leak.inInputs, 'the reset token appears in an input value').toBe(false);

  if (branch === 'form') {
    await expect(page.locator('[data-testid="reset-pass"]')).toBeVisible();
    await expect(page.locator('[data-testid="reset-confirm"]')).toBeVisible();
    await expect(page.locator('[data-testid="reset-submit"]')).toBeVisible();

    // A weak password is refused by the local rules — the token is never posted.
    await page.fill('[data-testid="reset-pass"]', 'abc');
    await page.fill('[data-testid="reset-confirm"]', 'abc');
    await page.click('[data-testid="reset-submit"]');

    const passError = page.locator('[data-testid="reset-pass-error"]');
    await passError.waitFor({ state: 'attached', timeout: 20_000 });
    await expect(passError).not.toHaveText('');

    // Still no token on screen after the refusal.
    const after = await page.evaluate(() => document.body.innerText || '');
    expect(after.includes(token), 'the reset token leaked into the failure state').toBe(false);
  } else {
    await expect(page.locator('[data-testid="reset-invalid"]')).toBeVisible();
  }
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-005 — /profile, signed in. The change-password form is NEVER submitted.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-005 profile renders the AppManager values, the Manager badge and the change-password form', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/profile');

  for (const id of ['pw-current', 'pw-new', 'pw-confirm', 'pw-submit']) {
    await expect(page.locator(`[data-testid="${id}"]`), `${id} missing`).toBeVisible();
  }

  await testid(page, 'profile-values', 20_000);
  const values = await tableCheck(page, 'profile-values');
  expect(values.verdict, `profile-values: ${values.detail}`).toBe('RENDERS');
  expect(values.detail, `profile-values has a blank cell: ${values.detail}`).toMatch(/, 0 blank$/);

  const badge = await testid(page, 'profile-role-badge', 20_000);
  expect(((await badge.innerText()) || '').trim()).toBe('Manager');

  const note = page.locator('[data-testid="profile-identity-note"]');
  await expect(note).toBeVisible();
  await expect(note).toContainText(/stores no passwords/i);
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-006 — the app shell
// ─────────────────────────────────────────────────────────────────────────────
// Amended 2026-08-28 (BRD-5, BRD-124): SEVEN items — Misses & rework sits between Routing and Export.
test('REQ-UI-006 app shell shows the seven nav items in order, a repo-count badge and no /playbook item', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/');

  await expect(page.locator('[data-testid="app-sidebar"]').first()).toBeVisible();

  const ids = ['nav-repos', 'nav-coverage', 'nav-three-questions', 'nav-harness', 'nav-routing', 'nav-misses', 'nav-export'];
  for (const id of ids) {
    expect(await page.locator(`[data-testid="${id}"]`).count(), `${id} missing`).toBeGreaterThan(0);
  }

  const order = await domOrder(page, ids);
  expect(order.some(i => i < 0), `a nav item is missing: ${JSON.stringify(order)}`).toBe(false);
  for (let i = 1; i < order.length; i++) {
    expect(order[i], `${ids[i]} is not after ${ids[i - 1]} in DOM order: ${JSON.stringify(order)}`).toBeGreaterThan(order[i - 1]);
  }

  // BRD-108 — the framework is a header switch, never a nav item.
  expect(await page.locator('a[href*="/playbook"]').count(), 'a /playbook nav item exists').toBe(0);

  const badge = page.locator('[data-testid="nav-repo-count"]').first();
  await badge.waitFor({ state: 'attached', timeout: 20_000 });
  expect((((await badge.textContent()) || '')).trim().length, 'nav-repo-count is empty').toBeGreaterThan(0);
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-007 — header Sync now. Deliberately NOT pressed: it hits the GitHub API.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-007 header Sync now is present and enabled with a relative-time last-sync badge', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/');

  const sync = page.locator('[data-testid="sync-now"]').first();
  await expect(sync).toBeVisible();
  // Presence + enabled state only. Pressing Sync now would call GitHub and mutate sync bookkeeping,
  // so this test never clicks it.
  await expect(sync).toBeEnabled();

  const badge = page.locator('[data-testid="last-sync-badge"]').first();
  await expect(badge).toBeVisible();
  const text = (((await badge.textContent()) || '')).trim();
  expect(text.length, 'last-sync-badge is empty').toBeGreaterThan(0);
  expect(text, `last-sync-badge does not read as a relative time: "${text}"`).toMatch(/ago|never|just now/i);
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-008 — header user menu. Sign out is NEVER clicked.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-008 the user menu is the only sign-out and opens the profile', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/');

  // Counts every VISIBLE element whose own text nodes read exactly "Sign out" — the icon+label span in
  // the menu counts, and so would any stray bare button elsewhere in the shell.
  const countSignOutControls = () =>
    page.evaluate(() =>
      Array.from(document.querySelectorAll('*'))
        .filter(el => {
          const own = Array.from(el.childNodes)
            .filter(n => n.nodeType === 3)
            .map(n => n.textContent || '')
            .join('');
          return /^\s*sign out\s*$/i.test(own);
        })
        .filter(el => (el as HTMLElement).getClientRects().length > 0)
        .map(el => `${el.tagName}[${el.getAttribute('data-testid') ?? ''}]`),
    );

  // With the menu CLOSED there must be no sign-out control anywhere on the page.
  const strayClosed = await countSignOutControls();
  expect(strayClosed, `a sign-out control is visible outside the user menu: ${strayClosed.join(', ')}`).toEqual([]);

  const menu = page.locator('[data-testid="user-menu"]').first();
  await expect(menu).toBeVisible();
  await menu.click();

  for (const id of ['user-menu-name', 'user-menu-email', 'user-menu-profile', 'user-menu-repos', 'user-menu-signout']) {
    await page.locator(`[data-testid="${id}"]`).first().waitFor({ state: 'visible', timeout: 20_000 });
  }

  // Positive control: the same scan finds the menu's own sign-out once the menu is open, which proves
  // the closed-menu count of 0 above is a real result and not a scan that can never match.
  const strayOpen = await countSignOutControls();
  expect(strayOpen.length, 'the sign-out scan found nothing even with the menu open').toBeGreaterThan(0);

  await expect(page.locator('[data-testid="user-menu-email"]').first()).toContainText(USER1.email);

  // Sign out is present but deliberately not clicked.
  await expect(page.locator('[data-testid="user-menu-signout"]').first()).toBeVisible();

  await page.locator('[data-testid="user-menu-profile"]').first().click();
  await page.waitForURL(url => url.pathname === '/profile', { timeout: 30_000 });
  await page.locator('[data-testid="password-card"]').first().waitFor({ state: 'attached', timeout: 20_000 });
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-009 — dark-first theme toggle (ADR-014)
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-009 the theme is dark with no cookie, the toggle is focusable and tflens-theme=light drops the dark class', async ({ page, context }) => {
  await signIn(page);

  // Clear ONLY the theme cookie so the session survives; no cookie must mean dark.
  await context.clearCookies({ name: 'tflens-theme' });
  await gotoScreen(page, '/');

  const cookiesAfterClear = (await context.cookies()).map(c => c.name);
  expect(cookiesAfterClear, 'the theme cookie survived the clear').not.toContain('tflens-theme');

  let htmlClass = await page.evaluate(() => document.documentElement.className);
  expect(htmlClass.split(/\s+/), `<html class="${htmlClass}"> is not dark with no cookie`).toContain('dark');

  // The toggle exists and can take keyboard focus.
  const toggle = page.locator('[data-testid="theme-toggle"]').first();
  await toggle.waitFor({ state: 'visible', timeout: 20_000 });
  const focused = await page.evaluate(() => {
    const el = document.querySelector('[data-testid="theme-toggle"]') as HTMLElement | null;
    if (!el) return false;
    const target =
      (el.matches('button, input, select, textarea, [tabindex]') ? el : el.querySelector<HTMLElement>('button, input, select, textarea, [tabindex]')) ?? el;
    target.focus();
    const active = document.activeElement as HTMLElement | null;
    return !!active && active !== document.body && (active === target || el.contains(active));
  });
  expect(focused, 'theme-toggle cannot receive keyboard focus').toBe(true);

  // An explicit light preference must drop the dark class on the very first byte.
  await context.addCookies([{ name: 'tflens-theme', value: 'light', url: 'http://localhost:5099' }]);
  await gotoScreen(page, '/');
  htmlClass = await page.evaluate(() => document.documentElement.className);
  expect(htmlClass.split(/\s+/), `<html class="${htmlClass}"> is still dark with tflens-theme=light`).not.toContain('dark');

  // Leave the context on the documented default.
  await context.clearCookies({ name: 'tflens-theme' });
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-010 — header Framework switch
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-010 the Framework switch shows on report routes only, carries both counts and persists the Playbook choice', async ({ page }) => {
  await signIn(page);

  for (const route of ['/', '/three-questions', '/harness', '/routing', '/export']) {
    await gotoScreen(page, route);
    expect(await page.locator('[data-testid="framework-switch"]').count(), `framework-switch missing on ${route}`).toBeGreaterThan(0);
  }

  for (const route of ['/profile', '/repos']) {
    await gotoScreen(page, route);
    expect(await page.locator('[data-testid="framework-switch"]').count(), `framework-switch must not render on ${route}`).toBe(0);
  }

  await gotoScreen(page, '/');
  for (const id of ['framework-count-techieflow', 'framework-count-playbook']) {
    const badge = page.locator(`[data-testid="${id}"]`).first();
    await badge.waitFor({ state: 'attached', timeout: 20_000 });
    expect((((await badge.textContent()) || '')).trim().length, `${id} is empty`).toBeGreaterThan(0);
  }

  // Select Playbook, then leave and come back: the choice must still be the selected one.
  const switchRoot = page.locator('[data-testid="framework-switch"]').first();
  const playbookTrigger = switchRoot.locator('button, [role="tab"], [role="radio"]').filter({ hasText: /playbook/i }).first();
  await playbookTrigger.waitFor({ state: 'visible', timeout: 20_000 });
  await playbookTrigger.click();
  await page.waitForTimeout(1500);

  await gotoScreen(page, '/routing');
  let selection = await frameworkSelection(page);
  expect(selection.label, `the Playbook choice did not persist across a navigation. framework-switch: ${selection.dump}`).toMatch(/playbook/i);

  // Restore the default axis so later tests see TechieFlow.
  const techieTrigger = page
    .locator('[data-testid="framework-switch"]')
    .first()
    .locator('button, [role="tab"], [role="radio"]')
    .filter({ hasText: /techieflow/i })
    .first();
  await techieTrigger.click();
  await page.waitForTimeout(1500);
  await gotoScreen(page, '/');
  selection = await frameworkSelection(page);
  expect(selection.label, `the switch did not return to TechieFlow. framework-switch: ${selection.dump}`).toMatch(/techieflow/i);
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-011 — /repos
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-011 repos renders the table, the KPI tiles and per-row sync/remove actions', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/repos');

  await testid(page, 'repos-table', 20_000);
  const table = await tableCheck(page, 'repos-table');
  expect(table.verdict, `repos-table: ${table.detail}`).toBe('RENDERS');
  // The Actions column is icon-only by design (row Sync / Remove buttons carry no text),
  // so exactly one blank cell per row is expected. Anything more is a real blank column.
  const [, rowsStr, , blankStr] = table.detail.match(/^(\d+) rows, (\d+) cells, (\d+) blank$/) ?? [];
  const rows = Number(rowsStr), blanks = Number(blankStr);
  expect(blanks, `repos-table has blank cells beyond the icon-only Actions column: ${table.detail}`)
    .toBeLessThanOrEqual(rows);

  await expect(page.locator('[data-testid="connect-repo"]').first()).toBeVisible();

  for (const id of ['kpi-repos', 'kpi-records', 'kpi-last-sync']) {
    const verdict = await renderCheck(page, id);
    expect(verdict.verdict, `${id}: ${verdict.detail}`).toBe('RENDERS');
    expect(verdict.detail.trim().length, `${id} rendered no text`).toBeGreaterThan(0);
  }

  const removeButtons = page.locator('[data-testid^="repo-remove-"]');
  const syncButtons = page.locator('[data-testid^="repo-sync-"]');
  expect(await removeButtons.count(), 'no per-row repo-remove-* button exists').toBeGreaterThan(0);
  expect(await syncButtons.count(), 'no per-row repo-sync-* button exists').toBeGreaterThan(0);
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-012 — Connect-repo dialog. connect-submit is NEVER pressed.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-012 the connect dialog keeps Connect disabled until validation passes, and a repo with no telemetry path never enables it', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/repos');

  await page.locator('[data-testid="connect-repo"]').first().click();

  for (const id of ['connect-input', 'connect-branch', 'connect-kind', 'connect-validate', 'connect-submit']) {
    await page.locator(`[data-testid="${id}"]`).first().waitFor({ state: 'visible', timeout: 20_000 });
  }

  const submit = page.locator('[data-testid="connect-submit"]').first();
  await expect(submit, 'connect-submit is enabled before any validation').toBeDisabled();

  // octocat/Hello-World is a real public repo that carries no TechieFlow or Playbook telemetry path,
  // so validation must fail and Connect must stay disabled. connect-submit is never pressed.
  const input = page.locator('[data-testid="connect-input"]').first();
  await input.fill('octocat/Hello-World');
  await input.blur();

  const validate = page.locator('[data-testid="connect-validate"]').first();
  await expect(validate).toBeEnabled({ timeout: 20_000 });
  await validate.click();

  await page
    .locator('[data-testid="connect-validation"], [data-testid="connect-rate-limit"]')
    .first()
    .waitFor({ state: 'attached', timeout: 45_000 });

  // The safety property holds in BOTH branches and is asserted first: whatever validation reported,
  // Connect must not become enabled for a repo with no telemetry path.
  await expect(submit, 'connect-submit became enabled for a repo with no telemetry path').toBeDisabled();

  // Which branch rendered depends on the environment, not on the app. GitHub's unauthenticated API
  // allows 60 requests/hour, and validation spends several per repo, so a run that follows a sync (or
  // any other run in the same hour) can legitimately find the quota exhausted. When that happens the
  // app correctly renders `connect-rate-limit` INSTEAD of `connect-validation` — asserting the latter
  // unconditionally turned correct behaviour into a red test. Grade the branch that actually rendered.
  const vRateLimited = (await page.locator('[data-testid="connect-rate-limit"]').count()) > 0;
  if (vRateLimited) {
    const vText = (await page.locator('[data-testid="connect-rate-limit"]').first().textContent()) ?? '';
    console.log(`BRANCH: REQ-UI-012 GitHub rate limit hit — validation not reachable :: ${vText.replace(/\s+/g, ' ').trim()}`);
    expect(vText, 'the rate-limit alert should say so in words').toMatch(/rate limit/i);
  } else {
    await expect(page.locator('[data-testid="connect-validation"]').first()).toBeAttached({ timeout: 20_000 });
  }
  await expect(submit).toBeDisabled();

  // Teardown. REQ-UI-012's acceptance does not name Escape (only REQ-UI-013's does), and
  // this dialog stops honouring Escape once a validation result is on screen — recorded as
  // an observation, not graded here. Cancel is the documented way out and does close it.
  await page.keyboard.press('Escape');
  await page.waitForTimeout(1200);
  const closedByEscape = (await page.locator('[data-testid="connect-input"]').count()) === 0;
  console.log(`BRANCH: REQ-UI-012 Escape closed the dialog after validation = ${closedByEscape}`);
  if (!closedByEscape) {
    await page.getByRole('button', { name: /^cancel$/i }).first().click();
  }
  await expect(page.locator('[data-testid="connect-input"]').first(), 'the connect dialog could not be dismissed at all').toBeHidden({ timeout: 20_000 });
});

// ─────────────────────────────────────────────────────────────────────────────
// REQ-UI-013 — Remove-repo confirmation. remove-confirm is NEVER pressed.
// ─────────────────────────────────────────────────────────────────────────────
test('REQ-UI-013 the remove dialog names the repo and cancelling leaves every row in place', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/repos');

  const rows = page.locator('[data-testid="repos-table"] tbody tr');
  const before = await rows.count();
  expect(before, 'no repo rows to exercise the remove dialog against').toBeGreaterThan(0);

  const removeButton = page.locator('[data-testid^="repo-remove-"]').first();
  const removeTestId = (await removeButton.getAttribute('data-testid')) || '';
  const repoName = removeTestId.replace(/^repo-remove-/, '');
  expect(repoName.length, `could not read the repo name from "${removeTestId}"`).toBeGreaterThan(0);

  await removeButton.click();

  for (const id of ['remove-title', 'remove-description', 'remove-cancel', 'remove-confirm']) {
    await page.locator(`[data-testid="${id}"]`).first().waitFor({ state: 'visible', timeout: 20_000 });
  }

  await expect(page.locator('[data-testid="remove-description"]').first()).toContainText(repoName);
  await expect(page.locator('[data-testid="remove-title"]').first()).toContainText(repoName);

  // Cancel — remove-confirm is destructive and is never pressed.
  await page.locator('[data-testid="remove-cancel"]').first().click();
  await expect(page.locator('[data-testid="remove-confirm"]').first(), 'Cancel did not close the remove dialog').toBeHidden({ timeout: 20_000 });

  await page.waitForTimeout(1000);
  const after = await page.locator('[data-testid="repos-table"] tbody tr').count();
  expect(after, `cancelling the remove dialog changed the row count (${before} -> ${after})`).toBe(before);
});

// REQ-UI-013's acceptance names Escape explicitly: "removal only proceeds through the confirm action
// (Escape/Cancel aborts with no data change)". TrBlazeUI 2.0.0's AlertDialog ships no Escape handling
// of its own (TR-014), so this is guarded by page-owned code that can silently regress — hence a test
// of its own. remove-confirm is NEVER pressed here.
test('REQ-UI-013 Escape dismisses the remove dialog and leaves every row in place', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/repos');

  const rows = page.locator('[data-testid="repos-table"] tbody tr');
  const before = await rows.count();
  expect(before, 'no repo rows to exercise the remove dialog against').toBeGreaterThan(0);

  const removeButton = page.locator('[data-testid^="repo-remove-"]').first();
  const removeTestId = (await removeButton.getAttribute('data-testid')) || '';
  const repoName = removeTestId.replace(/^repo-remove-/, '');

  await removeButton.click();
  await page.locator('[data-testid="remove-title"]').first().waitFor({ state: 'visible', timeout: 20_000 });
  await expect(page.locator('[data-testid="remove-title"]').first()).toContainText(repoName);

  await page.keyboard.press('Escape');

  await expect(
    page.locator('[data-testid="remove-title"]').first(),
    'Escape did not dismiss the remove AlertDialog',
  ).toBeHidden({ timeout: 20_000 });
  await expect(page.locator('[data-testid="remove-confirm"]').first()).toBeHidden({ timeout: 20_000 });

  await page.waitForTimeout(1000);
  const after = await page.locator('[data-testid="repos-table"] tbody tr').count();
  expect(after, `Escape on the remove dialog changed the row count (${before} -> ${after})`).toBe(before);

  // The dialog must still be openable afterwards — a listener torn down by the dismiss would
  // leave the second Escape dead.
  await page.locator('[data-testid^="repo-remove-"]').first().click();
  await page.locator('[data-testid="remove-title"]').first().waitFor({ state: 'visible', timeout: 20_000 });
  await page.keyboard.press('Escape');
  await expect(
    page.locator('[data-testid="remove-title"]').first(),
    'Escape stopped working after the first dismiss',
  ).toBeHidden({ timeout: 20_000 });
});
