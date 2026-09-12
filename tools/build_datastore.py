#!/usr/bin/env python3
"""Build the simulator's JSON data store from the PoB JSON (bases) and the poe2db parse (mods, items).

Inputs :
  data/pob/Bases/*.json              (from tools/lua_to_json.py)
  data/poe2db/parsed/mods_index.json (from tools/poe2db_parse.py)
  data/poe2db/parsed/classes/*.json
  data/poe2db/items/*.json           (from tools/poe2db_items.py)
Output : data/store/{bases,mods,currencies,omens,essences,item_classes,config}.json
"""
import json
import re
import sys
from collections import defaultdict, OrderedDict
from pathlib import Path

ROOT = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parent.parent / "data"
OUT = ROOT / "store"
OUT.mkdir(parents=True, exist_ok=True)

# ----------------------------------------------------------------------------
# 1. Item classes and mapping of PoB bases -> poe2db ModsView page
# ----------------------------------------------------------------------------
DEF_SUFFIX = {
    "Armour": "str", "Evasion": "dex", "Energy Shield": "int",
    "Armour/Evasion": "str_dex", "Armour/Energy Shield": "str_int",
    "Evasion/Energy Shield": "dex_int", "Armour/Evasion/Energy Shield": "str_dex_int",
}

# PoE2 item class name -> (poe2db page or callable, base file, slot, category)
ITEM_CLASSES = OrderedDict([
    ("Wand", dict(page="Wands", file="wand", slot="Weapon1H", group="Caster Weapon")),
    ("Staff", dict(page="Staves", file="staff", slot="Weapon2H", group="Caster Weapon")),
    ("Quarterstaff", dict(page="Quarterstaves", file="staff", slot="Weapon2H", group="Martial Weapon")),
    ("Sceptre", dict(page="Sceptres", file="sceptre", slot="Weapon1H", group="Caster Weapon")),
    ("Focus", dict(page="Foci", file="focus", slot="Offhand", group="Offhand")),
    ("Bow", dict(page="Bows", file="bow", slot="Weapon2H", group="Martial Weapon")),
    ("Crossbow", dict(page="Crossbows", file="crossbow", slot="Weapon2H", group="Martial Weapon")),
    ("Quiver", dict(page="Quivers", file="quiver", slot="Offhand", group="Offhand")),
    ("Spear", dict(page="Spears", file="spear", slot="Weapon1H", group="Martial Weapon")),
    ("Flail", dict(page="Flails", file="flail", slot="Weapon1H", group="Martial Weapon")),
    ("Claw", dict(page="Claws", file="claw", slot="Weapon1H", group="Martial Weapon")),
    ("Dagger", dict(page="Daggers", file="dagger", slot="Weapon1H", group="Martial Weapon")),
    ("One Hand Sword", dict(page="One_Hand_Swords", file="sword", slot="Weapon1H", group="Martial Weapon")),
    ("Two Hand Sword", dict(page="Two_Hand_Swords", file="sword", slot="Weapon2H", group="Martial Weapon")),
    ("One Hand Axe", dict(page="One_Hand_Axes", file="axe", slot="Weapon1H", group="Martial Weapon")),
    ("Two Hand Axe", dict(page="Two_Hand_Axes", file="axe", slot="Weapon2H", group="Martial Weapon")),
    ("One Hand Mace", dict(page="One_Hand_Maces", file="mace", slot="Weapon1H", group="Martial Weapon")),
    ("Two Hand Mace", dict(page="Two_Hand_Maces", file="mace", slot="Weapon2H", group="Martial Weapon")),
    ("Trap", dict(page="Traps", file="traptool", slot="Weapon1H", group="Martial Weapon")),
    ("Talisman", dict(page="Talismans", file="talisman", slot="Weapon1H", group="Martial Weapon")),
    ("Shield", dict(page=None, file="shield", slot="Offhand", group="Armour")),
    ("Buckler", dict(page="Bucklers", file="shield", slot="Offhand", group="Armour")),
    ("Body Armour", dict(page=None, file="body", slot="Body", group="Armour")),
    ("Helmet", dict(page=None, file="helmet", slot="Helmet", group="Armour")),
    ("Gloves", dict(page=None, file="gloves", slot="Gloves", group="Armour")),
    ("Boots", dict(page=None, file="boots", slot="Boots", group="Armour")),
    ("Amulet", dict(page="Amulets", file="amulet", slot="Amulet", group="Jewellery")),
    ("Ring", dict(page="Rings", file="ring", slot="Ring", group="Jewellery")),
    ("Belt", dict(page="Belts", file="belt", slot="Belt", group="Jewellery")),
    ("Life Flask", dict(page="Life_Flasks", file="flask", slot="Flask", group="Flask")),
    ("Mana Flask", dict(page="Mana_Flasks", file="flask", slot="Flask", group="Flask")),
    ("Charm", dict(page="Charms", file="flask", slot="Charm", group="Flask")),
    ("Jewel", dict(page=None, file="jewel", slot="Jewel", group="Jewel")),
])

