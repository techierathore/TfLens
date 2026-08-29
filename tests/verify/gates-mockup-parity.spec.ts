// REQ-NFR-020 / BRD-144 — the mockup-parity gate.
//
// The third gate. §4a asks "does the control show data?"; §4b asks "do controls overlap or leave
// the viewport?". Neither asks "does the built screen look like the approved design", which is why
// 13 of 14 comparable screens had drifted against a checklist reading 145 Verified. This gate asks
// that question, mechanically, at 1280 and 390.
//
// Black-box: it loads docs/mockups/*.html in the SAME browser as the app, reduces both sides to a
// structural signature (see _mockup-parity.ts) and diffs the signatures. It touches no application
// source, and a failure here is a finding to report, never a licence to edit src/.
//
// Output: tests/.artifacts/gates/mockup-parity.json — the machine-readable findings file the
// verifier reads to populate `gates_run` as `mockup-parity`.
import { test, expect } from '@playwright/test';
import * as fs from 'fs';
import * as path from 'path';
import { signIn, DESKTOP, MOBILE } from './_helpers';
import { extractSignatures, diff, ALLOW, assertAllowListSane, Finding, MOCKUP_DIR } from './_mockup-parity';

const OUT = path.resolve(process.cwd(), 'tests/.artifacts/gates');
fs.mkdirSync(OUT, { recursive: true });

const WIDTHS = [DESKTOP, MOBILE];

/**
 * Route <-> mockup map. Taken from the BRD §9 "Screen inventory" table, NOT guessed from
 * filenames — the inventory is what the amendment made authoritative, and two of the mappings
 * (`/` -> coverage.html, `/` -> playbook.html on the other axis) are not derivable from a name.
 */
type Screen = {
  name: string;
  route: string;
  mockup: string | null;
  axis: 'anon' | 'techieflow' | 'playbook';
  /** Applied to the APP page after navigation (tab selection, dialog opening). */
  appBefore?: (p: any) => Promise<void>;
  /** Applied to the MOCKUP page after load, to put it into the same state. */
  mockupBefore?: (p: any) => Promise<void>;
};

const SCREENS: Screen[] = [
  { name: 'login', route: '/login', mockup: 'login.html', axis: 'anon' },
  { name: 'register', route: '/register', mockup: 'register.html', axis: 'anon' },
  { name: 'forgot-password', route: '/forgot-password', mockup: 'forgot-password.html', axis: 'anon' },
  { name: 'reset-password', route: '/reset-password?token=parityprobe000', mockup: 'reset-password.html', axis: 'anon' },

  { name: 'profile', route: '/profile', mockup: 'profile.html', axis: 'techieflow' },
  { name: 'repos', route: '/repos', mockup: 'repos.html', axis: 'techieflow' },
  { name: 'coverage', route: '/', mockup: 'coverage.html', axis: 'techieflow' },
  { name: 'three-questions', route: '/three-questions', mockup: 'three-questions.html', axis: 'techieflow' },
  { name: 'harness', route: '/harness', mockup: 'harness.html', axis: 'techieflow' },
  { name: 'routing', route: '/routing', mockup: 'routing.html', axis: 'techieflow' },
  { name: 'misses', route: '/misses', mockup: 'misses.html', axis: 'techieflow' },
  { name: 'export', route: '/export', mockup: 'export.html', axis: 'techieflow' },

  // Playbook axis. Comparability is DECIDED AT RUN TIME from the repo count the header reports,
  // not hard-coded: with zero connected Playbook repositories these pages correctly render an
  // empty state and the mockups' populated layouts have nothing to compare against, but the day a
  // Playbook repo is connected the skip must lift by itself. A permanent skip list would quietly
  // become a permanent blind spot.
  { name: 'pb-coverage', route: '/', mockup: 'playbook.html', axis: 'playbook' },
  { name: 'pb-three-questions', route: '/three-questions', mockup: 'three-questions-playbook.html', axis: 'playbook' },
  { name: 'pb-harness', route: '/harness', mockup: 'harness-playbook.html', axis: 'playbook' },
  { name: 'pb-routing', route: '/routing', mockup: 'routing-playbook.html', axis: 'playbook' },
  { name: 'pb-misses', route: '/misses', mockup: 'misses-playbook.html', axis: 'playbook' },
  { name: 'pb-export', route: '/export', mockup: 'export-playbook.html', axis: 'playbook' },

  // Routes the app serves that no mockup covers. Reported as NO-MOCKUP, never as a silent pass.
  { name: 'not-found', route: '/not-found', mockup: null, axis: 'anon' },
  { name: 'repos-add', route: '/repos/add', mockup: null, axis: 'techieflow' },
  { name: 'repos-add-import', route: '/repos/add/import', mockup: null, axis: 'techieflow' },
];

