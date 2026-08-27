// §4a DevGuide render sweep + §4b visual-truth gate.
// Control map comes from docs/TfLens-DevGuide-Screens.md "Control -> data path" tables.
// Black-box: touches no application source.
import { test } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { signIn, gotoScreen, renderCheck, tableCheck, visualCheck, collectErrors, DESKTOP, MOBILE } from './_helpers';

const OUT = path.resolve(process.cwd(), 'tests/.artifacts/gates');
fs.mkdirSync(OUT, { recursive: true });

type ScreenResult = {
  screen: string;
  route: string;
  reachable: boolean;
  controls: { id: string; verdict: string; detail: string }[];
  visual: { width: number; problems: string[]; shot: string }[];
  consoleErrors: string[];
};

const results: ScreenResult[] = [];

/** testids that share a table's prefix but are not tables (notes, bar strips, empty states). */
const NOT_A_TABLE = /-(note|unlisted|bars|empty|trigger|count)(-|$)/;

function write() {
  fs.writeFileSync(path.join(OUT, 'render-visual.json'), JSON.stringify(results, null, 2));
}

/** Run the render gate over a screen's control set, then the visual gate at two widths. */
async function sweep(
  page: any,
  screen: string,
  route: string,
  opts: {
    required: string[];              // must render
    tables?: string[];               // must have rows + non-blank cells
    prefixes?: string[];             // dynamic testid prefixes: at least one must exist and render
    tablePrefixes?: string[];        // dynamic tables
    conditional?: string[];          // recorded, never failed on absence
    conditionalPrefixes?: string[];  // dynamic ids that are conditional by design
    before?: (p: any) => Promise<void>;
  },
) {
  const errors = collectErrors(page);
  const res: ScreenResult = { screen, route, reachable: true, controls: [], visual: [], consoleErrors: [] };

  try {
    await page.setViewportSize(DESKTOP);
    await page.goto(route, { waitUntil: 'domcontentloaded' });
    if (opts.before) await opts.before(page);
    await page.waitForTimeout(2500);
  } catch (e) {
    res.reachable = false;
    res.controls.push({ id: route, verdict: 'UNREACHABLE', detail: String(e).slice(0, 160) });
    results.push(res); write();
    return res;
  }

  const seen: string[] = [];

  for (const id of opts.required) {
    const r = await renderCheck(page, id);
    res.controls.push(r);
    if (r.verdict === 'RENDERS') seen.push(id);
  }
  for (const id of opts.tables ?? []) {
    const r = await tableCheck(page, id);
    res.controls.push(r);
    if (r.verdict === 'RENDERS') seen.push(id);
  }
  for (const pfx of opts.prefixes ?? []) {
    const ids: string[] = await page.$$eval(`[data-testid^="${pfx}"]`, (els: Element[]) =>
      els.map(e => e.getAttribute('data-testid') || '').filter(Boolean));
    if (ids.length === 0) {
      res.controls.push({ id: `${pfx}*`, verdict: 'RENDER-EMPTY', detail: 'no element with this prefix exists' });
      continue;
    }
    for (const id of Array.from(new Set(ids)).slice(0, 12)) {
      const r = await renderCheck(page, id);
      res.controls.push(r);
      if (r.verdict === 'RENDERS') seen.push(id);
    }
  }
  for (const pfx of opts.tablePrefixes ?? []) {
    const all: string[] = await page.$$eval(`[data-testid^="${pfx}"]`, (els: Element[]) =>
      els.map(e => e.getAttribute('data-testid') || '').filter(Boolean));
    // Siblings that share a table's testid prefix but are notes, bar strips or empty states.
    const ids = all.filter(id => !NOT_A_TABLE.test(id));
    if (ids.length === 0) {
      res.controls.push({ id: `${pfx}*`, verdict: 'RENDER-EMPTY', detail: 'no table with this prefix exists' });
      continue;
    }
    for (const id of Array.from(new Set(ids)).slice(0, 12)) {
      const r = await tableCheck(page, id);
      res.controls.push(r);
      if (r.verdict === 'RENDERS') seen.push(id);
    }
  }
  for (const pfx of opts.conditionalPrefixes ?? []) {
    const ids: string[] = await page.$$eval(`[data-testid^="${pfx}"]`, (els: Element[]) =>
      els.map(e => e.getAttribute('data-testid') || '').filter(Boolean));
    if (ids.length === 0) {
      res.controls.push({ id: `${pfx}*`, verdict: 'RENDERS', detail: 'conditional: absent (state not reached)' });
      continue;
    }
    for (const id of Array.from(new Set(ids))) {
      const r = await renderCheck(page, id);
      res.controls.push(r);
      if (r.verdict === 'RENDERS') seen.push(id);
    }
  }
  for (const id of opts.conditional ?? []) {
    const n = await page.locator(`[data-testid="${id}"]`).count();
    res.controls.push({ id, verdict: n > 0 ? 'RENDERS' : 'RENDERS', detail: n > 0 ? 'conditional: present' : 'conditional: absent (state not reached)' });
    if (n > 0) seen.push(id);
  }

  const slug = screen.replace(/[^a-z0-9]+/gi, '-').replace(/^-|-$/g, '').toLowerCase();
  for (const vp of [DESKTOP, MOBILE]) {
    await page.setViewportSize(vp);
    await page.waitForTimeout(900);
    const problems = await visualCheck(page, seen, vp.width);
    const shot = path.join(OUT, `${slug}-${vp.width}.png`);
    // Viewport capture, not fullPage: the shell is a fixed-height flex layout whose page pane
    // scrolls internally, so a fullPage stitch produces a tall mostly-empty canvas with the
    // header repeated — legible evidence matters more than capturing the scrollback.
    await page.screenshot({ path: shot, fullPage: false }).catch(() => {});
    res.visual.push({ width: vp.width, problems, shot: path.relative(process.cwd(), shot) });
  }
  await page.setViewportSize(DESKTOP);

  res.consoleErrors = errors.filter(e => !/favicon|DevTools|blazor.*reconnect/i.test(e)).slice(0, 8);
  results.push(res); write();
  return res;
}

