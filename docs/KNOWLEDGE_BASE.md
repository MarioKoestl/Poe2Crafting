# Path of Exile 2 – Crafting-Wissensdatenbank (Stand 11.09.2026, Patch 0.5.x)

Quelle fuer alle Item-Texte: poe2db.tw (Export vom 11.09.2026, research/poe2db/poe2db_export_v2.json). Mechanik-Erklaerungen aus poe2db (Community-Wiki-Seite "Crafting"), poe2wiki.net, mobalytics.gg und poe2craft.com. Alles, was nicht aus einem Item-Text stammt und nicht bestaetigt ist, steht in Abschnitt 12 als OFFEN/UNVERIFIZIERT.

Sprachkonvention: Spielbegriffe bleiben Englisch (Prefix, Suffix, Rare, Exalted Orb, ...), Erklaerungen sind Deutsch.

---

## 1. Items, Rarity und Affix-Slots

| Rarity | Farbe | Explizite Mods | Hinweis |
|---|---|---|---|
| Normal | weiss | 0 | nur Implicit der Basis (+ Sockel, Quality) |
| Magic | blau | max. 1 Prefix + 1 Suffix (also 1 oder 2 Mods) | Name = Prefixname + Basis + Suffixname |
| Rare | gelb | max. 3 Prefixes + 3 Suffixes (also 1 bis 6 Mods) | zufaelliger Eigenname |
| Unique | orange | fest definierte Mods | mit normaler Currency nicht veraenderbar (Ausnahmen: Vaal Orb, Divine Orb, Sockel/Runen, Quality, Vaal Cultivation Orb) |

Ein Rare muss nicht voll sein: Alchemy erzeugt 4 Mods, Regal auf ein Magic mit 2 Mods erzeugt 3 usw.

Jedes Item hat ausserdem:
- Item Level (ilvl): kommt vom Gebiet, in dem es gedroppt ist (oder von der Herstellung). Begrenzt, welche Mod-Tiers rollen koennen (Mod-Level <= ilvl).
- Level-/Attribut-Anforderung: von der Basis, zusaetzlich heben Mods die Level-Anforderung an (poe2db zeigt fuer einen Mod mit Level 80 "Effective: 64", d. h. vermutlich 80 % des Mod-Levels als Item-Anforderung – siehe OFFEN).
- Quality (0 bis 20 %, mit Vaal-Infusern bis 30 %): Waffen mehr Schaden, Ruestung mehr Defences, Ring/Amulett/Jewel per Katalysator Mod-Verstaerkung.
- Augment-Sockel (bei Waffen und Ruestung; Anzahl je Klasse, PoB-Basen: socketLimit 3 bei Wands, 4 bei Body Armour usw.).

Item-Klassen (poe2db, 33 Ausruestungsklassen): Claws, Daggers, Wands, One Hand Swords, One Hand Axes, One Hand Maces, Sceptres, Spears, Flails, Bows, Staves, Two Hand Swords, Two Hand Axes, Two Hand Maces, Quarterstaves, Crossbows, Traps, Talismans, Quivers, Shields, Bucklers, Foci, Gloves, Boots, Body Armours, Helmets, Amulets, Rings, Belts, Life Flasks, Mana Flasks, Charms, Jewels (Ruby/Emerald/Sapphire/Diamond und Time-Lost-Varianten). Dazu Waystones, Tablets, Relics (eigene Mod-Pools).

Ruestungs-Basen haben einen Defence-Typ, der den Mod-Pool bestimmt: str (Armour), dex (Evasion), int (Energy Shield), str_dex, str_int, dex_int, str_dex_int. Shields: str, str_dex, str_int; Bucklers (dex) und Foci (int) sind eigene Klassen. Zusaetzlich gibt es seit 0.5 "Runeforged"-Basen (Tag runeforged, mit Runic Ward statt eines Teils der Defences) und "Karui"-Basen.

---

## 2. Modifier (Mods)

