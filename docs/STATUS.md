# POE2 Crafting Simulator – Arbeitsstand (Stand: 2026-09-11)

Dieses Dokument ist der Wiederaufsetzpunkt fuer eine neue Session. Es enthaelt alle Entscheidungen, alle bisherigen Erkenntnisse zu den Datenquellen, die Extraktions-Snippets und den Plan.

## 1. Entscheidungen mit Mario (verbindlich)

- Umfang: ALLE Item-Klassen (Waffen, Ruestung, Schmuck, Flasks, Charms, Jewels). Waystones/Tablets/Relics/Gems vorerst nicht.
- Technologie: .NET / C# (Mario versteht C#). Vorschlag (noch nicht bestaetigt): .NET-Solution mit Core-Library (Domain + Crafting-Engine), JSON-Datenspeicher, Blazor-Web-UI (laeuft lokal per `dotnet run`, oeffnet im Browser), xUnit-Tests. Ziel-Framework net8.0 mit RollForward=LatestMajor, damit es mit SDK 8/9/10 laeuft.
- Sprache: Spiel-Client EN, Simulator-UI EN.
- Simulation: Standard = echtes Zufallsergebnis nach Gewichten; zusaetzlich kann das Ergebnis manuell gewaehlt werden (Planung); vor jedem Schritt Anzeige der moeglichen Ergebnisse mit Wahrscheinlichkeiten.
- Item-Import: Annahme = In-Game Text (Item markieren, Ctrl+C bzw. Ctrl+Alt+C fuer Advanced-Descriptions) + manuelles Zusammenbauen ab Basis. Noch von Mario bestaetigen lassen.
- Dedizierter Datenspeicher (Spieldaten als JSON-Dateien im Projektordner) + Speicherstaende fuer Crafting-Projekte, die einen Neustart ueberleben.
- Projektordner: C:\Development\POE2Crafting (aktuell leer).
- Arbeitsweise: Bei Unklarheiten sofort nachfragen statt annehmen. Vor jeder Dateiaenderung die neueste Version laden (Mario editiert zwischendurch).
- Phasenplan: (1) Wissensdatenbank aus poe2db.tw aufbauen, (2) Design-Vorschlag + offene Fragen klaeren, (3) Simulator bauen.

## 2. Offene Fragen an Mario

Beantwortet (2026-09-11):
- .NET SDKs installiert: 8.0.406, 9.0.200, 10.0.302 -> Ziel net8.0 oder net10.0 moeglich; Vorschlag net8.0 + RollForward=LatestMajor.
- Item-Beispiel (Ctrl+Alt+C) siehe 4.7 – bestaetigt: In-Game "Tier: 1" = bester Tier, Nummerierung = poe2db.

Noch offen:
1. Blazor Server als UI ok? (Alternative: WPF/Avalonia Desktop-App, aber Web-UI ist deutlich schneller zu bauen und zu testen.)
2. Waystone-/Tablet-/Relic-Crafting spaeter dazunehmen? (Rohdaten werden vorsorglich mit exportiert.)
3. "Crafted"-Modifier (siehe 4.7): Herkunft klaeren (Alloy? Perfect Essence? Kalguuran/Runeforged?).

## 3. Datenquellen und Zugriffswege

