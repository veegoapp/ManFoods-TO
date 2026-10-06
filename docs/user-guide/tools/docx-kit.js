// Shared Word-building helpers for the user guide (McDonald's colours, centred images, step layout).
const fs = require('fs');
const path = require('path');
const D = require(process.env.DOCX_PATH || 'docx');
const { Document, Packer, Paragraph, TextRun, ImageRun, Table, TableRow, TableCell, WidthType, ShadingType, BorderStyle, AlignmentType, Header, Footer, PageNumber, LevelFormat } = D;

const RED = 'DA291C', YEL = 'FFC72C', BLK = '27251F', GREY = '666666';
const FONT = 'Arial';
const ROOT = path.join(__dirname, '..');
const LOGO = path.join(ROOT, 'images', 'logo.png');
const pngSize = f => { const b = fs.readFileSync(f); return [b.readUInt32BE(16), b.readUInt32BE(20)]; };

const run = (t, o = {}) => new TextRun({ text: t, font: FONT, color: BLK, size: 21, ...o });
const para = (parts, o = {}) => new Paragraph({ spacing: { after: 90, line: 290 }, ...o, children: [].concat(parts).map(x => typeof x === 'string' ? run(x) : x) });
const labelled = (label, text) => para([run(label + ' ', { bold: true }), run(text)]);

const imgPath = (dir, name) => name.includes('/') ? path.join(ROOT, 'images', dir.split('/')[0], name + '.png') : path.join(ROOT, 'images', dir, name + '.png');
const image = (dir, name, w = 560, keep = true) => {
  const f = imgPath(dir, name);
  const [pw, ph] = pngSize(f);
  const W = w >= 600 ? 730 : Math.min(w, 730);
  return new Paragraph({ alignment: AlignmentType.CENTER, keepNext: keep, spacing: { before: 60, after: 120 }, children: [new ImageRun({ type: 'png', data: fs.readFileSync(f), transformation: { width: W, height: Math.round(W * ph / pw) } })] });
};

const none = { style: BorderStyle.NONE, size: 0, color: 'FFFFFF' };
const imageGrid = (dir, names) => {
  const n = names.length, perRow = Math.min(n, 4);   // up to 4 cards per row
  const colW = Math.floor(11000 / perRow);
  const w = Math.min(175, Math.floor(colW / 15) - 16);
  const rows = [];
  for (let i = 0; i < names.length; i += perRow) {
    const chunk = names.slice(i, i + perRow);
    while (chunk.length < perRow) chunk.push(null);
    rows.push(new TableRow({ cantSplit: true, children: chunk.map(n => new TableCell({
      width: { size: colW, type: WidthType.DXA }, borders: { top: none, bottom: none, left: none, right: none }, margins: { top: 40, bottom: 80, left: 40, right: 40 },
      children: [n ? (() => { const f = imgPath(dir, n); const [pw, ph] = pngSize(f); return new Paragraph({ alignment: AlignmentType.CENTER, children: [new ImageRun({ type: 'png', data: fs.readFileSync(f), transformation: { width: w, height: Math.round(w * ph / pw) } })] }); })() : new Paragraph({ children: [] })],
    })) }));
  }
  return new Table({ alignment: AlignmentType.CENTER, width: { size: colW * perRow, type: WidthType.DXA }, columnWidths: Array(perRow).fill(colW), rows });
};

const cell = (t, w, hdr) => new TableCell({
  width: { size: w, type: WidthType.DXA }, margins: { top: 70, bottom: 70, left: 110, right: 110 },
  shading: { type: ShadingType.CLEAR, fill: hdr ? YEL : 'FFFFFF', color: 'auto' },
  borders: ['top', 'bottom', 'left', 'right'].reduce((a, k) => (a[k] = { style: BorderStyle.SINGLE, size: 4, color: 'D9D9D6' }, a), {}),
  children: [new Paragraph({ children: [run(t, { bold: hdr || undefined, size: 19 })] })],
});
const defsTable = ({ head, rows }, widths = [2600, 6800]) => new Table({
  width: { size: widths.reduce((a, b) => a + b, 0), type: WidthType.DXA }, columnWidths: widths,
  rows: [head, ...rows].map((r, i) => new TableRow({ cantSplit: true, tableHeader: i === 0, children: r.map((t, j) => cell(t, widths[j], i === 0)) })),
});