test.describe.configure({ mode: 'serial' });
// The sweep walks 11 screens x 2 viewports in one test; the default 60s ceiling is for
// single-assertion tests, not a whole-app gate pass.
test.setTimeout(900_000);

test('gate sweep: anonymous screens', async ({ page }) => {
  await sweep(page, 'login', '/login', {
    required: ['auth-brand-panel', 'login-email', 'login-pass', 'login-submit', 'login-register-link'],
    conditional: ['login-pass-toggle', 'login-error', 'login-reset-done'],
  });
  await sweep(page, 'register', '/register', {
    required: ['reg-first', 'reg-last', 'reg-email', 'reg-pass', 'password-strength', 'reg-confirm', 'reg-manager-note', 'reg-submit'],
    conditional: ['reg-email-error', 'reg-pass-error', 'reg-confirm-error', 'reg-error'],
  });
  await sweep(page, 'forgot-password', '/forgot-password', {
    required: ['forgot-email', 'forgot-submit', 'forgot-back'],
    conditional: ['forgot-sent'],
  });
  await sweep(page, 'reset-password', '/reset-password?token=verifyprobe0000', {
    required: [],
    prefixes: ['reset-'],
    conditional: ['reset-pass', 'reset-confirm', 'reset-submit', 'reset-invalid', 'reset-request-new', 'reset-done', 'reset-error'],
  });
});

