#!/usr/bin/env python3
"""Build data/instills.json (Liquid Emotions recipes for instilling notable passives on amulets) from the parsed poe2db export.

Usage: poe2db_instills.py [repo_root]   (default: the repository containing this script)
Reads research/poe2db-parsed.zip -> items/Liquid_Emotions.json, sections "Liquid Emotions Passives" and
"Liquid Emotions Only Passives" (notables that exist only through instilling).
Output: [ { "notable": "Flamekeeper", "emotions": ["Guilt", "Ire", "Ire"], "effects": ["20% increased Fire Damage", ...], "instillOnly": false } ]
"""
import json
import re
import sys
import zipfile
from pathlib import Path

SECTIONS = {"Liquid Emotions Passives": False, "Liquid Emotions Only Passives": True}


def notable_name(slug):
    """'/us/Blinding_Flash' -> 'Blinding Flash' (poe2db list entries have no name, only the slug)."""
    return slug.rsplit("/", 1)[-1].replace("_", " ")


def clean(text):
    return re.sub(r"\s+%", "%", re.sub(r"\s+", " ", text)).strip()


def main():
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent
    with zipfile.ZipFile(root / "research" / "poe2db-parsed.zip") as z:
        entries = json.loads(z.read("items/Liquid_Emotions.json").decode("utf-8"))

    recipes = []
    for e in entries:
        if e.get("section") not in SECTIONS:
            continue
        emotions = [x.strip() for x in e.get("properties", {}).get("Liquid Emotions", "").split(",") if x.strip()]
        if len(emotions) != 3:
            continue
        recipes.append({
            "notable": notable_name(e["slug"]),
            "emotions": emotions,
            "effects": [clean(o["text"]) for o in e.get("other", []) if o.get("class") == "implicitMod"],
            "instillOnly": SECTIONS[e["section"]],
        })

    recipes.sort(key=lambda r: r["notable"])
    (root / "data" / "instills.json").write_text(json.dumps(recipes, indent=1, ensure_ascii=False), encoding="utf-8")
    print(f"{len(recipes)} instill recipes written")


if __name__ == "__main__":
    main()
