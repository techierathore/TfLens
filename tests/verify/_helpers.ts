// Shared helpers for the TfLens verification suite.
// Black-box only: nothing here touches application source.
import { Page, expect, Locator } from '@playwright/test';

export const USER1 = { email: 'tflensdemo@techierathore.com', password: 'TfLensDemo!23' };
export const USER2 = { email: 'tflenstest2@techierathore.com', password: 'TfLensTest2!23' };

export const DESKTOP = { width: 1280, height: 800 };
export const MOBILE = { width: 390, height: 844 };

/** Sign in through the real /login form and wait for the shell. */
export async function signIn(page: Page, user = USER1) {
  await page.goto('/login');
  await page.waitForSelector('[data-testid="login-email"]');
  await page.fill('[data-testid="login-email"]', user.email);
  await page.fill('[data-testid="login-pass"]', user.password);
  await Promise.all([
    page.waitForURL(u => !u.pathname.startsWith('/login'), { timeout: 45_000 }),
    page.click('[data-testid="login-submit"]'),
  ]);
  await page.waitForSelector('[data-testid="app-sidebar"]', { timeout: 30_000 });
}

/** Navigate inside the shell and wait for Blazor Server to finish the first render. */
export async function gotoScreen(page: Page, route: string) {
  await page.goto(route);
  await page.waitForSelector('[data-testid="app-sidebar"]', { timeout: 30_000 });
  await page.waitForLoadState('networkidle').catch(() => {});
  // Blazor Server: give the circuit a beat to swap skeletons for data.
  await page.waitForTimeout(1500);
}

/** Wait until a testid exists, tolerating Blazor's deferred render. */
export async function testid(page: Page, id: string, timeout = 20_000): Promise<Locator> {
  const loc = page.locator(`[data-testid="${id}"]`);
  await loc.first().waitFor({ state: 'attached', timeout });
  return loc.first();
}

export type RenderVerdict = 'RENDERS' | 'RENDER-EMPTY' | 'RENDER-ERROR' | 'UNREACHABLE';

/** §4a render gate for a single control: present AND carrying non-placeholder text. */
export async function renderCheck(page: Page, id: string): Promise<{ id: string; verdict: RenderVerdict; detail: string }> {
  const loc = page.locator(`[data-testid="${id}"]`).first();
  const count = await page.locator(`[data-testid="${id}"]`).count();
  if (count === 0) return { id, verdict: 'RENDER-EMPTY', detail: 'element absent from the DOM' };
  const visible = await loc.isVisible().catch(() => false);
  const text = ((await loc.innerText().catch(() => '')) || '').trim();
  if (!visible) return { id, verdict: 'RENDER-EMPTY', detail: 'element present but not visible' };
  if (text.length === 0) {
    // A control may legitimately be an icon-only button; treat an empty box as empty.
    const box = await loc.boundingBox();
    if (!box || box.width === 0 || box.height === 0) return { id, verdict: 'RENDER-EMPTY', detail: 'zero-size, no text' };
    return { id, verdict: 'RENDERS', detail: 'no text but sized (icon control)' };
  }
  return { id, verdict: 'RENDERS', detail: text.slice(0, 60).replace(/\s+/g, ' ') };
}

/** §4a render gate for a table: rows > 0 AND cells non-empty. */
export async function tableCheck(page: Page, id: string): Promise<{ id: string; verdict: RenderVerdict; detail: string }> {
  const table = page.locator(`[data-testid="${id}"]`).first();
  if ((await page.locator(`[data-testid="${id}"]`).count()) === 0)
    return { id, verdict: 'RENDER-EMPTY', detail: 'table absent' };
  const rows = table.locator('tbody tr');
  const n = await rows.count();
  if (n === 0) return { id, verdict: 'RENDER-EMPTY', detail: 'zero rows' };
  let blank = 0, cells = 0;
  for (let i = 0; i < n; i++) {
    const tds = rows.nth(i).locator('td');
    const c = await tds.count();
    for (let j = 0; j < c; j++) {
      cells++;
      const t = ((await tds.nth(j).innerText().catch(() => '')) || '').trim();
      if (t.length === 0) blank++;
    }
  }
  if (cells > 0 && blank === cells) return { id, verdict: 'RENDER-EMPTY', detail: `${n} rows, every cell blank` };
  return { id, verdict: 'RENDERS', detail: `${n} rows, ${cells} cells, ${blank} blank` };
}

/**
 * §4b visual-truth gate: geometry over a set of controls at the current viewport.
 * All measurement happens in ONE page.evaluate — an O(n^2) round-trip per pair is far
 * too slow over the CDP channel for screens carrying 30+ controls.
 */
