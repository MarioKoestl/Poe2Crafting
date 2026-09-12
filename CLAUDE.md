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
│   │   │   ├── CraftingEngine.cs   # Check/Preview/Execute für Currency+Omen
│   │   │   ├── ModPool.cs          # Mod-Kandidaten, Gewichte, ComputeAllDisplayTiers()
│   │   │   ├── CraftingPathFinder.cs # Findet optimale Crafting-Pfade
│   │   │   └── Rng.cs              # Seeded RNG
│   │   └── Items/
│   │       ├── Item.cs             # Item, ItemMod, Rarity, ModKind
│   │       └── ItemParser.cs       # Ctrl+Alt+C Text → Item
│   └── POE2Crafting.Web/           # Blazor UI
│       ├── Pages/
│       │   ├── Index.razor         # Hauptseite: links Item, rechts Simulator/Planner Tabs
│       │   └── ItemComposer.razor  # NEU: Inline-Composer zum Zusammenstellen von Items
│       ├── Components/
│       │   ├── ItemDisplay.razor   # Item-Anzeige (Mods, Stats)
│       │   ├── CurrencySelector.razor
│       │   ├── PreviewPanel.razor
│       │   ├── ItemBuilder.razor   # Planner: Target-Item definieren + Pfade finden
│       │   └── PlannerPanel.razor  # Planner: Ergebnisse anzeigen
│       ├── Services/
│       │   └── CraftingSession.cs  # Per-User Session: CurrentItem, History, Engine
│       └── wwwroot/css/site.css    # EINZIGE CSS-Datei
├── data/                           # JSON-Datenspeicher (Spieldaten)
├── research/                       # poe2db Exports, PoB-Daten
├── tools/                          # Python-Skripte für Datenaufbereitung
└── tests/
```

## Wichtige Architektur-Konzepte

### Tiers
- ModDef.Tier: Globaler Tier, berechnet in GameData.ComputeTiers() (T1 = best innerhalb Family/Gen/Category global)
- Display-Tiers: Per-Base berechnet in ModPool.ComputeAllDisplayTiers() — SINGLE SOURCE OF TRUTH
- T1 = bester (höchster Level), wie im Spiel

### ModPool
- `_byPage`: nur Category=="normal" Mods
- `_categorized`: non-normal browsable categories (breach_otherworldly, desecrated)
- `Candidates()`: filtert nach AffixType, ItemLevel, Family-Exklusivität, Base-Tags
- `ComputeAllDisplayTiers(Item)`: berechnet Display-Tiers für alle sichtbaren Mods einer Base

### Item-Modell
- Rarity: Normal, Magic, Rare, Unique
- ItemMod: ModId, Kind (Explicit/Crafted/Desecrated/Implicit), Affix (Prefix/Suffix/Other), Values, Def
- Magic: max 1P/1S, Rare: max 3P/3S
- Familien-Exklusivität: nie zwei Mods derselben Familie

### CraftingSession (Scoped per User)
- Hält CurrentItem, History, Engine, RNG
- ImportItem(): Ctrl+Alt+C Text parsen
- ComposeItem(): Item mit gewählter Rarity und Mods erstellen (für ItemComposer)
- Execute(): Currency/Omen auf Item anwenden
- Save/Load: JSON-Projekte

### UI-Layout (Index.razor)
- Links: Item-Panel (Import, Compose, ItemDisplay, History)
- Rechts: Tabs (Simulator | Crafting Planner)
  - Simulator: CurrencySelector → PreviewPanel (Wahrscheinlichkeiten + Würfeln/Wählen)
  - Planner: ItemBuilder (Target definieren) → PlannerPanel (Pfade anzeigen)

### ItemComposer (NEU)
- Inline im linken Panel (ersetzt das alte Create-Modal)
- Gleicher Mod-Browser wie ItemBuilder (Family-Groups, Tier-Buttons, Prefix/Suffix Tabs)
- Kategorien: normal, breach_otherworldly, desecrated
- Erstellt Item via Session.ComposeItem() → wird CurrentItem
- Integration mit Planner: "Path from Current Item" in ItemBuilder nutzt das composed Item

## Gewichte
- poe2db-Schätzwerte, NICHT echte Spielgewichte
- Immer pro (Mod, Klassenseite) gespeichert
- Als Schätzung gekennzeichnet und editierbar

## Arbeitsweise (WICHTIG)
- Bei Unklarheiten SOFORT nachfragen statt annehmen
- Vor JEDER Dateiänderung die neueste Version laden — Mario editiert oft zwischen Turns
- Sprache: Spiel-Client EN, UI EN, Kommentare EN, Kommunikation mit Mario DE/Österreichisch
- Build: `dotnet build` im Projekt-Root
- Run: `dotnet run --project src/POE2Crafting.Web`

## Offene Punkte / Nächste Schritte
- Essenzen/Alloys im Engine implementieren
- Desecration-Mechanik
- Corruption/Vaal
- Katalysatoren
- Expedition-/Crest-Mods (Mechanik unklar)
- Weitere Details: siehe Claude-Projekt "POE 2" → poe2-crafting/STATUS.md und KNOWLEDGE_BASE.md
