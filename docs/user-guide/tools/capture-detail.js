#!/usr/bin/env node
// Usage:  GUIDE_USER=... GUIDE_PASS=... node capture-detail.js <storeprofile|storeleaderprofile|actioncenterdetail|reportdetail|all> [en|ar]
// The "inside" pages: reached the same way a user reaches them (the link on the parent page). Navigation and tab
// clicks only - nothing is saved, edited, closed or downloaded.
const fs = require('fs');
const path = require('path');
const { openBrowser, BASE, OUT, slug } = require('./capture');
const { maskPage } = require('./mask');

const wait = ms => new Promise(r => setTimeout(r, ms));

async function prepare(page, lang) {
  const leaks = await maskPage(page, lang);
  if (leaks.length) throw new Error('real names still visible: ' + leaks.join(' | '));
  await page.evaluate(word => { const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT); let n; while ((n = w.nextNode())) if (n.nodeValue.includes('${ntdL.provisionalBadge}')) n.nodeValue = n.nodeValue.replace('${ntdL.provisionalBadge}', word); }, lang === 'ar' ? 'مبدئي' : 'Provisional');
  // two portal display glitches that would look like errors in a printed guide: raw "[object Object]" cells and a raw &#x27;
  await page.evaluate(() => {
    document.querySelectorAll('td').forEach(td => { if (td.innerText.includes('[object Object]')) td.textContent = 'The reasons behind the score are listed here.'; });
    const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT); let n; while ((n = w.nextNode())) if (n.nodeValue.includes('&#x27;')) n.nodeValue = n.nodeValue.replace(/&#x27;/g, "'");
  });
  await page.addStyleTag({ content: '*{animation:none!important;transition:none!important}' });
  const h = await page.evaluate(() => document.documentElement.scrollHeight);
  await page.setViewportSize({ width: 1440, height: Math.min(h + 50, 9000) });
  await wait(1200);
}
const clip = async (page, dir, file, locator, maxH) => {
  const box = await locator.boundingBox(); if (!box || box.height < 20) return null;
  await page.screenshot({ path: path.join(dir, file + '.png'), clip: { x: box.x, y: box.y, width: box.width, height: maxH ? Math.min(box.height, maxH) : box.height } });
  return file;
};
const title = async loc => slug((await loc.innerText()).split('\n').map(s => s.trim()).filter(Boolean)[0] || 'card');

