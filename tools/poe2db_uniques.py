#!/usr/bin/env python3
"""Build data/uniques.json (unique items with their own modifiers) from the poe2db export.

Usage: poe2db_uniques.py [repo_root]   (default: the repository containing this script)
Reads research/poe2db/poe2db_export_v2.json -> raw["Unique_item"], the rendered poe2db list of every unique item.
A unique grants its own modifiers, so they are not in mods.json (the craftable pool) and are kept per unique here.
Output: [ { "id": "Crown_of_the_Pale_King", "name": "Crown of the Pale King", "baseType": "Cultist Crown",
            "icon": "...", "requirements": {"level": 16, "str": 15, "int": 15},
            "implicits": [...], "mods": ["(50-100)% increased Armour and Energy Shield", ...] } ]
"""
import html
import json
import re
import sys
from pathlib import Path

# one unique = the link with its name and base, followed by its requirement and modifier lines
ENTRY = re.compile(
    r'<a class="UniqueItem"[^>]*href="[^"]*?/(?P<slug>[^"/]+)"[^>]*>\s*'
    r'<span class="uniqueName">(?P<name>[^<]*)</span>\s*'
    r'<span class="uniqueTypeLine">(?P<base>[^<]*)</span>',
    re.S)
ICON = re.compile(r'<img[^>]+src="(?P<url>[^"]+)"')
LINE = re.compile(r'<div class="(?P<kind>implicitMod|explicitMod|requirements)">(?P<body>.*?)</div>\s*(?=<div|</div)', re.S)
REQUIREMENT = re.compile(r'(?:Level\s+(?P<level>\d+)|(?P<value>\d+)\s+(?P<attr>Str|Dex|Int))')


def text_of(fragment):
    """The plain modifier text: '(21<span class="ndash">-</span>26)' -> '(21-26)', keyword links and mod-value spans removed."""
    fragment = re.sub(r'<span class="ndash">.*?</span>', "-", fragment, flags=re.S)
    fragment = re.sub(r"<[^>]+>", "", fragment)
    fragment = html.unescape(fragment).replace("–", "-").replace("�", "-").replace("\xa0", " ")
    return re.sub(r"\s+", " ", fragment).strip()


def requirements_of(fragment):
    result = {}
    for m in REQUIREMENT.finditer(text_of(fragment)):
        if m.group("level"):
            result["level"] = int(m.group("level"))
        else:
            result[m.group("attr").lower()] = int(m.group("value"))
    return result


def main():
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent
    export = json.loads((root / "research" / "poe2db" / "poe2db_export_v2.json").read_text(encoding="utf-8"))
    page = export["raw"]["Unique_item"]

    uniques, seen = [], set()
    matches = list(ENTRY.finditer(page))
    for i, match in enumerate(matches):
        name = text_of(match.group("name"))
        if not name or name in seen:
            continue
        seen.add(name)
        # everything up to the next unique belongs to this one; the icon sits in the link right before it
        block = page[match.end(): matches[i + 1].start() if i + 1 < len(matches) else len(page)]
        before = page[max(0, match.start() - 1200): match.start()]
        icons = ICON.findall(before)

        entry = {
            "id": match.group("slug"),
            "name": name,
            "baseType": text_of(match.group("base")),
            "icon": icons[-1] if icons else None,
            "requirements": {},
            "implicits": [],
            "mods": [],
        }
        for line in LINE.finditer(block):
            body = text_of(line.group("body"))
            if not body:
                continue
            if line.group("kind") == "requirements":
                entry["requirements"] = requirements_of(line.group("body"))
            elif line.group("kind") == "implicitMod":
                entry["implicits"].append(body)
            else:
                entry["mods"].append(body)
        if entry["mods"] or entry["implicits"]:
            uniques.append(entry)

    uniques.sort(key=lambda u: u["name"])
    out = root / "data" / "uniques.json"
    out.write_text(json.dumps(uniques, ensure_ascii=False, indent=1) + "\n", encoding="utf-8")
    print(f"{len(uniques)} uniques -> {out}")
    print(f"{sum(len(u['mods']) for u in uniques)} modifiers, {sum(len(u['implicits']) for u in uniques)} implicits")


if __name__ == "__main__":
    main()
