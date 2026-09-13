#!/usr/bin/env python3
"""Download the icons of every currency, omen, essence, alloy and catalyst of the data store, so the app serves them locally.

Usage: poe2db_icons.py [repo_root]   (default: the repository containing this script)
- icon art paths come from poe2db list pages (link  href="Slug"><img src=".../Art/2DItems/...webp">); slugs missing there
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
import urllib.request
from pathlib import Path

BASE = "https://poe2db.tw/us/"
LIST_PAGES = ["Stackable_Currency", "Omen", "Essence", "Catalysts"]
DATA_FILES = ["currencies.json", "omens.json", "essences.json", "alloys.json", "catalysts.json"]
ART_PREFIX = "Art/2DItems/"
SOURCES = ["https://repoe-fork.github.io/poe2/", "https://cdn.poe2db.tw/image/"]
LINK_ICON = re.compile(r'href="([^"#?]+)"><img[^>]*?src="https://cdn\.poe2db\.tw/image/(Art/2DItems/[^"]+\.webp)"')
ANY_ICON = re.compile(r'https://cdn\.poe2db\.tw/image/(Art/2DItems/[^"\' ]+\.webp)')
HEADERS = {"User-Agent": "POE2Crafting icon collector"}


def fetch(url):
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=30) as resp:
        return resp.read()


def art_paths(slugs):
    """slug -> art path (Art/2DItems/...webp) from poe2db."""
    found = {}
    for page in LIST_PAGES:
        for slug, path in LINK_ICON.findall(fetch(BASE + page).decode("utf-8", errors="replace")):
            found.setdefault(slug, path)
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
    slugs = sorted({e["slug"].replace("/us/", "") for f in DATA_FILES for e in json.loads((data_dir / f).read_text(encoding="utf-8")) if e.get("slug")})

    icons = {}
    for slug, path in art_paths(slugs).items():
        local = download(path, icons_dir)
        if local:
            icons[slug] = local
        else:
            print(f"{slug}: download failed ({path})")

    (data_dir / "icons.json").write_text(json.dumps(dict(sorted(icons.items())), indent=1), encoding="utf-8")
    missing = [s for s in slugs if s not in icons]
    print(f"{len(icons)}/{len(slugs)} icons; missing: {missing}")


if __name__ == "__main__":
    main()
