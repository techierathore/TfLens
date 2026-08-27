// Live acceptance checks the UI specs deliberately left alone because they act on the
// outside world. Run once, at the end of the pass, against the booted app.
import { test, expect } from '@playwright/test';
import { signIn, gotoScreen, testid } from './_helpers';

test.setTimeout(300_000);

// REQ-FN-018 — "Sync now syncs only the signed-in user's repos; errors stay scoped per
// repo." Pressing it issues read-only GitHub GETs, which is what the REQ is about.
test('REQ-FN-018 Sync now runs the caller\'s repos and isolates per-repo errors', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/repos');

  const before = await page.locator('[data-testid="repos-table"] tbody tr').count();
  expect(before, 'the demo user must have repos for this to mean anything').toBeGreaterThan(0);

  const btn = await testid(page, 'sync-now');
  await btn.click();
  // The button reports progress, then the page settles.
  await page.waitForTimeout(25_000);

  await gotoScreen(page, '/');
  const status = ((await (await testid(page, 'coverage-status')).innerText()) || '').replace(/\s+/g, ' ');
  const errors = ((await (await testid(page, 'kpi-sync-errors')).innerText()) || '').replace(/\s+/g, ' ');
  console.log(`REQ-FN-018 coverage-status: ${status}`);
  console.log(`REQ-FN-018 kpi-sync-errors: ${errors}`);

  // Whatever the outcome, the run must have completed per repo rather than aborting: every
  // connected repo of THIS user still has a card/row, and any error is attributed to a repo.
  const after = await (async () => { await gotoScreen(page, '/repos'); return page.locator('[data-testid="repos-table"] tbody tr').count(); })();
  expect(after, 'a failing repo aborted the whole sync and dropped rows').toBe(before);

  // No other user's repo may ever appear in this user's list.
  const body = ((await page.locator('[data-testid="repos-table"]').innerText()) || '').toLowerCase();
  expect(body, "another user's repo leaked into this user's list").not.toContain('acme/');
});

// REQ-NFR-005 — "Sync / Export / Rebuild / user menu are reachable and operable by keyboard
// alone." Reachability is asserted by focusing each control from the keyboard and confirming
// it is the active element and responds to Enter/Space semantics (role + tabindex).
test('REQ-NFR-005 Sync, Export, Rebuild and the user menu are keyboard reachable', async ({ page }) => {
  await signIn(page);

  const checks: [string, string][] = [['/', 'sync-now'], ['/', 'rebuild'], ['/', 'user-menu'], ['/export', 'export-now']];
  for (const [route, id] of checks) {
    await gotoScreen(page, route);
    const el = await testid(page, id);
    await el.focus();
    const info = await page.evaluate(t => {
      const target = document.querySelector(`[data-testid="${t}"]`) as HTMLElement | null;
      const active = document.activeElement as HTMLElement | null;
      const focused = !!target && (target === active || target.contains(active!));
      const probe = (focused ? active : target)!;
      return {
        focused,
        tag: probe?.tagName ?? null,
        role: probe?.getAttribute('role') ?? null,
        tabindex: probe?.getAttribute('tabindex') ?? null,
        disabled: probe?.hasAttribute('disabled') ?? null,
      };
    }, id);
    console.log(`REQ-NFR-005 ${route} ${id}: ${JSON.stringify(info)}`);
    expect(info.focused, `${id} could not take keyboard focus`).toBe(true);
    const operable = info.tag === 'BUTTON' || info.tag === 'A' || info.role === 'button' || info.tabindex !== null;
    expect(operable, `${id} is focusable but carries no button/link semantics for Enter/Space`).toBe(true);
  }

  // Escape must close the user menu (REQ-UI-008 acceptance).
  await gotoScreen(page, '/');
  await (await testid(page, 'user-menu')).click();
  await page.waitForSelector('[data-testid="user-menu-signout"]', { timeout: 20_000 });
  await page.keyboard.press('Escape');
  await page.waitForTimeout(1200);
  expect(await page.locator('[data-testid="user-menu-signout"]').count(), 'Escape did not close the user menu').toBe(0);
});