PAGE_PREFIX = {"Shield": "Shields", "Body Armour": "Body_Armours", "Helmet": "Helmets", "Gloves": "Gloves", "Boots": "Boots"}


def classify_base(name, b, fname):
    """Return (itemClass, page) for a PoB base entry, or (None, None) to skip."""
    t = b.get("type")
    tags = b.get("tags", {})
    sub = b.get("subType")
    if t == "Staff":
        return ("Quarterstaff", "Quarterstaves") if "warstaff" in tags else ("Staff", "Staves")
    if t == "Shield":
        if "buckler" in tags or sub == "Evasion":
            return "Buckler", "Bucklers"
        if sub in DEF_SUFFIX:
            return "Shield", "Shields_" + DEF_SUFFIX[sub]
        return "Shield", "Shields_str"
    if t in ("Body Armour", "Helmet", "Gloves", "Boots"):
        suf = DEF_SUFFIX.get(sub)
        if suf is None:
            # no defences (e.g. Golden Mantle) -> assume all-attribute pool
            suf = "str_dex_int" if t == "Body Armour" else "str"
        if suf == "str_dex_int" and t != "Body Armour":
            # poe2db has no calc page for tri-defence helmets/gloves/boots; engine falls back to tag filtering
            return t, None
        return t, PAGE_PREFIX[t] + "_" + suf
    if t == "Flask":
        return ("Life Flask", "Life_Flasks") if sub == "Life" else ("Mana Flask", "Mana_Flasks")
    if t == "Charm":
        return "Charm", "Charms"
    if t == "Jewel":
        if name == "Timeless Jewel":
            return None, None
        return "Jewel", name.replace(" ", "_")
    if t == "TrapTool":
        return "Trap", "Traps"
    if t in ("Fishing Rod", "Transcendent Limb"):
        return None, None
    for cls, meta in ITEM_CLASSES.items():
        if cls == t:
            return cls, meta["page"]
    return None, None


bases = []
for f in sorted((ROOT / "pob" / "Bases").glob("*.json")):
    data = json.loads(f.read_text(encoding="utf-8"))
    for name, b in data.items():
        cls, page = classify_base(name, b, f.stem)
        if cls is None:
            continue
        tags = b.get("tags", {})
        entry = OrderedDict(
            id=re.sub(r"[^A-Za-z0-9]+", "_", name).strip("_"),
            name=name,
            itemClass=cls,
            modPage=page,
            subType=b.get("subType"),
            tags=sorted(k for k, v in tags.items() if v),
            implicit=b.get("implicit"),
            implicitModTypes=b.get("implicitModTypes") or [],
            socketLimit=b.get("socketLimit"),
            quality=b.get("quality"),
            hidden=bool(b.get("hidden")),
            requirements=b.get("req") or {},
        )
        for k in ("weapon", "armour", "flask", "charm", "jewelRadius"):
            if k in b:
                entry[k] = b[k]
        bases.append(entry)

print("bases:", len(bases))
(OUT / "bases.json").write_text(json.dumps(bases, ensure_ascii=False, indent=1), encoding="utf-8")

# item class catalogue
classes_out = []
for cls, meta in ITEM_CLASSES.items():
    pages = sorted({b["modPage"] for b in bases if b["itemClass"] == cls and b["modPage"]})
    classes_out.append(OrderedDict(name=cls, slot=meta["slot"], group=meta["group"], modPages=pages,
                                   baseCount=sum(1 for b in bases if b["itemClass"] == cls)))
