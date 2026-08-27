// REQ-FN-063 / REQ-FN-064 smoke — the BRD §13 parity gate, driven against the running app.
// Proves the quotable banner reports the REAL state of data/parity-last.json. On 2026-08-27 the
// procedure produced an EMPTY DIFF for the first time — the framework's fix to tf-metrics.sh
// (dedupe_sessions + pooled.session_duplicates_collapsed) removed the session-count disagreement,
// and TfLens now persists and reports the collapse count too — so the record was written and the
// banner flipped from NOT QUOTABLE to quotable.
import { test, expect } from '@playwright/test';
import { signIn, gotoScreen, testid, visualCheck, DESKTOP, MOBILE } from './_helpers';

test('parity gate: /export banner reports the recorded passing run as quotable', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/export');

  const banner = await testid(page, 'quotable-banner');
  const text = (await banner.innerText()).trim();
  console.log('BANNER: ' + text.replace(/\s+/g, ' '));

  // A passing record whose parser version and script hash both still describe this build.
  expect(text).toContain('QUOTABLE');
  expect(text).not.toContain('NOT QUOTABLE');
  expect(text).not.toContain('no parity run has ever been recorded');

  // The "no usable record" alert must be gone now that a record exists.
  expect(await page.locator('[data-testid="parity-none"]').count()).toBe(0);

  // ...and the parity FACTS table must be present, carrying the evidence.
  const facts = await testid(page, 'parity-facts');
  const factsText = (await facts.innerText()).trim();
  console.log('PARITY FACTS: ' + factsText.replace(/\s+/g, ' ').slice(0, 600));
  expect(factsText).toContain('2026-08-27');
  expect(factsText).toContain('960d12b4');

  // The compare output is the evidence, not a summary of it: it must say PASS.
  const output = await testid(page, 'parity-output');
  const outputText = (await output.innerText()).trim();
  console.log('COMPARE OUTPUT (tail): ' + outputText.replace(/\s+/g, ' ').slice(-300));
  expect(outputText).toContain('0 finding(s)');
  expect(outputText).toContain('PASS');

  await page.screenshot({ path: 'tests/.artifacts/parity/export-banner.png', fullPage: true });
});

test('parity gate: the export the compare ran against is reachable from the UI', async ({ page }) => {
  await signIn(page);
  await gotoScreen(page, '/export');

  // Dataset SHAs are what pins the comparison (REQ-FN-062); they must render, not be empty.
  const shas = await testid(page, 'dataset-shas');
  const shaText = (await shas.innerText()).trim();
  console.log('DATASET SHAS: ' + shaText.replace(/\s+/g, ' ').slice(0, 400));
  expect(await page.locator('[data-testid="dataset-shas-empty"]').count()).toBe(0);
  expect(shaText).toContain('TechieFlow');
  expect(shaText).toContain('TrBlazeUI');

  // Re-run the export from the button, so the smoke exercises the same code the verb ran.
  await page.click('[data-testid="export-now"]');
  await page.waitForTimeout(4000);
  const snapshots = await testid(page, 'snapshots');
  const snapText = (await snapshots.innerText()).trim();
  console.log('SNAPSHOTS: ' + snapText.replace(/\s+/g, ' ').slice(0, 400));
  expect(await page.locator('[data-testid="snapshots-empty"]').count()).toBe(0);

  await page.screenshot({ path: 'tests/.artifacts/parity/export-surface.png', fullPage: true });
});

// §4b visual-truth gate for /export at both widths: nothing overlaps, nothing leaves the viewport
// unreachably, and the page does not scroll sideways.
//
// Sign-in happens at DESKTOP and the viewport is resized afterwards, which is the pattern the rest of
// the suite uses: the shell's sidebar is `hidden md:flex`, so at 390px it never becomes visible and
// signIn's wait for it would time out. The geometry itself is delegated to the shared visualCheck,
// which already knows the two idioms a naive intersection test gets wrong — a control nested inside
// another, and a wide table deliberately parked inside an `overflow-x-auto` region.
test('parity gate: /export is visually clean at 1280 and 390', async ({ page }) => {
  await page.setViewportSize(DESKTOP);
  await signIn(page);
  await gotoScreen(page, '/export');

  const ids = ['quotable-banner', 'export-card', 'parity-facts', 'parity-output', 'dataset-shas', 'snapshots'];
  const present: string[] = [];
  for (const id of ids) {
    if (await page.locator(`[data-testid="${id}"]`).count() > 0) present.push(id);
  }
  console.log('CONTROLS ON /export: ' + present.join(', '));
  expect(present).toContain('quotable-banner');
  expect(present).toContain('parity-facts');

  for (const vp of [DESKTOP, MOBILE]) {
    await page.setViewportSize(vp);
    await page.waitForTimeout(900);

    const problems = await visualCheck(page, present, vp.width);
    console.log(`VISUAL @${vp.width}: ` + (problems.length ? problems.join(' | ') : 'clean'));
    expect(problems, `visual problems at ${vp.width}`).toEqual([]);

    const overflow = await page.evaluate(() =>
      document.documentElement.scrollWidth - document.documentElement.clientWidth);
    console.log(`HORIZONTAL OVERFLOW @${vp.width}: ${overflow}px`);
    expect(overflow, `the page scrolls sideways at ${vp.width}`).toBeLessThanOrEqual(2);

    // Viewport capture, not fullPage: the shell is a fixed-height flex layout whose page pane
    // scrolls internally, so a fullPage stitch repeats the header down a mostly-empty canvas.
    await page.screenshot({ path: `tests/.artifacts/parity/export-${vp.width}.png`, fullPage: false });
  }

  await page.setViewportSize(DESKTOP);
});
