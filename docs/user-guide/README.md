# Crew Insights Hub - User Guide (Home area, Operations Managers)

Built page by page so one page can be reviewed and changed without touching the rest.

```
docs/user-guide/
  en/pages/NN-<page>.docx        one Word file per portal page (00 = cover + contents)
  en/ManFoods_Portal_Guide_EN.docx / .pdf   the merged guide
  content/en/<page>.js           text of each page (used to generate the first draft)
  images/en/<page>/*.png         one screenshot per chart / card / table (names already masked)
  tools/                         capture, build, merge, pdf scripts (no credentials inside)
```

## Editing a page
1. Open `en/pages/NN-<page>.docx` in Word and edit it. After the first review round this file is the source of truth - do not regenerate it from `content/` or you will lose manual edits.
2. Re-merge: `python3 tools/merge.py en` (needs `pip install docxcompose`).
3. PDF with the contents page filled in: `python3 tools/make_pdf.py en` (needs LibreOffice). In Word, open the merged `.docx`, accept "update fields" (or right-click the contents > Update Field).

## Whole guide in one go
`node tools/build-all.js en` rebuilds the cover, the quick reference, every page in `content/order.json` and the contact page. It overwrites the page files, so only use it before pages have been edited by hand.

## Inside pages
Store Profile, Store Leader Profile, Action Center Detail and Report Detail are explained inside their parent page (Stores, Scorecard, Action Center, Reports). `node tools/capture-detail.js all en` captures them by following the same links a user clicks (navigation and tab clicks only; it never saves, closes or downloads anything). Their images live in `images/en/storeprofile|storeleaderprofile|actioncenterdetail|reportdetail/` and are referenced from the parent page's content as `'<folder>/<name>'`.

## Adding a page
`node tools/capture.js <page> en` -> write `content/en/<page>.js` -> `node tools/build-page.js NN <page> en` -> merge. Page order = side-menu order. Page definitions live in `tools/pages.js`.

## Screenshots
`GUIDE_USER=... GUIDE_PASS=... node tools/capture.js all en` logs in with a **view-only** portal user (credentials only from the environment, never stored) and clicks nothing except sign-in.
Store and person names are replaced in the browser before each screenshot (Store 1, Operation Consultant 1 ...); the capture aborts if a real name is still visible. The real-to-generic map is kept outside the repo (`GUIDE_MASK_MAP`, default `~/.guide-mask-map.json`). Always eyeball new screenshots before sharing.
If `cdn.jsdelivr.net` is unreachable, point `GUIDE_CDN_DIR` at a folder with `chart.js@4.4.0` and `bootstrap-icons@1.11.3` from npm.

Other env vars: `PLAYWRIGHT_PATH`, `CHROMIUM_PATH`, `DOCX_PATH`, `GUIDE_VERSION`, `GUIDE_DATE`.
