// REQ-UI-073 / REQ-UI-074 / REQ-UI-075 — the approved brand mark in its three places (BRD-207..BRD-209).
// Black-box: reads the served document, its head and the computed geometry; screenshots go to
// tests/.artifacts/brand/.
import { test, expect, Page, BrowserContext } from '@playwright/test';
import { signIn, gotoScreen, DESKTOP, MOBILE } from './_helpers';

const SHOTS = 'tests/.artifacts/brand';
const GLYPH = '/brand/TfLens-appiconfg.svg';
/** The brand tile's fill, #0284C7, in both themes (ADR-033). */
const TILE_FILL = 'rgb(2, 132, 199)';

type Theme = 'dark' | 'light';

/** The theme is a first-byte cookie (ADR-014): no cookie is dark, `light` is light. */
async function setTheme(context: BrowserContext, baseURL: string, theme: Theme) {
  await context.clearCookies({ name: 'tflens-theme' });
  if (theme === 'light') {
    await context.addCookies([{ name: 'tflens-theme', value: 'light', url: baseURL }]);
  }
}

/** Collects console errors so a missing icon (the old `favicon` 404) cannot hide again. */
function watchErrors(page: Page): string[] {
  const errors: string[] = [];
  page.on('console', m => { if (m.type() === 'error') errors.push(m.text()); });
  page.on('response', r => { if (r.status() >= 400) errors.push(`${r.status()} ${r.url()}`); });
  return errors;
}

/** Reads the BRD-207 head off the live document. */
async function readHead(page: Page) {
  return page.evaluate(() => {
    const attr = (sel: string, a: string) => document.head.querySelector(sel)?.getAttribute(a) ?? null;
    return {
      title: document.title,
      svgIcon: attr('link[rel="icon"][type="image/svg+xml"]', 'href'),
      pngIcon: attr('link[rel="icon"][sizes="32x32"]', 'href'),
      touch: attr('link[rel="apple-touch-icon"]', 'href'),
      ogTitle: attr('meta[property="og:title"]', 'content'),
      ogType: attr('meta[property="og:type"]', 'content'),
      ogDesc: attr('meta[property="og:description"]', 'content'),
      ogImage: attr('meta[property="og:image"]', 'content'),
      ogW: attr('meta[property="og:image:width"]', 'content'),
      ogH: attr('meta[property="og:image:height"]', 'content'),
      twCard: attr('meta[name="twitter:card"]', 'content'),
      twImage: attr('meta[name="twitter:image"]', 'content'),
    };
  });
}

async function expectHead(page: Page, baseURL: string, titleIs: RegExp) {
  const head = await readHead(page);
  expect(head.title, 'document title').toMatch(titleIs);
  expect(head.svgIcon).toBe('/brand/TfLens-appicon.svg');
  expect(head.pngIcon).toBe('/brand/favicon-32.png');
  expect(head.touch).toBe('/brand/apple-touch-icon.png');
  expect(head.ogTitle).toBe('TfLens');
  expect(head.ogType).toBe('website');
  expect(head.ogDesc).toBe('A read-only lens over TechieFlow and AI-First-Playbook telemetry');
  expect(head.ogImage, 'og:image is absolute, from the request host').toBe(new URL('/brand/TfLens.png', baseURL).href);
  expect(head.ogW).toBe('1024');
  expect(head.ogH).toBe('1024');
  expect(head.twCard).toBe('summary');
  expect(head.twImage).toBe(head.ogImage);
  for (const href of [head.svgIcon!, head.pngIcon!, head.touch!, head.ogImage!]) {
    const res = await page.request.get(new URL(href, baseURL).href);
    expect(res.status(), `${href} answers`).toBe(200);
  }
}

async function noHorizontalOverflow(page: Page) {
  const over = await page.evaluate(() => document.documentElement.scrollWidth - document.documentElement.clientWidth);
  expect(over, 'horizontal overflow').toBeLessThanOrEqual(0);
}

