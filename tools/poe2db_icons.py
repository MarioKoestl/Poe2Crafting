#!/usr/bin/env python3
"""Download the icons of every currency, omen, essence, alloy, catalyst, augment and base item of the data store, so the app serves them locally.

Usage: poe2db_icons.py [repo_root]   (default: the repository containing this script)
- icon art paths come from poe2db list pages (link  href="Slug"><img src=".../Art/2DItems/...webp">): the currency pages and, for base
  items, the item class pages derived from the classes' mod pages ("Body_Armours_str" -> "Body_Armours"); slugs missing there
  are looked up on their own item page (first 2DItems image)
- the files are downloaded from the RePoE-fork GitHub pages mirror (same art paths, public; poe2db's CDN refuses
  some folders such as Essence outside its own site), falling back to poe2db
- files land in src/POE2Crafting.Web/wwwroot/img/icons/<art path>; existing files are not downloaded again
- data/icons.json maps each slug to the local web path (e.g. "img/icons/Currency/Essence/LifeEssence.webp")
"""
import json
import re
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

BASE = "https://poe2db.tw/us/"
LIST_PAGES = ["Stackable_Currency", "Omen", "Essence", "Catalysts", "Augment"]
DATA_FILES = ["currencies.json", "omens.json", "essences.json", "alloys.json", "catalysts.json"]
# base items: slug = the base's id ("Gold_Amulet"); hidden bases (not offered in the app) are skipped
BASES_FILE = "bases.json"
ATTRIBUTE_SUFFIX = re.compile(r"(_(str|dex|int))+$")
JEWEL_PAGES = {"Diamond", "Emerald", "Ruby", "Sapphire"}


def base_list_pages(item_classes):
    """poe2db item class pages listing the base types with their icons, from the classes' mod pages."""
    pages = set()
    for cls in item_classes:
        for page in cls.get("modPages") or []:
            page = ATTRIBUTE_SUFFIX.sub("", page)
            pages.add("Jewels" if page.replace("Time-Lost_", "") in JEWEL_PAGES else page)
    return sorted(pages)
# augments (runes, soul cores, idols) have no data file of their own: their names come from the "socketable" mods
AUGMENT_CATEGORY = "socketable"


def augment_slug(name):
    """Same rule as GameData: "Soul Core of Tacati" -> "Soul_Core_of_Tacati", "Aldur's Legacy" -> "Aldurs_Legacy"."""
    return name.replace("'", "").replace(" ", "_")
ART_PREFIX = "Art/2DItems/"
SOURCES = ["https://repoe-fork.github.io/poe2/", "https://cdn.poe2db.tw/image/"]
LINK_ICON = re.compile(r'href="([^"#?]+)"><img[^>]*?src="https://cdn\.poe2db\.tw/image/(Art/2DItems/[^"]+\.webp)"')
ANY_ICON = re.compile(r'https://cdn\.poe2db\.tw/image/(Art/2DItems/[^"\' ]+\.webp)')
HEADERS = {"User-Agent": "POE2Crafting icon collector"}


def fetch(url):
    url = urllib.parse.quote(url, safe=":/")  # names like "Legacy of Mjölner"
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=30) as resp:
        return resp.read()


def art_paths(slugs, pages):
    """slug -> art path (Art/2DItems/...webp) from poe2db."""
    found = {}
    for page in pages:
        try:
            for slug, path in LINK_ICON.findall(fetch(BASE + page).decode("utf-8", errors="replace")):
                found.setdefault(slug, path)
        except Exception as ex:  # noqa: BLE001 - a missing list page only means more single lookups
            print(f"list page {page}: {ex}")
        time.sleep(1)
    paths = {s: found[s] for s in slugs if s in found}
    for slug in (s for s in slugs if s not in paths):
        try:
            match = ANY_ICON.search(fetch(BASE + slug.replace("/us/", "")).decode("utf-8", errors="replace"))
            if match:
                paths[slug] = match.group(1)
        except Exception as ex:  # noqa: BLE001 - report and continue
            print(f"{slug}: {ex}")
        time.sleep(1)
    return paths


def download(art_path, icons_dir):
    """Download an icon once; returns the local web path or None."""
    target = icons_dir / art_path[len(ART_PREFIX):]
    if not target.exists():
        for source in SOURCES:
            try:
                data = fetch(source + art_path)
            except Exception:  # noqa: BLE001 - try the next source
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(data)
            break
        else:
            return None
    return "img/icons/" + art_path[len(ART_PREFIX):]


def main():
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent
    data_dir = root / "data"
    icons_dir = root / "src" / "POE2Crafting.Web" / "wwwroot" / "img" / "icons"
    slugs = {e["slug"].replace("/us/", "") for f in DATA_FILES for e in json.loads((data_dir / f).read_text(encoding="utf-8")) if e.get("slug")}
    mods = json.loads((data_dir / "mods.json").read_text(encoding="utf-8"))
    slugs |= {augment_slug(m["name"]) for m in mods if m.get("category") == AUGMENT_CATEGORY}
    bases = [b for b in json.loads((data_dir / BASES_FILE).read_text(encoding="utf-8")) if not b.get("hidden")]
    slugs |= {b["id"] for b in bases}
    # poe2db spells some base slugs from the name ("Scouts_Vest", "Two-Stone_Ring") where the data store's id differs ("Scout_s_Vest")
    name_slugs = {augment_slug(b["name"]): b["id"] for b in bases if augment_slug(b["name"]) != b["id"]}
    slugs |= set(name_slugs)
    slugs = sorted(slugs)
    pages = LIST_PAGES + base_list_pages(json.loads((data_dir / "item_classes.json").read_text(encoding="utf-8")))

    icons = {}
    for slug, path in art_paths(slugs, pages).items():
        local = download(path, icons_dir)
        if local:
            icons[slug] = local
        else:
            print(f"{slug}: download failed ({path})")

    for name_slug, base_id in name_slugs.items():
        if name_slug in icons:
            icons.setdefault(base_id, icons.pop(name_slug))
        # a name spelling that is not a poe2db slug is no missing icon
        slugs.remove(name_slug)
    (data_dir / "icons.json").write_text(json.dumps(dict(sorted(icons.items())), indent=1), encoding="utf-8")
    missing = [s for s in slugs if s not in icons]
    print(f"{len(icons)}/{len(slugs)} icons; missing: {missing}")


if __name__ == "__main__":
    main()
