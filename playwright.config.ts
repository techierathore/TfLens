import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/verify',
  outputDir: './tests/.artifacts/test-results',
  reporter: 'line',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  // TF_VERIFY_GREP narrows a verify run to the tests carrying the row ids in scope (a scoped *verify).
  ...(process.env.TF_VERIFY_GREP ? { grep: new RegExp(process.env.TF_VERIFY_GREP) } : {}),
  fullyParallel: false,
  workers: 1,
  use: {
    headless: true,
    // BASE_URL is what tf-verify-tests.sh --base sets (TF-031); TFLENS_BASE_URL is kept for hand runs.
    baseURL: process.env.BASE_URL || process.env.TFLENS_BASE_URL || 'http://localhost:5099',
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
});
