#!/usr/bin/env python3
"""Merge the per-page Word files into one guide.

    python3 merge.py [en|ar]

Takes <lang>/pages/*.docx in file-name order (00-cover-contents first) and writes
<lang>/ManFoods_Portal_Guide_<LANG>.docx. Edit a page file in Word, then run this again.
"""
import glob, os, sys
from docx import Document
from docxcompose.composer import Composer

lang = sys.argv[1] if len(sys.argv) > 1 else "en"
root = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", lang)
files = sorted(glob.glob(os.path.join(root, "pages", "*.docx")))
if not files:
    sys.exit("no page files found")
master = Document(files[0])
composer = Composer(master)
for f in files[1:]:
    composer.append(Document(f))
out = os.path.join(root, f"ManFoods_Portal_Guide_{lang.upper()}.docx")
composer.save(out)
print("merged", len(files), "files ->", os.path.relpath(out))
