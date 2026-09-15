#!/usr/bin/env python3
"""Names and icons of the items traded on the PoE2 Currency Exchange, so the Market page can show GGG's metadata ids.

Usage: poe2_exchange_items.py [repo_root]   (default: the repository containing this script)
- the exchange API (https://web.poecdn.com/api/currency-exchange/poe2/<hour>) names items by metadata id
  ("Metadata/Items/Currency/CurrencyModValues"); RePoE-fork's base_items.json maps them to name, item class and art
- items: every id seen in the last few hourly digests (all leagues); entries of an existing data/exchange_items.json are kept,
  so running it again during a league only adds items (ids the app doesn't know are shown with a name derived from the id)
- icons are downloaded like poe2db_icons.py (RePoE-fork mirror, existing files are not downloaded again)
- writes data/exchange_items.json: [{id, name, itemClass, icon}]
"""
import json
import sys
import time
from pathlib import Path

from poe2db_icons import ART_PREFIX, download, fetch

BASE_ITEMS = "https://repoe-fork.github.io/poe2/base_items.min.json"
EXCHANGE = "https://web.poecdn.com/api/currency-exchange/poe2/"
DIGEST_HOURS = 6


def recent_ids():
    """Item ids of the last complete hourly digests (all leagues)."""
    ids = set()
    hour = int(time.time()) // 3600 * 3600
    for back in range(1, DIGEST_HOURS + 1):
        try:
            digest = json.loads(fetch(f"{EXCHANGE}{hour - back * 3600}"))
        except Exception as ex:  # noqa: BLE001 - the current hour is not published yet (404); report others
            print(f"digest -{back}h: {ex}")
            continue
        for market in digest.get("markets", []):
            ids.update(market["market_pair"])
        time.sleep(1)
    return ids


def main():
    root = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent
    data_dir = root / "data"
    icons_dir = root / "src" / "POE2Crafting.Web" / "wwwroot" / "img" / "icons"
    target = data_dir / "exchange_items.json"

    bases = json.loads(fetch(BASE_ITEMS))
    ids = recent_ids()
    if target.exists():
        ids |= {e["id"] for e in json.loads(target.read_text(encoding="utf-8"))}

    items, unknown = [], []
    for item_id in sorted(ids):
        base = bases.get(item_id)
        if not base or not base.get("name"):
            unknown.append(item_id)
            continue
        dds = (base.get("visual_identity") or {}).get("dds_file") or ""
        icon = download(dds[:-4] + ".webp", icons_dir) if dds.startswith(ART_PREFIX) else None
        items.append({"id": item_id, "name": base["name"], "itemClass": base["item_class"], "icon": icon})

    target.write_text(json.dumps(sorted(items, key=lambda e: e["name"]), indent=1, ensure_ascii=False), encoding="utf-8")
    print(f"{len(items)} items, {sum(1 for e in items if e['icon'])} with icon; unknown ids: {unknown}")


if __name__ == "__main__":
    main()
