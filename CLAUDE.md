# POE2 Crafting Simulator

## Projekt-Überblick
Path of Exile 2 Crafting-Simulator: Blazor Server Web-App (.NET 9) die PoE2-Crafting simuliert.
Zeigt Wahrscheinlichkeiten, erlaubt Würfeln + manuelle Wahl, hat einen Crafting-Planner der optimale Pfade findet.

## Technologie
- .NET 9, Blazor Server (blazor.web.js, AddRazorComponents().AddInteractiveServerComponents())
- Kein Component-Scoped CSS (.razor.css) — alles in `src/POE2Crafting.Web/wwwroot/css/site.css`
- POE2-Dark-Theme mit CSS Custom Properties (--bg-dark, --text, --accent, etc.)
- JSON-Datenspeicher unter `data/` (bases.json, mods.json, currencies.json, etc.)
- xUnit Tests unter `tests/POE2Crafting.Tests/`

## Projektstruktur
```
C:\Development\POE2Crafting\
├── src/
│   ├── POE2Crafting.Core/          # Domain + Engine (kein UI)
│   │   ├── Data/
│   │   │   ├── GameData.cs         # Lädt alle JSON, Lookups, PagesFor, EssenceModFor, CurrencyVariants, ClassMatchesTarget
│   │   │   └── Models.cs           # ModDef, BaseItem, CurrencyDef, OmenDef, SimAssumptions, ModTiers, ModCategories
│   │   ├── Engine/
│   │   │   ├── CraftingEngine.cs   # Generische Checks + gemeinsame Bausteine (Kandidaten, Add/Remove, MakeRare, FreeSlots)
│   │   │   ├── Operations/         # EINE Klasse pro Currency-Op (CraftOperation: Check/Preview/Execute)
│   │   │   │   ├── AddModOperation.cs, AlchemyOperation.cs, ChaosOperation.cs, AnnulOperation.cs
│   │   │   │   ├── DivineOperation.cs, ChanceOperation.cs, FractureOperation.cs, FlagOperation.cs
│   │   │   │   ├── EssenceOperation.cs   # Essenzen + Alloys
│   │   │   │   ├── DesecrateOperation.cs # Knochen, Omens, Well-of-Souls-Reveal
│   │   │   │   ├── VaalOperation.cs, SacrificeOperation.cs, ArchitectOperation.cs # Corruption
│   │   │   │   └── QualityOperation.cs (+Vaal-Infuser), CatalystOperation.cs, SocketOperation.cs, ExtractOperation.cs, FluxOperation.cs
│   │   │   ├── ModPool.cs          # Mod-Kandidaten, Gewichte, DisplayTier() (gecacht pro Base)
│   │   │   ├── CraftingPathFinder.cs # Planner-Einstieg: FindPathsFromItem (→ Planning/FromItemPlanner), ToMermaid
│   │   │   ├── Planning/           # ItemGoal (Diff Item ↔ Ziel), FromItemPlanner (Greedy pro Tool-Policy)
│   │   │   ├── CraftingPath.cs     # TargetItemSpec, TargetMod.Matches(), CraftingStrategy
│   │   │   ├── Outcomes.cs         # CraftAction, OmenEffects, StepPreview, ManualChoice, CraftResult, CraftContext
│   │   │   └── Rng.cs              # Seeded RNG (PickWeighted, SampleWeighted)
│   │   └── Items/
│   │       ├── Item.cs             # Item (FromBase, AddMod), ItemMod, RevealContext, Rarity, ModKind
│   │       ├── ModText.cs          # ALLE Zahlen-/Range-Regeln für Mod-Texte (Render, StatSignature, MidValue, Tokens)
│   │       └── ItemParser.cs       # Ctrl+C / Ctrl+Alt+C Text → Item (Mod-Auflösung pro Base)
│   └── POE2Crafting.Web/           # Blazor UI
│       ├── Pages/
│       │   ├── Index.razor         # Hauptseite: links Item, rechts Simulator/Planner Tabs
│       │   └── ItemComposer.razor  # Inline-Composer zum Zusammenstellen von Items
│       ├── Components/
│       │   ├── ItemDisplay.razor   # Item-Anzeige (Mods, Runen, Implicits, Display-Tiers)
│       │   ├── BaseItemForm.razor  # Klasse/Base/Rarity/ilvl eines ItemDraft (Composer + Planner)
│       │   ├── ModBrowser.razor    # Mod-Auswahl eines ItemDraft, gruppiert nach ModTiers.TierGroupKey
│       │   ├── ModOptionRow.razor  # Mod-Zeile mit %/P-S/Tier/Choose (Preview + Well of Souls)
│       │   ├── DistributionRow.razor # Balken-Zeile (Removal, Outcomes)
│       │   ├── CurrencySelector.razor # Gruppen: Orbs, Essences, Alloys
│       │   ├── PreviewPanel.razor  # Wahrscheinlichkeiten, Würfeln, manuelle Wahl (Chaos 2-stufig)
│       │   ├── RevealPanel.razor   # Well of Souls: Optionen würfeln/wählen, Abyssal Echoes, ganzer Pool
│       │   ├── ItemBuilder.razor   # Planner: Target-Item definieren + Pfade finden
│       │   ├── PlannerPanel.razor  # Planner: Ergebnisse anzeigen
│       │   └── MermaidDiagram.razor
│       ├── Services/
│       │   ├── CraftingSession.cs  # Per-User Session: CurrentItem, History, Reveal-State (Apply/Commit/Restore)
│       │   ├── ItemDraft.cs        # State von Composer/Planner: Base, Rarity, ilvl, Selection, Preview
│       │   ├── ModSelection.cs     # Regeln für gewählte Mods (Slots, ilvl, Familie, Tier-Tausch)
│       │   └── AffixUi.cs          # P/S-Buchstabe + CSS-Klassen
│       └── wwwroot/css/site.css    # EINZIGE CSS-Datei
├── data/                           # JSON-Datenspeicher (Spieldaten)
├── research/                       # poe2db Exports, PoB-Daten
├── tools/                          # Python-Skripte für Datenaufbereitung
└── tests/
```