(OUT / "item_classes.json").write_text(json.dumps(classes_out, ensure_ascii=False, indent=1), encoding="utf-8")

# ----------------------------------------------------------------------------
# 2. Mods (from poe2db)
# ----------------------------------------------------------------------------
idx = json.loads((ROOT / "poe2db" / "parsed" / "mods_index.json").read_text(encoding="utf-8"))
mods = []
for key, m in idx.items():
    mods.append(OrderedDict(
        id=key,
        category=m["category"],
        gen=m["gen"],
        family=m["family"],
        name=m["name"],
        level=m["level"],
        text=m["text"],
        altTexts=m.get("altTexts", []),
        spawnTags=m["spawnTags"],
        modTags=m["modTags"],
        addsNo=m.get("addsNo", []),
        weights=m["classes"],
    ))
# enrich essence / perfect_essence / special entries with extra fields from class files
extra = {}
for cf in sorted((ROOT / "poe2db" / "parsed" / "classes").glob("*.json")):
    doc = json.loads(cf.read_text(encoding="utf-8"))
    for cat, entries in doc["categories"].items():
        for e in entries:
            ex = {k: e[k] for k in ("IsPerfect", "IsAlloy", "Removes", "reqlvl", "code", "type", "ranges") if k in e and e[k] not in (None, [])}
            if ex:
                extra.setdefault(e["id"], {}).update(ex)
for m in mods:
    if m["id"] in extra:
        m.update(extra[m["id"]])
mods.sort(key=lambda m: (m["category"], m["gen"], m["family"] or "", -m["level"], m["name"]))
(OUT / "mods.json").write_text(json.dumps(mods, ensure_ascii=False, indent=1), encoding="utf-8")
print("mods:", len(mods))

# ----------------------------------------------------------------------------
# 3. Currencies, omens, essences, alloys, catalysts (from poe2db item lists)
# ----------------------------------------------------------------------------
items = json.loads((ROOT / "poe2db" / "items" / "Stackable_Currency.json").read_text(encoding="utf-8"))
omens_raw = json.loads((ROOT / "poe2db" / "items" / "Omen.json").read_text(encoding="utf-8"))


def num(v):
    try:
        return int(str(v).replace(",", ""))
    except Exception:
        return None


