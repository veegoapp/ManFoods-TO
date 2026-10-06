#!/usr/bin/env node
// Usage:  GUIDE_USER=... GUIDE_PASS=... node capture.js <page|all> [en|ar]
// Logs in with a VIEW-ONLY portal user, opens each page, masks names in the browser,
// and saves one PNG per element into docs/user-guide/images/<lang>/<page>/.
// Credentials come from the environment only. Nothing is clicked except sign-in.
const fs = require('fs');
const path = require('path');
const { chromium } = require(process.env.PLAYWRIGHT_PATH || 'playwright');
const { maskPage } = require('./mask');
const PAGES = require('./pages');

const BASE = process.env.GUIDE_BASE || 'https://mcd-crew-hub.runasp.net';
const CDN = process.env.GUIDE_CDN_DIR; // optional local copy of chart.js@4.4.0 + bootstrap-icons@1.11.3 when jsdelivr is blocked
const OUT = path.join(__dirname, '..', 'images');
const slug = s => s.toLowerCase().replace(/[^a-z0-9]+/g, '_').replace(/^_|_$/g, '');

async function openBrowser(lang) {
  const browser = await chromium.launch({ executablePath: process.env.CHROMIUM_PATH || '/opt/pw-browsers/chromium' });
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 1100 }, deviceScaleFactor: 1.5 });
  if (CDN) await ctx.route(/cdn\.jsdelivr\.net\/npm\//, route => {
    const u = route.request().url(); let f = null, ct = 'application/octet-stream';
    if (/chart\.js@4\.4\.0.*chart\.umd/.test(u)) { f = CDN + '/chart.js/dist/chart.umd.js'; ct = 'application/javascript'; }
    else if (/bootstrap-icons@1\.11\.3\/font\/bootstrap-icons(\.min)?\.css/.test(u)) { f = CDN + '/bootstrap-icons/font/bootstrap-icons.min.css'; ct = 'text/css'; }
    else if (/bootstrap-icons@1\.11\.3\/font\/fonts\/([^?#]+)/.test(u)) { f = CDN + '/bootstrap-icons/font/fonts/' + u.match(/fonts\/([^?#]+)/)[1]; ct = /woff2/.test(f) ? 'font/woff2' : 'font/woff'; }
    return f && fs.existsSync(f) ? route.fulfill({ body: fs.readFileSync(f), contentType: ct, headers: { 'access-control-allow-origin': '*' } }) : route.abort();
  });
  await ctx.addCookies([{ name: 'mf-lang', value: lang, url: BASE }]);
  const page = await ctx.newPage();
  await page.goto(BASE + '/login', { waitUntil: 'networkidle' });
  await page.fill('#Email', process.env.GUIDE_USER);
  await page.fill('#Password', process.env.GUIDE_PASS);
  await Promise.all([page.waitForNavigation({ waitUntil: 'networkidle' }), page.press('#Password', 'Enter')]);
  return { browser, page };
}

async function shoot(page, key, lang) {
  const def = PAGES[key];
  const dir = path.join(OUT, lang, key); fs.mkdirSync(dir, { recursive: true });
  await page.setViewportSize({ width: 1440, height: 1100 });
  await page.goto(BASE + def.url, { waitUntil: 'domcontentloaded' });
  await page.waitForFunction(sel => [...document.querySelectorAll(sel)].some(e => e.offsetParent !== null), def.ready || 'canvas, tbody tr', { timeout: def.timeout || 90000 });
  await page.waitForTimeout(3000);
  const leaks = await maskPage(page, lang);
  if (leaks.length) throw new Error(`[${key}] real names still visible: ${leaks.join(' | ')}`);
  // the portal sometimes shows an unrendered template placeholder where a "Provisional" badge belongs
  await page.evaluate(word => { const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT); let n; while ((n = w.nextNode())) if (n.nodeValue.includes('${ntdL.provisionalBadge}')) n.nodeValue = n.nodeValue.replace('${ntdL.provisionalBadge}', word); }, lang === 'ar' ? 'مبدئي' : 'Provisional');
  await page.addStyleTag({ content: '*{animation:none!important;transition:none!important}' });
  if (def.open) await page.evaluate(tags => tags.forEach(t => document.querySelectorAll(t).forEach(d => d.setAttribute('open', ''))), def.open);
  const h = await page.evaluate(() => document.documentElement.scrollHeight);
  await page.setViewportSize({ width: 1440, height: Math.min(h + 50, 9000) });
  await page.waitForTimeout(1200);
  const up = (loc, cls) => cls ? loc.locator(`xpath=ancestor-or-self::*[contains(concat(" ",normalize-space(@class)," ")," ${cls} ") or local-name()="${cls}"][1]`) : loc;
  const done = [];
  let elements = def.elements;
  if (def.auto) {
    // no hand-written selectors: filters + KPI cards + every top-level card, named from its id or heading
    const found = await page.evaluate(() => {
      const vis = e => e.offsetParent !== null;
      const list = [];
      [...document.querySelectorAll('.chart-card')].filter(vis).filter(e => !e.parentElement.closest('.chart-card')).forEach((c, i) => {
        const h = c.querySelector('h5,h6,.chart-title,.card-title');
        const r = c.getBoundingClientRect(); if (r.height < 60) return;
        c.setAttribute('data-gcap', 'c' + i);
        list.push({ i, id: c.id || '', tid: (c.querySelector('tbody') || {}).id || '', title: h ? h.innerText.trim().split('\n')[0] : '', table: !!c.querySelector('tbody tr') && !c.querySelector('canvas') });
      });
      return list;
    });
    elements = [def.filters || { name: 'filters', union: 'auto-filters' }, { name: 'kpi', sel: '.kpi-card', each: '.kpi-label' }, ...(def.extra || [])];
    const used = new Set();
    for (const f of found) {
      let n = slug((f.id || f.title || f.tid.replace(/Body$/, '') || 'insights').replace(/Card$/, ''));
      if (!n || used.has(n)) n = n + '_' + f.i; used.add(n);
      elements.push({ name: n, sel: `[data-gcap="c${f.i}"]`, maxH: f.table ? (def.maxH || 640) : undefined });
    }
  }
  const only = (process.argv[4] || '').split(',').filter(Boolean);
  if (only.length) elements = elements.filter(e => only.some(o => e.name.startsWith(o)));
  for (const el of elements) {
    const targets = [];
    if (el.union) {
      if (!(await page.locator('select:visible, .reset-filters-btn:visible').count())) continue;
      const box = await page.evaluate(sel => {
        if (sel === 'auto-filters') {
          // from the top of the page header down to the lowest filter control that sits above the first card
          const vis = e => e.offsetParent !== null && e.getBoundingClientRect().height > 0;
          const first = [...document.querySelectorAll('.kpi-grid,.chart-card,.charts-grid,.cmp-kpi-grid')].filter(vis).map(e => e.getBoundingClientRect().top);
          const limit = first.length ? Math.min(...first) : Infinity;
          const hdr = document.querySelector('.page-header');
          const rs = [hdr, ...document.querySelectorAll('.page-content select, .page-content input, .page-content button, .page-content [class*=select], .page-content [class*=dropdown], .page-content [class*=filter]')]
            .filter(e => e && vis(e) && !e.closest('.page-guide-panel') && !e.classList.contains('page-guide-orb')).map(e => e.getBoundingClientRect()).filter(r => r.top < limit - 4 && r.width > 20);
          const x = Math.min(...rs.map(a => a.left)), y = Math.min(...rs.map(a => a.top));
          return { x, y, width: Math.max(...rs.map(a => a.right)) - x, height: Math.min(Math.max(...rs.map(a => a.bottom)), limit - 6) - y };
        }
        const r = [...document.querySelectorAll(sel)].filter(e => e.offsetParent).map(e => (e.closest('[class*=select-wrap]') || e).getBoundingClientRect());
        const x = Math.min(...r.map(a => a.left)), y = Math.min(...r.map(a => a.top));
        return { x, y, width: Math.max(...r.map(a => a.right)) - x, height: Math.max(...r.map(a => a.bottom)) - y };
      }, el.union);
      targets.push({ file: el.name, box: { x: Math.max(box.x - 8, 0), y: box.y - 8, width: box.width + 16, height: box.height + 16 } });
    } else {
      const base = page.locator(el.sel);
      const n = el.each ? Math.min(await base.count(), el.limit || 99) : 1;
      for (let i = 0; i < n; i++) {
        const loc = el.xpath ? base.first().locator('xpath=' + el.xpath) : up(el.each ? base.nth(i) : base.first(), el.up);
        const box = await loc.boundingBox(); if (!box) continue;
        let file = el.name;
        if (el.each) file += '_' + (typeof el.each === 'string' ? slug(await loc.locator(el.each).first().innerText()) : i + 1);
        targets.push({ file, box: { x: box.x, y: box.y, width: box.width, height: el.maxH ? Math.min(box.height, el.maxH) : box.height } });
      }
    }
    for (const t of targets) { await page.screenshot({ path: path.join(dir, t.file + '.png'), clip: t.box }); done.push(t.file); }
  }
  console.log(`${lang}/${key}: ${done.length} images -> ${done.join(', ')}`);
}

(async () => {
  const [which = 'all', lang = 'en'] = process.argv.slice(2);
  if (!process.env.GUIDE_USER || !process.env.GUIDE_PASS) throw new Error('Set GUIDE_USER and GUIDE_PASS');
  const keys = which === 'all' ? Object.keys(PAGES) : [which];
  const { browser, page } = await openBrowser(lang);
  try { for (const k of keys) await shoot(page, k, lang); } finally { await browser.close(); }
})().catch(e => { console.error(e.message); process.exit(1); });