test('gate sweep: authenticated screens', async ({ page }) => {
  await signIn(page);

  const shell = ['app-sidebar', 'nav-repos', 'nav-coverage', 'nav-three-questions', 'nav-harness',
    'nav-routing', 'nav-export', 'nav-repo-count', 'sync-now', 'last-sync-badge', 'theme-toggle', 'user-menu'];

  await sweep(page, 'profile', '/profile', {
    required: [...shell, 'profile-identity-note', 'profile-name', 'profile-email', 'profile-role-badge',
      'pw-current', 'pw-new', 'pw-confirm', 'pw-submit'],
    tables: ['profile-values'],
  });

  await sweep(page, 'repos', '/repos', {
    required: [...shell, 'connect-repo', 'kpi-repos', 'kpi-records', 'kpi-last-sync'],
    tables: ['repos-table'],
    prefixes: ['repo-sync-', 'repo-remove-', 'repo-status-'],
    conditional: ['repos-empty', 'repos-empty-connect'],
  });

  await sweep(page, 'coverage', '/', {
    required: [...shell, 'framework-switch', 'framework-count-techieflow', 'framework-count-playbook',
      'coverage-parser', 'coverage-status', 'kpi-repos-synced', 'kpi-gate-records', 'kpi-newest-age',
      'kpi-sync-errors', 'unknown-fields', 'rebuild-card', 'rebuild'],
    prefixes: ['repo-card-', 'repo-sha-', 'repo-state-'],
    tablePrefixes: ['repo-streams-'],
    conditional: ['unknown-fields-trigger', 'unknown-fields-none', 'schema-version-alert', 'coverage-empty', 'coverage-error'],
  });

  await sweep(page, 'three-questions', '/three-questions', {
    required: [...shell, 'framework-switch', 'schema-note', 'type-tabs', 'taint-list'],
    // `gate-dist-note-*` / `gate-dist-unlisted-*` share the `gate-dist-` prefix but are notes,
    // not tables — they are graded as controls (see NOT_A_TABLE below).
    prefixes: ['type-tab-', 'kpi-first-pass-', 'kpi-escape-', 'kpi-failures-', 'segment-facts-',
      'late-gate-'],
    tablePrefixes: ['gate-dist-'],
    // Both `gate-dist-` notes are guarded and absent by design most of the time, so neither can be a
    // required control:
    //   `gate-dist-unlisted-{type}` renders only when a failure names a gate outside GateOrder.
    //   `gate-dist-note-{type}`     renders only when a provenance has too few failures to state a
    //                               distribution — `@if (vLive.GateDistributionNote is not null || …)`.
    // The note was previously listed as required, which passed only because the dataset happened to
    // trigger it. Against the owner's real repositories no provenance is short of failures, so it
    // correctly did not render and the gate reported a RENDER-EMPTY for a control working as designed
    // (REQ-UI-020; the DevGuide already documents both as conditional).
    conditionalPrefixes: ['gate-dist-unlisted-', 'gate-dist-note-'],
    conditional: ['taint-trigger'],
  });

  await sweep(page, 'harness', '/harness', {
    required: [...shell, 'framework-switch', 'harness-note', 'harness-col-claude-code', 'harness-col-opencode',
      'harness-col-codex', 'opencode-cost', 'opencode-cost-note'],
    tables: ['tokens-table'],
    tablePrefixes: ['harness-table-'],
    conditional: ['harness-null-footnote', 'tokens-chart', 'opencode-cost-value', 'opencode-cost-basis'],
  });

  // Routing is tabbed: sweep each tab's panel.
  for (const tab of ['drift', 'models', 'repricing', 'poolable']) {
    const map: Record<string, any> = {
      drift: {
        required: ['kpi-routing-fields', 'kpi-unrouted', 'kpi-distinct-models'],
        tables: [], conditional: ['drift-empty'], tablePrefixes: ['drift-table'],
      },
      // `model-tokens` must be an exact table id, not a prefix: `model-tokens-bars` and
      // `model-tokens-empty` share the prefix and are not tables.
      models: { required: [], tables: ['model-tokens'], conditional: ['model-tokens-bars', 'model-tokens-empty'] },
      repricing: {
        required: ['repricing-actual', 'repricing-actual-value', 'repricing-actual-estimate',
          'repricing-max', 'repricing-max-value', 'repricing-max-estimate',
          'repricing-delta', 'repricing-delta-value', 'repricing-delta-estimate', 'edit-prices'],
        conditional: ['missing-prices', 'repricing-actual-excluded', 'repricing-max-excluded', 'repricing-delta-share'],
      },
      poolable: {
        required: ['pooled-rework', 'pooled-batch', 'pooled-throughput', 'pooled-tokens-per-verified', 'pooled-commit-cadence'],
        conditional: ['pooled-rework-value', 'pooled-batch-value', 'pooled-throughput-value'],
      },
    };
    const cfg = map[tab];
    await sweep(page, `routing-${tab}`, '/routing', {
      required: [...(tab === 'drift' ? shell : []), 'routing-tab-drift', 'routing-tab-models',
        'routing-tab-repricing', 'routing-tab-poolable', ...cfg.required],
      tables: cfg.tables ?? [],
      tablePrefixes: cfg.tablePrefixes ?? [],
      conditional: [...(cfg.conditional ?? []), 'routing-error'],
      before: async (p: any) => {
        const trig = p.locator(`[data-testid="routing-tab-${tab}"]`).first();
        await trig.waitFor({ state: 'attached', timeout: 20_000 });
        const target = p.locator(`[data-testid="routing-tab-${tab}"]`).first();
        await target.click({ force: true }).catch(async () => {
          await p.evaluate((t: string) => {
            const el = document.querySelector(`[data-testid="routing-tab-${t}"]`) as HTMLElement | null;
            (el?.closest('[role="tab"]') as HTMLElement | null)?.click();
          }, tab);
        });
        await p.waitForTimeout(1800);
      },
    });
  }

  await sweep(page, 'export', '/export', {
    required: [...shell, 'framework-switch', 'export-parser-version', 'quotable-banner', 'export-now',
      'export-target', 'dataset-shas', 'snapshots'],
    // Both tables have a documented Empty counterpart; a table that is absent because its
    // Empty state is showing is correct behaviour, so they are graded conditionally and the
    // Empty element is what proves the state was reached.
    tables: ['dataset-shas-table'],
    conditional: ['snapshots-table', 'export-framework-note', 'export-facts', 'dataset-shas-empty', 'snapshots-empty',
      'parity-record', 'parity-facts', 'parity-none', 'parity-output'],
  });
});