## Wichtige Architektur-Konzepte

### Tiers
- Tiers zählen pro **Familie + Stat** (ModText.StatSignature): eine Familie wie `IncreaseSocketedGemLevel` enthält Physical/Fire/All-Spell-Skill-Varianten mit eigener Tier-Reihe (verifiziert an Marios Staff: "of Desolation" = T2)
- ModDef.Tier: globaler Fallback-Tier aus GameData.ComputeTiers()
- Display-Tiers: `ModPool.DisplayTier(mod, item)` — SINGLE SOURCE OF TRUTH, pro Base gecacht; `TryDisplayTier` liefert null für Mods ohne Tier (Essenz-/Alloy-Mods)
- T1 = bester (höchster Level), wie im Spiel

### ModPool
- `_byCategoryPage`: category → page → Mods (normal + BrowsableCategories breach_otherworldly, desecrated)
- `Candidates()`: filtert nach AffixType, ItemLevel, MinModLevel, Family-Exklusivität, Base-Tags
- `PagesFor(item)` delegiert an `GameData.PagesFor(base, class)`

### Item-Modell
- Rarity: Normal, Magic, Rare, Unique
- ItemMod: ModId, Kind (Explicit/Crafted/Desecrated/Implicit/CorruptedImplicit/...), Affix (Prefix/Suffix/Other, **Default Other**), Values, Def, SourceName
- `ItemMod.IsAffix` = belegt einen Slot (Kind Explicit/Crafted/Desecrated UND Affix != Other)
- Slot-Limits NUR über `SimAssumptions.MaxPrefixes/MaxSuffixes/MaxAffixes(rarity)` (config.json), nie hartkodieren
- Familien-Exklusivität: nie zwei Mods derselben Familie (egal welche Kategorie)
- Item.Bind() setzt ItemClass immer aus der Base (Spiel sagt "Staves", Daten "Staff")

