import { test, expect, Page } from '@playwright/test';
import { signIn, gotoScreen, USER1, USER2, DESKTOP, MOBILE } from './_helpers';

/**
 * REQ-UI-072 / REQ-FN-139 — the price providers screen (docs/mockups/prices.html).
 *
 * The page exists so a money figure can be traced to a published rate and a date somebody checked it.
 * These clauses hold it to that: the four providers the estate runs on are listed, every provider says
 * whether its rates were read from an endpoint or typed from a page, a long rate list is paged and can
 * be filtered, and the standing note that these are prices rather than bills is on the face of the page.
 *
 * Every test that changes the book touches only a probe provider and removes it again, because the book
 * is the one rate card every figure is priced from. The tests that only read sign in as the demo user
 * (USER1); the one that writes signs in as test user 2 (USER2), so the demo account never makes a change.
 */
const PROBE = 'ui-probe';
const tid = (page: Page, id: string) => page.locator(`[data-testid="${id}"]`);

async function removeProbe(page: Page) {
  await gotoScreen(page, '/prices');
  const btn = tid(page, `prices-remove-${PROBE}`);
  if ((await btn.count()) > 0) {
    await btn.click();
    await expect(tid(page, `prices-provider-${PROBE}`)).toHaveCount(0);
  }
}

