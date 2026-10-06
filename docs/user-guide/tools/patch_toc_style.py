#!/usr/bin/env python3
"""Give the contents page more breathing room: sets the 'toc 1' paragraph style (spacing, size, dotted right tab).

    python3 patch_toc_style.py en/pages/00-cover-contents.docx [space-after-twips] [font-half-points]
"""
import sys, zipfile, shutil
from lxml import etree
W = 'http://schemas.openxmlformats.org/wordprocessingml/2006/main'
path = sys.argv[1]; after = sys.argv[2] if len(sys.argv) > 2 else '200'; size = sys.argv[3] if len(sys.argv) > 3 else '26'
zin = zipfile.ZipFile(path)
root = etree.fromstring(zin.read('word/styles.xml'))
for st in root.findall('{%s}style' % W):
    if st.get('{%s}styleId' % W) == 'TOC1': root.remove(st)
style = etree.fromstring(f'''<w:style xmlns:w="{W}" w:type="paragraph" w:styleId="TOC1"><w:name w:val="toc 1"/><w:basedOn w:val="Normal"/><w:next w:val="Normal"/><w:uiPriority w:val="39"/><w:unhideWhenUsed/><w:pPr><w:tabs><w:tab w:val="right" w:leader="dot" w:pos="11000"/></w:tabs><w:spacing w:before="0" w:after="{after}" w:line="360" w:lineRule="auto"/></w:pPr><w:rPr><w:sz w:val="{size}"/></w:rPr></w:style>''')
root.append(style)
tmp = path + '.tmp'
with zipfile.ZipFile(tmp, 'w', zipfile.ZIP_DEFLATED) as zout:
    for it in zin.infolist():
        data = zin.read(it.filename)
        if it.filename == 'word/styles.xml': data = etree.tostring(root, xml_declaration=True, encoding='UTF-8', standalone=True)
        zout.writestr(it, data)
zin.close(); shutil.move(tmp, path); print('TOC1 style set in', path)
