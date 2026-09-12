#!/usr/bin/env python3
"""Normalise poe2db.tw ModsView JSON dumps (one per item class) into clean JSON.

Input : directory with modsview_<Class>.json (raw argument of `new ModsView({...})`)
Output: <out>/classes/<Class>.json  (per-class categories with normalised entries)
        <out>/mods_index.json        (every distinct mod with the classes/weights it appears on)
"""
import html as htmlmod
import json
import re
import sys
from collections import defaultdict
from pathlib import Path

TAG_RE = re.compile(r"<[^>]+>")
WS_RE = re.compile(r"[ \t]+")
RANGE_RE = re.compile(r"\((-?\d+(?:\.\d+)?)\s*[—–-]\s*(-?\d+(?:\.\d+)?)\)")
GEN = {"1": "prefix", "2": "suffix", "3": "corruption_upgrade", "4": "implicit?", "5": "corrupted_implicit", "0": "socketable"}


def strip_html(s):
    if s is None:
        return ""
    s = str(s)
    s = re.sub(r"<br\s*/?>", "\n", s, flags=re.I)
    s = s.replace("</div>", "\n")
    s = TAG_RE.sub("", s)
    s = htmlmod.unescape(s)
    s = s.replace("—", "-").replace("–", "-")
    s = "\n".join(WS_RE.sub(" ", line).strip() for line in s.split("\n"))
    s = re.sub(r"\n{2,}", "\n", s).strip()
    return s


def hover_hash(url):
    if not url:
        return None
    m = re.search(r"([0-9a-f]{64})", str(url))
    if m:
        return m.group(1)
    m = re.search(r"Mods%2F([A-Za-z0-9_]+)", str(url))
    if m:
        return m.group(1)
    return None


def norm_entry(cat, e):
    text = strip_html(e.get("str"))
    name = strip_html(e.get("Name"))
    gen_id = str(e.get("ModGenerationTypeID", ""))
    code = e.get("Code")
    hh = hover_hash(e.get("hover"))
    entry = {
        "id": code or hh or f"{cat}:{name}:{e.get('Level')}:{text[:40]}",
        "code": code,
        "hash": hh if hh and len(hh) == 64 else None,
        "category": cat,
        "genId": gen_id,
        "gen": GEN.get(gen_id, gen_id),
        "family": (e.get("ModFamilyList") or [None])[0],
        "families": e.get("ModFamilyList") or [],
        "name": name,
        "level": int(e.get("Level") or 0),
        "weight": int(float(e.get("DropChance") or 0)),
        "text": text,
        "ranges": [[float(a), float(b)] for a, b in RANGE_RE.findall(text)],
        "spawnTags": e.get("spawn_no") or [],
        "modTags": e.get("fossil_no") or [],
        "addsNo": e.get("adds_no") or [],
        "type": e.get("type"),
    }
    for k in ("IsPerfect", "IsAlloy", "Removes", "reqlvl", "ID", "ModTypeID", "ModDomainsID", "TagsList", "WeightList"):
        if k in e:
            entry[k] = e[k]
    return entry


def iter_sources(src: Path):
    """Yield (className, rawModsViewDict) from either a directory of modsview_*.json
    files or a single bundle json ({modsview:{cls:{...}}, raw:{page:html}})."""
    if src.is_file():
        bundle = json.loads(src.read_text(encoding="utf-8"))
        rawdir = src.parent / "raw_pages"
        rawdir.mkdir(exist_ok=True)
        for page, html_text in (bundle.get("raw") or {}).items():
            (rawdir / f"{page}.html").write_text(html_text, encoding="utf-8")
        for cls, mv in (bundle.get("modsview") or {}).items():
            yield cls, mv
    else:
        for f in sorted(src.glob("modsview_*.json")):
            yield f.stem[len("modsview_"):], json.loads(f.read_text(encoding="utf-8"))


def main(src, out):
    src, out = Path(src), Path(out)
    (out / "classes").mkdir(parents=True, exist_ok=True)
    index = {}
    summary = []
    for cls, raw in iter_sources(src):
        cats = {}
        titles = {}
        for k, v in raw.items():
            if isinstance(v, list) and v and isinstance(v[0], dict):
                cats[k] = [norm_entry(k, e) for e in v]
                titles[k] = strip_html((raw.get("config", {}).get(k) or {}).get("title", k))
        base = raw.get("baseitem", {})
        doc = {
            "class": cls,
            "classCode": raw.get("opt", {}).get("ItemClassesCode"),
            "classId": raw.get("opt", {}).get("ItemClassesID"),
            "baseName": strip_html(base.get("cn") or base.get("href")),
            "categoryTitles": titles,
            "counts": {k: len(v) for k, v in cats.items()},
            "categories": cats,
        }
        (out / "classes" / f"{cls}.json").write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")
        for cat, entries in cats.items():
            for e in entries:
                key = e["id"]
                rec = index.setdefault(key, {
                    "id": key, "category": cat, "gen": e["gen"], "family": e["family"], "name": e["name"],
                    "level": e["level"], "text": e["text"], "spawnTags": e["spawnTags"], "modTags": e["modTags"],
                    "addsNo": e["addsNo"], "classes": {},
                })
                rec["classes"][cls] = e["weight"]
                if rec["text"] != e["text"] and e["text"] not in rec.setdefault("altTexts", []):
                    rec["altTexts"].append(e["text"])
        summary.append((cls, doc["counts"]))
    (out / "mods_index.json").write_text(json.dumps(index, ensure_ascii=False, indent=1), encoding="utf-8")
    for cls, counts in summary:
        print(cls, counts)
    print("distinct mods:", len(index))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