# engine operation mapping for the crafting currencies we simulate
OPS = {
    "Orb of Transmutation": dict(op="transmute", rarityIn=["Normal"], rarityOut="Magic", adds=1),
    "Greater Orb of Transmutation": dict(op="transmute", rarityIn=["Normal"], rarityOut="Magic", adds=1),
    "Perfect Orb of Transmutation": dict(op="transmute", rarityIn=["Normal"], rarityOut="Magic", adds=1),
    "Orb of Augmentation": dict(op="augment", rarityIn=["Magic"], adds=1),
    "Greater Orb of Augmentation": dict(op="augment", rarityIn=["Magic"], adds=1),
    "Perfect Orb of Augmentation": dict(op="augment", rarityIn=["Magic"], adds=1),
    "Regal Orb": dict(op="regal", rarityIn=["Magic"], rarityOut="Rare", adds=1),
    "Greater Regal Orb": dict(op="regal", rarityIn=["Magic"], rarityOut="Rare", adds=1),
    "Perfect Regal Orb": dict(op="regal", rarityIn=["Magic"], rarityOut="Rare", adds=1),
    "Exalted Orb": dict(op="exalt", rarityIn=["Rare"], adds=1),
    "Greater Exalted Orb": dict(op="exalt", rarityIn=["Rare"], adds=1),
    "Perfect Exalted Orb": dict(op="exalt", rarityIn=["Rare"], adds=1),
    "Chaos Orb": dict(op="chaos", rarityIn=["Rare"], removes=1, adds=1),
    "Greater Chaos Orb": dict(op="chaos", rarityIn=["Rare"], removes=1, adds=1),
    "Perfect Chaos Orb": dict(op="chaos", rarityIn=["Rare"], removes=1, adds=1),
    "Orb of Alchemy": dict(op="alchemy", rarityIn=["Normal", "Magic"], rarityOut="Rare", adds=4),
    "Orb of Annulment": dict(op="annul", rarityIn=["Magic", "Rare"], removes=1),
    "Divine Orb": dict(op="divine", rarityIn=["Magic", "Rare", "Unique"]),
    "Orb of Chance": dict(op="chance", rarityIn=["Normal"]),
    "Fracturing Orb": dict(op="fracture", rarityIn=["Rare"], minMods=4),
    "Vaal Orb": dict(op="vaal", rarityIn=["Normal", "Magic", "Rare", "Unique"]),
    "Mirror of Kalandra": dict(op="mirror", rarityIn=["Normal", "Magic", "Rare"]),
    "Hinekora's Lock": dict(op="lock", rarityIn=["Normal", "Magic", "Rare", "Unique"]),
    "Scroll of Wisdom": dict(op="identify"),
    "Artificer's Orb": dict(op="socket"),
    "Orb of Extraction": dict(op="extract"),
    "Blacksmith's Whetstone": dict(op="quality", qualityTarget="martial_weapon"),
    "Arcanist's Etcher": dict(op="quality", qualityTarget="caster_weapon"),
    "Armourer's Scrap": dict(op="quality", qualityTarget="armour"),
    "Glassblower's Bauble": dict(op="quality", qualityTarget="flask"),
    "Vaal Armourer's Infuser": dict(op="vaal_quality", qualityTarget="armour"),
    "Vaal Blacksmith's Infuser": dict(op="vaal_quality", qualityTarget="martial_weapon"),
    "Vaal Arcanist's Infuser": dict(op="vaal_quality", qualityTarget="caster_weapon"),
    "Vaal Catalysing Infuser": dict(op="vaal_quality", qualityTarget="jewellery"),
    "Architect's Orb": dict(op="architect", rarityIn=["Normal", "Magic", "Rare", "Unique"], requiresCorrupted=True),
    "Yaomac's Orb of Sacrifice": dict(op="sacrifice", rarityIn=["Rare"], requiresCorrupted=True, target="weapon_or_quiver"),
    "Kopec's Orb of Sacrifice": dict(op="sacrifice", rarityIn=["Rare"], requiresCorrupted=True, target="armour"),
    "Kamasa's Orb of Sacrifice": dict(op="sacrifice", rarityIn=["Rare"], requiresCorrupted=True, target="jewellery"),
    "Yugul's Orb of Sacrifice": dict(op="sacrifice", rarityIn=["Rare"], requiresCorrupted=True, target="jewel"),
    "Gnawed Jawbone": dict(op="desecrate", rarityIn=["Rare"], target="weapon_or_quiver", maxItemLevel=64),
    "Preserved Jawbone": dict(op="desecrate", rarityIn=["Rare"], target="weapon_or_quiver"),
    "Ancient Jawbone": dict(op="desecrate", rarityIn=["Rare"], target="weapon_or_quiver", minModLevel=40),
    "Gnawed Rib": dict(op="desecrate", rarityIn=["Rare"], target="armour", maxItemLevel=64),
    "Preserved Rib": dict(op="desecrate", rarityIn=["Rare"], target="armour"),
    "Ancient Rib": dict(op="desecrate", rarityIn=["Rare"], target="armour", minModLevel=40),
    "Gnawed Collarbone": dict(op="desecrate", rarityIn=["Rare"], target="jewellery", maxItemLevel=64),
    "Preserved Collarbone": dict(op="desecrate", rarityIn=["Rare"], target="jewellery"),
    "Ancient Collarbone": dict(op="desecrate", rarityIn=["Rare"], target="jewellery", minModLevel=40),
    "Altered Collarbone": dict(op="desecrate", rarityIn=["Rare"], target="jewellery", otherworldly=True),
    "Preserved Cranium": dict(op="desecrate", rarityIn=["Rare"], target="jewel"),
    "Blazing Flux": dict(op="flux", element="Fire"),
    "Chilling Flux": dict(op="flux", element="Cold"),
    "Crackling Flux": dict(op="flux", element="Lightning"),
    "Void Flux": dict(op="flux", element="Chaos"),
}