### 2.1 Arten von Mods auf einem Item
- Implicit: fest an der Basis (z. B. Wand "Grants Skill: Level (1-20) Chaos Bolt", Amulett-Attribut). Divine Orb wuerfelt nur Werte neu (mit Omen of the Blessed sogar nur Implicits).
- Explicit Prefix / Suffix: die eigentlichen Zufalls-Mods. Im Item-Text mit Alt/Ctrl+Alt+C: `{ Prefix Modifier "Glyphic" (Tier: 2) — Damage, Caster }`.
- Enchantment: Runen/Soul Cores in Augment-Sockeln (im Text "(rune)"), Distilled/Liquid-Emotion-Instilling auf Amuletten (Notable Passive), Flask/Charm-Enchants.
- Corruption (Corrupted Implicit): durch Vaal Orb; kann mit Orb of Sacrifice "upgegradet" werden (Kategorie corruption_upgrade).
- Desecrated: aus Knochen (Abyss), zunaechst "Unrevealed", am Well of Souls aufdecken. Zaehlen als Prefix/Suffix.
- Crafted: garantierte Mods aus Alloys und Perfect/Corrupted Essences, im Text `{ Crafted Suffix Modifier "of the Stars" }` (ohne Tier). Belegen einen normalen Prefix-/Suffix-Slot.
- Fractured: durch Fracturing Orb fixierter Mod (kann nicht mehr entfernt/veraendert werden).
- Sanctified: Zustand nach Divine Orb + Omen of Sanctification (Mechanik siehe OFFEN).

### 2.2 Familie (Family / Group)
Jeder Mod gehoert zu einer Familie (poe2db `ModFamilyList`, PoB `group`). Ein Item kann nie zwei Mods derselben Familie tragen. Beispiele:
- Wands: `WeaponCasterDamagePrefix` (% increased Spell Damage), `SpellDamageAndMana` (Hybrid Spell Damage + Mana), `IncreasedMana`, `WeaponDamageTypePrefix` – diese Familie enthaelt Fire/Cold/Lightning/Chaos UND Spell Physical Damage, d. h. nur EINER dieser fuenf Elementschaden-Mods pro Wand.
- `IncreaseSocketedGemLevel`-Familien fuer "+# to Level of all (X) Spell Skills".

### 2.3 Tags
Jeder Mod hat Craft-Tags (poe2db `fossil_no`, PoB `modTags`): attribute, life, mana, resource, defences, armour, evasion, energy_shield, damage, physical, elemental, fire, cold, lightning, chaos, caster, attack, speed, critical, gem, minion, ailment, bleed, poison, resistance, ... Sie steuern: Omen of Homogenising Exaltation/Coronation ("Modifier of the same type"), Omen of Catalysing Exaltation (Katalysator-Qualitaet erhoeht die Chance auf Mods des passenden Typs), Katalysator-Wirkung, und die Anzeige im Spiel (die Tags stehen im Mod-Header).

### 2.4 Spawn-Tags (auf welchen Basen ein Mod rollen kann)
Jeder Mod hat eine geordnete Liste von Spawn-Tags mit Gewicht (poe2db `spawn_no`, PoB `weightKey/weightVal`, z. B. `{ "no_fire_spell_mods": 0, "wand": 1, "default": 0 }`). Jede Basis hat Tags (PoB `tags`: wand, onehand, default, int_armour, str_dex_armour, ring, no_cold_spell_mods, physical_implicit_skill, runeforged, ...). Regel: Der ERSTE Tag in der Mod-Liste, den die Basis besitzt, bestimmt das Gewicht. Gewicht 0 = kann nicht rollen. Negative Tags wie `no_fire_spell_mods` auf einer Basis (z. B. "Bone Wand": no_chaos/cold/fire/lightning_spell_mods, physical_implicit_skill) verhindern so bestimmte Elementmods.

### 2.5 Tiers
- Poe2db und das Spiel nummerieren gleich: Tier 1 = bester Tier. Die Anzahl Tiers ist je Mod-Familie und Klassenvariante unterschiedlich (Spell Damage auf Wand 8 Tiers, Mana 11 Tiers, Dexterity 8 Tiers).
- Gleiche Affix-Namen bedeuten NICHT gleiche Werte: "Runic" = (105-119)% Spell Damage auf Wand/Focus, (209-238)% auf Staff, (35-39)% auf Ring. Tier-Nummer ist familienbezogen: "of Desolation" ist auf Wands Tier 3 (+4 Physical Spell Skills), auf Staves Tier 2 (+5-6).
- Jeder Tier hat ein Mindest-Item-Level (`Level`): z. B. Spell Damage Wand: T8 ilvl 1, T7 8, T6 16, T5 33, T4 46, T3 60, T2 70, T1 80. "Runic" braucht also ilvl 80+; "+5 to Level of all Fire Spell Skills" (of Inferno) ilvl 81.
- Werte: jeder Tier hat einen Wertebereich (105-119). Der konkrete Wert wird beim Rollen gleichverteilt gewuerfelt; Divine Orb wuerfelt alle Werte neu (innerhalb des Tiers, Tier bleibt).