### Engine-Operationen
- Neue Currency-Mechanik = neue `CraftOperation`-Klasse unter `Engine/Operations/` + Eintrag im Engine-Konstruktor; KEIN switch über Op-Ids
- Generische Checks (Rarity, Target-Klasse, Mirrored, Corrupted, Max-ilvl, Omen-Zuordnung) macht nur `CraftingEngine.Check`
- Omen-Effekt-Ids als Konstanten in `OmenEffects`, Kategorie-Namen in `ModCategories`
- MEHRERE Omens pro Aktion: `CraftAction.Omens` / `CraftContext.Omens` (Liste; `OmenEffects.None` = keine, nie null); `ctx.OmenIs(effect)`, `omens.Has/WithEffect`, `OmenEffects.RestrictedType(omens)`; `CraftOperation.AcceptsOmen(ctx, omen)` pro Omen. Widersprüche (`OmenEffects.Conflict`, ANNAHME): gleiches Omen doppelt, Prefix- vs. Suffix-Restriktion, zwei Boss-Omens, Putrefaction + Restriktion. UI: Omens als Toggle, blockierte mit Grund im Tooltip. Planner probiert Einzel-Omens und verträgliche Paare
- Alle Currencies aus den Daten haben eine Operation (Test `Every_simulated_currency_has_an_operation` prüft das)
- Klassen-Gruppen (weapon_or_quiver, armour, ring_or_amulet, socketable, equipment, ...) NUR in der Tabelle `GameData.TargetGroups`; Currency nutzt `CurrencyDef.ClassTarget` (Target ?? QualityTarget), sonst `CraftOperation.DefaultClassTarget`
- Gemeinsame Bausteine u. a.: `PickOutcome`/`NormaliseOutcomes` (benannte Ergebnisse + Handwahl), `AddCorruptionEnchant`, `RemoveOne`, `AddOne`
- `CraftOperation.WorksOnCorrupted` / `RequiresCorrupted` statt eigener Corrupted-Checks

### Quality, Katalysatoren, Sockel, Flux
- Quality-Currencies: +config `qualityPerUse` je Rarity (UNVERIFIED), Cap = `Item.MaxQuality()` (Base-Quality/Default 20 + "Maximum Quality"-Mods wie Essence of the Breach)
- Vaal-Infuser: wie Quality, Cap +10, Corruption-Chance 0 bei Start ≤ Max, +5 %/Punkt darüber (poe2wiki)
- Katalysatoren: synthetische Currencies (Op "catalyst", Section Catalysts), +5 % je Nutzung, Typ ersetzt, Menge bleibt (Annahme); `Item.QualityTag`/`QualityEnhances` für verstärkte Mods (Badge in ItemDisplay)
- Omen of Catalysing Exaltation: Gewicht passender Mods × (1 + Quality × `catalysingWeightBonusPerQuality`), Quality wird verbraucht (Annahme)
- Artificer's Orb: +1 Sockel bis `BaseItem.SocketLimit`; Orb of Extraction: zerstört Item, nennt Runen
- Flux: Fire/Cold/Lightning-Res-Mods → Element der Flux (Void: → Chaos); gleiches Level-Tier (höchstes ≤ Quell-Level), Wert relativ via `ModText.RescaleValues`

### Corruption
- Vaal Orb: Ergebnisse aus config `vaalOutcomes` (je 25 %, Quelle maxroll/timesaver.gg 0.5.x); nicht mögliche Ergebnisse fallen raus, Rest normiert; Omen of Corruption streicht no_change
  - corrupted_implicit: Enchantment aus Kategorie corrupted (`ModPool.CorruptionEnchantCandidates`, ohne vorhandene Familien) → `ModKind.CorruptedImplicit`
  - reroll_mods: 1-3 Affixe (config) werden je durch einen neuen Mod gleichen Typs ersetzt (ANNAHME, Quelle sagt nur "randomized")
  - add_socket_or_quality: +1 Sockel über Limit (Martial Weapon, Armour-Gruppe, Focus) bzw. Quality bis 23 % (Wand/Staff); Schmuck/Jewel: Ergebnis entfällt
