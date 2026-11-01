import { test, expect } from '@playwright/test';
import { signIn, gotoScreen, DESKTOP } from './_helpers';

// REQ-UI-076 (owner UAT, 2026-11-01): on /repos the "Kind" column showed "techieflow" — the framework.
// The framework now has its own "Framework" column, and "Kind" holds the codebase type the repository
// declares (project_type): Application, Library, Framework or Docs — never the framework name.

const KINDS = ['Application', 'Library', 'Framework', 'Docs', '—'];

test('REQ-UI-076 Repos grid shows Framework and Kind as separate columns', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/repos');
  const table = page.locator('[data-testid="repos-table"]');
  await expect(table).toBeVisible();

  const heads = (await table.locator('thead th').allTextContents()).map(h => h.trim());
  expect(heads, 'header row').toContain('Framework');
  expect(heads, 'header row').toContain('Kind');

  const frameworks = await page.locator('[data-testid^="repo-framework-"]').allTextContents();
  const kinds = await page.locator('[data-testid^="repo-kind-"]').allTextContents();
  expect(frameworks.length, 'one framework badge per row').toBeGreaterThan(0);
  expect(kinds.length, 'one kind badge per row').toBe(frameworks.length);

  for (const f of frameworks) expect(['TechieFlow', 'Playbook']).toContain(f.trim());
  for (const k of kinds) {
    expect(KINDS, `Kind "${k.trim()}" is a codebase type`).toContain(k.trim());
    expect(k.trim().toLowerCase(), 'Kind never repeats the framework').not.toMatch(/^(techieflow|playbook)$/);
  }
  // The demo account's repos declare project types in their records, so at least one Kind is not blank.
  expect(kinds.some(k => k.trim() !== '—'), 'a declared Kind is shown').toBe(true);
});