### 3.1 poe2db.tw (Primaerquelle, von Mario gewuenscht)
- Aus der Cloud-Sandbox NICHT erreichbar (Proxy 403). Erreichbar nur ueber den eingebauten Browser der Desktop-App (Site-Zugriff fuer poe2db.tw und poe2wiki.net dauerhaft freigegeben) und ueber WebFetch (funktioniert fuer Listen-/Itemseiten, liefert aber nur eine Zusammenfassung, keine Rohdaten).
- Item-Klassen-Seiten: https://poe2db.tw/us/<Klasse>#ModifiersCalc. Klassen (33): Claws, Daggers, Wands, One_Hand_Swords, One_Hand_Axes, One_Hand_Maces, Sceptres, Spears, Flails, Bows, Staves, Two_Hand_Swords, Two_Hand_Axes, Two_Hand_Maces, Quarterstaves, Crossbows, Traps, Talismans, Quivers, Shields, Bucklers, Foci, Gloves, Boots, Body_Armours, Helmets, Amulets, Rings, Belts, Life_Flasks, Mana_Flasks, Charms, Jewels. (Weitere: Waystones, Tablet, Relics, Cultivated.)
- Weitere wichtige Seiten: /us/Crafting, /us/Modifiers, /us/Desecrated_Modifiers, /us/Reforging_Bench, /us/Omen, /us/Augment (Runen/Soul Cores), /us/Liquid_Emotions, /us/Stackable_Currency (150 Items), /us/Essence. Einzelseiten: /us/<Name_mit_Unterstrichen> (Apostroph weglassen oder %27).
- Die Mod-Tabellen (ModifiersCalc) sind NICHT im Roh-HTML als Tabelle, sondern als Inline-Script `new ModsView({...})` (ca. 400 KB JSON pro Klasse) und werden client-seitig mit Mustache gerendert. Das JSON laesst sich im Browser per fetch + Klammer-Matcher extrahieren (Snippet in Abschnitt 6).
- JSON-Struktur: `baseitem`, `config` (Kategorie-Titel), `gen` {1: Prefix, 2: Suffix}, `opt` (ItemClassesCode, ItemClassesID, ids, attr), und pro Kategorie ein Array: `normal` (Base Prefix/Suffix), `corrupted` (Corrupted = Vaal-Implicits), `desecrated`, `corruption_upgrade` (Titel "Orb of Sacrifice"), `essence`, `perfect_essence` (Titel "Perfect Essence / Alloy"), `socketable` (Titel "Augment" = Runen/Soul Cores), `bonded` ("Bonded Modifiers"), `destruction` ("Thrud's Might"), `rotmother` ("Rotmother's Ducat"), ausserdem chronomancy, marksman, decay, soul, berserking, liquid, misc, breach_* usw. (bei Wands leer).
- Felder pro Eintrag: `Name` (Affix-Name, bei Essenzen/Runen ein HTML-Link mit Itemname), `Level` (ilvl-Anforderung), `ModGenerationTypeID` (1 Prefix, 2 Suffix, 3 = corruption_upgrade, 5 = corrupted implicit, 0 = socketable), `ModFamilyList` (Familie = Exklusivitaetsgruppe, z. B. WeaponCasterDamagePrefix; Fire/Cold/Lightning/Chaos/PhysSpell-Damage teilen sich bei Wands die Familie WeaponDamageTypePrefix, d. h. nur einer davon pro Item), `DropChance` (Gewicht, siehe 4.4), `str` (Stat-Text als HTML, mit `<span class='mod-value'>` und `—` fuer Bereiche), `fossil_no` (Mod-Tags wie caster, damage, attribute, mana, elemental, fire, amanamu_mod), `spawn_no` (Spawn-Tags, auf welchen Basen der Mod vorkommt, z. B. wand, staff, int_armour, str_dex_armour, ring), `adds_no`, `mod_no` (Tag-Badges HTML), `hover` (URL mit 64-hex Hash, Hover-Seite zeigt Name, Family, Domain, GenerationType, Req. level (Effective), Gold Price, Stats, Spawn Tags mit Gewichten, Craft Tags), bei Essenzen zusaetzlich `Code` (= interne Mod-ID, z. B. SpellDamageOnWeapon2, EssenceSpellSkillLevel1H1), `IsPerfect`, `IsAlloy`, `Removes`, `reqlvl`, `type`.
- Zahlen fuer Wands: normal 185, corrupted 14, desecrated 15, corruption_upgrade 21, essence 18, perfect_essence 12, destruction 9, socketable 100, bonded 78 (452 Zeilen, 289 Familien-Titel im DOM).
- Der Hash in `hover` konnte nicht als SHA-256 der Mod-ID reproduziert werden (240 Varianten getestet). Join zu PoB-Daten daher ueber (GenerationType, Affix-Name, Level, normalisierter Text, Family).