- Orb of Sacrifice: Enchantment → Upgrade (`GameData.CorruptionUpgradeFor`, Name `CorruptionX` → `CorruptionUpgradeX`, 110/115 vorhanden) + zufälligen Mod entfernen
- Architect's Orb: 50 % (config) zweites Enchantment anderer Familie + `Item.TwiceCorrupted`, sonst zerstört
- Import: "(enchant)"-Zeilen auf Corrupted-Items werden als Corruption-Enchantments aufgelöst; "Twice Corrupted" im Footer
- `ModDef.DisplayName`: Enchantments haben in den Daten nur Codes als Namen → in der UI immer DisplayName verwenden
- Nicht modelliert: Unique-Reroll (x0.78-1.22), Jewel-Sonderergebnis (Affix hinzufügen/entfernen), Vaal-Infuser

### Desecration
- Knochen (Op "desecrate", Target weapon_or_quiver/armour/jewellery/jewel) fügen ein `ItemMod { Unrevealed, Kind=Desecrated, Reveal=RevealContext }` hinzu; volle Slots → zufälliger Mod weg, Unrevealed übernimmt dessen Affix-Typ (Annahme)
- Nicht auf Sanctified; nicht wenn schon ein Desecrated-Mod da ist (außer Mark of the Abyssal Lord, der ersetzt wird)
- Omens: Necromancy (P/S), Sovereign/Liege/Blackblooded (Boss-Tag, nur Waffe/Schmuck), Putrefaction (alle Mods → N Unrevealed + Corrupted), Abyssal Echoes (einmal Reveal-Optionen neu würfeln, im RevealPanel)
- Reveal: `Engine.RevealPool/RollRevealOptions/Reveal`; Optionen = config `revealOptionCount` (3); Ancient-Bones: MinModLevel, Altered Collarbone: + breach_otherworldly
- Datenlage: Desecrated-Mods fast alle Level 65, Gewicht 1 (keine poe2db-Schätzung) → Gnawed-Bones auf Waffen finden meist nichts

### Essenzen & Alloys
- `GameData.AllCurrencies`: currencies.json + synthetische CurrencyDef für Essenzen/Alloys (Op="essence", `.Essence`) und Katalysatoren (Op="catalyst") → gleicher Engine-/UI-Pfad; `FindCurrency` sucht in allen
- `GameData.EssenceModFor(essence, base, class)`: garantierter Mod aus Kategorie essence/perfect_essence, Page-Match über Weights-Keys
- Lesser/Normal/Greater: Magic → Rare + Explicit-Mod. Perfect/Corrupted/Alloy: entfernt 1 Mod (gleiche Familie wird ersetzt; ist der Slot-Typ voll, nur dieser Typ) + Crafted-Mod
- Crystallisation-Omens nur für Perfect/Corrupted; `onlyOneCraftedModPerItem` aus config

### Planner
- Ziel-Mod trifft mit seinem Tier ODER besser (`TargetMod.Matches`, gleiche Familie+Stat, Level ≥), abschaltbar (`AllowBetterTiers`)
- Nur noch "Path from Current Item" (Mario, 13.09.2026: Pfade ab leerer Base braucht er nicht) — der alte Template-Planner ab Normal-Base wurde entfernt; eine Normal-Base als Current Item funktioniert trotzdem (Transmute-Moves)
- Wahrscheinlichkeiten kommen direkt aus `CraftingEngine.Preview` (gleiche Regeln wie Simulator)

### CraftingSession (Scoped per User)
- Hält CurrentItem, History, Engine, RNG
- ImportItem(): Itemtext parsen (Fehler + Anzahl nicht zuordenbarer Mods in LastError)
- SetItem(item, action): Composer/Import → CurrentItem, neue History
- Execute(): Currency/Omen/Essenz anwenden; InvalidOperationException aus der Engine → LastError (kein Circuit-Crash)
- Save/Load: JSON-Projekte