export async function visualCheck(page: Page, ids: string[], width: number): Promise<string[]> {
  return page.evaluate(
    ({ ids, width }) => {
      const problems: string[] = [];
      const items: { id: string; el: Element; r: DOMRect }[] = [];

      for (const id of ids) {
        const el = document.querySelector(`[data-testid="${id}"]`);
        if (!el) continue;
        const cs = getComputedStyle(el as HTMLElement);
        if (cs.display === 'none' || cs.visibility === 'hidden') continue;
        const r = el.getBoundingClientRect();
        if (r.width <= 0 || r.height <= 0) { problems.push(`${id}: zero-size box @${width}`); continue; }
        if (r.right < -2 || r.left > width + 2) {
          // A control sitting outside the viewport is only a defect if it is genuinely
          // unreachable. Inside a deliberately horizontally-scrollable region (the
          // `overflow-x-auto` boxes the DevGuide prescribes for wide tables and tab strips)
          // it is reachable by scrolling that region, which is the intended mobile design.
          let scrollable = false;
          for (let p: Element | null = el.parentElement; p; p = p.parentElement) {
            const ov = getComputedStyle(p as HTMLElement).overflowX;
            if ((ov === 'auto' || ov === 'scroll') && p.scrollWidth > p.clientWidth + 2) { scrollable = true; break; }
          }
          if (!scrollable) {
            problems.push(`${id}: off-viewport and NOT inside a scrollable region (x=${Math.round(r.left)}, w=${Math.round(r.width)}) @${width}`);
          }
          continue;
        }
        items.push({ id, el, r });
      }

      // What a reader can actually SEE. A wide table parked inside an `overflow-x-auto` box — the
      // idiom the DevGuide prescribes, and which the off-viewport branch above already tolerates —
      // has rows whose own getBoundingClientRect reports the FULL, unclipped width: a 5-column
      // cross-tab inside a 490px card reports 879px and appears to collide with the next column,
      // when on screen it is clipped by its scroll box and nothing overlaps at all. Clipping each
      // rect to its clipping ancestors before the intersection test is what makes the overlap gate
      // measure the rendered page rather than the layout tree. A control genuinely hidden by an
      // ancestor clips to an empty box and is dropped by the zero-size guard below, so this cannot
      // hide a real occlusion — only an imaginary one.
      const clip = (el: Element, r: DOMRect): DOMRect => {
        let left = r.left, top = r.top, right = r.right, bottom = r.bottom;
        for (let p: Element | null = el.parentElement; p; p = p.parentElement) {
          const cs = getComputedStyle(p as HTMLElement);
          const clipsX = cs.overflowX === 'auto' || cs.overflowX === 'scroll' || cs.overflowX === 'hidden';
          const clipsY = cs.overflowY === 'auto' || cs.overflowY === 'scroll' || cs.overflowY === 'hidden';
          if (!clipsX && !clipsY) continue;
          const pr = p.getBoundingClientRect();
          if (clipsX) { left = Math.max(left, pr.left); right = Math.min(right, pr.right); }
          if (clipsY) { top = Math.max(top, pr.top); bottom = Math.min(bottom, pr.bottom); }
        }
        return new DOMRect(left, top, Math.max(0, right - left), Math.max(0, bottom - top));
      };

      for (const item of items) {
        item.r = clip(item.el, item.r);
      }

      const tol = 2;
      for (let i = 0; i < items.length; i++) {
        for (let j = i + 1; j < items.length; j++) {
          const A = items[i], B = items[j];
          if (A.r.width <= tol || A.r.height <= tol || B.r.width <= tol || B.r.height <= tol) continue;
          if (A.el.contains(B.el) || B.el.contains(A.el)) continue;
          // Full geometric containment is the badge-inside-its-row case (a SidebarMenuBadge
          // laid over the empty right end of its nav item). That is a design idiom, not an
          // occlusion. Only a PARTIAL intersection means two controls collide.
          const inside = (X: DOMRect, Y: DOMRect) =>
            X.left >= Y.left - tol && X.right <= Y.right + tol &&
            X.top >= Y.top - tol && X.bottom <= Y.bottom + tol;
          if (inside(A.r, B.r) || inside(B.r, A.r)) continue;
          const ox = Math.min(A.r.right, B.r.right) - Math.max(A.r.left, B.r.left);
          const oy = Math.min(A.r.bottom, B.r.bottom) - Math.max(A.r.top, B.r.top);
          if (ox > tol && oy > tol) {
            problems.push(`${A.id} overlaps ${B.id} by ${Math.round(ox)}x${Math.round(oy)}px @${width}`);
          }
        }
      }

      const de = document.documentElement;
      if (de.scrollWidth > de.clientWidth + 2) {
        problems.push(`page scrolls horizontally: scrollWidth ${de.scrollWidth} > clientWidth ${de.clientWidth} @${width}`);
      }
      return problems;
    },
    { ids, width },
  );
}

/** Console/Blazor error collector — attach before navigating. */
export function collectErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text().slice(0, 200)); });
  page.on('pageerror', e => errors.push(`pageerror: ${String(e).slice(0, 200)}`));
  return errors;
}

export { expect };
