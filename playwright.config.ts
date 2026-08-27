import { defineConfig } from '@playwright/test';

export default defineConfig({
  testDir: './tests/verify',
  outputDir: './tests/.artifacts/test-results',
  reporter: 'line',
  timeout: 60_000,
  expect: { timeout: 15_000 },
  fullyParallel: false,
  workers: 1,
  use: {
    headless: true,
    baseURL: process.env.TFLENS_BASE_URL ?? 'http://localhost:5099',
    screenshot: 'only-on-failure',
    trace: 'retain-on-failure',
  },
});