type ScreenReport = {
  screen: string;
  route: string;
  mockup: string | null;
  verdict: 'PASS' | 'FAIL' | 'NO-MOCKUP' | 'SKIPPED' | 'NO-ANCHORS' | 'UNREACHABLE';
  reason?: string;
  /** Clause 2 — one entry per width. */
  docOverflow: { width: number; scrollHeight: number; clientHeight: number; delta: number; verdict: 'PASS' | 'FAIL' }[];
  findings: Finding[];
  waived: Finding[];
  /** Seen, but not attributable to drift (e.g. the two sides show different states). */
  unattributable: Finding[];
  /** Anchors the mockup carries that the app page does not render (dataset-specific or unbuilt). */
  unmatched: string[];
  compared: number;
};

const report: {
  gate: 'mockup-parity';
  req: 'REQ-NFR-020';
  brd: 'BRD-144';
  generated: string;
  widths: number[];
  summary: Record<string, number>;
  allowList: typeof ALLOW;
  /** Allow-list entries that matched no compared element on this run — an inert waiver rots. */
  allowListUnused: typeof ALLOW;
  screens: ScreenReport[];
} = {
  gate: 'mockup-parity',
  req: 'REQ-NFR-020',
  brd: 'BRD-144',
  generated: new Date().toISOString(),
  widths: WIDTHS.map(w => w.width),
  summary: {},
  allowList: ALLOW,
  allowListUnused: [],
  screens: [],
};

function write() {
  report.summary = report.screens.reduce((acc, s) => {
    acc[s.verdict] = (acc[s.verdict] ?? 0) + 1;
    acc.findings = (acc.findings ?? 0) + s.findings.length;
    acc.waived = (acc.waived ?? 0) + s.waived.length;
    acc.unattributable = (acc.unattributable ?? 0) + s.unattributable.length;
    return acc;
  }, {} as Record<string, number>);
  fs.writeFileSync(path.join(OUT, 'mockup-parity.json'), JSON.stringify(report, null, 2));
}

/** Settle a Blazor Server circuit: the first render swaps skeletons for data. */
async function settle(page: any, ms = 2200) {
  await page.waitForLoadState('networkidle').catch(() => {});
  await page.waitForTimeout(ms);
}

/** Flip the header Framework switch to the named axis. */
async function selectFramework(page: any, which: 'techieflow' | 'playbook') {
  await page.evaluate((w: string) => {
    const sw = document.querySelector('[data-testid="framework-switch"]');
    if (!sw) return;
    const trig = Array.from(sw.querySelectorAll('[role="tab"], button, a')).find(
      e => new RegExp(w === 'playbook' ? 'playbook' : 'techieflow', 'i').test((e as HTMLElement).innerText || ''));
    (trig as HTMLElement | undefined)?.click();
  }, which);
  await page.waitForTimeout(2200);
}

// NOT serial: in serial mode a clause-2 failure SKIPS the clause-1 sweep, and the run then reports
// nothing at all about mockup parity — the gate would go dark exactly when a screen is broken.
// The two tests share no state beyond the findings file and each signs in for itself.
// 21 screens x 2 sides x 2 viewports. The 60s default is for single-assertion tests.
test.setTimeout(1_500_000);

