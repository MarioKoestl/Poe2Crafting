#!/usr/bin/env python3
"""Collect the maximum number of augment sockets per base from poe2db.

Every base page on poe2db carries a row "Sockets.socket_info" such as
"1:5:100" (one socket) or "1:5:100, 1:5:100 2:5:100" (up to two sockets):
the socket count of a configuration is the number of space-separated
entries, the maximum over all configurations is the base's socket limit.
The PoB export used by build_datastore.py carries different (wrong) limits,
so the result is written to data/sockets.json (base id -> sockets) and
applied to data/bases.json; build_datastore.py prefers data/sockets.json
over the PoB value as well.

Usage: python tools/poe2db_sockets.py [--apply-only]
  --apply-only  do not fetch, only write the limits of data/sockets.json into data/bases.json
"""
import json
import re
import sys
import time
import urllib.request
from pathlib import Path

DATA = Path(__file__).resolve().parent.parent / "data"
BASES = DATA / "bases.json"
SOCKETS = DATA / "sockets.json"
HEADERS = {"User-Agent": "POE2Crafting socket collector"}


def socket_limit(info: str) -> int | None:
    """'1:5:100, 1:5:100 2:5:100' -> 2 (largest configuration)."""
    best = max((len(group.split()) for group in info.split(",")), default=0)
    return best or None


def fetch_limit(base_id: str) -> int | None:
    url = "https://poe2db.tw/us/" + base_id
    with urllib.request.urlopen(urllib.request.Request(url, headers=HEADERS), timeout=30) as resp:
        html = resp.read().decode("utf-8", "replace")
    infos = re.findall(r"Sockets\.socket_info</td><td>([^<]*)</td>", html)
    limits = [socket_limit(i) for i in infos]
    limits = [l for l in limits if l]
    return max(limits) if limits else None


def apply(bases: list[dict], sockets: dict[str, int]) -> int:
    """Write the poe2db limits into the bases; bases without a poe2db value keep the class value of their siblings."""
    by_class: dict[str, set[int]] = {}
    for b in bases:
        if b["id"] in sockets:
            by_class.setdefault(b["itemClass"], set()).add(sockets[b["id"]])
    changed = 0
    for b in bases:
        if b.get("socketLimit") is None:
            continue
        limit = sockets.get(b["id"])
        if limit is None:
            values = by_class.get(b["itemClass"], set())
            if len(values) != 1:
                print(f"no socket data for {b['name']} ({b['itemClass']}), kept {b['socketLimit']}")
                continue
            limit = next(iter(values))
        if b["socketLimit"] != limit:
            b["socketLimit"] = limit
            changed += 1
    return changed


def main() -> None:
    bases = json.loads(BASES.read_text(encoding="utf-8"))
    sockets: dict[str, int] = json.loads(SOCKETS.read_text(encoding="utf-8")) if SOCKETS.exists() else {}
    if "--apply-only" not in sys.argv:
        todo = [b for b in bases if b.get("socketLimit") is not None]
        for i, b in enumerate(todo, 1):
            try:
                limit = fetch_limit(b["id"])
            except Exception as e:  # 404 for ids with apostrophes/brackets, network
                print(f"{b['id']}: {e}")
                continue
            if limit:
                sockets[b["id"]] = limit
            if i % 100 == 0:
                print(f"{i}/{len(todo)}")
                SOCKETS.write_text(json.dumps(sockets, indent=1), encoding="utf-8")
            time.sleep(0.25)
        SOCKETS.write_text(json.dumps(sockets, indent=1), encoding="utf-8")
    changed = apply(bases, sockets)
    BASES.write_text(json.dumps(bases, ensure_ascii=False, indent=1), encoding="utf-8")
    print(f"sockets: {len(sockets)} bases from poe2db, {changed} limits changed in bases.json")


if __name__ == "__main__":
    main()