### 2.6 Gewichte (WICHTIG fuer den Simulator)
- poe2db: "Modifier weight information cannot be obtained from game files." Die in poe2db angezeigten Gewichte (`DropChance`) sind Schaetzungen. Muster: Spell Damage (Wand) T8..T1 = 1000/1000/1000/600/400/200/100/50; Elementschaden 500/500/500/400/300/200/100/50; Attribute und Mana alle 1000; Spell-Skill-Level +1..+5 = 1000/750/500/250/100. Fuer 473 von 3827 Mods sind die Gewichte je Item-Klasse verschieden (Dexterity: 1000 auf Bows, 750 auf Spears, 500 auf Crossbows, 1 auf Claws/Daggers = keine Schaetzung). Deshalb speichert der Datenspeicher das Gewicht je (Mod, Klassenseite).
- Echte Spawn-Gewichte in den Spieldateien sind nur 1/0 (erlaubt/nicht erlaubt); wie das Spiel tatsaechlich Tiers auswaehlt, ist nicht dokumentiert (OFFEN). Der Simulator rechnet mit den poe2db-Schaetzwerten, kennzeichnet sie als Schaetzung und macht sie editierbar.
- Wahrscheinlichkeit eines Mods beim Hinzufuegen (Modell wie poe2db): Kandidaten = alle Mods der passenden Generierungsart (Prefix oder Suffix), deren Familie noch nicht auf dem Item ist, deren Level <= ilvl (und >= Minimum Modifier Level bei Greater/Perfect-Currency) und die auf der Basis spawnen koennen. P(Mod) = Gewicht / Summe aller Kandidaten-Gewichte. Ob Prefix oder Suffix hinzugefuegt wird: wenn beides moeglich ist, zufaellig (Annahme: gewichtet nach Gesamtgewicht der jeweiligen Kandidaten – OFFEN, alternativ 50/50).

---

## 3. Grundlegende Currency (Originaltexte poe2db)

Rarity-Voraussetzungen laut poe2db-Crafting-Tabelle: Transmutation/Alchemy/Chance nur auf Normal; Augmentation nur auf Magic; Regal, Lesser/normale/Greater Essence nur auf Magic; Exalted, Chaos, Perfect/Corrupted Essence, Alloy, Desecration nur auf Rare; Annulment auf Magic oder Rare; Divine auf alles mit Mods (auch Unique); Vaal auf alles nicht Korrumpierte.

| Currency | Text | Engine-Semantik |
|---|---|---|
| Orb of Transmutation | Upgrades a Normal item to a Magic item with 1 modifier | Normal -> Magic, 1 zufaelliger Mod (Prefix oder Suffix) |
| Greater / Perfect Orb of Transmutation | dito, Minimum Modifier Level 44 / 70 | wie oben, aber nur Mods mit Level >= 44 / 70 (Tiers unterhalb sind ausgeschlossen) |
| Orb of Augmentation | Augments a Magic item with a new random modifier | Magic mit 1 Mod -> 2 Mods (fuellt den freien Slot-Typ) |
| Greater / Perfect Orb of Augmentation | Minimum Modifier Level 44 / 70 | |
| Regal Orb | Upgrades a Magic item to a Rare item, adding 1 modifier | Magic -> Rare, +1 Mod (Magic mit 1 Mod -> Rare mit 2, mit 2 -> 3) |
| Greater / Perfect Regal Orb | Minimum Modifier Level 35 / 50 | |
| Exalted Orb | Augments a Rare item with a new random modifier | Rare +1 Mod; nur wenn ein Slot frei ist (max 3P/3S) |
| Greater / Perfect Exalted Orb | Minimum Modifier Level 35 / 50 | |
| Chaos Orb | Removes a random modifier and augments a Rare item with a new random modifier | Rare: 1 zufaelliger Mod weg (Fractured ausgenommen), dann 1 neuer zufaelliger Mod (Prefix oder Suffix nach freien Slots). KEIN Komplett-Reroll wie in PoE1 |
| Greater / Perfect Chaos Orb | Minimum Modifier Level 35 / 50 | neuer Mod hat mindestens Level 35 / 50 |
| Orb of Alchemy | Upgrades a Normal or Magic item to a Rare item with 4 random modifiers | Normal -> Rare mit 4 Mods (Verteilung Prefix/Suffix zufaellig, mit Omen steuerbar). Auf Magic: laut Text erlaubt (OFFEN: bestehende Mods bleiben und werden auf 4 aufgefuellt, oder Reroll?) |
| Orb of Annulment | Removes a random modifier from an item | Magic oder Rare: 1 zufaelliger (nicht fracturierter) Mod weg; Rarity bleibt |
| Divine Orb | Randomises the numeric values of modifiers on an item | alle Werte innerhalb ihrer Tiers neu wuerfeln (Implicits und Explicits; mit Omen of the Blessed nur Implicits) |
| Orb of Chance | Unpredictably either upgrades a Normal item to Unique rarity or destroys it | Normal -> Unique derselben Basis oder Item zerstoert (Omen of Chance: kein Zerstoeren; Omen of the Ancients: zufaelliges Unique derselben Klasse) |
| Fracturing Orb | Fracture a random modifier on a rare item with at least 4 modifiers, locking it in place. | ein zufaelliger Mod wird "fractured": immun gegen Entfernen/Ersetzen durch Chaos/Annulment/Essences/Alloys |
| Mirror of Kalandra | Creates a Mirrored copy of an item | Kopie ist "Mirrored" und nicht mehr veraenderbar; Original ebenfalls Mirrored |
| Hinekora's Lock | Allows an item to foresee the result of the next Currency item used on it. Modifying the item in any way removes the ability to foresee | Vorschau des naechsten Currency-Ergebnisses; man kann es dann annehmen oder die Currency nicht anwenden |
| Scroll of Wisdom | Identifies an item | unidentifizierte Items zeigen ihre Mods erst nach Identifikation |
| Shards | Transmutation/Chance/Regal/Artificer's Shard | 10 Shards = 1 Orb (OFFEN: genaue Zahl je Typ) |