test.describe('price providers', () => {
  test('REQ-UI-072 lists the seeded providers with their rates and their provenance', async ({ page }) => {
    await signIn(page, USER1);
    await gotoScreen(page, '/prices');

    // The standing note is the one thing on this page that must never be missable: every figure it
    // feeds is a price applied to measured tokens, and none of it is money anybody was billed.
    await expect(tid(page, 'prices-standing-note')).toBeVisible();
    await expect(tid(page, 'prices-standing-note')).toContainText('never as spend');
    await expect(tid(page, 'prices-model-count')).toHaveText(/^\d+ rates? over \d+ providers?$/);

    for (const id of ['anthropic', 'openai', 'opencode-go', 'openrouter']) {
      await expect(tid(page, `prices-provider-${id}`)).toBeVisible();
      // How current the rates are is stated per provider, because a price list nobody has checked
      // for a year is a different claim from one checked today.
      await expect(tid(page, `prices-checked-${id}`)).toBeVisible();
    }

    // Endpoint and typed rates are different claims, with different labels.
    await expect(tid(page, 'prices-kind-openrouter')).toHaveText('read from its endpoint');
    for (const id of ['anthropic', 'openai', 'opencode-go']) {
      await expect(tid(page, `prices-kind-${id}`)).toHaveText('typed from the published page');
    }

    // The rates the estate is actually priced at.
    await expect(tid(page, 'prices-table-anthropic')).toContainText('claude-fable-5');
    await expect(tid(page, 'prices-table-anthropic')).toContainText('$50');
  });

  test('REQ-UI-072 a long rate list is paged five at a time', async ({ page }) => {
    await signIn(page, USER1);
    await gotoScreen(page, '/prices');

    const rows = tid(page, 'prices-table-anthropic').locator('tbody tr');
    await expect(rows).toHaveCount(5);
    await expect(tid(page, 'prices-provider-anthropic')).toContainText('5 of 10 rates shown');
    await expect(tid(page, 'prices-page-prev-anthropic')).toBeDisabled();

    const first = await rows.first().innerText();
    await tid(page, 'prices-page-next-anthropic').click();
    await expect(rows.first()).not.toHaveText(first);
    await expect(tid(page, 'prices-provider-anthropic')).toContainText('page 2 of 2');
    await expect(tid(page, 'prices-page-next-anthropic')).toBeDisabled();
  });

  test('REQ-UI-072 the endpoint provider can be filtered by model id', async ({ page }) => {
    await signIn(page, USER1);
    await gotoScreen(page, '/prices');
    test.skip((await tid(page, 'prices-filter-openrouter').count()) === 0, 'OpenRouter holds too few rates to be given a filter');

    await tid(page, 'prices-filter-openrouter').fill('claude');
    // The filter is applied by a server round trip; read the rows only once the page says it filtered,
    // or a loaded run reads the unfiltered first page (2026-09-11, full verify run).
    await expect(tid(page, 'prices-provider-openrouter')).toContainText('filtered from');
    await expect(tid(page, 'prices-table-openrouter').locator('tbody tr td:first-child').first()).toContainText(/claude/i);
    const models = await tid(page, 'prices-table-openrouter').locator('tbody tr td:first-child').allInnerTexts();
    expect(models.length).toBeGreaterThan(0);
    for (const m of models) expect(m.toLowerCase()).toContain('claude');
  });

  test('REQ-UI-072 a provider can be added, given a rate, and removed', async ({ page }) => {
    await signIn(page, USER2);
    await removeProbe(page);

    try {
      await tid(page, 'prices-new-name').fill(PROBE);
      await tid(page, 'prices-new-source').fill('https://example.invalid/pricing');
      await tid(page, 'prices-add').click();

      await expect(tid(page, `prices-provider-${PROBE}`)).toBeVisible();
      await expect(tid(page, `prices-provider-${PROBE}`)).toContainText('never priced at zero');
      await expect(tid(page, 'prices-message')).toContainText(`${PROBE} added`);

      await tid(page, 'prices-rate-provider').selectOption(PROBE);
      await tid(page, 'prices-rate-model').fill('ui-probe-model');
      await tid(page, 'prices-rate-in').fill('1.5');
      await tid(page, 'prices-rate-out').fill('7.5');
      await tid(page, 'prices-save-rate').click();

      await expect(tid(page, `prices-table-${PROBE}`)).toContainText('ui-probe-model');
      await expect(tid(page, `prices-table-${PROBE}`)).toContainText('$7.5');

      // A negative rate is refused and nothing is saved.
      await tid(page, 'prices-rate-model').fill('ui-probe-negative');
      await tid(page, 'prices-rate-in').fill('-1');
      await tid(page, 'prices-save-rate').click();
      await expect(tid(page, 'prices-message')).toContainText('Nothing was saved');
      await expect(tid(page, `prices-table-${PROBE}`)).not.toContainText('ui-probe-negative');
    } finally {
      await removeProbe(page);
    }
  });

  /**
   * REQ-UI-072 — the three things this page reads wrong when the library's own controls are not the
   * ones doing the work. Each was a page-local CSS workaround until TrBlazeUI 2.1.0-ci.10 shipped the
   * parameter for it (TR-025, TR-031, TR-032, TR-016); each is measured here rather than eyeballed, so a
   * regression in the library or a re-added workaround is a failing test and not a screenshot nobody
   * compares. Run at both widths: the rate headers were readable at 1280 and ran together at 390.
   */
  for (const size of [DESKTOP, MOBILE]) {
    test(`REQ-UI-072 the figure headers sit over their figures and the provider list keeps its arrow @${size.width}`, async ({ page }) => {
      // Sign in at desktop width and then narrow: below 768px the shell's sidebar is off-canvas
      // (owner decision 2026-09-11), so the shared helpers' "wait for a VISIBLE app-sidebar" never
      // comes true on a phone. The page under test is reached directly instead.
      await page.setViewportSize(DESKTOP);
      await signIn(page, USER1);
      await page.setViewportSize(size);
      await page.goto('/prices');
      await page.waitForSelector('[data-testid="prices-table-anthropic"]', { timeout: 30_000 });
      await page.waitForLoadState('networkidle').catch(() => {});
      await page.waitForTimeout(1500);

      // GUARD FIRST. Every assertion below reads a computed style to decide whether a page-local
      // CSS workaround is gone and the library's own parameter is doing the work. If the scoped
      // stylesheet failed to load at all — TfLens has served `TfLens.<hash>.styles.css` as zero
      // bytes under gzip while several builds shared one obj/ — every one of those reads would say
      // "the workaround is gone" for the wrong reason, and the suite would bless a broken page.
      // So prove the sheet is live on three rules only this page's .razor.css declares, and fail
      // loudly rather than measure anything on a page that has no scoped CSS.
      const scoped = await page.evaluate(() => {
        const flush = document.querySelector('[data-testid="prices-table-anthropic"]')!
          .closest('[class*="tflens-prices-flush"]');
        const foot = document.querySelector('.tflens-prices-foot');
        return {
          flushPadding: flush ? getComputedStyle(flush).padding : null,
          footBorderTop: foot ? getComputedStyle(foot).borderTopWidth : null,
        };
      });
      expect(scoped.flushPadding, 'scoped CSS is live: the table card body is flush').toBe('0px');
      expect(scoped.footBorderTop, 'scoped CSS is live: the card footer carries its rule').toBe('1px');

      // TR-031 — the column's Align must reach the header's own flex label box, not just the <th>.
      // A `text-right` on the th alone leaves the label at the left of the column, which is what put
      // "Input" and "Output" over nothing and made them read as one word on a phone.
      const heads = await tid(page, 'prices-table-anthropic').locator('thead th').evaluateAll(ths =>
        ths.map(th => {
          const box = th.querySelector('div');
          const label = box ?? th;
          return {
            text: (th.textContent || '').trim(),
            justify: getComputedStyle(label).justifyContent,
            align: getComputedStyle(th).textAlign,
            labelRight: label.getBoundingClientRect().right,
            thRight: th.getBoundingClientRect().right,
            padRight: parseFloat(getComputedStyle(th).paddingRight) || 0,
          };
        }));
      expect(heads.length, 'model plus four figure columns').toBe(5);

      for (const h of heads.slice(1)) {
        expect(h.justify, `${h.text}: the header label box is pushed to the end of the column`).toBe('flex-end');
        expect(h.align, `${h.text}: the header cell is right-aligned`).toBe('right');
        // And the label really does paint over the right edge of the column, not merely claim to.
        expect(Math.abs(h.thRight - h.padRight - h.labelRight),
          `${h.text}: label right edge sits on the column's content right edge`).toBeLessThanOrEqual(2);
      }
      expect(heads[0].justify, 'the Model column is left as it is').not.toBe('flex-end');

      // TR-025 — the cell density is the grid's own `Density="Compact"` with each column's padding
      // merged over it, not a page rule reaching into `th`/`td`. The numbers are the mockup's:
      // a 36px header row (10px around 12px text) and a 12px/16px body cell.
      const cells = await page.evaluate(() => {
        const t = document.querySelector('[data-testid="prices-table-anthropic"]')!;
        const read = (el: Element) => { const c = getComputedStyle(el); return { top: c.paddingTop, right: c.paddingRight, bottom: c.paddingBottom, left: c.paddingLeft, font: c.fontSize, h: Math.round(el.getBoundingClientRect().height) }; };
        return { th: read(t.querySelector('thead th')!), td: read(t.querySelector('tbody td')!) };
      });
      expect(cells.th.left, 'header cell keeps the mockup 16px gutter, not Compact 10px').toBe('16px');
      expect(cells.th.right).toBe('16px');
      expect(cells.th.font, 'header text is the mockup 12px').toBe('12px');
      expect(cells.th.h, 'header row is the Compact 36px box').toBe(36);
      expect(cells.td.left, 'body cell keeps the mockup 16px gutter').toBe('16px');
      expect(cells.td.top, 'body cell keeps the mockup 12px above').toBe('12px');
      expect(cells.td.bottom).toBe('12px');

      // TR-032 — NativeSelect turns the browser arrow off and paints its own as a background image.
      // The class carrying it used to be dropped by the class merge, leaving a bare text box.
      const select = await tid(page, 'prices-rate-provider').evaluate(el => ({
        tag: el.tagName.toLowerCase(),
        image: getComputedStyle(el).backgroundImage,
        appearance: getComputedStyle(el).appearance,
      }));
      expect(select.tag, 'the Provider list is a real <select>').toBe('select');
      expect(select.image, 'NativeSelect paints its own chevron').toContain('data:image/svg');
      expect(select.image).not.toBe('none');

      // TR-016 — the endpoint claim and the typed claim are two visibly different badges, and the
      // endpoint one is the blue the mockup draws (badge.info), not a bordered default.
      const kinds = await page.evaluate(() => {
        const read = (id: string) => {
          const el = document.querySelector(`[data-testid="${id}"]`);
          if (!el) return null;
          const cs = getComputedStyle(el);
          return { tag: el.tagName.toLowerCase(), bg: cs.backgroundColor, fg: cs.color };
        };
        return { endpoint: read('prices-kind-openrouter'), typed: read('prices-kind-anthropic') };
      });
      expect(kinds.endpoint, 'the endpoint provider carries a kind badge').not.toBeNull();
      expect(kinds.typed, 'a typed provider carries a kind badge').not.toBeNull();
      expect(kinds.endpoint!.bg, 'the endpoint claim is tinted, not transparent').not.toMatch(/rgba\(0, 0, 0, 0\)/);
      expect(kinds.endpoint!.bg, 'the two claims are visibly different').not.toBe(kinds.typed!.bg);

      // Nothing on the page may push the document sideways at either width.
      const overflow = await page.evaluate(() => {
        const de = document.documentElement;
        return { scroll: de.scrollWidth, client: de.clientWidth };
      });
      expect(overflow.scroll, `no horizontal overflow @${size.width}`).toBeLessThanOrEqual(overflow.client + 2);
    });
  }
});