const styles = {
  default: { document: { run: { font: FONT, size: 21 } } },
  paragraphStyles: [
    { id: 'Heading1', name: 'Heading 1', basedOn: 'Normal', next: 'Normal', quickFormat: true, run: { font: FONT, size: 40, bold: true, color: RED }, paragraph: { spacing: { before: 120, after: 60 }, outlineLevel: 0 } },
    { id: 'Heading2', name: 'Heading 2', basedOn: 'Normal', next: 'Normal', quickFormat: true, run: { font: FONT, size: 26, bold: true, color: BLK }, paragraph: { spacing: { before: 260, after: 100 }, keepNext: true, outlineLevel: 1, border: { left: { style: BorderStyle.SINGLE, size: 24, color: RED, space: 6 } } } },
  ],
};
const numbering = { config: [{ reference: 'b', levels: [{ level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT, style: { paragraph: { indent: { left: 540, hanging: 260 } } } }] }] };

const header = () => new Header({ children: [new Paragraph({ border: { bottom: { style: BorderStyle.SINGLE, size: 12, color: RED, space: 4 } }, children: [new ImageRun({ type: 'png', data: fs.readFileSync(LOGO), transformation: { width: 34, height: 30 } }), run("   McDonald's Egypt - Crew Insights Hub  |  User Guide", { bold: true, size: 18 })] })] });
const footer = () => new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER, children: [run('Page ', { size: 16, color: GREY }), new TextRun({ children: [PageNumber.CURRENT], font: FONT, size: 16, color: GREY })] })] });
const PAGE = { size: { width: 11906, height: 16838 }, margin: { top: 1500, bottom: 994, left: 432, right: 432, header: 706, footer: 706 } };


// ---------- layout engine ----------
// Screenshots narrower than ~1100 px are "half width" cards in the portal (it shows them two per row).
const isHalf = (dir, n) => pngSize(imgPath(dir, n))[0] < 1100;
const CAP = b => b.p || b.look || b.decide || b.note;
const capParas = caps => caps.map(b => b.p ? para(b.p, { spacing: { after: 70, line: 270 } }) : b.look ? labelled('What to look at:', b.look) : b.decide ? labelled('Then decide:', b.decide) : labelled('Good to know:', b.note));
const cellOf = (w, children) => new TableCell({ width: { size: w, type: WidthType.DXA }, borders: { top: none, bottom: none, left: none, right: none }, margins: { top: 40, bottom: 60, left: 70, right: 70 }, children: children.length ? children : [new Paragraph({ children: [] })] });
const twoCols = (left, right, wl = 5500, wr = 5500) => new Table({ alignment: AlignmentType.CENTER, width: { size: wl + wr, type: WidthType.DXA }, columnWidths: [wl, wr], rows: [new TableRow({ children: [cellOf(wl, left), cellOf(wr, right)] })] });
const figureCol = (dir, c, w) => [image(dir, c.imgs[0].img, w), ...capParas(c.caps)];

function parseSection(blocks) {
  const items = []; let i = 0;
  while (i < blocks.length) {
    if (blocks[i].img) {
      const imgs = [], caps = [];
      while (i < blocks.length && blocks[i].img) imgs.push(blocks[i++]);
      while (i < blocks.length && CAP(blocks[i])) caps.push(blocks[i++]);
      items.push({ t: 'cluster', imgs, caps });
    } else items.push({ t: 'blk', b: blocks[i++] });
  }
  return items;
}

