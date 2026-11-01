import { test, expect, Page } from '@playwright/test';
import { signIn, gotoScreen, testid, DESKTOP } from './_helpers';

// REQ-UI-077 (owner UAT, 2026-11-01): the report pages had no way to see one repository's figures.
// A repo picker in each report page's title row narrows every figure to the chosen repository, the
// choice carries to the other report pages in the same session, and "All repos" is today's view.
//
// The test leaves the demo user exactly as it found it: All repos, framework TechieFlow.

/** Text of the Gate-outcomes project_type tab strip — every tab label with its record count. */
async function tabStrip(page: Page): Promise<string> {
  return ((await page.locator('[data-testid="type-tabs"]').first().textContent()) || '').replace(/\s+/g, ' ').trim();
}

/** Opens the picker and returns the option texts, closing nothing (the caller picks one). */
async function openPicker(page: Page): Promise<string[]> {
  await (await testid(page, 'repo-picker')).click();
  const options = page.locator('[role="option"]');
  await options.first().waitFor({ state: 'visible', timeout: 10_000 });
  return (await options.allTextContents()).map(t => t.trim());
}

/** Picks one option by its exact text and waits for the page to re-query. */
async function pick(page: Page, text: string) {
  await page.locator('[role="option"]').filter({ hasText: new RegExp(`^\\s*${text.replace(/[.*+?^${}()|[\]\\/]/g, '\\$&')}\\s*$`) })
    .first().click();
  await page.waitForTimeout(1_500);
  await page.waitForFunction(
    () => document.querySelectorAll('main .animate-pulse').length === 0, undefined, { timeout: 30_000 },
  ).catch(() => {});
}

/** Puts the framework switch on TechieFlow if anything left it elsewhere. */
async function ensureTechieFlow(page: Page) {
  const tab = page.locator('[data-testid="framework-switch"] [role="tab"]', { hasText: 'TechieFlow' }).first();
  if ((await tab.getAttribute('aria-selected')) !== 'true') {
    await tab.click();
    await page.waitForTimeout(1_500);
  }
  await expect(tab).toHaveAttribute('aria-selected', 'true');
}

test('REQ-UI-077 — repo picker narrows Gate outcomes, carries to Routing, and All repos restores', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/gate-outcomes');
  await ensureTechieFlow(page);

  const allTabs = await tabStrip(page);
  expect(allTabs, 'gate outcomes has figures for all repos').not.toBe('');
  await expect(page.locator('[data-testid="repo-scope"]'), 'All repos states no single-repo scope').toHaveCount(0);

  const options = await openPicker(page);
  expect(options[0], 'the first option is All repos').toBe('All repos');
  const repos = options.slice(1);
  expect(repos.length, 'the signed-in user has TechieFlow repos to pick').toBeGreaterThan(0);

  // Pick repos until one gives a different tab strip from All repos — a repo holding every record would
  // legitimately read the same, so only a difference proves the figures were re-queried.
  let chosen = '';
  let oneTabs = allTabs;
  for (const repo of repos) {
    if (chosen) await openPicker(page);
    await pick(page, repo);
    chosen = repo;
    oneTabs = await tabStrip(page);
    if (oneTabs !== allTabs) break;
  }
  console.log(`all repos tabs = "${allTabs}" · ${chosen} tabs = "${oneTabs}"`);
  expect(oneTabs, `picking ${chosen} re-computes the gate figures`).not.toBe(allTabs);
  await expect(await testid(page, 'repo-scope'), 'the page says which repo its figures are for').toContainText(chosen);

  // The choice carries to another report page in the same session (in-app navigation keeps the circuit).
  await page.locator('a[href="/routing"], a[href="routing"]').first().click();
  await page.waitForURL(u => u.pathname === '/routing');
  await page.waitForTimeout(1_500);
  await expect(await testid(page, 'repo-picker'), 'Routing shows the carried choice').toContainText(chosen);
  await expect(await testid(page, 'repo-scope'), 'Routing states the carried scope').toContainText(chosen);

  // Back to All repos: the scope line goes and Gate outcomes shows its all-repos figures again.
  await openPicker(page);
  await pick(page, 'All repos');
  await expect(page.locator('[data-testid="repo-scope"]')).toHaveCount(0);

  await page.locator('a[href="/gate-outcomes"], a[href="gate-outcomes"]').first().click();
  await page.waitForURL(u => u.pathname === '/gate-outcomes');
  await page.waitForTimeout(1_500);
  await expect(await testid(page, 'repo-picker')).toContainText('All repos');
  expect(await tabStrip(page), 'All repos is today\'s view, unchanged').toBe(allTabs);

  await ensureTechieFlow(page);
});
