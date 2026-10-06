// Replaces real store / person names with generic labels IN THE BROWSER ONLY, right before a
// screenshot is taken. The portal data is never changed. The real->generic map is kept OUTSIDE
// the repo (GUIDE_MASK_MAP, default ~/.guide-mask-map.json) so the same store always gets the same number.
const fs = require('fs');
const os = require('os');
const path = require('path');

const MAP_PATH = process.env.GUIDE_MASK_MAP || path.join(os.homedir(), '.guide-mask-map.json');
const LABELS = {
  en: { emp: 'Employee', leader: 'Store Leader', comment: 'Example comment text.', store: 'Store', oc: 'Operation Consultant', od: 'Operation Director', om: 'Operation Manager', soc: 'Senior Operation Consultant' },
  ar: { emp: 'موظف', leader: 'قائد ستور', comment: 'نص تعليق توضيحي.', store: 'ستور', oc: 'استشاري عمليات', od: 'مدير أول تشغيل', om: 'مدير تشغيل', soc: 'استشاري أول عمليات' },
};

function loadMap() {
  try { return JSON.parse(fs.readFileSync(MAP_PATH, 'utf8')); } catch { return { stores: {}, people: { oc: {}, od: {}, om: {}, soc: {} } }; }
}

async function maskPage(page, lang = 'en') {
  const map = loadMap();
  // 1) read every store / person that the page's filter dropdowns know about
  const found = await page.evaluate(() => {
    const out = { stores: [], people: { oc: [], od: [], om: [], soc: [] } };
    document.querySelectorAll('select').forEach(sel => {
      const k = sel.id.replace(/Select$/, '');
      const opts = [...sel.options].slice(1).map(o => o.text.trim()).filter(Boolean);
      // ids look like ocSelect / odSelect / ctOc / wpSoc - the role must be a whole word (not the end of "period")
      const m = k.match(/^(soc|oc|om|od)$|[a-z](Soc|Oc|Om|Od)$/);
      const role = m ? (m[1] || m[2]).toLowerCase() : null;
      if (/store$/i.test(k)) opts.forEach(t => { const m = t.match(/^(\d{5,})\s*\|\s*(.+)$/); if (m) out.stores.push([m[1], m[2].trim()]); });
      else if (role) out.people[role].push(...opts);
    });
    // names that only appear inside table columns (store leaders, employees, responsible people)
    out.leaders = []; out.emps = [];
    document.querySelectorAll('table').forEach(t => {
      const heads = [...t.querySelectorAll('thead th')].map(h => h.innerText.trim().toLowerCase());
      heads.forEach((h, i) => {
        const kind = /^(store leader|قائد المتجر|قائد الفرع)$/.test(h) ? 'leaders' : /^(name|employee|employee name|trainer|responsible|الاسم|الموظف|المسؤول)$/.test(h) ? 'emps' : null;
        if (kind) t.querySelectorAll('tbody tr').forEach(tr => { const c = tr.cells[i]; const v = c && c.innerText.replace(/^[▼▶►▲\s]+/, '').trim(); if (v && v.length > 3 && !/^\d/.test(v)) out[kind].push(v); });
      });
    });
    return out;
  });
  // 2) assign stable numbers (new names get the next free number)
  for (const [code, name] of found.stores) {
    if (!map.stores[code]) map.stores[code] = { n: Object.keys(map.stores).length + 1, name };
    else map.stores[code].name = name;
  }
  for (const role of Object.keys(found.people)) {
    for (const name of found.people[role]) if (!map.people[role][name]) map.people[role][name] = Object.keys(map.people[role]).length + 1;
  }
  map.people.leader = map.people.leader || {}; map.people.emp = map.people.emp || {};
  const known = new Set(['oc', 'od', 'om', 'soc'].flatMap(r => Object.keys(map.people[r])));
  for (const name of found.leaders) if (!known.has(name) && !map.people.leader[name]) map.people.leader[name] = Object.keys(map.people.leader).length + 1;
  fs.writeFileSync(MAP_PATH, JSON.stringify(map), { mode: 0o600 });

  // 3) rewrite the DOM and chart labels, then report anything real that is still visible
  return page.evaluate(({ map, labels, ar }) => {
    const dg = n => ar ? String(n).replace(/\d/g, d => '٠١٢٣٤٥٦٧٨٩'[d]) : String(n);
    const esc = s => s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    const rules = []; // [regex, replacement]
    const secrets = [];
    const stores = Object.entries(map.stores).sort((a, b) => b[1].name.length - a[1].name.length);
    for (const [code, s] of stores) {
      const lab = labels.store + ' ' + dg(s.n);
      rules.push([new RegExp(esc(code) + '\\s*\\|\\s*' + esc(s.name), 'g'), lab]);
    }
    for (const [code, s] of stores) {
      const lab = labels.store + ' ' + dg(s.n);
      if (s.name.length >= 4) rules.push([new RegExp(esc(s.name), 'g'), lab]);
      rules.push([new RegExp('\\b' + esc(code) + '\\b', 'g'), lab]);
      secrets.push(s.name);
    }
    for (const role of ['soc', 'oc', 'od', 'om', 'leader']) { // 'emp' is handled per table column (thousands of names would make this slow)
      Object.entries(map.people[role] || {}).sort((a, b) => b[0].length - a[0].length).forEach(([name, n]) => {
        rules.push([new RegExp((role === 'leader' ? '(?:' + esc(labels.leader) + '\\s+)?' : '') + esc(name), 'g'), labels[role] + ' ' + dg(n)]);
        secrets.push(name);
      });
    }
    const fix = t => { let r = t; for (const [re, rep] of rules) r = r.replace(re, rep); return r; };
    const w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    let n;
    while ((n = w.nextNode())) { if (n.parentElement && /^(SCRIPT|STYLE)$/.test(n.parentElement.tagName)) continue; const t = fix(n.nodeValue); if (t !== n.nodeValue) n.nodeValue = t; }
    document.querySelectorAll('option').forEach(o => { o.text = fix(o.text); });
    document.querySelectorAll('[title],[aria-label],[placeholder]').forEach(e => ['title', 'aria-label', 'placeholder'].forEach(a => { const v = e.getAttribute(a); if (v) e.setAttribute(a, fix(v)); }));
    if (window.Chart && Chart.instances) Object.values(Chart.instances).forEach(c => { if (c.data && c.data.labels) { c.data.labels = c.data.labels.map(l => typeof l === 'string' ? fix(l) : l); c.update('none'); } });
    // individual employees (leavers, trainers, at-risk lists ...): any table column called Name / Employee
    // that is not already a masked store/role label becomes "Employee N"; ID columns become E1001, E1002 ...
    const done = new RegExp('(' + ['emp', 'leader', 'store', 'oc', 'od', 'om', 'soc'].map(k => esc(labels[k])).join('|') + ') [\\d٠-٩]+');
    let emp = 0, eid = 1000, lead = 0;
    document.querySelectorAll('table').forEach(t => {
      const heads = [...t.querySelectorAll('thead th')].map(h => h.innerText.trim().toLowerCase());
      const nameCols = heads.map((h, i) => /^(name|employee|employee name|trainer|responsible|الاسم|الموظف|المسؤول)$/.test(h) ? i : -1).filter(i => i >= 0);
      const leaderCols = heads.map((h, i) => /^(store leader|قائد المتجر|قائد الفرع)$/.test(h) ? i : -1).filter(i => i >= 0);
      const commentCols = heads.map((h, i) => /^(comment|comments|التعليق|تعليق)$/.test(h) ? i : -1).filter(i => i >= 0);
      const idCols = heads.map((h, i) => /^(id|employee id|emp id|employee no\.?|emp no\.?|number|الرقم|رقم الموظف)$/.test(h) ? i : -1).filter(i => i >= 0);
      t.querySelectorAll('tbody tr').forEach(tr => {
        nameCols.forEach(i => { const c = tr.cells[i]; if (c && c.innerText.trim() && !done.test(c.innerText)) { const lead = (c.innerText.match(/^[▼▶►▲\s]+/) || [''])[0]; c.textContent = lead + labels.emp + ' ' + dg(++emp); } });
        leaderCols.forEach(i => { const c = tr.cells[i]; if (c && c.innerText.trim() && !done.test(c.innerText)) c.textContent = labels.leader + ' ' + dg(++lead); });
        commentCols.forEach(i => { const c = tr.cells[i]; if (c && c.innerText.trim()) c.textContent = labels.comment; });
        idCols.forEach(i => { const c = tr.cells[i]; if (c && /\d/.test(c.innerText)) c.textContent = 'E' + dg(++eid); });
      });
    });
    const body = document.body.innerText + ' ' + [...document.querySelectorAll('option')].map(o => o.text).join(' ');
    return secrets.filter(s => s.length > 3 && body.includes(s));
  }, { map, labels: LABELS[lang], ar: lang === 'ar' });
}
module.exports = { maskPage, LABELS };
