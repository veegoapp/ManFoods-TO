#!/usr/bin/env node
// node build-page.js <order> <slug> [lang]   e.g.  node build-page.js 02 workforceplanning en
// Builds en/pages/<order>-<slug>.docx from content/<lang>/<slug>.js using images/<lang>/<slug>/.
const path = require('path');
const { writePage, ROOT } = require('./docx-kit');
const [order, slug, lang = 'en'] = process.argv.slice(2);
const content = require(path.join(ROOT, 'content', lang, slug + '.js'));
writePage(content, path.join(lang, slug), path.join(ROOT, lang, 'pages', `${order}-${slug}.docx`)).then(() => console.log('built', `${lang}/pages/${order}-${slug}.docx`));
