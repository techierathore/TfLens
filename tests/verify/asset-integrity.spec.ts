// REQ-NFR-015 — asset-integrity gate.
//
// WHY THIS FILE EXISTS. On 2026-08-28 the owner's UAT session showed /login rendered as an
// unstyled single column (docs/uatissuessc/Login.png). The cause was one absent stylesheet:
// the Blazor scoped-CSS bundle TfLens.styles.css, which at the time carried 100% of the auth
// layout. Nothing anywhere noticed. There was no console error, no Blazor error boundary, no
// server log line — and every existing gate passed, because the render gate asks "does this
// control carry text" and the visual gate asks "do these boxes overlap", and an unstyled page
// answers both innocently. A page can lose its entire stylesheet and still look intentional.
//
// So this gate asks the one question none of the others do: did every asset the document
// declares actually arrive? It is deliberately dumb — status 200 and a non-empty body for every
// <link rel="stylesheet"> and every <script src> the page emits, anonymous and authenticated.
import { test, expect } from '@playwright/test';
import { signIn, gotoScreen } from './_helpers';

/** Every asset URL the document declares, resolved against the page. */
async function declaredAssets(page: import('@playwright/test').Page): Promise<string[]> {
  return page.evaluate(() => {
    const urls: string[] = [];
    document.querySelectorAll('link[rel="stylesheet"][href]').forEach(l =>
      urls.push(new URL((l as HTMLLinkElement).getAttribute('href')!, document.baseURI).href));
    document.querySelectorAll('script[src]').forEach(s =>
      urls.push(new URL((s as HTMLScriptElement).getAttribute('src')!, document.baseURI).href));
    return [...new Set(urls)];
  });
}

async function assertAllArrive(page: import('@playwright/test').Page, label: string) {
  const urls = await declaredAssets(page);
  // A page that declares nothing has not been rendered — that is a failure, not a pass.
  expect(urls.length, `${label}: the document declared no stylesheets or scripts at all`).toBeGreaterThan(2);

  const broken: string[] = [];
  for (const url of urls) {
    const res = await page.request.get(url);
    const body = await res.body().catch(() => Buffer.alloc(0));
    if (!res.ok() || body.length === 0) broken.push(`${res.status()} ${body.length}B  ${url}`);
  }
  expect(broken, `${label}: declared assets that did not arrive:\n  ${broken.join('\n  ')}`).toEqual([]);
}

// THE CLAUSE ADDED 2026-09-01, after the same user-visible failure recurred.
//
// On 2026-08-30 the owner found /harness rendered as three full-width stacked tables showing the raw
// `Metric | Value` headers the scoped CSS hides — the 2026-08-28 defect wearing a different hat. The
// gate above passed throughout, and it was right to: the server answered every asset correctly. The
// browser never asked. In Development the document linked the bundle UNFINGERPRINTED, and the response
// carried ETag + Last-Modified but no Cache-Control at all, so the browser fell back to heuristic
// caching and reused its copy across a rebuild. The trigger was this project's own edit to
// Harness.razor.css: before it, the owner's cached bundle matched the server's; after it, it did not.
//
// A gate that fetches in a fresh context can never see that, which is why the fix is structural rather
// than a cleverer fetch: MapStaticAssets fingerprints every asset `Assets[...]` resolves, so a rebuilt
// file has a NEW url that no cache entry can answer, and serves the unfingerprinted path `no-cache` so
// the browser must revalidate. This asserts that property, not the symptom — the symptom needs a real
// browser profile carrying a stale entry across a rebuild, which no headless run has.
function cacheSafety(url: string, cacheControl: string | undefined) {
  const fingerprinted = /[?&]v=[A-Za-z0-9_\-]{6,}/.test(url) || /\.[A-Za-z0-9]{8,}\.(css|js)$/.test(url);
  const cc = (cacheControl ?? '').toLowerCase();
  const revalidates = cc.includes('no-cache') || cc.includes('no-store') || cc.includes('must-revalidate')
    || /max-age\s*=\s*0\b/.test(cc);
  return { fingerprinted, revalidates, ok: fingerprinted || revalidates };
}

