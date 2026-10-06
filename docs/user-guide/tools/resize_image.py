#!/usr/bin/env python3
"""Resize one picture inside a page .docx without touching anything else (keeps hand edits).

    python3 resize_image.py <file.docx> <picture-index> <new-width-px> [crop|scale]

* picture-index: the order of the pictures in the document, starting at 0 (see inspect_images.py)
* scale (default): the picture keeps its shape, so it also gets taller or shorter
* crop: the picture gets the new width but keeps its current height; the extra is cropped off the bottom
  (use it to make a table screenshot full width without pushing the content below it to the next page)
"""
import sys, zipfile, shutil, os
from lxml import etree

NS = {'w': 'http://schemas.openxmlformats.org/wordprocessingml/2006/main',
      'wp': 'http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing',
      'a': 'http://schemas.openxmlformats.org/drawingml/2006/main',
      'pic': 'http://schemas.openxmlformats.org/drawingml/2006/picture'}
EMU = 9525

def main(path, index, width_px, mode='scale'):
    zin = zipfile.ZipFile(path)
    root = etree.fromstring(zin.read('word/document.xml'))
    drawings = root.findall('.//w:drawing', NS)
    d = drawings[index]
    ext = d.find('.//wp:extent', NS)
    cx, cy = int(ext.get('cx')), int(ext.get('cy'))
    new_cx = int(width_px * EMU)
    scale = new_cx / cx
    blip_fill = d.find('.//pic:blipFill', NS)
    src = blip_fill.find('a:srcRect', NS)
    if mode == 'crop':
        new_cy = cy
        t = int(src.get('t', 0)) if src is not None else 0
        b = int(src.get('b', 0)) if src is not None else 0
        visible = 100000 - t - b
        new_visible = visible / scale                   # same height at a larger scale shows less of the picture
        new_b = max(0, 100000 - t - int(new_visible))
        if src is None:
            src = etree.Element('{%s}srcRect' % NS['a']); blip_fill.insert(1, src)
        src.set('b', str(new_b))
        if t: src.set('t', str(t))
    else:
        new_cy = int(cy * scale)
    for e in (ext, d.find('.//a:ext', NS)):
        e.set('cx', str(new_cx)); e.set('cy', str(new_cy))
    tmp = path + '.tmp'
    with zipfile.ZipFile(tmp, 'w', zipfile.ZIP_DEFLATED) as zout:
        for item in zin.infolist():
            data = zin.read(item.filename)
            if item.filename == 'word/document.xml':
                data = etree.tostring(root, xml_declaration=True, encoding='UTF-8', standalone=True)
            zout.writestr(item, data)
    zin.close(); shutil.move(tmp, path)
    print(f'picture {index}: {round(cx/EMU)}x{round(cy/EMU)} -> {round(new_cx/EMU)}x{round(new_cy/EMU)} px ({mode})')

if __name__ == '__main__':
    a = sys.argv
    main(a[1], int(a[2]), float(a[3]), a[4] if len(a) > 4 else 'scale')