### 3.1 "Minimum Modifier Level" (Greater/Perfect-Varianten)
Der neue Mod wird nur aus Tiers gewaehlt, deren Level >= dem angegebenen Minimum ist (Greater Exalted 35: keine Tiers mit ilvl-Anforderung < 35). Das Item Level muss natuerlich weiterhin >= Mod-Level sein. Effekt: schlechte Tiers werden aus dem Pool entfernt, hohe Tiers werden entsprechend wahrscheinlicher.

### 3.2 Was passiert, wenn nichts moeglich ist
Exalted auf ein volles Rare (3P/3S), Augmentation auf ein Magic mit 2 Mods, Regal auf ein Rare usw. sind im Spiel nicht anwendbar (Currency laesst sich nicht benutzen, wird nicht verbraucht). Der Simulator zeigt solche Currencies als "nicht anwendbar" mit Grund.

---

## 4. Omens (Ritual-Altaere; "While this item is active in your inventory ...")
Ein Omen liegt aktiv im Inventar und veraendert die NAECHSTE Anwendung der genannten Currency; er wird dabei verbraucht. Nur ein Omen desselben Typs ist gleichzeitig aktiv; verschiedene Omens fuer verschiedene Currencies koennen parallel aktiv sein (OFFEN: Kombination zweier Omens auf dieselbe Currency, z. B. Greater + Sinistral Exaltation – vermutlich nicht moeglich).

Chaos Orb:
- Omen of Whittling: entfernt den Mod mit dem niedrigsten Level (= niedrigster Tier-Level auf dem Item), dann normaler Chaos-Add. (Nerf-Historie: OFFEN.)
- Omen of Sinistral Erasure / Dextral Erasure: entfernt nur einen Prefix / nur einen Suffix.
- Omen of Chaotic Rarity / Quantity / Monsters / Effectiveness: nur Waystones (ersetzt alle Mods ohne den genannten Typ).

Exalted Orb:
- Omen of Greater Exaltation: fuegt zwei Mods hinzu (braucht zwei freie Slots).
- Omen of Sinistral / Dextral Exaltation: nur Prefix / nur Suffix.
- Omen of Homogenising Exaltation: neuer Mod hat denselben "Typ" (Tag) wie ein bestehender Mod des Items.
- Omen of Catalysing Exaltation: verbraucht die gesamte Katalysator-Qualitaet und erhoeht die Chance auf Mods des Katalysator-Typs (nur Ring/Amulett/Jewel mit Katalysator-Qualitaet).

Regal Orb:
- Omen of Sinistral / Dextral Coronation: nur Prefix / nur Suffix.
- Omen of Homogenising Coronation: gleicher Typ wie ein bestehender Mod.

