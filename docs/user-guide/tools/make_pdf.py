#!/usr/bin/env python3
"""Export the merged guide to PDF with the table of contents filled in (LibreOffice, headless).

    python3 make_pdf.py [en|ar]

Word fills the contents page itself when you open the .docx and accept "update fields";
this script does the same update before exporting the PDF.
"""
import os, subprocess, sys, time

lang = sys.argv[1] if len(sys.argv) > 1 else "en"
here = os.path.dirname(os.path.abspath(__file__))
docx = os.path.abspath(os.path.join(here, "..", lang, f"ManFoods_Portal_Guide_{lang.upper()}.docx"))
pdf = docx[:-5] + ".pdf"
port = 2002
proc = subprocess.Popen(["soffice", "--headless", "--norestore", f"--accept=socket,host=localhost,port={port};urp;"],
                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
try:
    import uno
    from com.sun.star.beans import PropertyValue
    ctx = None
    for _ in range(60):
        try:
            local = uno.getComponentContext()
            resolver = local.ServiceManager.createInstanceWithContext("com.sun.star.bridge.UnoUrlResolver", local)
            ctx = resolver.resolve(f"uno:socket,host=localhost,port={port};urp;StarOffice.ComponentContext")
            break
        except Exception:
            time.sleep(1)
    if ctx is None:
        sys.exit("could not connect to LibreOffice")
    desktop = ctx.ServiceManager.createInstanceWithContext("com.sun.star.frame.Desktop", ctx)
    pv = lambda n, v: PropertyValue(Name=n, Value=v)
    doc = desktop.loadComponentFromURL(uno.systemPathToFileUrl(docx), "_blank", 0, (pv("Hidden", True),))
    idx = doc.getDocumentIndexes()
    for i in range(idx.getCount()):
        idx.getByIndex(i).update()
    doc.storeToURL(uno.systemPathToFileUrl(pdf), (pv("FilterName", "writer_pdf_Export"),))
    doc.close(True)
    print("pdf ->", os.path.relpath(pdf))
finally:
    proc.terminate()