### 3.2 Path of Building PoE2 (strukturierte Massendaten)
- GitHub: https://github.com/PathOfBuildingCommunity/PathOfBuilding-PoE2 (letzter Commit 10.09.2026, Version v0.23.x). In der Sandbox geklont unter /home/claude/research/pob-poe2 (Sandbox ist fluechtig, ggf. neu klonen: `git clone --depth 1 <url>`, ca. 1 Minute).
- Relevante Dateien in src/Data: ModItem.lua (2550 Mods: type Prefix/Suffix, affix, Stat-Text(e), statOrder, level, group, weightKey/weightVal (nur 1/0 = erlaubt/nicht erlaubt, KEINE echten Gewichte), modTags, tradeHashes), ModItemExclusive.lua (5500, u. a. CorruptionUpgrade*, Unique-Mods), ModCorrupted.lua (127 Vaal-Implicits), ModRunes.lua (932, inkl. Bonded), Essence.lua (82), ModJewel.lua (377), ModFlask.lua (78), ModCharm.lua (51), ModMap.lua, CurrencyNames.lua, LiquidEmotions.lua, Bases/*.lua (28 Dateien: amulet, axe, belt, body, boots, bow, claw, crossbow, dagger, fishing, flail, flask, focus, gloves, helmet, incursionlimb, jewel, mace, quiver, ring, sceptre, shield, spear, staff, sword, talisman, traptool, wand – Basen mit Tags, Implicit, Waffen-/Ruestungswerten, Anforderungen).
- Beispiel ModItem-Eintrag: `["SpellDamageOnWeapon8_"] = { type = "Prefix", affix = "Runic", "(105-119)% increased Spell Damage", level = 80, group = "WeaponSpellDamage", weightKey = { "wand", "default" }, weightVal = { 1, 0 }, modTags = { "caster_damage", "damage", "caster" } }`.
- Geplante Rollenverteilung: PoB = Struktur (Basen, Mods, Gruppen, Tags, Level, Wertebereiche); poe2db = Gewichte (DropChance), Spezialkategorien und Verifikation.

### 3.3 Weitere Quellen
- poe2wiki.net: fuer WebFetch gesperrt (Anubis), im eingebauten Browser lesbar. Seite "Modifier" gelesen (Prefix/Suffix/Implicit/Explicit/Enchantment/Corruption/Desecrated-Definitionen, lokale vs. globale Mods).
- mobalytics.gg/poe-2 Guides: per WebFetch lesbar. maxroll.gg und game8.co: per robots.txt gesperrt. pathofexile.com Forum: lesbar.

## 4. Fachliche Erkenntnisse (bisher)

### 4.1 Grundregeln
- Normal: 0 explizite Mods. Magic: max 1 Prefix + 1 Suffix. Rare: max 3 Prefix + 3 Suffix (kann auch weniger haben). Unique: fest.
- Modifier-Typen: Implicit, Explicit (Prefix/Suffix), Enchantment (Runen/Instilling), Corruption (Vaal), Desecrated (Knochen, zunaechst verdeckt, Aufdecken am Well of Souls), Bonded/Socket-Bound (Runen).
- Ein Item kann nicht zwei Mods derselben Familie haben.
- Mods haben Tags (Damage, Caster, Attribute, Mana, Elemental, Fire, ...) – relevant fuer Omens (Homogenising), Essenzen, Katalysatoren.
- Item Level begrenzt die Tiers: Mod-Level (Level-Feld) muss <= Item Level sein.
- Mods erhoehen die Level-Anforderung des Items: poe2db zeigt z. B. "Req. level 80 (Effective: 64)" (vermutlich 80 % des Mod-Levels, noch pruefen).

### 4.2 Currency (aus poe2db Stackable_Currency, Originaltexte)
- Orb of Transmutation: "Upgrades a Normal item to a Magic item with 1 modifier". Greater (Minimum Modifier Level: 44), Perfect (Min Mod Level: 70).
- Orb of Augmentation: "Augments a Magic item with a new random modifier". Greater (Min 44), Perfect (Min 70).
- Regal Orb: "Upgrades a Magic item to a Rare item, adding 1 modifier". Greater (Min 35), Perfect (Min 50).
- Exalted Orb: "Augments a Rare item with a new random modifier". Greater (Min 35), Perfect (Min 50).
- Chaos Orb: "Removes a random modifier and augments a Rare item". Greater (Min 35), Perfect (Min 50). (PoE2: entfernt EINEN Mod und fuegt einen neuen hinzu, kein Komplett-Reroll.)
- Orb of Alchemy: "Upgrades a Normal or Magic item to a Rare item with 4 random modifiers".
- Orb of Annulment: "Removes a random modifier from an item".
- Divine Orb: "Randomises the numeric values of modifiers on an item".
- Orb of Chance: "Unpredictably either upgrades a Normal item to Unique rarity or destroys it".
- Fracturing Orb: "Fracture a random modifier on a rare item with at least 4 modifiers".
- Vaal Orb: "Modifies an item unpredictably and Corrupts it". Omen of Corruption erzwingt eine Aenderung.
- Architect's Orb: "Modifies a Corrupted Equipment or Jewel item unpredictably or destroys it".
- Yaomac's / Kopec's / Kamasa's / Yugul's Orb of Sacrifice: "Upgrades a Corruption Enchantment on a Rare Weapon or Quiver / Armour / Amulet, Ring or Belt / Jewel and removes a random Modifier" (entspricht poe2db-Kategorie corruption_upgrade).
- Mirror of Kalandra: "Creates a Mirrored copy of an item". Hinekora's Lock: "Allows an item to foresee the result of the next Currency item used on it".
- Artificer's Orb: "Adds an Augment Socket to a Martial Weapon, wand, staff or Armour". Orb of Extraction: "Destroys an Equipment item, returning any non Socket-Bound Augments socketed in it".
- Qualitaet: Blacksmith's Whetstone (martial weapon), Arcanist's Etcher (wand, staff, sceptre), Armourer's Scrap (armour), Glassblower's Bauble (flask), Gemcutter's Prism (gem); Vaal Armourer's / Blacksmith's / Arcanist's / Catalysing Infuser: "exceeding maximum quality by up to 10%".
- Vaal Cultivation Orb: "Replaces up to 2 modifiers on a Corrupted Vaal Unique". Core Destabiliser (Soul Core), Crystallised Corruption (Skill Gem), Ancient Infuser (Tablet).
- Shards: Transmutation Shard, Chance Shard, Regal Shard, Artificer's Shard. Scroll of Wisdom: identifiziert.
- Jeweller's Orbs (Lesser/Greater/Perfect): Support-Gem-Sockel 3/4/5.
- Desecration-Knochen: Gnawed/Preserved/Ancient Jawbone (Rare Weapon oder Quiver), Rib (Rare Armour), Collarbone (Rare Amulet, Ring, Belt); Gnawed: Maximum Item Level 64; Ancient: Minimum Modifier Level 40; Preserved Cranium (Rare Jewel), Preserved Vertebrae (Rare Waystone), Altered Collarbone ("with a chance for otherworldly modifiers").
- Liquid/Distilled Emotions (Ire, Guilt, Greed, Paranoia, Envy, Disgust, Despair, Fear, Suffering, Isolation, Melancholy, Ferocity, Contempt + Ancient-Varianten fuer Time-Lost Jewels), Katalysatoren (26 Stueck, Liste noch zu holen), Essenzen (95 Stueck, Liste noch zu holen).

### 4.3 Omens (komplette Liste von poe2db.tw/us/Omen, Originaltexte, jeweils "While this item is active in your inventory ...")
- Whittling: next Chaos Orb will remove the lowest level modifier.
- Sinistral/Dextral Erasure: next Chaos Orb will remove only prefix/suffix modifiers.
- Sinistral/Dextral Alchemy: next Orb of Alchemy will result in the maximum number of prefix/suffix modifiers.
- Sinistral/Dextral Coronation: next Regal Orb will add only prefix/suffix modifiers.
- Homogenising Coronation: next Regal Orb will add a Modifier of the same type as an existing Modifier.
- Corruption: next Vaal Orb will always result in change.
- Greater Exaltation: next Exalted Orb will add two random modifiers. Sinistral/Dextral Exaltation: only prefix/suffix. Homogenising Exaltation: same type as an existing Modifier. Catalysing Exaltation: consumes all Catalyst Quality to increase the chance of the corresponding type of Modifier.
- Greater Annulment: next Orb of Annulment will remove two modifiers. Sinistral/Dextral Annulment: only prefix/suffix. Omen of Light: Annulment removes only Desecrated modifiers.
- The Blessed: next Divine Orb will only reroll Implicit Modifiers. Sanctification: next Divine Orb used on a Rare item will Sanctify it.
- Chance: next Orb of Chance will not destroy the Item. The Ancients: next Orb of Chance will upgrade the Item to a random Unique of the same Item Class.
- Sinistral/Dextral Crystallisation: next Perfect or Corrupted Essence will remove only Prefix/Suffix modifiers.
- Abyssal Echoes: next reveal of Desecrated modifiers can be rerolled once. The Sovereign / The Liege / The Blackblooded: next Weapon or Jewellery Desecration guarantees a random Ulaman / Amanamu / Kurgal modifier. Putrefaction: next Desecration replaces all modifiers, up to 6 Unrevealed modifiers, Corrupts the item. Sinistral/Dextral Necromancy: Desecration adds only prefix/suffix.
- Recombination: Beschreibung noch offen. Chaotic Rarity/Quantity/Monsters/Effectiveness: Chaos Orb auf Waystones.
- Nicht-Crafting: Refreshment, Resurgence, Amelioration, Answered Prayers, Secret Compartments, the Hunt, Reinforcements, Gambling, Bartering, Aldur's/Medved's/Vorana's/Uhtred's/Olroth's Saga.

### 4.4 WICHTIG: Gewichte
- poe2db zeigt in der ModifiersCalc den Hinweis: "Modifier weight information cannot be obtained from game files." Die dort angezeigten DropChance-Werte sind poe2db-Schaetzungen (Muster: Spell Damage Tiers 1000/1000/1000/600/400/200/100/50 vom niedrigsten zum hoechsten Tier; Elementar-Damage 500/500/500/400/300/200/100/50; Attribute und Mana alle 1000; Spell-Skill-Level +1..+5 = 1000/750/500/250/100). Die echten Spawn-Tags im Spiel haben nur Gewicht 1/0 (Hover-Seite: "wand: 1 default: 0"), PoB genauso.
- Konsequenz fuer den Simulator: Wahrscheinlichkeiten auf Basis der poe2db-Schaetzwerte, klar als Schaetzung kennzeichnen und im Datenspeicher editierbar machen.

### 4.5 poe2db-Wahrscheinlichkeit
- `pct` in der Tabelle = DropChance / Summe aller Prefix- bzw. Suffix-Gewichte, die beim eingestellten Item Level moeglich sind (z. B. 1000 -> 2.415 % bei Wands).

### 4.6 Tier-Nummerierung (GEKLAERT)
- poe2db: T1 = bester Tier, Nummerierung innerhalb der Mod-Familie (bei Wands hat "+# to Level of all Spell Skills" nur T2/T3/T5/T7, weil hoehere Tiers auf anderen Klassen liegen).
- In-Game (Marios Item vom 11.09.2026): `"Glyphic" (Tier: 2)` fuer 189-208 % Spell Damage auf Staff (Runic = Tier 1), `"Chalybeous" (Tier: 3)` Mana, `"of Desolation" (Tier: 2)` +5-6 Physical Spell Skills auf Staff. Also: In-Game Tier 1 = bester Tier, identisch mit poe2db. Der alte Forum-Thread von 12/2024 (aufwaerts zaehlend) ist ueberholt.
- Tier-Nummern gelten je Mod-Familie und Item-Klassen-Variante: "of Desolation" ist auf Wands T3 (+4), auf Staves Tier 2 (+5-6) – Staff-Mods sind eigene Mods mit eigenen Werten/Tiers.

### 4.7 Item-Text-Format (Ctrl+Alt+C, Advanced) – echtes Beispiel von Mario
```
Item Class: Staves
Rarity: Rare
Dusk Spire
Sanctified Staff
--------
Quality: +12% (augmented)
--------
Requires: Level 56, 99 Int
--------
Sockets: S S 
--------
Item Level: 82
--------
+1 to Level of all Spell Skills (rune)
+1 to Level of all Plant Skill Gems (rune)
--------
Grants Skill: Level 18 Consecrate
--------
{ Prefix Modifier "Glyphic" (Tier: 2) — Damage, Caster }
200(189-208)% increased Spell Damage
{ Prefix Modifier "Chalybeous" (Tier: 3) — Mana }
+238(209-248) to maximum Mana
{ Prefix Modifier "Electrifying" (Tier: 2) — Damage, Elemental, Lightning }
Gain 49(49-54)% of Damage as Extra Lightning Damage
{ Suffix Modifier "of Desolation" (Tier: 2) — Physical, Caster, Gem }
+6(5-6) to Level of all Physical Spell Skills
{ Suffix Modifier "of Havoc" (Tier: 5) — Caster, Critical }
53(50-59)% increased Critical Hit Chance for Spells
{ Crafted Suffix Modifier "of the Stars" }
46(25-50)% chance to gain Nature's Archon when your Plants Overgrow
```
Parser-Erkenntnisse: Abschnitte durch `--------` getrennt; `Requires: Level 56, 99 Int` (eine Zeile); `Quality: +12% (augmented)`; `Sockets: S S` (Augment-Sockel, Runen-Mods enden mit `(rune)`); `Grants Skill: ...` = Implicit der Basis (Sanctified Staff); Mod-Header `{ Prefix|Suffix Modifier "Name" (Tier: N) — Tag, Tag }` mit Gedankenstrich, danach die Statzeile mit `gerollt(min-max)`; es gibt `{ Crafted Suffix Modifier "of the Stars" }` OHNE Tier – ein "Crafted"-Modifier (belegt einen Suffix-Slot; Herkunft vermutlich Alloy/Perfect-Essence-Kategorie "Perfect Essence / Alloy" von poe2db – zu klaeren). Erwartbar ausserdem: (implicit), (enchant), (desecrated), Corrupted, Mirrored, Fractured, Unidentified.

### 4.7b Neue Erkenntnisse aus dem poe2db-Bundle (11.09.2026)
- Essenzen: Lesser/normal/Greater = "Upgrades a Magic item to a Rare item, adding a guaranteed modifier" (Mod je Item-Klasse, Wertebereich je Stufe). Perfect Essence = "Removes a random modifier and augments a Rare item with a new guaranteed modifier". Corrupted Essences (Hysteria, Delirium, Horror, Insanity, the Abyss, the Breach) ebenfalls "Removes a random modifier and augments a Rare item ..." mit Spezial-Mods (z. B. Delirium: Body Armour bekommt "Allocates a random Notable Passive Skill"; Breach: Jewellery +20 % to Maximum Quality; Insanity: Belt "On Corruption, Item gains two Enchantments").
- ALLOYS sind Currency-Items (Runic, Adaptive, Protective, Expansive, Swift, Cyclonic, Prismatic, Mystic, Sovereign, Celestial, Transcendent, The Runebinder's, The Runefather's Alloy): "Removes a random modifier and augments a Rare item with a new guaranteed modifier" mit Mod je Item-Klasse. Marios "Crafted Suffix Modifier of the Stars" = The Runebinder's Alloy (Staff: 25-50 % chance to gain Nature's Archon when your Plants Overgrow). Im Item-Text erscheinen Alloy-/Perfect-Essence-Mods als "{ Crafted Prefix/Suffix Modifier ... }" ohne Tier.
- Weitere Currency (0.5): Blazing/Chilling/Crackling/Void Flux (wandeln alle Resistenz-Mods eines Items in Fire/Cold/Lightning/Chaos um), Perfect Flux (Skills auf Item auf Level 20), Starlit Ore (5 Varianten), Verisium-Varianten, Crests (Medved/Vorana/Uhtred/Olroth), Hinekora's Lock ("Modifying the item in any way removes the ability to foresee"), Vaal Cultivation Orb, Orb of Extraction, Vaal-Infuser ("with a chance of Corrupting it"), Fracturing Orb ("locking it in place").
- Katalysatoren: 13 Typen fuer Ring/Amulett (Flesh=Life, Neural=Mana, Carapace=Defences, Uul-Netol's=Physical, Xoph's=Fire, Tul's=Cold, Esh's=Lightning, Chayula's=Chaos, Reaver=Attack, Sibilant=Caster, Skittering=Speed, Adaptive=Attribute, Necrotic=Minion) + "Refined" Varianten fuer Jewels; "Replaces other quality types".
- poe2db Crafting-Seite (Community-Wiki-Tabelle): Transmutation/Alchemy/Chance nur auf Normal; Regal + Lesser/normal/Greater Essence nur auf Magic; Augmentation nur Magic; Exalted nur Rare; Annulment Magic oder Rare; Chaos nur Rare; Perfect Essence nur Rare; Desecration nur Rare (Collarbone: Amulett/Ring/Guertel, Jawbone: Waffe/Koecher, Rib: Ruestung, Cranium: Jewel). Omen-Kurzfassungen identisch mit Itemtexten.
- Spezialkategorien in den ModsView-Daten: breach_otherworldly/breach_minion/breach_caster (Ringe/Guertel/Amulette – Breach-Mods), marksman + decay (Gloves), chronomancy (Boots), soul (Body Armour), berserking (Helmets), destruction (Waffen = "Thrud's Might"), liquid (Jewels = Liquid-Emotion-Mods), corruption_upgrade (= Orb of Sacrifice, Gewicht 1), corrupted (Vaal-Implicits), desecrated (Amanamu/Kurgal/Ulaman, Level 65), socketable/bonded (Runen, Soul Cores). Diese "Namens"-Kategorien (Medved's, Vorana's, Kolr's, Katla's, of Chronomancy, of Destruction) sind vermutlich Expedition-/Kalguuran-Mods (Crests?) – Mechanik noch zu klaeren.

### 4.8a poe2db ModifiersCalc-Seiten (vollstaendige Liste von /us/Modifiers, 63 Stueck)
Claws, Daggers, Wands, One_Hand_Swords, One_Hand_Axes, One_Hand_Maces, Sceptres, Spears, Flails, Bows, Staves, Two_Hand_Swords, Two_Hand_Axes, Two_Hand_Maces, Quarterstaves, Crossbows, Traps, Talismans, Amulets, Rings, Belts, Quivers, Bucklers, Foci, Life_Flasks, Mana_Flasks, Charms, Gloves_{str,dex,int,str_dex,str_int,dex_int}, Boots_{...6}, Helmets_{...6}, Body_Armours_{str,dex,int,str_dex,str_int,dex_int,str_dex_int}, Shields_{str,str_dex,str_int}, Ruby, Emerald, Sapphire, Diamond, Time-Lost_{Ruby,Emerald,Sapphire,Diamond}. (Relics hat auch ModsView; Waystones/Tablet nicht.) Ruestungs-Klassen haben KEINEN Calc auf der Klassenseite, sondern pro Attribut-Kombination.
Export-Stand 11.09.2026: alle 63 + Relics = 64 ModsView-JSONs plus 12 Roh-HTML-Uebersichtsseiten in einem Bundle `poe2db_export_v2.json` (35 MB), als Browser-Download ausgeloest (Ziel: Marios Downloads, dann manuell nach research\). Bundle-Struktur: {exportedAt, source, modsview:{<Page>: ModsView-JSON}, raw:{<Page>: html}, calcPages:[...]}.

### 4.8 Export der poe2db-Rohdaten (Weg gefunden, 11.09.2026)
- device_bash ist auf Marios Rechner derzeit nicht verfuegbar (Windows-Update vom 08.09. verhindert den Workspace). Downloads-Ordner wollte Mario nicht freigeben.
- Versucht: File System Access API im eingebetteten Browser. `showDirectoryPicker` zeigt den Windows-Dialog, bricht aber danach mit AbortError ab (keine Schreibfreigabe-UI). `showSaveFilePicker` funktioniert bis zum Dialog, `createWritable()` wirft "not allowed by the user agent". => Schreiben ueber File System Access ist im Claude-Browser blockiert.
- Funktionierender Weg: Daten per fetch() im poe2db-Tab sammeln (window.__bundle), dann `<a download>` mit Blob -> Download in Marios Downloads-Ordner -> Mario verschiebt die Datei manuell nach `C:\Development\POE2Crafting\research\` -> device_stage_files in die Sandbox -> tools/poe2db_parse.py. Falls Downloads blockiert sind: letzter Ausweg ist Haeppchen-Transfer durch den Chat (nur Gewichte + Spezialkategorien, ca. 150-300 KB).

## 5. Plan / Task-Liste (Stand)

1. [in Arbeit] poe2db studieren: Mechaniken, Rarity/Affix-Regeln, Tiers. (/us/Crafting noch nicht gelesen!)
2. [teilweise] Katalog aller Crafting-Items: Originaltexte aller Currency/Essenzen/Alloys/Katalysatoren/Omens/Knochen liegen jetzt lokal in items/*.json (aus dem poe2db-Bundle). Offen sind nur noch Semantik-Details, die nicht im Itemtext stehen (Vaal-Ergebnisliste + Wahrscheinlichkeiten, Whittling-Definition, Homogenising "same type", Verhalten bei unerfuellbaren Omens, Desecration-Reveal-Ablauf, Sanctify, Recombination, Thrud's Might/Rotmother's Ducat/Breach-Ringe-Mechanik) -> gezielte WebFetches statt der 6 Subagents (die am Session-Limit abgebrochen sind, ohne zu schreiben).
3. [ERLEDIGT 11.09.] Massendaten: PoB-Repo geklont und nach JSON konvertiert (research/pob-data-json.zip); poe2db-Bundle poe2db_export_v2.json (64 ModsView-Seiten + 12 Roh-HTML-Seiten) liegt in research/poe2db/ und ist mit tools/poe2db_parse.py geparst (research/poe2db-parsed.zip: classes/<Seite>.json, mods_index.json mit 3827 Mods inkl. Gewicht je Klasse) und mit tools/poe2db_items.py sind die Item-Listen extrahiert (items/Stackable_Currency.json = 150 Currency + 95 Essenzen + 26 Katalysatoren, Omen.json = 53, Augment.json = 619 Runen/Soul Cores, Essence.json, Liquid_Emotions.json, Unique_item.json, Desecrated_Modifiers.json ...).
   WICHTIG: 473 von 3827 Mods haben je Item-Klasse unterschiedliche poe2db-Gewichte (z. B. Dexterity-Suffix 1000 auf Bows, 750 auf Spears, 500 auf Crossbows, 1 auf Claws/Daggers = keine Schaetzung vorhanden). Gewichte daher immer je Klasse speichern.
4. [teilweise] Normalisierung in das Simulator-Datenmodell (Basen aus PoB + Mods/Gewichte aus poe2db + Currency/Omen/Essenz-Listen) steht noch aus.
5. [offen] Verifikation gegen poe2db.
6. [offen] Wissensdatenbank-Dokument (Markdown) in Ordner docs/ und ins Claude-Projekt "POE 2".
7. [offen] Design-Vorschlag + Fragen an Mario (Abschnitt 2).
8. [offen] Simulator bauen (.NET/C#).
9. [offen] Verifikation + Auslieferung nach C:\Development\POE2Crafting.

Kontext-Hinweis: Die Browser-Extraktion aller 33 Klassen laeuft komplett durch den Chat-Kontext (kein Datei-Download aus dem Browser moeglich, device_bash hatte Mount-Fehler). Deshalb kompakt extrahieren: pro Klasse nur neue Eintraege (Dedupe ueber window.__seen im Browser-Tab), Format `gen|name|level|weight|family|text` (~90 Zeichen), Spezialkategorien vollstaendig. Geschaetzt 80-150k Tokens einmalig. Alternative: device_bash pruefen, ob er Internet hat (dann Rohseiten direkt in den Projektordner laden und per device_stage_files in die Sandbox holen).

## 6. Browser-Snippets (javascript_tool im poe2db-Tab)

ModsView-JSON einer Klasse holen:
```js
const html = await fetch('/us/Wands').then(r=>r.text());
const s=html.indexOf('new ModsView('); const start=s+'new ModsView('.length;
let depth=0,i=start,inStr=false,esc=false;
for(;i<html.length;i++){const c=html[i];
  if(inStr){ if(esc){esc=false} else if(c==='\\'){esc=true} else if(c==='"'){inStr=false} }
  else { if(c==='"')inStr=true; else if(c==='{')depth++; else if(c==='}'){depth--; if(depth===0){i++;break}} } }
const data=JSON.parse(html.slice(start,i)); window.__d=data;
```
Kompakte Ausgabe (Idee): fuer jede Kategorie `data[cat]` Eintraege zu Zeilen `cat|gen|family|name|level|weight|spawn_no|fossil_no|plainText|CodeOrHash10` machen; HTML aus `str`/`Name` mit `.replace(/<[^>]+>/g,'')` entfernen, `—` durch `-` ersetzen; bereits gesehene Schluessel in `window.__seen` (Set) ueberspringen.

Item-Klassen-Liste: aus https://poe2db.tw/us/Items (siehe 3.1).

## 7. Recherche-Auftraege, die neu gestartet werden muessen (Kurzfassung)

Alle mit WebFetch auf poe2db.tw (+ mobalytics, pathofexile.com Forum, reddit, dving.net) – poe2wiki/maxroll/game8 sind gesperrt:
- A) Currency-Orbs: fuer jede Currency exakte Beschreibung, Anwendbarkeit (Rarity/Klasse), Vorbedingungen, genaues Verhalten inkl. Zufall, "Minimum Modifier Level"-Semantik, Verhalten bei corrupted/mirrored/fractured, Patch-Historie. Ausgabe notes/currency.md.
- B) Omens: fuer jeden Crafting-Omen exakte Semantik (z. B. "lowest level modifier", "same type", Verhalten wenn Bedingung unerfuellbar, Verbrauch), Aktivierung, Herkunft (Ritual), Patch-Historie, Omen of Recombination. Ausgabe notes/omens.md.
- C) Essenzen: Tiers Lesser/normal/Greater/Perfect + Corrupted (Hysteria, Delirium, Horror, Insanity, the Abyss ...), was jede Stufe mechanisch tut (Rarity-Uebergang, entfernt Mod?, garantierter Mod/Tier), Familienliste und Mod je Item-Klasse, Omen-Interaktion (Crystallisation), Herkunft. Ausgabe notes/essences.md.
- D) Desecration (Knochen, Well of Souls, 1-aus-3-Reveal?, Amanamu/Kurgal/Ulaman, Limits, Annulment/Omen of Light) und Corruption (Vaal-Ergebnisse + Wahrscheinlichkeiten, was danach noch geht, Orbs of Sacrifice, Architect's Orb, Vaal Cultivation Orb). Ausgabe notes/desecration-corruption.md.
- E) Uebrige Systeme: Augment-Sockel/Artificer's Orb/Runen/Soul Cores/Talismans/Bonded/Orb of Extraction, Katalysatoren (alle 26), Distilled Emotions, Reforging Bench Rezepte, Alloys, Thrud's Might, Rotmother's Ducat, Recombination, Sanctification, Hinekora's Lock, Qualitaet je Klasse, Neuerungen 0.4/0.5 (Greater/Perfect Currency, Cultivated, Runeforged, Fists of Stone). Ausgabe notes/other-systems.md.
- F) Regeln + Patch-Historie 0.1 bis 0.5.x (aktuelle Version/Datum, Tier-Anzeige in-game, Item-Level-Gating, "Minimum Modifier Level", Level-Requirement-Regel, Auswahlverfahren beim Wuerfeln, Tags). Ausgabe notes/rules-and-history.md.
