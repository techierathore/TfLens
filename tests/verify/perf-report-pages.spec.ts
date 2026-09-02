// §4c supplementary measurement for REQ-NFR-001.
//
// The shipped harness (.tfcore/utils/tf-perf.sh) sends no cookie, so it can only reach the
// anonymous routes — and REQ-NFR-001's budget ("perf-budget: p95 load <= 1500ms @ concurrency 1")
// is about the REPORT pages, which sit behind the auth gate. This spec measures the pages the
// budget actually names, from an authenticated session, at concurrency 1. It is recorded as
// supplementary evidence beside the harness run, never as a substitute for it.
import { test } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { signIn } from './_helpers';

const ROUTES = ['/', '/gate-outcomes', '/harness', '/routing', '/export'];
const SAMPLES = 12;

test.setTimeout(900_000);

test('perf: report-page load at concurrency 1 (REQ-NFR-001)', async ({ page }) => {
  await signIn(page);
  // Warm every route once so the figures describe steady state, not first-hit JIT + query plan.
  for (const r of ROUTES) { await page.goto(r); await page.waitForLoadState('load'); }

  const out: Record<string, { samples: number[]; p50: number; p95: number; max: number }> = {};
  const pct = (xs: number[], p: number) => {
    const s = [...xs].sort((a, b) => a - b);
    return Math.round(s[Math.min(s.length - 1, Math.floor((p / 100) * s.length))] * 10) / 10;
  };

  for (const route of ROUTES) {
    const samples: number[] = [];
    for (let i = 0; i < SAMPLES; i++) {
      await page.goto('about:blank');
      const t0 = Date.now();
      await page.goto(route);
      await page.waitForLoadState('load');
      samples.push(Date.now() - t0);
    }
    out[route] = { samples, p50: pct(samples, 50), p95: pct(samples, 95), max: Math.max(...samples) };
    console.log(`PERF ${route}: p50=${out[route].p50}ms p95=${out[route].p95}ms max=${out[route].max}ms (n=${SAMPLES})`);
  }

  const all = Object.values(out).flatMap(v => v.samples);
  const overall = { p50: pct(all, 50), p95: pct(all, 95), max: Math.max(...all), samples: all.length };
  console.log(`PERF OVERALL: p50=${overall.p50}ms p95=${overall.p95}ms max=${overall.max}ms n=${overall.samples} budget=1500ms`);

  const dir = path.resolve(process.cwd(), 'tests/.artifacts/perf');
  fs.mkdirSync(dir, { recursive: true });
  fs.writeFileSync(path.join(dir, 'REQ-NFR-001-report-pages.json'),
    JSON.stringify({ budget_ms: 1500, concurrency: 1, build_config: 'Release', per_route: out, overall }, null, 2));
});