currencies = []
essences = []
alloys = []
catalysts = []
for it in items:
    name = it["name"]
    props = it.get("properties", {})
    rec = OrderedDict(
        name=name,
        slug=it.get("slug"),
        section=it["section"],
        stackSize=props.get("Stack Size"),
        minModLevel=num(props.get("Minimum Modifier Level")),
        maxItemLevel=num(props.get("Maximum Item Level")),
        description=it.get("description", []),
    )
    desc = it.get("description", [])
    if it["section"] == "Catalysts":
        m = re.search(r"enhances (.+?) modifiers on (a|an) (.+?) Replaces", " ".join(desc))
        rec["qualityType"] = m.group(1) if m else None
        rec["target"] = m.group(3) if m else None
        catalysts.append(rec)
        continue
    is_alloy = name.endswith("Alloy")
    is_essence = it["section"] == "Essence" and not is_alloy
    if is_alloy and any(a["name"] == name for a in alloys):
        continue
    if is_alloy or is_essence:
        tier = "Perfect" if name.startswith("Perfect") else "Greater" if name.startswith("Greater") else "Lesser" if name.startswith("Lesser") else ("Alloy" if is_alloy else ("Corrupted" if name in (
            "Essence of Hysteria", "Essence of Delirium", "Essence of Horror", "Essence of Insanity", "Essence of the Abyss", "Essence of the Breach") else "Normal"))
        rec["tier"] = tier
        rec["removesRandomModifier"] = desc[0].startswith("Removes a random modifier") if desc else False
        rec["rarityIn"] = ["Rare"] if rec["removesRandomModifier"] else ["Magic"]
        rec["rarityOut"] = "Rare"
        # per-class guaranteed mod text lines: "Focus or Wand : (35 — 44) % increased Spell Damage"
        per_class = []
        for line in desc[1:]:
            m = re.match(r"^(.+?)\s*:\s*(.+)$", line)
            if m:
                per_class.append(OrderedDict(targets=[t.strip() for t in re.split(r",| or ", m.group(1))], text=m.group(2).replace("—", "-")))
        rec["guaranteedByClass"] = per_class
        (alloys if is_alloy else essences).append(rec)
        continue
    if name in OPS:
        rec.update(OPS[name])
    currencies.append(rec)

(OUT / "currencies.json").write_text(json.dumps(currencies, ensure_ascii=False, indent=1), encoding="utf-8")
(OUT / "essences.json").write_text(json.dumps(essences, ensure_ascii=False, indent=1), encoding="utf-8")
(OUT / "alloys.json").write_text(json.dumps(alloys, ensure_ascii=False, indent=1), encoding="utf-8")
(OUT / "catalysts.json").write_text(json.dumps(catalysts, ensure_ascii=False, indent=1), encoding="utf-8")
print("currencies:", len(currencies), "essences:", len(essences), "alloys:", len(alloys), "catalysts:", len(catalysts))