Orb of Alchemy:
- Omen of Sinistral / Dextral Alchemy: Ergebnis hat die maximale Anzahl Prefixes (3P+1S) / Suffixes (1P+3S).

Orb of Annulment:
- Omen of Greater Annulment: entfernt zwei Mods.
- Omen of Sinistral / Dextral Annulment: nur Prefix / nur Suffix.
- Omen of Light: entfernt nur Desecrated Mods.

Divine Orb:
- Omen of the Blessed: wuerfelt nur Implicit-Werte neu.
- Omen of Sanctification: Divine Orb auf ein Rare "sanctifies" es (Sanctified Items koennen nicht mehr desecrated werden; weitere Wirkung OFFEN).

Orb of Chance:
- Omen of Chance: Item wird nicht zerstoert (wenn kein Unique kommt, bleibt es unveraendert).
- Omen of the Ancients: Upgrade zu einem zufaelligen Unique derselben Item-Klasse.

Vaal Orb:
- Omen of Corruption: Vaal Orb fuehrt immer zu einer Aenderung (kein "nichts passiert").

Perfect oder Corrupted Essence:
- Omen of Sinistral / Dextral Crystallisation: der entfernte Mod ist nur ein Prefix / nur ein Suffix.

Desecration:
- Omen of Abyssal Echoes: die drei Reveal-Optionen duerfen einmal neu gewuerfelt werden.
- Omen of Sinistral / Dextral Necromancy: nur Prefix / nur Suffix.
- Omen of the Sovereign / the Liege / the Blackblooded: garantiert Ulaman- / Amanamu- / Kurgal-Mod (nur Waffe oder Schmuck).
- Omen of Putrefaction: ersetzt ALLE Mods durch bis zu 6 Unrevealed Desecrated Mods und korrumpiert das Item.

Nicht-Crafting-Omens (nur der Vollstaendigkeit halber): Refreshment, Resurgence, Amelioration, Answered Prayers, Secret Compartments, the Hunt, Reinforcements, Gambling, Bartering, die fuenf Saga-Omens (Expedition-Logbuecher). Omen of Recombination: existiert als Item (Drop Level 75), hat auf poe2db keinen Beschreibungstext (vermutlich nicht aktiv / OFFEN).

---

## 5. Essences
Alle Essenzen sind Currency mit klassenabhaengigem garantiertem Mod (die Liste je Klasse liegt in den Daten: research/poe2db items/Stackable_Currency.json, Abschnitt Essence; Mod-Werte je Klasse in den ModsView-Kategorien `essence` und `perfect_essence`).

| Stufe | Text | Semantik |
|---|---|---|
| Lesser Essence of X | Upgrades a Magic item to a Rare item, adding a guaranteed modifier | Magic -> Rare, garantierter Mod niedriger Stufe (z. B. Sorcery: Wand/Focus 35-44 % Spell Damage, Staff 69-88 %) |
| Essence of X | dito | mittlere Stufe (Sorcery: 55-64 % / 109-128 %) |
| Greater Essence of X | dito | hohe Stufe (Sorcery: 75-89 % / 149-188 %) |
| Perfect Essence of X | Removes a random modifier and augments a Rare item with a new guaranteed modifier | auf Rare: 1 zufaelliger Mod weg, dann Spezial-Mod (Sorcery: Wand +3 / Staff +5 to Level of all Spell Skills; Body: Body Armour 8-10 % increased maximum Life) |
| Corrupted Essences (Hysteria, Delirium, Horror, Insanity, the Abyss, the Breach) | Removes a random modifier and augments a Rare item with a new guaranteed modifier | Spezial-Mods: Delirium = Body Armour "Allocates a random Notable Passive Skill"; Horror = Gloves/Boots 60 % increased effect of Socketed Augment Items; Insanity = Belt "On Corruption, Item gains two Enchantments"; the Abyss = Equipment "Mark of the Abyssal Lord" (Desecration ersetzt diesen Mod durch einen hoeheren Unrevealed Desecrated Mod); the Breach = Jewellery +20 % to Maximum Quality; Hysteria = klassenabhaengig (Helmet +1 Minion Skills, Boots 30 % Movement Speed, Gloves Crit Bonus, Belt Stun Threshold, ...) |