function layoutSection(blocks, dir) {
  const items = parseSection(blocks);
  // single half-width figures can be paired even when other things sit between them (same step only)
  const singles = items.filter(x => x.t === 'cluster' && x.imgs.length === 1 && isHalf(dir, x.imgs[0].img));
  const consumed = new Set(); const pairAt = new Map();
  for (let i = 0; i + 1 < singles.length; i += 2) { pairAt.set(singles[i], singles[i + 1]); consumed.add(singles[i + 1]); }
  const out = [];
  for (const it of items) {
    if (consumed.has(it)) continue;
    if (it.t === 'blk') { out.push(...render([it.b], dir, true)); continue; }
    const half = it.imgs.every(m => isHalf(dir, m.img));
    if (pairAt.has(it)) {
      const o = pairAt.get(it);
      out.push(twoCols(figureCol(dir, it, 345), figureCol(dir, o, 345)), para('', { spacing: { after: 30 } }));
    } else if (it.imgs.length === 1 && half) {
      if (it.caps.length) out.push(twoCols([image(dir, it.imgs[0].img, 345)], capParas(it.caps), 5500, 5500), para('', { spacing: { after: 30 } }));
      else out.push(image(dir, it.imgs[0].img, 420));
    } else if (it.imgs.length > 1 && half) {
      // consecutive small tables/charts: two columns, each image goes under the shorter column
      const cols = [[], []], hs = [0, 0];
      for (const m of it.imgs) { const [pw, ph] = pngSize(imgPath(dir, m.img)); const c = hs[1] < hs[0] ? 1 : 0; cols[c].push(image(dir, m.img, 345)); hs[c] += 345 * ph / pw; }
      out.push(twoCols(cols[0], cols[1]), ...capParas(it.caps));
    } else {
      it.imgs.forEach((m, ix) => out.push(image(dir, m.img, isHalf(dir, m.img) ? 420 : m.w || 600, ix === it.imgs.length - 1 && it.caps.length > 0)));
      out.push(...capParas(it.caps));
    }
  }
  return out;
}

// content block -> docx element(s)
function render(blocks, dir, flat) {
  if (!flat && blocks.some(b => b.img)) {
    // split into steps at each h2 and lay each step out on its own
    const out = []; let cur = [];
    const flush = () => { out.push(...layoutSection(cur, dir)); cur = []; };
    for (const b of blocks) { if (b.h2) { flush(); out.push(...render([b], dir, true)); } else cur.push(b); }
    flush(); return out;
  }
  const out = [];
  for (const b of blocks) {
    if (b.h2) out.push(new Paragraph({ heading: D.HeadingLevel.HEADING_2, pageBreakBefore: !!b.pb, children: [new TextRun({ text: b.h2 })] }));
    else if (b.where) out.push(para([run('Where to find it: ', { color: GREY, size: 19 }), run(b.where, { color: GREY, size: 19 })]));
    else if (b.p) out.push(para(b.p));
    else if (b.look) out.push(labelled('What to look at:', b.look));
    else if (b.decide) out.push(labelled('Then decide:', b.decide));
    else if (b.note) out.push(labelled('Good to know:', b.note));
    else if (b.img) out.push(image(dir, b.img, b.w));
    else if (b.grid) out.push(imageGrid(dir, b.grid), para('', { spacing: { after: 40 } }));
    else if (b.defs) out.push(defsTable(b.defs, b.widths), para('', { spacing: { after: 40 } }));
    else if (b.bullets) b.bullets.forEach(t => out.push(new Paragraph({ numbering: { reference: 'b', level: 0 }, spacing: { after: 60, line: 280 }, children: [].concat(t).map(x => typeof x === 'string' ? run(x) : x) })));
    else throw new Error('Unknown block ' + JSON.stringify(b).slice(0, 60));
  }
  return out;
}

async function writePage(content, dir, outFile) {
  const children = [new Paragraph({ heading: D.HeadingLevel.HEADING_1, pageBreakBefore: true, children: [new TextRun({ text: content.title })] }), ...render(content.blocks, dir)];
  const doc = new Document({ creator: "McDonald's Egypt", title: content.title, styles, numbering, sections: [{ properties: { page: PAGE }, headers: { default: header() }, footers: { default: footer() }, children }] });
  fs.mkdirSync(path.dirname(outFile), { recursive: true });
  fs.writeFileSync(outFile, await Packer.toBuffer(doc));
}
module.exports = { D, RED, YEL, BLK, GREY, FONT, ROOT, LOGO, run, para, styles, numbering, header, footer, PAGE, writePage, render };
