import { chromium } from 'playwright';
const b = await chromium.launch();
for (const w of [1280, 390]) {
  const p = await b.newPage({ viewport: { width: w, height: 900 } });
  const errs=[]; p.on('pageerror',e=>errs.push(e.message));
  await p.goto('file:///mnt/c/1MyCode/TfLens/docs/mockups/repos.html');
  await p.click('#tab-import');
  // horizontal overflow check
  const of = await p.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth + 1);
  await p.screenshot({ path: `/tmp/repos-${w}.png`, fullPage: false });
  console.log(w, 'overflow:', of, 'errors:', errs.length);
  await p.close();
}
await b.close();