Familien (19): the Body (Life), the Mind (Mana), Enhancement (Defences), Abrasion (Physical), Flames (Fire), Ice (Cold), Electricity (Lightning), Ruin (Chaos), Battle (Attack), Sorcery (Spell), Haste (Speed), the Infinite (Attributes), Seeking (Critical), Insulation (Fire Res), Thawing (Cold Res), Grounding (Lightning Res), Alacrity (Cast/Attack Speed?), Opulence (Rarity), Command (Minion/Spirit).

Der Mod aus einer Essenz belegt einen normalen Prefix-/Suffix-Slot und hat eine Familie; die Essenz ist nur anwendbar, wenn ein passender Slot frei ist bzw. (Perfect) nachdem ein Mod entfernt wurde. Perfect-/Corrupted-Essence-Mods erscheinen im Item-Text als "Crafted" Modifier (OFFEN: ob auch Lesser/Greater als Crafted angezeigt werden – vermutlich nein, sie sind normale Mods).

---

## 6. Alloys (0.5)
13 Currency-Items: Runic, Adaptive, Protective, Expansive, Swift, Cyclonic, Prismatic, Mystic, Sovereign, Celestial, Transcendent, The Runebinder's, The Runefather's Alloy. Text: "Removes a random modifier and augments a Rare item with a new guaranteed modifier", der Mod haengt von der Item-Klasse ab, z. B.
- Celestial Alloy: Staff or Wand: +(142-188) to maximum Mana, +1 to Level of all Spell Skills; Martial Weapon: +(327-427) Accuracy, (5-8) % Attack Speed.
- Transcendent Alloy: Staff: (39-47) % Cast Speed + Gain (11-16) % of Elemental Damage as Extra Cold; Focus/Wand: (26-31) % Cast Speed + (7-11) %; Martial Weapon: (15-20) % Physical Damage + (7-10) all Attributes.
- The Runebinder's Alloy: Staff: (25-50) % chance to gain Nature's Archon when your Plants Overgrow ("of the Stars" – der Crafted Suffix auf Marios Staff); Wand: +1 to Limit for Elemental Skills; Sceptre: +(4-5) max Puppet Master stacks; Crossbow: +2 Ballista Totems; Bow: (40-50) % Mark effect.
- Runic Alloy: Ring/Amulet/Belt Runic-Ward-Mods. Adaptive/Protective/Expansive/Swift/Cyclonic/Prismatic/Mystic/Sovereign: je Klasse ein Mod (siehe Datenliste).
Regel (poe2db-Kategorie "Perfect Essence / Alloy", `Removes: true`): wie Perfect Essence. Annahme: nur ein Alloy-/Perfect-Essence-Mod pro Item (OFFEN, sehr wahrscheinlich).

---

## 7. Desecration (Abyss, seit 0.3)
- Knochen: Jawbone (Rare Weapon oder Quiver), Rib (Rare Armour), Collarbone (Rare Amulet, Ring, Belt), Cranium (Rare Jewel, "Unstable Desecration": steigende Zerstoerungschance bei Wiederholung), Vertebrae (Rare Waystone). Stufen: Gnawed (nur Items bis Item Level 64), Preserved (jedes Level), Ancient (Minimum Modifier Level 40). Altered Collarbone: Chance auf "otherworldly" Mods (Breach-Mods, Kategorie breach_otherworldly).
- Ablauf (poe2db): "Desecrating an item adds an Unrevealed Desecrated modifier. If modifiers are full then a random modifier is also removed. These modifiers can be revealed at the Well of Souls. Items with Desecrated Modifiers cannot be Desecrated again." Beim Aufdecken am Well of Souls werden drei Optionen angeboten, eine wird gewaehlt (Omen of Abyssal Echoes: einmal neu wuerfeln).
- Familien: Amanamu, Kurgal, Ulaman (Praefixe "Amanamu's" usw., Suffixe "of Amanamu" usw., Level 65, Tags amanamu_mod/kurgal_mod/ulaman_mod, unveiled_mod). Beispiele Wand: Amanamu (74-89) % increased Elemental Damage; (55-64) % Spell Damage + Minions deal (55-64) % increased Damage; Spell Damage with Spells that cost Life. Ulaman: Gain (21-25) % of Damage as Extra Physical Damage. Dazu "Custom/Lich's Desecrated" Mods auf Unique "The Unborn Lich".
- Desecrated Mods zaehlen als Prefix/Suffix (belegen Slots), koennen mit Annulment (Omen of Light: nur Desecrated) oder Chaos entfernt werden. Nicht moeglich auf Corrupted oder Sanctified Items. Pro Item nur ein Desecration-Vorgang (ausser Mark of the Abyssal Lord).