/** Box, radius and loaded state of the visible element carrying a test id. */
async function boxOf(page: Page, testid: string) {
  return page.locator(`[data-testid="${testid}"]:visible`).first().evaluate(el => {
    const r = el.getBoundingClientRect();
    const cs = getComputedStyle(el);
    // The mark is the mockup's `.mark` box: a filled tile holding an <svg> that draws the glyph file.
    const glyph = el.querySelector('svg image');
    const g = glyph?.getBoundingClientRect();
    return { x: r.x, y: r.y, w: r.width, h: r.height, radius: cs.borderTopLeftRadius, fill: cs.backgroundColor,
             src: glyph?.getAttribute('href') ?? null, loaded: !!g && g.width > 0 && g.height > 0 };
  });
}

for (const theme of ['dark', 'light'] as Theme[]) {
  for (const vp of [DESKTOP, MOBILE]) {
    test(`REQ-UI-073 REQ-UI-074 /login head and brand tile @${vp.width} ${theme}`, async ({ page, context, baseURL }) => {
      await setTheme(context, baseURL!, theme);
      await page.setViewportSize(vp);
      const errors = watchErrors(page);
      await page.goto('/login');
      await page.waitForSelector('[data-testid="brand-mark"]');
      await page.waitForLoadState('networkidle').catch(() => {});

      expect(await page.evaluate(() => document.documentElement.classList.contains('dark'))).toBe(theme === 'dark');
      await expectHead(page, baseURL!, /^Sign in · TfLens$/);

      const mark = await boxOf(page, 'brand-mark');
      const desk = vp.width >= 768;
      expect(mark.src).toBe(GLYPH);
      expect(mark.fill, 'the tile is the brand sky blue').toBe(TILE_FILL);
      expect(mark.loaded, 'the glyph is drawn').toBe(true);
      expect(Math.round(mark.w)).toBe(desk ? 44 : 34);
      expect(Math.round(mark.h)).toBe(desk ? 44 : 34);
      expect(mark.radius).toBe(desk ? '12px' : '9px');
      // The tile's own pixel is the brand colour in both themes (the old box took the theme's primary).
      const word = page.locator('.auth-wordmark');
      await expect(word).toHaveText('TfLens');
      const wb = await word.boundingBox();
      expect(wb!.x, 'the wordmark sits right of the tile').toBeGreaterThan(mark.x + mark.w);
      await noHorizontalOverflow(page);
      await page.screenshot({ path: `${SHOTS}/login-${vp.width}-${theme}.png` });
      expect(errors.filter(e => !/websocket|reconnect|DevTools/i.test(e)), 'console / http errors').toEqual([]);
    });
  }
}

for (const route of ['/register', '/forgot-password']) {
  test(`REQ-UI-074 ${route} carries the same tile`, async ({ page }) => {
    await page.setViewportSize(DESKTOP);
    await page.goto(route);
    await page.waitForLoadState('networkidle').catch(() => {});
    await expect(page.locator('[data-testid="brand-mark"]')).toBeVisible();
    const mark = await boxOf(page, 'brand-mark');
    expect(mark.src).toBe(GLYPH);
      expect(mark.fill, 'the tile is the brand sky blue').toBe(TILE_FILL);
    expect(mark.loaded).toBe(true);
    expect(Math.round(mark.w)).toBe(44);
  });
}

