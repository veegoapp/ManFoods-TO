#!/usr/bin/env python3
"""List the pictures in a .docx (index, size, the text before it, crop). Usage: python3 inspect_images.py file.docx"""
import sys,zipfile,re
from lxml import etree
ns={'w':'http://schemas.openxmlformats.org/wordprocessingml/2006/main','wp':'http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing','a':'http://schemas.openxmlformats.org/drawingml/2006/main','pic':'http://schemas.openxmlformats.org/drawingml/2006/picture'}
z=zipfile.ZipFile(sys.argv[1]); root=etree.fromstring(z.read('word/document.xml'))
body=root.find('w:body',ns)
last=''
i=0
for el in root.iter():
    if el.tag=='{%s}p'%ns['w']:
        t=''.join(el.itertext()).strip()
        if t and not el.findall('.//w:drawing',ns): last=t[:50]
        for d in el.findall('.//w:drawing',ns):
            ext=d.find('.//wp:extent',ns); kind='inline' if d.find('wp:inline',ns) is not None else 'anchor'
            sr=d.find('.//a:srcRect',ns)
            cx,cy=int(ext.get('cx')),int(ext.get('cy'))
            print(i,kind,round(cx/9525),'x',round(cy/9525),'| after:',last,'| crop:',dict(sr.attrib) if sr is not None else None)
            i+=1