const pages = {
  async storeprofile(page, dir, lang) {
    await page.goto(BASE + '/home/dashboard/stores', { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('#storeCardsContainer a[href*="storeprofile" i]', { timeout: 120000 });
    const href = await page.evaluate(() => document.querySelector('#storeCardsContainer a[href*="storeprofile" i]').getAttribute('href'));
    await page.goto(BASE + href, { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => document.querySelector('#spKpis') && document.querySelector('#spKpis').innerText.trim().length > 5 && document.querySelector('.sp-tabs'), null, { timeout: 90000 });
    await wait(5000);
    await prepare(page, lang);
    const out = [];
    for (const [file, sel, maxH] of [['hero', '.sp-hero'], ['health', '#spHealthSummary'], ['kpis', '#spKpis'], ['tabs', '.sp-tabs']]) out.push(await clip(page, dir, file, page.locator(sel).first(), maxH));
    const tabs = await page.locator('.sp-tabs > *').count();
    for (let i = 0; i < tabs; i++) {
      const tab = page.locator('.sp-tabs > *').nth(i);
      await tab.click(); await wait(5000);
      await prepare(page, lang);
      const key = ((await page.locator('section.sp-panel.active').first().getAttribute('id')) || 'tab' + i).replace('spPanel-', '');
      const cards = page.locator('section.sp-panel.active .sp-card');
      const n = await cards.count();
      if (process.env.GUIDE_DEBUG) console.log('tab', i, key, 'cards', n);
      for (let j = 0; j < n; j++) out.push(await clip(page, dir, `${key}_${j + 1}_${await title(cards.nth(j))}`.slice(0, 70), cards.nth(j), 700));
    }
    return out;
  },
  async storeleaderprofile(page, dir, lang) {
    await page.goto(BASE + '/home/dashboard/scorecard', { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('#scBody a[href]', { timeout: 120000 });
    const href = await page.evaluate(() => document.querySelector('#scBody a[href]').getAttribute('href'));
    await page.goto(BASE + href, { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => document.querySelector('#slpKpis') && document.querySelector('#slpKpis').innerText.trim().length > 5, null, { timeout: 90000 });
    await wait(6000);
    await prepare(page, lang);
    const out = [];
    out.push(await clip(page, dir, 'hero', page.locator('.slp-hero').first()));
    const k = page.locator('#slpKpis .kpi-card');
    for (let i = 0; i < await k.count(); i++) out.push(await clip(page, dir, 'kpi_' + await title(k.nth(i)), k.nth(i)));
    const cols = page.locator('#slpBody .row.g-3 > [class*=col-]');
    for (let i = 0; i < await cols.count(); i++) out.push(await clip(page, dir, `card_${i + 1}_${await title(cols.nth(i))}`.slice(0, 70), cols.nth(i), 700));
    out.push(await clip(page, dir, 'comments', page.locator('#slpBody > .chart-card').last(), 520));
    return out;
  },
  async actioncenterdetail(page, dir, lang) {
    await page.goto(BASE + '/home/dashboard/actioncenter', { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('#achBody a[href*="actioncenterdetail" i]', { timeout: 120000 });
    const href = await page.evaluate(() => document.querySelector('#achBody a[href*="actioncenterdetail" i]').getAttribute('href'));
    await page.goto(BASE + href, { waitUntil: 'domcontentloaded' });
    await page.waitForFunction(() => document.querySelector('#acdContent') && document.querySelector('#acdContent').innerText.trim().length > 50, null, { timeout: 90000 });
    await wait(6000);
    await prepare(page, lang);
    const out = [];
    out.push(await clip(page, dir, 'header', page.locator('.page-header').first()));
    const blocks = page.locator('#acdContent > *');
    for (let i = 0; i < await blocks.count(); i++) {
      const b = blocks.nth(i); if (!(await b.isVisible())) continue;
      out.push(await clip(page, dir, `${String(i + 1).padStart(2, '0')}_${await title(b)}`.slice(0, 70), b, 640));
    }
    return out;
  },
  async reportdetail(page, dir, lang) {
    await page.goto(BASE + '/home/dashboard/reports', { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('.row.g-4.mb-4 a[href]', { timeout: 120000 });
    const href = await page.evaluate(() => document.querySelector('.row.g-4.mb-4 a[href]').getAttribute('href'));
    await page.goto(BASE + href, { waitUntil: 'domcontentloaded' });
    await page.waitForSelector('.card-dark', { timeout: 90000 });
    await wait(4000);
    await prepare(page, lang);
    const out = [];
    out.push(await clip(page, dir, 'header', page.locator('.page-header').first()));
    out.push(await clip(page, dir, 'filters', page.locator('.card-dark.mb-4').first()));
    out.push(await clip(page, dir, 'download', page.locator('.card-dark.text-center').first()));
    return out;
  },
};

(async () => {
  const [which = 'all', lang = 'en'] = process.argv.slice(2);
  if (!process.env.GUIDE_USER || !process.env.GUIDE_PASS) throw new Error('Set GUIDE_USER and GUIDE_PASS');
  const keys = which === 'all' ? Object.keys(pages) : [which];
  const { browser, page } = await openBrowser(lang);
  try {
    for (const k of keys) {
      const dir = path.join(OUT, lang, k); fs.mkdirSync(dir, { recursive: true });
      await page.setViewportSize({ width: 1440, height: 1100 });
      const done = (await pages[k](page, dir, lang)).filter(Boolean);
      console.log(`${lang}/${k}: ${done.length} images -> ${done.join(', ')}`);
    }
  } finally { await browser.close(); }
})().catch(e => { console.error(e.message); process.exit(1); });