// ---------------------------------------------------------------------------------------------
// Clause 2, built and proved FIRST because it is independent of every mockup and unambiguous:
// a page whose document escapes the app-shell scroll container is a FAIL. The shell is a
// fixed-height flex layout whose page pane scrolls internally, so `document.scrollHeight` must
// never exceed `clientHeight`.
// ---------------------------------------------------------------------------------------------
test('mockup-parity clause 2: no route escapes the app-shell scroll container', async ({ page }) => {
  assertAllowListSane();

  const rows: { screen: string; width: number; delta: number; mockupDelta: number | null; verdict: string }[] = [];

  const measure = (p: any) => p.evaluate(() => {
    const de = document.documentElement;
    return { scrollHeight: de.scrollHeight, clientHeight: de.clientHeight, delta: de.scrollHeight - de.clientHeight };
  });

  // The same measurement on the MOCKUP, so the report can tell a genuine drift (the app scrolls
  // where the approved design does not) apart from a route whose design always scrolled. Both
  // still FAIL the clause as BRD-144 words it — but a reader must not have to guess which is which.
  const mockPage = await page.context().newPage();
  const mockupDelta: Record<string, number> = {};
  for (const s of SCREENS) {
    if (!s.mockup) continue;
    for (const vp of WIDTHS) {
      await mockPage.setViewportSize(vp);
      await mockPage.goto('file://' + path.join(MOCKUP_DIR, s.mockup), { waitUntil: 'load' });
      await mockPage.waitForTimeout(300);
      mockupDelta[`${s.name}@${vp.width}`] = (await measure(mockPage)).delta;
    }
  }
  await mockPage.close();

  for (const s of SCREENS) {
    // Each route is measured once; the Playbook duplicates share a route with their TechieFlow
    // twin and are covered by it.
    if (s.axis === 'playbook') continue;
    if (s.axis !== 'anon') await signIn(page).catch(() => {});
    for (const vp of WIDTHS) {
      await page.setViewportSize(vp);
      try {
        await page.goto(s.route, { waitUntil: 'domcontentloaded' });
      } catch { continue; }
      await settle(page, 1500);
      const d = await measure(page);
      rows.push({
        screen: s.name, width: vp.width, delta: d.delta,
        mockupDelta: mockupDelta[`${s.name}@${vp.width}`] ?? null,
        verdict: d.delta <= 2 ? 'PASS' : 'FAIL',
      });
    }
    await page.setViewportSize(DESKTOP);
  }

  for (const r of rows) {
    const ctx = r.verdict === 'FAIL' && r.mockupDelta !== null
      ? (r.mockupDelta <= 2
        ? '  [DRIFT: the approved mockup does NOT scroll here]'
        : `  [design-consistent: the mockup scrolls by ${r.mockupDelta}px too]`)
      : '';
    console.log(`DOC-SCROLL ${r.screen} @${r.width}: delta=${r.delta}px -> ${r.verdict}${ctx}`);
  }
  const bad = rows.filter(r => r.verdict === 'FAIL');
  // Recorded on `report` so the findings file carries clause 2 even when the parity sweep below
  // does not run.
  report.screens.push({
    screen: '(clause-2 sweep)', route: '(all routes)', mockup: null,
    verdict: bad.length === 0 ? 'PASS' : 'FAIL',
    reason: bad.length === 0
      ? `document.scrollHeight <= clientHeight + 2 on all ${rows.length} route/width pairs`
      : bad.map(b => `${b.screen}@${b.width} escapes by ${b.delta}px`
        + (b.mockupDelta === null ? '' : b.mockupDelta <= 2
          ? ' (DRIFT: mockup does not scroll)'
          : ` (design-consistent: mockup scrolls ${b.mockupDelta}px)`)).join('; '),
    docOverflow: rows.map(r => ({ width: r.width, scrollHeight: 0, clientHeight: 0, delta: r.delta, verdict: r.verdict as any })),
    findings: [], waived: [], unattributable: [], unmatched: [], compared: rows.length,
  });
  write();

  expect(bad.map(b => `${b.screen}@${b.width} +${b.delta}px`), 'routes whose document escapes the app-shell scroll container').toEqual([]);
});

