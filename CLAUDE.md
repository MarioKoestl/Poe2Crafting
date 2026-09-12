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
│   │   │   ├── GameData.cs         # Lädt alle JSON, hält Lookups, ComputeTiers()
│   │   │   └── Models.cs           # ModDef, BaseItem, CurrencyDef, OmenDef, etc.
│   │   ├── Engine/
│   │   │   ├── CraftingEngine.cs   # Check/Preview/Execute für Currency+Omen+Essenzen/Alloys
│   │   │   ├── ModPool.cs          # Mod-Kandidaten, Gewichte, DisplayTier() (gecacht pro Base)
│   │   │   ├── CraftingPathFinder.cs # Findet Crafting-Pfade (nutzt Engine.AdditionDistribution)
│   │   │   ├── CraftingPath.cs     # TargetItemSpec, TargetMod.Matches(), CraftingStrategy
│   │   │   ├── Outcomes.cs         # CraftAction, StepPreview, ManualChoice, CraftResult
│   │   │   └── Rng.cs              # Seeded RNG
│   │   └── Items/
│   │       ├── Item.cs             # Item, ItemMod, Rarity, ModKind, ModText.StatSignature()
│   │       └── ItemParser.cs       # Ctrl+C / Ctrl+Alt+C Text → Item (Mod-Auflösung pro Base)
│   └── POE2Crafting.Web/           # Blazor UI
│       ├── Pages/
│       │   ├── Index.razor         # Hauptseite: links Item, rechts Simulator/Planner Tabs
│       │   └── ItemComposer.razor  # Inline-Composer zum Zusammenstellen von Items
│       ├── Components/
│       │   ├── ItemDisplay.razor   # Item-Anzeige (Mods, Runen, Implicits, Display-Tiers)
│       │   ├── BaseItemForm.razor  # Klasse/Base/Rarity/ilvl (Composer + Planner)
│       │   ├── ModBrowser.razor    # Mod-Auswahl (Composer + Planner), gruppiert nach Familie+Stat
│       │   ├── CurrencySelector.razor # Gruppen: Orbs, Essences, Alloys
│       │   ├── PreviewPanel.razor  # Wahrscheinlichkeiten, Würfeln, manuelle Wahl (Chaos 2-stufig)
│       │   ├── ItemBuilder.razor   # Planner: Target-Item definieren + Pfade finden
│       │   ├── PlannerPanel.razor  # Planner: Ergebnisse anzeigen
│       │   └── MermaidDiagram.razor
│       ├── Services/
│       │   ├── CraftingSession.cs  # Per-User Session: CurrentItem, History, Engine
│       │   └── ModSelection.cs     # Regeln für gewählte Mods (Slots, ilvl, Familie, Tier-Tausch)
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
- ItemMod: ModId, Kind (Explicit/Crafted/Desecrated/Implicit/...), Affix (Prefix/Suffix/Other), Values, Def, SourceName
- `ItemMod.IsAffix` = belegt einen Slot (Kind Explicit/Crafted/Desecrated UND Affix != Other)
- Slot-Limits NUR über `SimAssumptions.MaxPrefixes/MaxSuffixes/MaxAffixes(rarity)` (config.json), nie hartkodieren
- Familien-Exklusivität: nie zwei Mods derselben Familie (egal welche Kategorie)
- Item.Bind() setzt ItemClass immer aus der Base (Spiel sagt "Staves", Daten "Staff")

### Essenzen & Alloys
- `GameData.EssenceCurrencies`: synthetische CurrencyDef (Op="essence", `.Essence`) → gleicher Engine-/UI-Pfad wie Orbs
- `GameData.EssenceModFor(essence, base, class)`: garantierter Mod aus Kategorie essence/perfect_essence, Page-Match über Weights-Keys
- Lesser/Normal/Greater: Magic → Rare + Explicit-Mod. Perfect/Corrupted/Alloy: entfernt 1 Mod (gleiche Familie wird ersetzt; ist der Slot-Typ voll, nur dieser Typ) + Crafted-Mod
- Crystallisation-Omens nur für Perfect/Corrupted; `onlyOneCraftedModPerItem` aus config

### Planner
- Ziel-Mod trifft mit seinem Tier ODER besser (`TargetMod.Matches`, gleiche Familie+Stat, Level ≥), abschaltbar (`AllowBetterTiers`)
- Wahrscheinlichkeiten über `CraftingEngine.AdditionDistribution` (gleiche Prefix/Suffix-Regel wie Simulator)
- Strategien: Transmute→Aug→Regal→Exalt (Magic-Phase 1P+1S), Essenz-Start, Alchemy+Annul, Chaos-Spam; ab Current Item: Exalt/Augment/Regal-Completion, Annul+Exalt
- PoE2 hat keinen Orb of Scouring → Restart = "Start over with a new base"

### CraftingSession (Scoped per User)
- Hält CurrentItem, History, Engine, RNG
- ImportItem(): Itemtext parsen (Fehler + Anzahl nicht zuordenbarer Mods in LastError)
- SetItem(item, action): Composer/Import → CurrentItem, neue History
- Execute(): Currency/Omen/Essenz anwenden; InvalidOperationException aus der Engine → LastError (kein Circuit-Crash)
- Save/Load: JSON-Projekte

### UI-Layout (Index.razor)
- Links: Item-Panel (Import, Compose, ItemDisplay, History)
- Rechts: Tabs (Simulator | Crafting Planner)
  - Simulator: CurrencySelector → PreviewPanel (Wahrscheinlichkeiten + Würfeln/Wählen)
  - Planner: ItemBuilder (Target definieren) → PlannerPanel (Pfade anzeigen)

### ItemComposer / ItemBuilder
- Beide nutzen `BaseItemForm` + `ModBrowser` + `ModSelection` (keine Duplikate mehr)
- Klick auf anderen Tier derselben Familie tauscht den Tier
- Kategorien: normal, breach_otherworldly, desecrated
- Composer → `Session.SetItem()`; Planner: "Use current item's base" + "Path from Current Item" (nur bei gleicher Base)

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

## Offene Punkte / Nächste Schritte
- Desecration-Mechanik (Knochen, Unrevealed, Reveal 1-aus-3)
- Corruption/Vaal
- Katalysatoren
- Expedition-/Crest-Mods (Mechanik unklar)
- Planner: Currency-Kosten berücksichtigen (empfiehlt aktuell z. B. Perfect-Orbs rein nach Wahrscheinlichkeit)
- Planner: Alchemy/Chaos-Schätzung ignoriert Prefix/Suffix-Caps; Perfect-Essence-/Alloy-Strategien fehlen noch
- Essenzen: ob Mod-Level > Item-Level blockiert, ist unverifiziert (derzeit nur Hinweis)
- Weitere Details: siehe Claude-Projekt "POE 2" → poe2-crafting/STATUS.md und KNOWLEDGE_BASE.md
