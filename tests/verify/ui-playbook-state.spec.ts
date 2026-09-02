/**
 * REQ-UI-034 — the Playbook framework state of all five report pages.
 *
 * Black-box only. The header Framework switch is driven for real; nothing here reaches into
 * application source or the database.
 *
 * What the requirement fixes, and therefore what these specs assert:
 *   - every report page's Playbook state carries the standing axis note (`playbook-axis-note`);
 *   - each page renders EITHER real Playbook figures OR the Phase-3 `playbook-empty` state with its
 *     Connect-a-Playbook-repo action — never a blank surface and never the TechieFlow surface;
 *   - `/gate-outcomes` renders `pb-phases-{name}` once there is Playbook data;
 *   - no chart, column or table on a Playbook-state page mixes `phase_gate` with TechieFlow `gate`
 *     data — the TechieFlow surfaces are absent from every Playbook-state page;
 *   - switching back to TechieFlow restores the TechieFlow surface on all five (no regression);
 *   - the render gate (rows present AND cells non-empty) and the visual gate (no sibling overlap, no
 *     horizontal page scroll) hold at 1280 and at 390.
 */
import { test } from '@playwright/test';
import {
  DESKTOP,
  MOBILE,
  expect,
  gotoScreen,
  renderCheck,
  signIn,
  tableCheck,
  testid,
  visualCheck,
} from './_helpers';

/** The five report routes, in sidebar order. */
const REPORT_ROUTES = ['/', '/gate-outcomes', '/harness', '/routing', '/export'] as const;

/**
 * The TechieFlow-only surfaces that must NOT appear while the Playbook state is showing.
 * Each is a control that renders TechieFlow `gate` / run data; seeing one on the Playbook axis
 * would mean the two provenance axes had been mixed on one page (SCHEMA.md §11).
 */
const TECHIEFLOW_ONLY: Record<string, string[]> = {
  '/': ['coverage-kpis', 'rebuild-card', 'unknown-fields'],
  '/gate-outcomes': ['type-tabs', 'gate-outcomes-empty', 'taint-trigger'],
  '/harness': ['harness-columns', 'tokens-table', 'opencode-cost'],
  '/routing': ['routing-tabs', 'drift-empty', 'drift-table', 'model-tokens'],
  '/export': [],
};

/** Drives the header Framework switch and waits until the choice has taken. */
async function switchFramework(page: import('@playwright/test').Page, label: 'TechieFlow' | 'Playbook'): Promise<string> {
  const sw = await testid(page, 'framework-switch');
  const trigger = sw.locator('[role="tab"]').filter({ hasText: label }).first();
  if ((await trigger.count()) === 0) {
    await sw.getByText(label, { exact: false }).first().click();
  } else {
    await trigger.click();
  }
  await page.waitForTimeout(2_500);
  return page.evaluate(() => {
    const root = document.querySelector('[data-testid="framework-switch"]');
    if (!root) return '';
    const active = Array.from(root.querySelectorAll('[role="tab"]')).find(
      t => t.getAttribute('aria-selected') === 'true' || t.getAttribute('data-state') === 'active',
    );
    return (active?.textContent || '').trim();
  });
}

/** True when a testid is present in the DOM. */
async function exists(page: import('@playwright/test').Page, id: string): Promise<boolean> {
  return (await page.locator(`[data-testid="${id}"]`).count()) > 0;
}

/** Every data-testid currently in the DOM, minus the shell chrome. */
async function contentTestIds(page: import('@playwright/test').Page): Promise<string[]> {
  return page.evaluate(() => {
    const chrome = /^(app-|nav-|sidebar-|framework-|user-|theme-|breadcrumb-|sync-|login-|toast)/;
    return Array.from(document.querySelectorAll('[data-testid]'))
      .map(e => e.getAttribute('data-testid') || '')
      .filter(id => id.length > 0 && !chrome.test(id));
  });
}