---

## 8. Corruption (Vaal)
- Vaal Orb: "Modifies an item unpredictably and Corrupts it." Danach: "Corrupted items cannot be modified again" / "Most methods of item crafting and modification cannot be used on Corrupted items." Ergebnisse auf Ausruestung (Community-Wissen, Wahrscheinlichkeiten OFFEN): (a) keine Aenderung ausser dem Corrupted-Status, (b) Implicit wird durch einen Corrupted Implicit ersetzt (Kategorie corrupted, 115 Mods, z. B. Wand "+1 to Level of all X Spell Skills", "(10-20) % reduced Attribute Requirements"), (c) Mods werden neu gewuerfelt (Rare -> Rare mit neuen Mods), (d) weitere (Sockel/Quality?) – OFFEN. Omen of Corruption: nie Ergebnis (a).
- Danach noch moeglich: Divine Orb? (OFFEN), Runen einsetzen (Soul Cores: "canSocketInCorruptedSanctified"), Quality-Currency (OFFEN), Orb of Sacrifice, Architect's Orb.
- Orbs of Sacrifice (Yaomac's: Rare Weapon/Quiver, Kopec's: Rare Armour, Kamasa's: Rare Amulet/Ring/Belt, Yugul's: Rare Jewel): "Upgrades a Corruption Enchantment on a Rare ... and removes a random Modifier" – der Corrupted Implicit wird zur staerkeren Variante (Kategorie corruption_upgrade, 110 Mods, z. B. Freeze-Damage 50-75 %, Jewel Cold Res 15-20 %), dafuer geht ein zufaelliger Mod verloren.
- Architect's Orb: "Modifies a Corrupted Equipment or Jewel item unpredictably or destroys it" (Ergebnisse OFFEN).
- Vaal-Infuser (Armourer's/Blacksmith's/Arcanist's/Catalysing): Quality ueber Maximum bis +10 %, mit Chance auf Corruption.
- Vaal Cultivation Orb: "Replaces up to 2 modifiers on a Corrupted Vaal Unique. Replaces other Uniques with a Corrupted Unique of the same Item Class" (Cultivated Uniques, 48 Stueck).

---

## 9. Augments, Quality, Katalysatoren
- Artificer's Orb: "Adds an Augment Socket to a Martial Weapon, wand, staff or Armour" (bis zum Sockel-Limit der Basis). Orb of Extraction: zerstoert das Item und gibt nicht Socket-gebundene Augments zurueck.
- Augment-Typen: Runen (Kalguuran; Lesser/normal/Greater, z. B. Desert Rune = Fire Damage auf Waffen / Fire Res auf Ruestung; Unique-Runen wie "Saqawal's Rune of the Sky", "Legacy of Lifesprig"), Soul Cores (Vaal; Limit 1 pro Item bei manchen), Abyssal Eyes, Idols (Azmeri), Ancient Augments (nur einer pro Item), Congealed Mist. Effekt je nach Item-Typ (Waffe vs. Ruestung), poe2db-Kategorie socketable; "Bonded"-Mods sind die Socket-bound-Varianten ("permanently fill any Augment Socket ... cannot be removed, replaced or extracted"). 3x gleiche Rune -> naechste Stufe (Reforging Bench).
- Quality: Blacksmith's Whetstone (Martial Weapon), Arcanist's Etcher (Wand/Staff/Sceptre), Armourer's Scrap (Armour), Glassblower's Bauble (Flask), Gemcutter's Prism (Gem). Max 20 %, Vaal-Infuser bis 30 %. Zuwachs je Anwendung nach Rarity (Normal 5, Magic 2, Rare 1 – OFFEN).
- Katalysatoren (Ring/Amulett; "Refined" fuer Jewels): Flesh (Life), Neural (Mana), Carapace (Defences), Uul-Netol's (Physical), Xoph's (Fire), Tul's (Cold), Esh's (Lightning), Chayula's (Chaos), Reaver (Attack), Sibilant (Caster), Skittering (Speed), Adaptive (Attribute), Necrotic (Minion). "Adds quality that enhances X modifiers ... Replaces other quality types". Qualitaet verstaerkt die Werte der Mods mit passendem Tag (1 % Qualitaet = 1 % groessere Mod-Werte, max 20 %, Essence of the Breach +20 % Maximum Quality).
- Distilled/Liquid Emotions: Instilling von Amuletten (3 Emotions -> ein Notable Passive), auf Jewels als Kategorie `liquid` (Diluted Liquid Ire: 10-20 % Armour usw.).
- Flux (0.5): Blazing/Chilling/Crackling/Void Flux wandeln alle Resistenz-Mods des Items in Fire/Cold/Lightning/Chaos um; Perfect Flux: Skills des Items auf Level 20.

---

## 10. Reforging Bench und Sonstiges
- Reforging Bench (Hideout): 3 Rares derselben Klasse -> neues Rare; 3 Uniques -> zufaelliges Unique?; 3 Runen -> naechste Rune; Jewel-Rezepte (Ruby+Emerald+Sapphire -> Diamond, Time-Lost analog); 3 gleiche Amulette -> ...; "Legacy of X" Rezepte (Aldur's Legacy + Unique -> Legacy-Unique); 60x Breach Ring -> Grasping Mail. (Details in raw_pages/Reforging_Bench.html.)
- Expedition-Mods (poe2db-Kategorien soul/berserking/marksman/decay/chronomancy/destruction mit Praefixen Medved's, Vorana's, Kolr's, Katla's, of Chronomancy, of Destruction): vermutlich ueber die Crests (Medved's Crest of the Circle, Vorana's Crest of the Scythe, Uhtred's Crest of the Chalice, Olroth's Crest of the Sun) / Kalguuran-Logbuecher – Mechanik OFFEN. "Thrud's Might" ist der poe2db-Titel der Kategorie destruction (Waffen, "of Destruction": (10-20) % increased Explicit X Modifier magnitudes).
- Breach-Mods auf Ringen/Guerteln/Amuletten (breach_caster/minion/otherworldly, z. B. "Tul's", "of Xoph"): ueber Altered Collarbone bzw. Breach Rings – OFFEN.
- Exceptional Items: Normal-Items mit Quality ueber Maximum oder zusaetzlichem Augment-Sockel.

---

## 11. Item-Text-Format (Import)
Ctrl+C (einfach) und Ctrl+Alt+C (Advanced) im Spiel. Beispiel siehe docs/STATUS.md 4.7. Bloecke durch `--------` getrennt: Item Class / Rarity / Name / Basis; Quality; Requires: Level X, Y Int; Sockets: S S; Item Level: N; Runen-Zeilen mit "(rune)"; Implicit-Zeilen (z. B. "Grants Skill: ..." oder "(implicit)"); Mods als `{ Prefix|Suffix Modifier "Name" (Tier: N) — Tag, Tag }` gefolgt von der Statzeile `wert(min-max)`; `{ Crafted ... }` fuer Alloy/Perfect-Essence-Mods; "(desecrated)", "(enchant)", "Corrupted", "Mirrored", "Fractured Item", "Unidentified" moeglich.

---

## 12. OFFEN / UNVERIFIZIERT (fuer den Simulator als konfigurierbare Annahmen)
1. Tatsaechliches Auswahlverfahren des Spiels fuer Mod und Tier (Gewichte) – Simulator nutzt poe2db-Schaetzwerte.
2. Prefix-vs-Suffix-Wahl beim Hinzufuegen, wenn beides frei ist (gewichtet vs. 50/50).
3. Orb of Alchemy auf Magic: bestehende Mods bleiben? (Text: "Upgrades a Normal or Magic item to a Rare item with 4 random modifiers".)
4. Vaal-Orb-Ergebnisliste und Wahrscheinlichkeiten; Architect's Orb-Ergebnisse; was auf Corrupted Items noch geht (Divine? Quality? Sockel?).
5. Omen of Whittling: "lowest level modifier" = niedrigster Mod-Level (Tier-Level); Verhalten bei Gleichstand.
6. Homogenising: "same type" = gemeinsamer Craft-Tag (welcher, wenn mehrere?).
7. Sinistral/Dextral-Omens, wenn der Slot-Typ voll ist: Currency nicht anwendbar oder Omen wirkungslos?
8. Sanctified-Mechanik (Omen of Sanctification).
9. Alloy/Perfect Essence: nur einer pro Item? Ersetzt ein zweiter den ersten?
10. Level-Anforderung durch Mods ("Effective" = 80 %?).
11. Shards je Orb, Quality-Zuwachs je Rarity.
12. Expedition-/Crest-Mods, Breach-Ring-Mods, Omen of Recombination.
13. Essence-Level-Anforderungen (ilvl) je Stufe (poe2db `reqlvl`/`Level` in Kategorie essence: z. B. Lesser Sorcery Level 8, Perfect Level 72).
