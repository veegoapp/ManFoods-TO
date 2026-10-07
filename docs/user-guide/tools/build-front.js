#!/usr/bin/env node
// node build-front.js [lang]  ->  <lang>/pages/00-cover-contents.docx  (cover page + table of contents)
// The contents page is a real Word table of contents: it fills itself with the page titles (Heading 1)
// and their page numbers when the merged document is opened / updated (right-click > Update Field).
const fs = require('fs');
const path = require('path');
const K = require('./docx-kit');
const { D } = K;
const { Document, Packer, Paragraph, TextRun, ImageRun, AlignmentType, BorderStyle, TableOfContents, SectionType } = D;

const VERSION = process.env.GUIDE_VERSION || 'Version 1.0';
const DATE = process.env.GUIDE_DATE || 'October 2026';
const t = (text, o) => new TextRun({ text, font: K.FONT, ...o });
const center = (children, o = {}) => new Paragraph({ alignment: AlignmentType.CENTER, ...o, children });
const [lang = 'en'] = process.argv.slice(2);

const cover = [
  center([], { spacing: { before: 1800 } }),
  center([new ImageRun({ type: 'png', data: fs.readFileSync(K.LOGO), transformation: { width: 190, height: 167 } })], { spacing: { after: 400 } }),
  center([t("McDonald's Egypt", { size: 32, color: K.GREY, bold: true })], { spacing: { after: 40 } }),
  center([t('Human Resources', { size: 26, color: K.GREY })], { spacing: { after: 200 } }),
  center([t('Crew Insights Hub', { size: 76, bold: true, color: K.RED })], { spacing: { after: 100 } }),
  center([t('User Guide', { size: 48, bold: true, color: K.BLK })], { border: { bottom: { style: BorderStyle.SINGLE, size: 24, color: K.YEL, space: 14 } }, spacing: { after: 400 } }),
  center([t('For Operations Managers', { size: 28, color: K.BLK })], { spacing: { before: 200, after: 2400 } }),
  center([t(`${VERSION}  |  ${DATE}`, { size: 22, color: K.GREY })]),
];
const contents = [
  new Paragraph({ spacing: { after: 240 }, border: { left: { style: BorderStyle.SINGLE, size: 24, color: K.RED, space: 6 } }, children: [t('Contents', { size: 40, bold: true, color: K.RED })] }),
  new TableOfContents('Contents', { hyperlink: true, headingStyleRange: '1-1' }),
];
const doc = new Document({
  creator: "McDonald's Egypt", title: 'Crew Insights Hub - User Guide', styles: K.styles, numbering: K.numbering,
  features: { updateFields: true },
  sections: [
    { properties: { page: K.PAGE }, children: cover },
    { properties: { type: SectionType.NEXT_PAGE, page: K.PAGE }, headers: { default: K.header() }, footers: { default: K.footer() }, children: contents },
  ],
});
const out = path.join(K.ROOT, lang, 'pages', '00-cover-contents.docx');
fs.mkdirSync(path.dirname(out), { recursive: true });
Packer.toBuffer(doc).then(b => { fs.writeFileSync(out, b); console.log('built', path.relative(K.ROOT, out)); });