# omens: description -> target currency + effect
OMEN_RULES = [
    (r"next Chaos Orb will remove the lowest level modifier", "Chaos Orb", "remove_lowest_level"),
    (r"next Chaos Orb will remove only prefix", "Chaos Orb", "remove_prefix_only"),
    (r"next Chaos Orb will remove only suffix", "Chaos Orb", "remove_suffix_only"),
    (r"next Chaos Orb will replace all Modifiers on a Waystone", "Chaos Orb", "waystone_reroll"),
    (r"next Orb of Alchemy will result in the maximum number of prefix", "Orb of Alchemy", "max_prefixes"),
    (r"next Orb of Alchemy will result in the maximum number of suffix", "Orb of Alchemy", "max_suffixes"),
    (r"next Regal Orb will add only prefix", "Regal Orb", "add_prefix_only"),
    (r"next Regal Orb will add only suffix", "Regal Orb", "add_suffix_only"),
    (r"next Regal Orb will add a Modifier of the same type", "Regal Orb", "homogenising"),
    (r"next Vaal Orb will always result in change", "Vaal Orb", "force_change"),
    (r"next Exalted Orb will add two random modifiers", "Exalted Orb", "add_two"),
    (r"next Exalted Orb will add only prefix", "Exalted Orb", "add_prefix_only"),
    (r"next Exalted Orb will add only suffix", "Exalted Orb", "add_suffix_only"),
    (r"next Exalted Orb will add a Modifier of the same type", "Exalted Orb", "homogenising"),
    (r"next Exalted Orb will consume all Catalyst Quality", "Exalted Orb", "catalysing"),
    (r"next Orb of Annulment will remove two modifiers", "Orb of Annulment", "remove_two"),
    (r"next Orb of Annulment will remove only prefix", "Orb of Annulment", "remove_prefix_only"),
    (r"next Orb of Annulment will remove only suffix", "Orb of Annulment", "remove_suffix_only"),
    (r"next Orb of Annulment will remove only Desecrated", "Orb of Annulment", "remove_desecrated_only"),
    (r"next Divine Orb will only reroll Implicit", "Divine Orb", "implicits_only"),
    (r"next Divine Orb used on a Rare item will Sanctify", "Divine Orb", "sanctify"),
    (r"next Orb of Chance will not destroy", "Orb of Chance", "no_destroy"),
    (r"next Orb of Chance will upgrade the Item to a random Unique", "Orb of Chance", "random_unique_of_class"),
    (r"next Perfect or Corrupted Essence will remove only Prefix", "Essence", "remove_prefix_only"),
    (r"next Perfect or Corrupted Essence will remove only Suffix", "Essence", "remove_suffix_only"),
    (r"reveal Desecrated modifiers you can reroll", "Desecration", "reroll_reveal_once"),
    (r"Desecration attempt will guarantee a random Ulaman", "Desecration", "guarantee_ulaman"),
    (r"Desecration attempt will guarantee a random Amanamu", "Desecration", "guarantee_amanamu"),
    (r"Desecration attempt will guarantee a random Kurgal", "Desecration", "guarantee_kurgal"),
    (r"Desecration attempt will replace all modifiers", "Desecration", "putrefaction"),
    (r"Desecration attempt will add only prefix", "Desecration", "add_prefix_only"),
    (r"Desecration attempt will add only suffix", "Desecration", "add_suffix_only"),
]
omens = []
for it in omens_raw:
    desc = " ".join(it.get("description", []))
    target, effect = None, None
    for pat, tgt, eff in OMEN_RULES:
        if re.search(pat, desc):
            target, effect = tgt, eff
            break
    omens.append(OrderedDict(name=it["name"], slug=it.get("slug"), stackSize=it.get("properties", {}).get("Stack Size"),
                             description=desc, targetCurrency=target, effect=effect, crafting=target is not None))
(OUT / "omens.json").write_text(json.dumps(omens, ensure_ascii=False, indent=1), encoding="utf-8")
print("omens:", len(omens), "crafting omens:", sum(1 for o in omens if o["crafting"]))

# ----------------------------------------------------------------------------
# 4. Config with documented assumptions (editable by the user)
# ----------------------------------------------------------------------------
config = OrderedDict(
    schemaVersion=1,
    gameVersion="0.5.x (data exported 2026-09-11 from poe2db.tw)",
    assumptions=OrderedDict(
        weightsSource="poe2db.tw DropChance estimates (poe2db: 'Modifier weight information cannot be obtained from game files')",
        affixTypeSelection="weighted",  # weighted | equal ; how prefix vs suffix is chosen when both are possible
        magicMaxPrefixes=1, magicMaxSuffixes=1, rareMaxPrefixes=3, rareMaxSuffixes=3,
        alchemyModCount=4,
        alchemyOnMagicKeepsExistingMods=True,
        whittlingRule="lowest_mod_level_then_random",
        homogenisingRule="shares_any_mod_tag_with_random_existing_mod",
        restrictedOmenWhenNoSlot="currency_not_applicable",
        vaalOutcomes=OrderedDict(no_change=0.25, corrupted_implicit=0.25, reroll_mods=0.25, add_socket_or_quality=0.25),
        vaalOutcomesNote="UNVERIFIED placeholder distribution; edit freely",
        modLevelRequirementFactor=0.8,
        qualityPerUse=OrderedDict(Normal=5, Magic=2, Rare=1, Unique=1),
        shardsPerOrb=10,
        onlyOneCraftedModPerItem=True,
    ),
)
(OUT / "config.json").write_text(json.dumps(config, ensure_ascii=False, indent=1), encoding="utf-8")
print("store written to", OUT)