test.describe('REQ-UI-034 — the Playbook state of the five report pages', () => {
  test('every report page renders the Playbook state: axis note plus figures or the Phase-3 empty', async ({ page }) => {
    test.setTimeout(300_000);
    await page.setViewportSize(DESKTOP);
    await signIn(page);

    await gotoScreen(page, '/');
    const active = await switchFramework(page, 'Playbook');
    expect(active, `the Framework switch did not settle on Playbook (reads "${active}")`).toMatch(/playbook/i);

    for (const route of REPORT_ROUTES) {
      await gotoScreen(page, route);

      // The standing note is unconditional: no Playbook figure may be shown without it beside it.
      const note = await renderCheck(page, 'playbook-axis-note');
      expect(note.verdict, `${route}: playbook-axis-note — ${note.detail}`).toBe('RENDERS');
      const noteText = (await (await testid(page, 'playbook-axis-note')).innerText()).toLowerCase();
      expect(noteText, `${route}: the axis note must name phase_gate`).toContain('phase_gate');
      expect(noteText, `${route}: the axis note must say the axes never share a chart`).toMatch(
        /never share a chart|different axes/,
      );
      expect(noteText, `${route}: the axis note must say figures are never pooled`).toContain('never pooled');

      // Either real Playbook figures, or the Phase-3 empty state with its action. Never neither.
      const isEmpty = await exists(page, 'playbook-empty');
      if (isEmpty) {
        const connect = await renderCheck(page, 'playbook-empty-connect');
        expect(connect.verdict, `${route}: playbook-empty without its Connect action — ${connect.detail}`).toBe(
          'RENDERS',
        );
      } else {
        // The four report pages render Playbook-native figures under `pb-*`. /export renders the
        // shared ExportSurface parameterised with Framework=playbook (REQ-UI-032/033), whose controls
        // keep their `export-*` ids on both axes — the axis note above it is what says which one.
        const ids = await contentTestIds(page);
        const figures = ids.filter(
          id => id.startsWith('pb-') || (route === '/export' && id.startsWith('export-')),
        );
        expect(
          figures.length,
          `${route}: neither playbook-empty nor any pb-* figure rendered. content ids: ${JSON.stringify(ids)}`,
        ).toBeGreaterThan(0);
      }

      // The axis-separation half: no TechieFlow surface may be on screen at the same time.
      for (const id of TECHIEFLOW_ONLY[route] ?? []) {
        expect(
          await exists(page, id),
          `${route}: TechieFlow surface "${id}" is rendered on the Playbook axis — the axes are mixed`,
        ).toBe(false);
      }

      console.log(
        `PLAYBOOK ${route}: axis-note=yes, state=${isEmpty ? 'playbook-empty' : 'figures'}, ` +
          `ids=${JSON.stringify((await contentTestIds(page)).slice(0, 24))}`,
      );
    }
  });

  test('the Playbook state renders its data and passes the visual gate at 1280 and 390', async ({ page }) => {
    test.setTimeout(300_000);
    await page.setViewportSize(DESKTOP);
    await signIn(page);
    await gotoScreen(page, '/');
    await switchFramework(page, 'Playbook');

    for (const route of REPORT_ROUTES) {
      // Navigate at desktop and resize in place: the shell's sidebar is `hidden md:flex`, so a
      // navigation started at 390 never satisfies gotoScreen's wait for a visible app-sidebar.
      await page.setViewportSize(DESKTOP);
      await gotoScreen(page, route);

      for (const vp of [DESKTOP, MOBILE]) {
        await page.setViewportSize(vp);
        await page.waitForTimeout(900);

        const ids = (await contentTestIds(page)).filter(
          id => id.startsWith('pb-') || id.startsWith('playbook-'),
        );
        expect(ids.length, `${route} @${vp.width}: the Playbook state rendered no control at all`).toBeGreaterThan(0);

        // Render gate — every Playbook table must carry rows with non-blank cells.
        for (const id of ids.filter(i => i.includes('phases') || i.includes('stream') || i.includes('model-tokens'))) {
          const isTable = (await page.locator(`[data-testid="${id}"] tbody tr`).count()) > 0;
          if (!isTable) continue;
          const verdict = await tableCheck(page, id);
          expect(verdict.verdict, `${route} @${vp.width}: ${id} — ${verdict.detail}`).toBe('RENDERS');
        }

        // Visual gate — no sibling overlap, everything sized and reachable, no horizontal page scroll.
        const problems = await visualCheck(page, ids, vp.width);
        expect(problems, `${route} @${vp.width}: ${JSON.stringify(problems)}`).toEqual([]);

        // Viewport capture, not fullPage: the shell is a fixed-height flex layout whose page pane
        // scrolls internally, so a fullPage stitch produces a tall mostly-empty canvas.
        await page.screenshot({
          path: `tests/.artifacts/playbook/${route.replace(/\W+/g, '-').replace(/^-|-$/g, '') || 'root'}-${vp.width}.png`,
          fullPage: false,
        });
      }
    }

    await page.setViewportSize(DESKTOP);
  });

  test('the tabbed Playbook panels each render their own figures when there is data', async ({ page }) => {
    test.setTimeout(300_000);
    await page.setViewportSize(DESKTOP);
    await signIn(page);
    await gotoScreen(page, '/');
    await switchFramework(page, 'Playbook');

    // /routing — the phase totals, the main-vs-subagent split and the observed-model totals each live
    // behind their own tab, so each has to be opened before its figures can be gated.
    await gotoScreen(page, '/routing');
    if (await exists(page, 'playbook-empty')) {
      test.skip(true, 'no Playbook data in this environment — the empty state is covered by the first spec');
    }

    for (const [tab, panel] of [
      ['pb-routing-tab-phases', 'pb-routing-panel-phases'],
      ['pb-routing-tab-mainsub', 'pb-routing-panel-mainsub'],
      ['pb-routing-tab-models', 'pb-routing-panel-models'],
    ] as const) {
      await page.locator(`[data-testid="${tab}"]`).first().click();
      await page.waitForTimeout(700);
      const verdict = await renderCheck(page, panel);
      expect(verdict.verdict, `/routing ${panel} — ${verdict.detail}`).toBe('RENDERS');
      await page.screenshot({ path: `tests/.artifacts/playbook/${panel}-1280.png`, fullPage: false });
    }

    // The main-vs-subagent split is the figure REQ-UI-034 names for this page.
    await page.locator('[data-testid="pb-routing-tab-mainsub"]').first().click();
    await page.waitForTimeout(700);
    for (const id of ['pb-agent-split', 'pb-main-tokens', 'pb-subagent-tokens', 'pb-main-vs-sub']) {
      const verdict = await renderCheck(page, id);
      expect(verdict.verdict, `/routing ${id} — ${verdict.detail}`).toBe('RENDERS');
    }

    // /gate-outcomes — one tab per phase_gate, each carrying its own `pb-phases-{name}` table.
    await gotoScreen(page, '/gate-outcomes');
    const gateTabs = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid^="pb-gate-tab-"]')).map(
        e => (e.getAttribute('data-testid') || '').replace('pb-gate-tab-', ''),
      ),
    );
    expect(gateTabs.length, 'no phase_gate tab rendered on the Playbook axis').toBeGreaterThan(0);

    for (const gate of gateTabs) {
      await page.locator(`[data-testid="pb-gate-tab-${gate}"]`).first().click();
      await page.waitForTimeout(700);
      const table = await tableCheck(page, `pb-phases-${gate}`);
      expect(table.verdict, `/gate-outcomes pb-phases-${gate} — ${table.detail}`).toBe('RENDERS');

      // Every figure reaches the screen through FigureText, so a refusal reads as words, not a number.
      for (const id of [`pb-first-pass-${gate}`, `pb-catch-share-${gate}`, `pb-escape-rate-${gate}`]) {
        const verdict = await renderCheck(page, id);
        expect(verdict.verdict, `/gate-outcomes ${id} — ${verdict.detail}`).toBe('RENDERS');
      }
      console.log(`PB GATE ${gate}: ${table.detail}`);
      await page.screenshot({ path: `tests/.artifacts/playbook/pb-gate-${gate}-1280.png`, fullPage: false });
    }

    // No cost is ever rendered as a bare zero, and no currency-placeholder glyph reaches the screen.
    const costCells = await page.evaluate(() =>
      Array.from(document.querySelectorAll('[data-testid^="pb-phases-"] tbody td')).map(
        e => (e.textContent || '').trim(),
      ),
    );
    expect(
      costCells.some(t => t.includes('¤')),
      `a cost cell rendered the generic currency placeholder: ${JSON.stringify(costCells)}`,
    ).toBe(false);
    expect(
      costCells.some(t => t === '$0.00'),
      `an absent cost rendered as $0.00 rather than an em dash: ${JSON.stringify(costCells)}`,
    ).toBe(false);
  });

  test('switching back to TechieFlow restores the TechieFlow surface on all five pages', async ({ page }) => {
    test.setTimeout(300_000);
    await page.setViewportSize(DESKTOP);
    await signIn(page);

    await gotoScreen(page, '/');
    await switchFramework(page, 'Playbook');
    await gotoScreen(page, '/');
    const back = await switchFramework(page, 'TechieFlow');
    expect(back, `the Framework switch did not return to TechieFlow (reads "${back}")`).toMatch(/techieflow/i);

    for (const route of REPORT_ROUTES) {
      await gotoScreen(page, route);

      expect(
        await exists(page, 'playbook-axis-note'),
        `${route}: the Playbook axis note is still rendered on the TechieFlow axis`,
      ).toBe(false);
      expect(
        await exists(page, 'playbook-empty'),
        `${route}: the Playbook empty state is still rendered on the TechieFlow axis`,
      ).toBe(false);

      const ids = await contentTestIds(page);
      expect(
        ids.filter(id => id.startsWith('pb-')).length,
        `${route}: Playbook figures leaked onto the TechieFlow axis: ${JSON.stringify(ids.filter(i => i.startsWith('pb-')))}`,
      ).toBe(0);
      expect(ids.length, `${route}: the TechieFlow surface rendered nothing`).toBeGreaterThan(0);

      console.log(`TECHIEFLOW ${route}: ids=${JSON.stringify(ids.slice(0, 20))}`);
    }
  });
});
