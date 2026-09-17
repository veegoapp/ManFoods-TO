const {
  Document, Packer, Paragraph, TextRun, AlignmentType,
  ImageRun, Header, Footer, BorderStyle, SimpleField,
  ShadingType, Table, TableRow, TableCell, WidthType, VerticalAlign,
  PageBreak, TableOfContents, HeadingLevel,
} = require("docx");
const fs = require("fs");
const pages = require("./guide_content.json");

const RED = "DA291C";
const GOLD = "FFC72C";
const INK = "232135";
const MUTED = "6B6878";
const WHITE = "FFFFFF";

const logoBuf = fs.readFileSync(__dirname + "/mclogo.png");
const CONTENT_WIDTH = 10440;

const NO_BORDER = { style: BorderStyle.NONE, size: 0, color: "FFFFFF" };
const noBorders = { top: NO_BORDER, bottom: NO_BORDER, left: NO_BORDER, right: NO_BORDER, insideHorizontal: NO_BORDER, insideVertical: NO_BORDER };

const H2 = (text) => new Paragraph({
  children: [new TextRun({ text, bold: true, size: 22, color: RED })],
  border: { bottom: { color: GOLD, size: 8, style: BorderStyle.SINGLE, space: 2 } },
  spacing: { before: 160, after: 80 },
});
const P = (text) => new Paragraph({ children: [new TextRun({ text, size: 19, color: INK })], spacing: { after: 100, line: 264 } });
const PMuted = (text) => new Paragraph({ children: [new TextRun({ text, size: 17, color: MUTED, italics: true })], spacing: { after: 120 } });
const sectionBullet = (title, body) => new Paragraph({
  children: [
    new TextRun({ text: "•  ", bold: true, color: RED, size: 19 }),
    new TextRun({ text: title + " — ", bold: true, size: 19, color: INK }),
    new TextRun({ text: body, size: 19, color: INK }),
  ],
  indent: { left: 280, hanging: 200 },
  spacing: { after: 90 },
});

const header = new Header({
  children: [
    new Table({
      width: { size: CONTENT_WIDTH, type: WidthType.DXA },
      columnWidths: [7000, 3440],
      borders: noBorders,
      rows: [new TableRow({ children: [
        new TableCell({
          width: { size: 7000, type: WidthType.DXA },
          shading: { type: ShadingType.CLEAR, color: "auto", fill: RED },
          verticalAlign: VerticalAlign.CENTER,
          margins: { top: 100, bottom: 100, left: 160, right: 100 },
          children: [new Paragraph({ children: [new TextRun({ text: "Crew Workforce Insights Hub", bold: true, size: 25, color: WHITE })] })],
        }),
        new TableCell({
          width: { size: 3440, type: WidthType.DXA },
          shading: { type: ShadingType.CLEAR, color: "auto", fill: RED },
          verticalAlign: VerticalAlign.CENTER,
          margins: { top: 100, bottom: 100, left: 100, right: 160 },
          children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [new TextRun({ text: "Portal Guide", bold: true, size: 25, color: GOLD })] })],
        }),
      ] })],
    }),
  ],
});

const footer = new Footer({
  children: [
    new Table({
      width: { size: CONTENT_WIDTH, type: WidthType.DXA },
      columnWidths: [7000, 3440],
      borders: { ...noBorders, top: { style: BorderStyle.SINGLE, size: 18, color: GOLD } },
      rows: [new TableRow({ children: [
        new TableCell({
          width: { size: 7000, type: WidthType.DXA },
          verticalAlign: VerticalAlign.CENTER,
          margins: { top: 120, bottom: 60, left: 0, right: 100 },
          children: [new Paragraph({ children: [
            new TextRun({ text: "Portal Guide", bold: true, size: 19, color: RED }),
            new TextRun({ text: "   ·   Page ", size: 17, color: MUTED }),
            new SimpleField("PAGE", "1"),
            new TextRun({ text: " of ", size: 17, color: MUTED }),
            new SimpleField("NUMPAGES", "1"),
          ] })],
        }),
        new TableCell({
          width: { size: 3440, type: WidthType.DXA },
          verticalAlign: VerticalAlign.CENTER,
          margins: { top: 120, bottom: 60, left: 100, right: 0 },
          children: [new Paragraph({ alignment: AlignmentType.RIGHT, children: [new ImageRun({ type: "png", data: logoBuf, transformation: { width: 88, height: 66 } })] })],
        }),
      ] })],
    }),
  ],
});

const children = [];

// ── Cover ──
children.push(
  new Paragraph({ spacing: { before: 1200 }, children: [] }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [new ImageRun({ type: "png", data: logoBuf, transformation: { width: 160, height: 120 } })],
    spacing: { after: 300 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [new TextRun({ text: "Crew Workforce Insights Hub", bold: true, size: 44, color: RED })],
    spacing: { after: 80 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [new TextRun({ text: "Portal Guide", bold: true, size: 32, color: GOLD })],
    spacing: { after: 400 },
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [new TextRun({ text: "A page-by-page reference for every dashboard in the portal.", size: 20, color: MUTED, italics: true })],
    spacing: { after: 60 },
  }),
  new Paragraph({ children: [new PageBreak()] }),
);

// ── Table of contents (page name list, no live TOC field to keep it
// robust across viewers — see chat note on field-rendering issues) ──
children.push(
  new Paragraph({ children: [new TextRun({ text: "Contents", bold: true, size: 30, color: RED })], spacing: { after: 200 } }),
);
pages.forEach((p, i) => {
  children.push(new Paragraph({
    children: [new TextRun({ text: `${i + 1}. `, bold: true, color: RED, size: 20 }), new TextRun({ text: p.title, size: 20, color: INK })],
    spacing: { after: 80 },
  }));
});
children.push(new Paragraph({ children: [new PageBreak()] }));

pages.forEach((p, idx) => {
  children.push(
    new Paragraph({
      children: [new TextRun({ text: p.title, bold: true, size: 32, color: RED })],
      border: { bottom: { color: RED, size: 4, style: BorderStyle.SINGLE, space: 4 } },
      spacing: { after: 160 },
    }),
  );
  if (p.overview) children.push(P(p.overview));
  if (p.filternote) children.push(PMuted(p.filternote));
  if (p.sections && p.sections.length) {
    children.push(H2("What's on this page"));
    p.sections.forEach((s) => children.push(sectionBullet(s.title, s.body)));
  }
  if (idx < pages.length - 1) children.push(new Paragraph({ children: [new PageBreak()] }));
});

const doc = new Document({
  sections: [{
    properties: {
      page: { size: { width: 12240, height: 15840 }, margin: { top: 1000, bottom: 1200, left: 900, right: 900, header: 400, footer: 400 } },
    },
    headers: { default: header },
    footers: { default: footer },
    children,
  }],
});

Packer.toBuffer(doc).then((buf) => fs.writeFileSync(__dirname + "/Portal_Guide.docx", buf));