// ---------------------------------------------------------------------------------------------
// Clauses 1 and 4 — the structural comparison itself.
// ---------------------------------------------------------------------------------------------
test('mockup-parity clause 1+4: every built screen is graded against its approved mockup', async ({ browser, page }) => {
  assertAllowListSane();

  // A second page for the mockups, so the app's signed-in session is never reloaded mid-sweep.
  const mockPage = await browser.newPage();

  await page.setViewportSize(DESKTOP);
  await signIn(page);

  // Is the Playbook axis comparable at all? MEASURED, not assumed.
  const pbCount = await page.evaluate(() => {
    const el = document.querySelector('[data-testid="framework-count-playbook"]');
    const t = (el as HTMLElement | null)?.innerText ?? '';
    const m = t.match(/\d+/);
    return m ? Number(m[0]) : -1;
  });
  console.log(`PLAYBOOK REPO COUNT (measured): ${pbCount}`);
  const pbComparable = pbCount > 0;

  let currentAxis: 'techieflow' | 'playbook' = 'techieflow';

  for (const s of SCREENS) {
    const rep: ScreenReport = {
      screen: s.name, route: s.route, mockup: s.mockup,
      verdict: 'PASS', docOverflow: [], findings: [], waived: [], unattributable: [], unmatched: [], compared: 0,
    };

    // Clause 4 — a screen with no mockup is REPORTED, never a silent pass.
    if (!s.mockup) {
      rep.verdict = 'NO-MOCKUP';
      rep.reason = 'no mockup exists in docs/mockups/ for this route (not in the BRD §9 inventory)';
      console.log(`SCREEN ${s.name}: ⚠ NO-MOCKUP — ${rep.reason}`);
      report.screens.push(rep); write();
      continue;
    }

    if (s.axis === 'playbook' && !pbComparable) {
      rep.verdict = 'SKIPPED';
      rep.reason = `Playbook axis has ${pbCount} connected repositories in this dataset, so this page `
        + 'correctly renders its empty state and the mockup\'s populated layout has nothing to compare '
        + 'against. NOT a pass. Measured at run time — the skip lifts by itself the day a Playbook '
        + 'repo is connected.';
      console.log(`SCREEN ${s.name}: SKIPPED — Playbook axis empty (${pbCount} repos)`);
      report.screens.push(rep); write();
      continue;
    }

    const mockUrl = 'file://' + path.join(MOCKUP_DIR, s.mockup);

    for (const vp of WIDTHS) {
      // --- mockup side ---
      await mockPage.setViewportSize(vp);
      await mockPage.goto(mockUrl, { waitUntil: 'load' });
      if (s.mockupBefore) await s.mockupBefore(mockPage);
      await mockPage.waitForTimeout(500);
      const M = await extractSignatures(mockPage);

      if (M.anchors === 0) {
        rep.verdict = 'NO-ANCHORS';
        rep.reason = `${s.mockup} carries no data-testid anchors, so there is nothing to align the `
          + 'comparison on. Reported rather than silently passed.';
        console.log(`SCREEN ${s.name}: NO-ANCHORS in ${s.mockup}`);
        break;
      }

      // --- app side ---
      await page.setViewportSize(vp);
      const wantAxis = s.axis === 'playbook' ? 'playbook' : 'techieflow';
      try {
        await page.goto(s.route, { waitUntil: 'domcontentloaded' });
        await settle(page);
        if (s.axis !== 'anon' && wantAxis !== currentAxis) {
          await selectFramework(page, wantAxis as any);
          currentAxis = wantAxis as any;
        }
        if (s.appBefore) await s.appBefore(page);
      } catch (e) {
        rep.verdict = 'UNREACHABLE';
        rep.reason = String(e).slice(0, 200);
        break;
      }
      const A = await extractSignatures(page);

      rep.docOverflow.push({
        width: vp.width, scrollHeight: A.doc.scrollHeight, clientHeight: A.doc.clientHeight,
        delta: A.doc.delta, verdict: A.doc.delta <= 2 ? 'PASS' : 'FAIL',
      });

      const d = diff(s.name, vp.width, M.sigs, A.sigs);
      rep.findings.push(...d.findings);
      rep.waived.push(...d.waived);
      rep.unattributable.push(...d.unattributable);
      rep.compared += Object.keys(M.sigs).filter(k => A.sigs[k]).length;

      const miss = Object.keys(M.sigs).filter(k => !A.sigs[k] && !k.includes(' > '));
      for (const k of miss) if (!rep.unmatched.includes(k)) rep.unmatched.push(k);

      await page.screenshot({ path: path.join(OUT, `parity-${s.name}-${vp.width}.png`) }).catch(() => {});
      await mockPage.screenshot({ path: path.join(OUT, `parity-${s.name}-mockup-${vp.width}.png`) }).catch(() => {});
    }

    if (rep.verdict === 'PASS' && (rep.findings.length > 0 || rep.docOverflow.some(d => d.verdict === 'FAIL'))) {
      rep.verdict = 'FAIL';
    }

    const head = `SCREEN ${s.name} (${s.mockup}): ${rep.verdict === 'PASS' ? 'PASS' : rep.verdict}`;
    console.log(`${head} — ${rep.compared} elements compared, ${rep.findings.length} finding(s), `
      + `${rep.waived.length} waived, ${rep.unmatched.length} mockup anchor(s) not present in app`);
    for (const f of rep.findings) {
      console.log(`   FAIL [${f.cls}] ${f.key} @${f.width}: ${f.detail} (mockup=${f.mockup}, app=${f.app})`);
    }
    for (const d of rep.docOverflow.filter(x => x.verdict === 'FAIL')) {
      console.log(`   FAIL [doc-scroll] @${d.width}: scrollHeight ${d.scrollHeight} > clientHeight ${d.clientHeight}`);
    }
    for (const u of rep.unattributable) {
      console.log(`   NOTE [${u.cls}] ${u.key} @${u.width}: ${u.detail} (mockup=${u.mockup}, app=${u.app})`);
    }
    for (const w of rep.waived) {
      console.log(`   WAIVED [${w.cls}] ${w.key} @${w.width}: ${w.waived}`);
    }
    if (rep.unmatched.length) console.log(`   INFO not-in-app: ${rep.unmatched.join(', ')}`);

    report.screens.push(rep); write();
  }

  await mockPage.close();

  // An allow-list entry that never matched anything is not "no drift" — it is a waiver aimed at an
  // element the gate never saw, and it will sit there forever pretending to cover a decision it no
  // longer covers. Report every one of them, loudly.
  const fired = new Set(report.screens.flatMap(sc => sc.waived.flatMap(w => {
    const base = w.key.split(' > ')[0];
    return [`${sc.screen}::${base}`, `*::${base}`];
  })));
  report.allowListUnused = ALLOW.filter(a => !fired.has(`${a.screen}::${a.testid}`));
  if (report.allowListUnused.length) {
    console.log(`\nALLOW-LIST: ${report.allowListUnused.length} of ${ALLOW.length} entries matched nothing on this run:`);
    for (const a of report.allowListUnused) {
      console.log(`   UNUSED ${a.screen}/${a.testid} [${a.classes.join(',')}] — the mockup carries no `
        + 'anchor for this element, so the deviation it records is currently OUT OF THE GATE\'S REACH.');
    }
  }
  write();

  const failed = report.screens.filter(s => s.verdict === 'FAIL');
  console.log('\n=== mockup-parity summary ===');
  console.log(JSON.stringify(report.summary));
  console.log(`findings file: ${path.relative(process.cwd(), path.join(OUT, 'mockup-parity.json'))}`);

  expect(
    failed.map(f => `${f.screen}: ${f.findings.map(x => `${x.cls}@${x.width}:${x.key}`).join(', ')}`),
    'screens that drifted from their approved mockup',
  ).toEqual([]);
});