async function assertNoAssetCanGoStale(page: import('@playwright/test').Page, label: string) {
  const urls = await declaredAssets(page);
  const stale: string[] = [];

  for (const url of urls) {
    const res = await page.request.get(url);
    const cc = res.headers()['cache-control'];
    const verdict = cacheSafety(url, cc);
    if (!verdict.ok) {
      stale.push(`${url}\n      cache-control: ${cc ?? '(none)'} — not fingerprinted and not revalidated, `
        + `so a browser may reuse a pre-rebuild copy of this file with no way for the app to know`);
    }
  }

  expect(stale, `${label}: assets a browser can silently serve from a stale cache:\n  ${stale.join('\n  ')}`)
    .toEqual([]);
}

test('REQ-NFR-015 no asset /login declares can be served from a stale cache', async ({ page }) => {
  await page.goto('/login');
  await page.waitForSelector('[data-testid="login-email"]');
  await assertNoAssetCanGoStale(page, '/login');
});

test('REQ-NFR-015 no asset the authenticated shell declares can be served from a stale cache', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/harness');
  await assertNoAssetCanGoStale(page, '/harness');
});

test('REQ-NFR-015 every asset /login declares actually arrives', async ({ page }) => {
  await page.goto('/login');
  await page.waitForSelector('[data-testid="login-email"]');
  await assertAllArrive(page, '/login');
});

test('REQ-NFR-015 every asset the authenticated shell declares actually arrives', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/repos');
  await assertAllArrive(page, '/repos');
});

// The regression test for the specific defect: /login must remain a recognisable, usable sign-in
// screen even with the scoped-CSS bundle gone. Before the fix, blocking this one file collapsed
// the brand panel, the bullets and the form card into one column at x=0 — a pixel match for the
// owner's screenshot. The layout now lives in app.css, so the split must survive.
test('REQ-UI-001 /login still lays out when the scoped-CSS bundle is missing', async ({ page }) => {
  await page.route('**/TfLens.styles.css', r => r.fulfill({ status: 404, body: '' }));
  await page.setViewportSize({ width: 1280, height: 800 });
  await page.goto('/login');
  await page.waitForSelector('[data-testid="login-email"]');
  await page.waitForTimeout(500);

  const geo = await page.evaluate(() => {
    const brand = document.querySelector('[data-testid="auth-brand-panel"]') as HTMLElement | null;
    const panel = document.querySelector('.auth-panel') as HTMLElement | null;
    const bullet = document.querySelector('.auth-bullets li') as HTMLElement | null;
    const r = (e: HTMLElement | null) => (e ? e.getBoundingClientRect() : null);
    return {
      brand: r(brand) && { x: r(brand)!.x, w: r(brand)!.width },
      panelWidth: r(panel)?.width ?? null,
      bulletDisplay: bullet ? getComputedStyle(bullet).display : null,
      splitDirection: (() => {
        const s = document.querySelector('.auth-split') as HTMLElement | null;
        return s ? getComputedStyle(s).flexDirection : null;
      })(),
    };
  });

  // The two-column split is the layout's whole point at >=768px.
  expect(geo.splitDirection, 'the auth split collapsed to a single column').toBe('row');
  // The form card is a card, not a full-width band. 25rem = 400px.
  expect(geo.panelWidth, 'the sign-in card stretched full width').toBeLessThanOrEqual(420);
  // Each benefit line is a row (tick beside text), not a tick stacked above its label.
  expect(geo.bulletDisplay, 'the benefit bullets lost their flex row').toBe('flex');
  // The brand panel is padded, not flush against x=0.
  expect(geo.brand!.x, 'the brand panel sat flush at the viewport edge').toBeGreaterThanOrEqual(0);
  expect(geo.brand!.w, 'the brand panel took the whole viewport').toBeLessThan(1280);
});