test('gate sweep: playbook axis of the five report pages', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/');
  // Select the Playbook trigger inside the header framework switch.
  await page.evaluate(() => {
    const sw = document.querySelector('[data-testid="framework-switch"]');
    if (!sw) return;
    const trig = Array.from(sw.querySelectorAll('[role="tab"], button')).find(
      e => /playbook/i.test((e as HTMLElement).innerText || ''));
    (trig as HTMLElement | undefined)?.click();
  });
  await page.waitForTimeout(2500);

  for (const [name, route] of [['pb-coverage', '/'], ['pb-three-questions', '/three-questions'],
    ['pb-harness', '/harness'], ['pb-routing', '/routing'], ['pb-export', '/export']] as [string, string][]) {
    await sweep(page, name, route, {
      required: ['app-sidebar', 'framework-switch'],
      prefixes: [],
      conditional: ['playbook-empty', 'pb-phases-techieflow', 'coverage-status', 'schema-note',
        'harness-note', 'routing-tab-drift', 'quotable-banner'],
    });
  }

  // Restore the TechieFlow axis so nothing downstream inherits Playbook.
  await page.goto('/');
  await page.evaluate(() => {
    const sw = document.querySelector('[data-testid="framework-switch"]');
    if (!sw) return;
    const trig = Array.from(sw.querySelectorAll('[role="tab"], button')).find(
      e => /techieflow/i.test((e as HTMLElement).innerText || ''));
    (trig as HTMLElement | undefined)?.click();
  });
  await page.waitForTimeout(1500);
  write();
});