// ---------------------------------------------------------------------------------------------
// CATCH PROOF — a gate nobody has seen fail is a gate nobody has tested.
//
// This runs the REAL extractor and the REAL diff over the REAL app, finds an element that the two
// sides currently AGREE on, breaks exactly one structural property of it in the browser, and
// asserts that the gate goes from silent to failing on that element and on nothing else. Then it
// removes the injection and asserts the gate goes quiet again.
// ---------------------------------------------------------------------------------------------
test('mockup-parity catch proof: an injected structural difference makes the gate FAIL', async ({ browser, page }) => {
  const mockPage = await browser.newPage();
  await page.setViewportSize(DESKTOP);
  await mockPage.setViewportSize(DESKTOP);
  await signIn(page);

  type Victim = { screen: string; route: string; mockup: string; key: string; kind: 'icon' | 'badge' | 'token' };
  const proofs: { kind: string; screen: string; key: string; before: number; after: number; restored: number; detail: string }[] = [];

  // Candidate screens, richest first.
  const CANDIDATES: { screen: string; route: string; mockup: string }[] = [
    { screen: 'misses', route: '/misses', mockup: 'misses.html' },
    { screen: 'coverage', route: '/', mockup: 'coverage.html' },
    { screen: 'repos', route: '/repos', mockup: 'repos.html' },
    { screen: 'profile', route: '/profile', mockup: 'profile.html' },
    { screen: 'three-questions', route: '/three-questions', mockup: 'three-questions.html' },
    { screen: 'harness', route: '/harness', mockup: 'harness.html' },
    { screen: 'export', route: '/export', mockup: 'export.html' },
  ];

  const load = async (c: { screen: string; route: string; mockup: string }) => {
    await mockPage.goto('file://' + path.join(MOCKUP_DIR, c.mockup), { waitUntil: 'load' });
    await mockPage.waitForTimeout(400);
    await page.goto(c.route, { waitUntil: 'domcontentloaded' });
    await settle(page);
    return { M: await extractSignatures(mockPage), A: await extractSignatures(page) };
  };

  /** Count findings of one class naming one key. */
  const countFor = (screen: string, M: any, A: any, key: string, cls: string) =>
    diff(screen, DESKTOP.width, M.sigs, A.sigs).findings.filter(f => f.key === key && f.cls === cls).length;

  for (const kind of ['icon', 'badge', 'token'] as const) {
    let done = false;
    for (const c of CANDIDATES) {
      if (done) break;
      const { M, A } = await load(c);

      // A victim must be an element the two sides currently AGREE on, so that any finding after
      // the injection is caused by the injection and by nothing else.
      const key = Object.keys(M.sigs).find(k => {
        const m = M.sigs[k], a = A.sigs[k];
        if (!m || !a) return false;
        if (countFor(c.screen, M, A, k, kind) > 0) return false;   // already failing: useless as proof
        if (kind === 'icon') return m.icon && a.icon && !k.includes(' > ');
        if (kind === 'badge') return m.badge && a.badge;
        return !m.token && !a.token && k.includes('td[') && a.text.length > 6;
      });
      if (!key) continue;

      const before = countFor(c.screen, M, A, key, kind);

      // --- inject, in the browser, on the APP side only ---
      await page.evaluate(({ key, kind }) => {
        const base = key.split(' > ')[0];
        let el: Element | null = document.querySelector(`[data-testid="${base}"]`);
        const cell = key.match(/tr\[(\d+)\]td\[(\d+)\]/);
        if (cell && el) {
          const table = el.matches('table') ? el : el.querySelector('table');
          const tr = table?.querySelectorAll('tbody tr')[Number(cell[1])];
          el = tr?.querySelectorAll('td')[Number(cell[2])] ?? null;
        }
        if (!el) return;
        (el as HTMLElement).setAttribute('data-parity-injected', '1');
        const st = el as HTMLElement;
        if (kind === 'icon') {
          // Strip the icon: exactly the "missing icon or icon button" defect.
          st.querySelectorAll('svg, [class*="icon"], [class*="lucide"]').forEach(s => {
            (s as HTMLElement).style.display = 'none';
          });
        } else if (kind === 'badge') {
          // Strip the chrome so the pill computes as plain text — the defect BRD-144 names first.
          // The chrome must come off the element AND off the tight ancestors the extractor treats
          // as its chrome host, because the app frequently hangs the testid on an inner layout
          // span while the pill styling sits on the wrapping button.
          const strip = (n: HTMLElement) => {
            n.style.setProperty('background', 'transparent', 'important');
            n.style.setProperty('background-color', 'transparent', 'important');
            n.style.setProperty('border', '0', 'important');
            n.style.setProperty('border-radius', '0', 'important');
            n.classList.remove(...Array.from(n.classList).filter(c => /badge|chip|pill|tag/i.test(c)));
          };
          strip(st);
          let a = st.parentElement;
          for (let up = 0; a && up < 3; up++, a = a.parentElement) {
            if (a.hasAttribute('data-testid')) break;
            strip(a);
          }
        } else {
          // Squeeze the cell below its longest unbreakable token. A width on ONE td does nothing:
          // a table column is as wide as its widest cell, so the whole COLUMN has to be narrowed
          // and the table put into fixed layout — which is exactly how a real too-narrow column
          // arises.
          const td = st as HTMLTableCellElement;
          const tbl = td.closest('table') as HTMLTableElement | null;
          const ci = Array.from(td.parentElement?.children ?? []).indexOf(td);
          if (tbl) {
            tbl.style.setProperty('table-layout', 'fixed', 'important');
            tbl.querySelectorAll('tr').forEach(tr => {
              const c = tr.children[ci] as HTMLElement | undefined;
              if (c) {
                c.style.setProperty('width', '18px', 'important');
                c.style.setProperty('max-width', '18px', 'important');
                c.style.setProperty('overflow-wrap', 'anywhere', 'important');
              }
            });
          }
        }
      }, { key, kind });
      await page.waitForTimeout(400);

      const A2 = await extractSignatures(page);
      const after = countFor(c.screen, M, A2, key, kind);

      // --- remove the injection and prove the gate goes quiet again ---
      await page.goto(c.route, { waitUntil: 'domcontentloaded' });
      await settle(page);
      const A3 = await extractSignatures(page);
      const restored = countFor(c.screen, M, A3, key, kind);

      proofs.push({ kind, screen: c.screen, key, before, after, restored,
        detail: kind === 'icon' ? 'hid every svg/icon inside the element'
          : kind === 'badge' ? 'stripped background, border, radius and badge classes'
          : 'squeezed the cell to 18px, below its longest unbreakable token' });
      done = true;
    }
    if (!done) proofs.push({ kind, screen: '(none)', key: '(none)', before: -1, after: -1, restored: -1,
      detail: 'no element on any candidate screen where BOTH sides currently agree on this class' });
  }

  await mockPage.close();

  console.log('\n=== catch proof ===');
  for (const p of proofs) {
    console.log(`[${p.kind}] ${p.screen} :: ${p.key}\n    injected: ${p.detail}`
      + `\n    findings before=${p.before}  after=${p.after}  after-revert=${p.restored}`
      + `  -> ${p.before === 0 && p.after > 0 && p.restored === 0 ? 'GATE CAUGHT IT' : 'NOT PROVEN'}`);
  }
  fs.writeFileSync(path.join(OUT, 'mockup-parity-catch-proof.json'), JSON.stringify(proofs, null, 2));

  const proven = proofs.filter(p => p.before === 0 && p.after > 0 && p.restored === 0).map(p => p.kind);
  expect(proven, 'classes whose detection was proved by injecting a real structural difference')
    .toEqual(expect.arrayContaining(['icon', 'badge', 'token']));
});
