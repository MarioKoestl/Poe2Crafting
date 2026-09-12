#!/usr/bin/env python3
"""Extract item lists (name, slug, properties, description lines) from poe2db raw list pages.

Usage: poe2db_items.py <raw_pages_dir> <out_dir>
Pages handled: any page whose items are rendered as  div.col > div.d-flex  blocks
(Stackable_Currency, Omen, Essence, Augment, Liquid_Emotions, Cultivated, Unique_item ...).
Section headers (h5 "Name /count") are used to group the items.
"""
import json
import re
import sys
from pathlib import Path

from bs4 import BeautifulSoup


def text_of(el):
    return re.sub(r"\s+", " ", el.get_text(" ", strip=True)).strip()


def parse_page(html_text):
    soup = BeautifulSoup(html_text, "lxml")
    items = []
    # walk headers and following blocks in document order
    current_section = None
    for el in soup.find_all(["h5", "div"]):
        if el.name == "h5":
            t = text_of(el)
            m = re.match(r"(.+?)\s*/\s*(\d+)$", t)
            current_section = m.group(1).strip() if m else t
            continue
        if not ("col" in (el.get("class") or [])):
            continue
        blk = el.find("div", class_="d-flex", recursive=False)
        if blk is None:
            continue
        link = blk.select_one("div.flex-grow-1 > a[href]") or blk.select_one("a[href]")
        if link is None:
            continue
        name = text_of(link)
        slug = link.get("href")
        classes = link.get("class") or []
        props, explicit, enchant, other = [], [], [], []
        body = blk.select_one("div.flex-grow-1")
        for d in body.find_all("div", recursive=True) if body else []:
            cl = d.get("class") or []
            t = text_of(d)
            if not t:
                continue
            if "property" in cl:
                props.append(t)
            elif "explicitMod" in cl:
                explicit.append(t)
            elif "enchantMod" in cl:
                enchant.append(t)
            elif "separator" in cl or "flex-grow-1" in cl or not cl:
                continue
            elif d.find("div") is None:
                other.append({"class": " ".join(cl), "text": t})
        propmap = {}
        for p in props:
            if ":" in p:
                k, v = p.split(":", 1)
                propmap[k.strip()] = v.strip()
            else:
                propmap[p] = True
        items.append({
            "section": current_section,
            "name": name,
            "slug": slug,
            "linkClasses": classes,
            "properties": propmap,
            "description": explicit,
            "enchant": enchant,
            "other": other,
        })
    return items


def main(src, out):
    src, out = Path(src), Path(out)
    out.mkdir(parents=True, exist_ok=True)
    for f in sorted(src.glob("*.html")):
        items = parse_page(f.read_text(encoding="utf-8"))
        if not items:
            print(f"{f.stem}: no item blocks")
            continue
        (out / f"{f.stem}.json").write_text(json.dumps(items, ensure_ascii=False, indent=1), encoding="utf-8")
        secs = {}
        for it in items:
            secs[it["section"]] = secs.get(it["section"], 0) + 1
        print(f"{f.stem}: {len(items)} items  {secs}")


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