for (const theme of ['dark', 'light'] as Theme[]) {
  test(`REQ-UI-073 REQ-UI-075 /misses sidebar lockup @1280 ${theme}`, async ({ page, context, baseURL }) => {
    await page.setViewportSize(DESKTOP);
    await signIn(page);
    await setTheme(context, baseURL!, theme);
    const errors = watchErrors(page);
    await gotoScreen(page, '/misses');
    expect(await page.evaluate(() => document.documentElement.classList.contains('dark'))).toBe(theme === 'dark');
    await expectHead(page, baseURL!, /^Misses & rework · TfLens$/);

    const head = await page.locator('[data-testid="app-sidebar"] .tflens-sb-head').first().boundingBox();
    expect(Math.round(head!.height), 'sidebar head height').toBe(64);
    const mark = await boxOf(page, 'brand-mark');
    expect(mark.src).toBe(GLYPH);
      expect(mark.fill, 'the tile is the brand sky blue').toBe(TILE_FILL);
    expect(mark.loaded).toBe(true);
    expect([Math.round(mark.w), Math.round(mark.h), mark.radius]).toEqual([32, 32, '8px']);

    const wm = await boxOf(page, 'brand-wordmark');
    expect(Math.round(wm.h * 100) / 100, 'wordmark is 58% of the tile').toBeCloseTo(18.56, 1);
    expect(wm.x).toBeGreaterThan(mark.x + mark.w);
    expect(Math.abs((wm.y + wm.h / 2) - (mark.y + mark.h / 2)), 'wordmark centred on the tile').toBeLessThanOrEqual(1);
    // Light: the wordmark region of TfLens-horizontal.svg; dark: the white PNG — one box, its background
    // chosen by `html.dark`, never a filter.
    const wmStyle = await page.locator('[data-testid="brand-wordmark"]:visible').first().evaluate(el => {
      const cs = getComputedStyle(el);
      return { bg: cs.backgroundImage, filter: cs.filter, kids: el.children.length };
    });
    expect(wmStyle.bg).toContain(theme === 'dark' ? '/brand/TfLens-wordmark-white.png' : '/brand/TfLens-horizontal.svg');
    expect(wmStyle.filter, 'never a colour filter').toBe('none');
    expect(wmStyle.kids, 'the wordmark box holds no child image').toBe(0);
    const wmFile = theme === 'dark' ? '/brand/TfLens-wordmark-white.png' : '/brand/TfLens-horizontal.svg';
    expect((await page.request.get(wmFile)).status(), `${wmFile} answers 200`).toBe(200);
    expect(await page.locator('text=lens on TechieFlow & Playbook').count(), 'subtitle retired').toBe(0);
    await noHorizontalOverflow(page);
    await page.screenshot({ path: `${SHOTS}/misses-1280-${theme}.png` });

    // Collapsed to the icon rail: the tile alone.
    await page.locator('[data-testid="sidebar-trigger"]').first().click();
    await expect(page.locator('[data-testid="brand-wordmark"]').first()).toBeHidden({ timeout: 10_000 });
    await page.waitForTimeout(500);
    const railMark = await boxOf(page, 'brand-mark');
    expect([Math.round(railMark.w), Math.round(railMark.h)]).toEqual([32, 32]);
    await page.screenshot({ path: `${SHOTS}/misses-1280-${theme}-collapsed.png` });
    await page.locator('[data-testid="sidebar-trigger"]').first().click();
    expect(errors.filter(e => !/websocket|reconnect|DevTools/i.test(e)), 'console / http errors').toEqual([]);
  });

  test(`REQ-UI-075 /misses phone menu head @390 ${theme}`, async ({ page, context, baseURL }) => {
    await page.setViewportSize(DESKTOP);
    await signIn(page);
    await setTheme(context, baseURL!, theme);
    await page.setViewportSize(MOBILE);
    await page.goto('/misses');
    await page.locator('[data-testid="sidebar-trigger"]').first().waitFor({ state: 'visible', timeout: 30_000 });
    await page.waitForTimeout(1500);
    await expectHead(page, baseURL!, /^Misses & rework · TfLens$/);
    await noHorizontalOverflow(page);
    await page.screenshot({ path: `${SHOTS}/misses-390-${theme}.png` });

    await page.locator('[data-testid="sidebar-trigger"]').first().click();
    await page.waitForTimeout(1200);
    const mark = await boxOf(page, 'brand-mark');
    expect([Math.round(mark.w), Math.round(mark.h)]).toEqual([32, 32]);
    expect(mark.loaded).toBe(true);
    const wm = await boxOf(page, 'brand-wordmark');
    expect(wm.w, 'wordmark shows in the phone menu').toBeGreaterThan(70);
    await page.screenshot({ path: `${SHOTS}/misses-390-${theme}-menu.png` });
  });
}

test('REQ-UI-073 the error page carries the head and names itself', async ({ page, baseURL }) => {
  // An unknown URL is answered by a bare server 404 with no document at all (pre-existing; no Blazor page
  // renders), so the error page is the one non-report route BRD-207 names that this suite can open.
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await page.goto('/Error');
  await page.waitForLoadState('networkidle').catch(() => {});
  await expectHead(page, baseURL!, /^Error · TfLens$/);
});
