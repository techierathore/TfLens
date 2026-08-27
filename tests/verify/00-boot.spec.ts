import { test, expect } from '@playwright/test';
import { signIn, gotoScreen } from './_helpers';

test('boot: anonymous /login renders and the demo user can sign in', async ({ page }) => {
  await page.goto('/login');
  await expect(page.locator('[data-testid="login-email"]')).toBeVisible();
  await signIn(page);
  await gotoScreen(page, '/');
  expect(await page.locator('[data-testid="app-sidebar"]').count()).toBeGreaterThan(0);
});