### UI-Layout (Index.razor)
- Desktop (>1100px): App füllt den Viewport; JEDE Spalte/Pane hat genau EINE Scrollbar (`.panel-body`, `.pane`) — keine verschachtelten Scroll-Container (keine max-height+overflow in Listen/Mod-Browser!). Schmal: alles gestapelt, Seite scrollt
- Links: Item-Panel (Import, Compose, ItemDisplay, History); beim Composen wird die linke Spalte breiter (`.crafting-page.composing`)
- Rechts: Segmented Tabs (Simulator | Crafting Planner) + `.workspace` mit zwei Panes
  - Simulator: [RevealPanel + CurrencySelector] | [PreviewPanel]
  - Planner: [ItemBuilder (Target)] | [PlannerPanel (Pfade, pro Schritt aufklappbar "Item after this step")]
- Hauptaktionen unten in der Pane fixiert (`.sticky-actions`, `.action-bar`)
- Design-Tokens (Farben, Radien, Schatten) nur in `:root` von site.css

### Currency-/Omen-Infos (CraftItemLink)
- `GameData.FindCraftItem(name)` → `CraftItemInfo` (Kind, IconUrl, Description, Facts wie Min-Mod-Level) für alle Currencies, Essenzen, Alloys, Katalysatoren, Omens
- Icons LOKAL: `src/POE2Crafting.Web/wwwroot/img/icons/` + `data/icons.json` (Slug → lokaler Pfad), einmalig geladen mit `python tools/poe2db_icons.py` (Art-Pfade von poe2db, Dateien vom RePoE-fork-Mirror; poe2db-CDN blockiert z. B. Essenz-Icons außerhalb poe2db → nie direkt verlinken)
- `CraftItemLink` = markierter Name (Icon + Name, `ShowIcon=false` in Kacheln) → Popover beim HOVER (Mario will Hover, nicht Klick; fixed, per JS `positionPopover` platziert, schließt 150 ms nach Verlassen); Klicks gehen durch (Kachel wird ausgewählt)
- Currency-Suche filtert Name UND Beschreibung (z. B. "life" findet Essence of the Body); versteckte, nicht nutzbare Treffer werden als Hinweis gezählt
- `CraftText` = Text aus Namen mit " + " / " → " (CraftAction.DisplayName, Step-Currency, History) → jeder bekannte Name wird zum Link
- Neue Stellen, an denen Currency-/Omen-Namen angezeigt werden, IMMER über CraftItemLink/CraftText rendern

### ItemComposer / ItemBuilder
- Beide halten einen `ItemDraft` und nutzen `BaseItemForm` + `ModBrowser` (OnChanged="StateHasChanged" verdrahten!)
- Klick auf anderen Tier derselben Familie tauscht den Tier
- Mod-Suche durchsucht Prefixe UND Suffixe (Name, Text, Familie, Tags); ohne Suche zeigen die Tabs Prefix/Suffix
- Kategorien: normal, breach_otherworldly, desecrated
- Item-Panel: "Edit" öffnet den Composer mit dem aktuellen Item (Werte übernommen) → "Apply Changes" = `Session.EditItem()` als neuer History-Schritt (Undo geht); nicht editierbare Teile (Implicits, Quality, Sockel, Corruption, Unrevealed, Fractured-Flag) bleiben über `ItemDraft`-Template + `Item.WithoutAffixes()` erhalten. "New" = neues Item (History neu)
- Composer → `Session.SetItem()`; Planner: Ziel entsteht NUR über "Load current item as target" (keine eigene Base-/ilvl-Auswahl, `BaseItemForm ShowBase=false`, nur Target Rarity) + "Path from Current Item" (nur bei gleicher Base)

