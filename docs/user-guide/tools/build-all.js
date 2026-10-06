#!/usr/bin/env node
// node build-all.js [lang]  -> (re)generates cover + every page listed in content/order.json that has a content file.
// WARNING: this overwrites the page .docx files. Once a page has been edited by hand in Word,
// build only the pages you need with build-page.js instead, then run merge.py.
const fs = require('fs');
const path = require('path');
const { execFileSync } = require('child_process');
const ROOT = path.join(__dirname, '..');
const lang = process.argv[2] || 'en';
const { front = [], pages: order, back = [] } = require(path.join(ROOT, 'content', 'order.json'));
execFileSync('node', [path.join(__dirname, 'build-front.js'), lang], { stdio: 'inherit' });
const build = (prefix, slug) => {
  if (!fs.existsSync(path.join(ROOT, 'content', lang, slug + '.js'))) return;
  execFileSync('node', [path.join(__dirname, 'build-page.js'), prefix, slug, lang], { stdio: 'inherit' });
};
front.forEach((slug, i) => build('00' + 'abcdefgh'[i], slug));   // sorts right after 00-cover-contents
order.forEach((slug, i) => build(String(i + 1).padStart(2, '0'), slug));
back.forEach((slug, i) => build('99' + 'abcdefgh'[i], slug));    // sorts last
