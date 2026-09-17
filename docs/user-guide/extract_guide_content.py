import re, json, xml.etree.ElementTree as ET, os

REPO = "/home/user/ManFoods-TO"
resx_path = os.path.join(REPO, "Resources/SharedResource.resx")

tree = ET.parse(resx_path)
resx = {}
for data in tree.getroot().findall("data"):
    name = data.get("name")
    val = data.find("value")
    if name and val is not None:
        resx[name] = val.text or ""

def L(key):
    return resx.get(key, f"[[MISSING:{key}]]")

files = [
    "_ActionCenterContent.cshtml",
    "_WorkforceContent.cshtml",
    "_TurnoverContent.cshtml",
    "_NinetyDayTurnoverContent.cshtml",
    "_RetentionContent.cshtml",
    "_EarlyWarningContent.cshtml",
    "_ExitInterviewsContent.cshtml",
    "_ComparisonsContent.cshtml",
    "_ScorecardContent.cshtml",
    "_StoresContent.cshtml",
    "_ReportsContent.cshtml",
    "_ReportDetailContent.cshtml",
    "_StoreProfileContent.cshtml",
    "_ActionCenterDetailContent.cshtml",
]

pages = []
for fname in files:
    path = os.path.join(REPO, "Views/Shared/Dashboard", fname)
    with open(path, encoding="utf-8") as f:
        src = f.read()

    m = re.search(r'PageKey\s*=\s*"([^"]+)"', src)
    pagekey = m.group(1) if m else fname

    mtitle = re.search(r'<h1[^>]*>@L\["([^"]+)"\]', src)
    title_key = mtitle.group(1) if mtitle else None
    title = L(title_key) if title_key else pagekey

    moverview = re.search(r'Overview\s*=\s*L\["([^"]+)"\]', src)
    overview = L(moverview.group(1)) if moverview else None

    mfilter = re.search(r'FilterNote\s*=\s*L\["([^"]+)"\]', src)
    filternote = L(mfilter.group(1)) if mfilter else None

    sections = []
    for sm in re.finditer(
        r'Title\s*=\s*L\["([^"]+)"\]\s*,\s*Body\s*=\s*L\["([^"]+)"\]', src
    ):
        tkey, bkey = sm.groups()
        sections.append({"title": L(tkey), "body": L(bkey)})

    pages.append({
        "file": fname,
        "pagekey": pagekey,
        "title": title,
        "overview": overview,
        "filternote": filternote,
        "sections": sections,
    })

out_path = os.path.join(os.path.dirname(__file__), "guide_content.json")
with open(out_path, "w", encoding="utf-8") as f:
    json.dump(pages, f, ensure_ascii=False, indent=2)

for p in pages:
    print(p["pagekey"], "-", p["title"], "-", len(p["sections"]), "sections", "-", "OVERVIEW" if p["overview"] else "NO OVERVIEW")