### Crafting Planner (A → B)
- Target definieren: ItemBuilder mit ItemDraft/ModBrowser; "Load current item as target" übernimmt Base, Rarity, ilvl und Mods des aktuellen Items zum Editieren
- Werte pro Mod-Range (SelectedMod.Values): im Composer = echte Werte, im Planner = Mindestwerte (TargetMod.MinValues)
- "Path from Current Item" = `CraftingPathFinder.FindPathsFromItem` → `Engine/Planning/FromItemPlanner` → `PlanResult` (Strategies + Problems)
  - `ItemGoal.Compare(item, target)`: Kept / ToRemove / Missing / ValuesUnmet / RarityGap (Distance)
  - Greedy pro Tool-Policy (Basic, +Omens/Chaos, +Essences, +Desecration, +Fracture): alle Moves werden mit engine.Preview bewertet, nächster Zustand via engine.Execute mit ManualChoice; Score = success^(1/progress), bei Gleichstand mehr Progress
  - Moves: Annul (+Sinistral/Dextral/Light), Transmute/Augment/Regal/Exalt (+Omens), Chaos (Removal × Addition), Essenzen/Alloys (+Crystallisation), Desecration + Reveal (+Abyssal Echoes), Fracture als Schutz (nur 1. Schritt), Divine wenn nur Werte fehlen
  - Hinzugefügte Mods werden mit Minimalwerten angenommen; Reveal-Chance nimmt gleiche Gewichte an
  - WICHTIG: Züge, nach denen das Item eine höhere Rarity als das Ziel hat, sind verboten (Rarity kann nicht gesenkt werden; z. B. Essenz macht Magic → Rare). Zentral in `BuildGreedy` über `ItemGoal.RarityImpossible`; `CraftStep.Result` = Item nach dem Schritt (Test `No_strategy_step_leaves_the_target_rarity`)

## Gewichte
- poe2db-Schätzwerte, NICHT echte Spielgewichte
- Immer pro (Mod, Klassenseite) gespeichert
- Als Schätzung gekennzeichnet und editierbar

## Arbeitsweise (WICHTIG)
- Selbstständig arbeiten und NICHT nachfragen (Mario, 12.09.2026: "just do it and don't ask me again"); unverifizierte Spielmechanik als Annahme kennzeichnen (Notes in Preview / config.json)
- Vor JEDER Dateiänderung die neueste Version laden — Mario editiert oft zwischen Turns
- Sprache: Spiel-Client EN, UI EN, Kommentare EN, Kommunikation mit Mario DE/Österreichisch
- Build: `dotnet build` im Projekt-Root (`dotnet test` baut das Web-Projekt NICHT mit)
- Test: `dotnet test` (Integrationstests laden das echte `data/`)
- Run: `dotnet run --project src/POE2Crafting.Web`
- Git: GitHub `https://github.com/MarioKoestl/Poe2Crafting.git` (branch main); research/-Rohdaten sind gitignored
- **NIE committen oder pushen** — Mario macht alle Commits selbst
- **KEINE Code-Duplikate**: gemeinsame Logik in Funktionen / Komponenten / Core auslagern (Spielregeln gehören in POE2Crafting.Core, nicht in .razor)

## Offene Punkte / Nächste Schritte
- Desecration: Tier-Level der Desecrated-Mods (Daten haben meist nur Level 65), Cranium-Zerstörungschance, Reveal-Gewichte
- Corruption: Vaal-Infuser (Quality über Maximum), Unique-/Jewel-Sonderergebnisse, Vaal-Reroll bei Rares verifizieren
- Runen/Soul Cores einsetzen (Augments in Sockel), Perfect Flux, Reforging Bench
- Verifizieren: Quality pro Nutzung je Rarity, Katalysator-Menge/Typwechsel, Catalysing-Exaltation-Stärke, Flux-"equivalent"
- Expedition-/Crest-Mods (Mechanik unklar)
- Planner: Currency-Kosten berücksichtigen (empfiehlt aktuell z. B. Perfect-Orbs rein nach Wahrscheinlichkeit)
- Essenzen: ob Mod-Level > Item-Level blockiert, ist unverifiziert (derzeit nur Hinweis)
- Weitere Details: siehe Claude-Projekt "POE 2" → poe2-crafting/STATUS.md und KNOWLEDGE_BASE.md
