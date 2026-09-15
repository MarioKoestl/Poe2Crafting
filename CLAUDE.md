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
│   ├── POE2Crafting.Core/          # Domain + Engine + Spielregeln (kein UI)
│   │   ├── Data/                   # EINE Datei pro Datenmodell (BaseItem, ModDef, CurrencyDef, EssenceDef, OmenDef, CatalystDef, AugmentDef, InstillRecipe, ItemClassDef, GuideModels)
│   │   │   ├── GameData.cs         # Lädt alle JSON (Pflichtdateien werfen), Lookups (Find*, CurrenciesOf(op), OpOfOmenTarget, PagesFor, EssenceModsFor, Catalyst*/Augment*/Instill-Helfer)
│   │   │   ├── SimConfig.cs        # SimAssumptions (config.json, init-only; AffixTypeSelection-Enum, QualityPerUse nach Rarity), MaxAffixes = einzige Slot-Limit-Quelle
│   │   │   ├── ModCategories.cs    # ModCategories (+KindFor/CanAppearAs), ModFamilies (MaximumQuality, AbyssMark)
│   │   │   ├── CurrencyDef.cs      # CurrencyDef + CurrencyOps (alle Op-Ids als Konstanten) + CraftItemInfo
│   │   │   ├── EssenceDef.cs       # EssenceDef + EssenceTiers
│   │   │   ├── BaseStats.cs        # Vergleichbare Base-Werte (Armour/Evasion/ES/Ward/Block, Schaden/APS/Crit/DPS, Flask, Anforderungen) aus bases.json (BaseItem.Armour/Weapon/Flask/Requirements/SubType); PrimaryOf(subType)
│   │   │   ├── ClassTargets.cs     # Klassen-Gruppen (weapon_or_quiver, jewellery, ...) — EINZIGE Stelle mit Gruppen-Logik
│   │   │   └── ModTiers.cs         # Tier-Ranking pro Familie+Stat
│   │   ├── Drafting/               # ItemDraft, ModSelection (+SelectedMod.ValueBounds), FinishingTarget — Regeln von Composer/Planner-Target
│   │   ├── Engine/
│   │   │   ├── CraftingEngine.cs   # Generische Checks + gemeinsame Bausteine (Pick, ValuesFor, AddOne/AddMods, RemoveOne, PickOutcome<TKey>, MaxQuality, FreeSlots)
│   │   │   ├── CraftAction.cs, OmenEffects.cs, Candidates.cs (ModCandidate/RemovalCandidate, immutable), Outcomes.cs (Applicability, StepPreview, ManualChoice, CraftResult, InvalidChoiceException)
│   │   │   ├── ModPool.cs          # Mod-Kandidaten, Gewichte, DisplayTier() (gecacht pro Base, thread-safe)
│   │   │   ├── Rng.cs              # Seeded RNG (PickWeighted, SampleWeighted, RollRange)
│   │   │   ├── Operations/         # EINE internal Klasse pro Currency-Op (CraftOperation: Check/Preview/Execute) + CraftContext/ExecuteContext
│   │   │   └── Planning/           # namespace Engine.Planning
│   │   │       ├── CraftingPathFinder.cs  # Planner-Einstieg (PrepareTargets, ToMermaid)
│   │   │       ├── FromItemPlanner.cs (+ .Cache/.AffixMoves/.EssenceMoves/.ValueMoves/.FinishingMoves partials)  # Beam Search
│   │   │       ├── ItemGoal.cs, TargetItemSpec.cs (TargetItemSpec/TargetMod/AugmentTarget), CraftingStrategy.cs (PlanResult/CraftingStrategy/CraftStep, NewStep, Materials())
│   │   │       ├── GuideRunner.cs (+GuideWalkthrough), GuideLibrary.cs (Singleton-Cache der Walkthroughs)
│   │   │       ├── RecordedGuide.cs      # HistoryEntry, RecordedGuide (gespeicherte Simulator-History), RecordedGuideRunner → GuideWalkthrough
│   │   │       └── OutcomeChance.cs      # Chancen gewünschter Additions/Removals + TryExecute (Planner & Guides)
│   │   ├── Market/                 # Currency Exchange: ExchangeDigest (API-JSON → HourlyMarket), LeagueMarket (Preise/Routen/Rows), MarketSnapshot, ExchangeCatalog (+ExchangeCurrencies/-Categories), ExchangeQuote/QuoteRoutes/ItemMarket, MarketSettings
│   │   └── Items/
│   │       ├── Item.cs             # Item (FromBase, AddMod, ReplaceMod, Title, QualityText, EffectiveValues), ItemMod (StatValues), RevealContext (Describe), Rarity, ModKind(+OccupiesSlot), AffixTypeExtensions (Both/Lower/Opposite)
│   │       ├── ModText.cs          # ALLE Zahlen-/Range-Regeln (Render, StatSignature, Bounds, PossibleRolls, ChanceAtLeast, ScaleValues, RangesText)
│   │       ├── ItemParser.cs       # Ctrl+C / Ctrl+Alt+C Text → Item (Mod-Auflösung pro Base, Catalyst-Qualitytyp kanonisiert)
│   │       ├── ItemTextWriter.cs   # Item → Text (Round-Trip mit dem Parser, Test)
│   │       ├── ItemTextFormat.cs   # Marker/Header-Flags/Separator — gemeinsames Vokabular von Parser, Writer, ItemDiff
│   │       └── ItemDiff.cs         # Mod-/Runen-/Property-Änderungen (Multiset)
│   └── POE2Crafting.Web/           # Blazor UI
│       ├── Pages/Index.razor       # Hauptseite: links Item, rechts Tabs Simulator | Crafting Planner | Guides (Index besitzt das Pane-Layout)
│       ├── Pages/Market.razor      # /market: Currency Exchange (Core rates, Trade calculator, Preistabelle); Top-Bar-Navigation Crafting | Bases | Market in MainLayout
│       ├── Pages/Bases.razor       # /bases(?class=Gloves): alle Basis-Items einer Klasse, Filter Verteidigungstyp (SubType), sortierbare Spalten aus `BaseStats`, Bestwert je Spalte hervorgehoben, Runeforged-Varianten optional, "Craft" = neues Item im Projekt
│       ├── Components/             # u. a. ItemDisplay, ItemComposer, BaseItemForm, ModBrowser, CurrencySelector, PreviewPanel, RevealOptions, InstillPanel/Picker,
│       │                           # ItemBuilder, PlannerPanel, StrategyGuide, StrategyCard, GuideList, GuideDetail, GuideBulletList, ProjectPanel,
│       │                           # EmptyState, SearchBox, CraftIcon, CraftItemLink, CraftText, ItemDiffList, DistributionRow, ModOptionRow, MermaidDiagram
│       ├── Services/
│       │   ├── CraftingSession.cs  # Per-User Session: Projekt, CurrentItem, History, Reveal-State (lädt Projekt lazy)
│       │   ├── PlannerState.cs     # Per-User State von Planner + Guides (Draft, Ergebnis, Auswahl) + Changed-Event + PlannerStateComponentBase
│       │   ├── ProjectStore.cs     # Projekte als JSON (atomar, gecachte Liste)
│       │   ├── GuideCatalog.cs     # Singleton: kuratierte Guides + gespeicherte Guides (saved-guides/{Id}.json)
│       │   ├── JsonDocumentFolder.cs # ein JSON pro Dokument (atomar, kaputte Dateien übersprungen) — von ProjectStore und GuideCatalog geteilt
│       │   ├── MarketDataService.cs # Singleton + HostedService: stündliche Exchange-Digests laden, market-cache/, alle PollMinutes prüfen, Changed-Event
│       │   ├── UiFormat.cs         # Chancen/Versuche/Preise/Compact/CSS-Klassen/Plural (invariant)
│       │   ├── UiHelpers.cs        # Toggle-Extension, TextSearch.Matches, ChangingComponentBase (Change → OnChanged)
│       │   └── AffixUi.cs          # P/S-Buchstabe + CSS-Klassen
│       └── wwwroot/css/site.css    # EINZIGE CSS-Datei (alle Farben als Tokens in :root)
├── data/                           # JSON-Datenspeicher (Spieldaten)
├── research/                       # poe2db Exports, PoB-Daten
├── tools/                          # Python-Skripte für Datenaufbereitung
└── tests/                          # GlobalUsings.cs, TestData (Apply, BestMod, Spec, Plan, Defs, TestBases), [DataFact]/[DataTheory]
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
- Neue Currency-Mechanik = neue `CraftOperation`-Klasse unter `Engine/Operations/` + Eintrag im Engine-Konstruktor; KEIN switch über Op-Ids (Op-Ids nur über `CurrencyOps`)
- Ungültige manuelle Wahl → `InvalidChoiceException` (nur die fängt `OutcomeChance.TryExecute`/Session; andere Exceptions sind Bugs und dürfen nicht verschluckt werden)
- Kandidaten sind immutable (`ModCandidate.Normalised`, `RemovalCandidate.Uniform/UniformOf` liefern neue Objekte): Previews werden im Planner gecacht und geteilt
- Gewählt-oder-zufällig immer über `CraftingEngine.Pick`, benannte Ergebnisse über `PickOutcome<TKey>` (Label ↔ Key, z. B. Desecrate: AffixType, Augment: Sockel-Index), Corruption über `ExecuteContext.Corrupt()`
- `StepPreview.WeightsNote` getrennt von den Notes
- Divine Orb/Flux: `StepPreview.ValueRerolls` = neu gewürfelte Mods (Index + Mod mit den geltenden Ranges) → PreviewPanel zeigt Wert-Eingaben ("Apply these values" → `ManualChoice.Rerolls`, Werte außerhalb der Range → InvalidChoiceException)
- Generische Checks (Rarity, Target-Klasse, Mirrored, Corrupted, Max-ilvl, Omen-Zuordnung) macht nur `CraftingEngine.Check`
- Omen-Effekt-Ids als Konstanten in `OmenEffects`, Kategorie-Namen in `ModCategories`
- MEHRERE Omens pro Aktion: `CraftAction.Omens` / `CraftContext.Omens` (Liste; `OmenEffects.None` = keine, nie null); `ctx.OmenIs(effect)`, `omens.Has/WithEffect`, `OmenEffects.RestrictedType(omens)`; `CraftOperation.AcceptsOmen(ctx, omen)` pro Omen. Widersprüche (`OmenEffects.Conflict`, ANNAHME): gleiches Omen doppelt, Prefix- vs. Suffix-Restriktion, zwei Boss-Omens, Putrefaction + Restriktion. UI: Omens als Toggle, blockierte mit Grund im Tooltip. Planner probiert Einzel-Omens und verträgliche Paare
- Alle Currencies aus den Daten haben eine Operation (Test `Every_simulated_currency_has_an_operation` prüft das)
- Klassen-Gruppen (weapon_or_quiver, armour, ring_or_amulet, socketable, equipment, ...) NUR in `ClassTargets` (Konstanten + Tabelle); Currency nutzt `CurrencyDef.ClassTarget` (Target ?? QualityTarget), sonst `CraftOperation.DefaultClassTarget`
- Gemeinsame Bausteine u. a.: `PickOutcome`/`NormaliseOutcomes` (benannte Ergebnisse + Handwahl), `AddCorruptionEnchant`, `RemoveOne`, `AddOne`
- `CraftOperation.WorksOnCorrupted` / `RequiresCorrupted` statt eigener Corrupted-Checks

### "+4 Skills Amulett"-Technik (YouTube-Guide, Test `PlusFourAmuletTechTests`)
- Katalysator-Quality erhöht die WERTE passender Mods (Tag): `Item.EffectiveText(mod)` = alle Zahlen × (1 + Quality/100), ganze Zahlen abgerundet (+3 × 1.34 = +4, × 1.33 = +3); ItemDisplay zeigt den effektiven Wert (Basiswert im Tooltip)
- Desecrated-Mods (auch unrevealed) zählen für die 4 Mods, können aber NICHT fractured werden (1/3 statt 1/4)
- Omen of Whittling: unrevealed Desecrated zählt als Level 1 (vorher revealen!); Essence-of-the-Breach-Mod hat Level 1 → wegwhittlen, Quality bleibt
- Essence of the Abyss: Mark existiert als Prefix+Suffix derselben Familie → nimmt den Slot des entfernten Mods (mit Sinistral Crystallisation garantiert Prefix)
- Der Planner findet die Technik selbst (Tests `Planner_*` in `PlusFourAmuletTechTests`): Werte-Ziel "+4" auf fixem +3-Mod → Breach (+Crystallisation) → Katalysatoren → Breach-Mod entfernen; ohne toten Mod zuerst ein Blocker

### Quality, Katalysatoren, Sockel, Flux
- Quality-Currencies: +config `qualityPerUse` je Rarity (UNVERIFIED), Cap = `Item.MaxQuality()` (Base-Quality/Default 20 + "Maximum Quality"-Mods wie Essence of the Breach)
- Vaal-Infuser: wie Quality, Cap +10, Corruption-Chance 0 bei Start ≤ Max, +5 %/Punkt darüber (poe2wiki)
- Katalysatoren: synthetische Currencies (Op "catalyst", Section Catalysts), +5 % je Nutzung, Typ ersetzt, Menge bleibt (Annahme); `Item.QualityTag`/`QualityEnhances` für verstärkte Mods (Badge in ItemDisplay)
  - Quality nach der Nutzung frei wählbar (`StepPreview.Quality` = `QualityChoice(Type, Default, Min, Max)`, `ManualChoice.Quality`)
  - Wirkungstabelle im PreviewPanel: `QualityEffects.For(item, type, quality, max, GameData.HighestReachableQuality)` → pro verstärktem Mod jetzt / bei X % / bei Max / nächster Wertsprung (auch über dem Max, markiert "above max", z. B. +3 → +4 ab 34 %)
- Omen of Catalysing Exaltation: Gewicht passender Mods × (1 + Quality × `catalysingWeightBonusPerQuality`), Quality wird verbraucht (Annahme)
- Artificer's Orb: +1 Sockel bis `BaseItem.SocketLimit`; Orb of Extraction: zerstört Item, nennt Runen
- Flux: Fire/Cold/Lightning-Res-Mods → Element der Flux (Void: → Chaos); gleiches Level-Tier (höchstes ≤ Quell-Level), Wert wird im neuen Tier NEU gewürfelt (poe2wiki + Mario im Spiel) → Choose Outcome mit Werteauswahl wie Divine (`StepPreview.ValueRerolls` = `ValueReroll(Index, Mod)` mit den Ranges des NEUEN Mods, `CraftingEngine.RerolledValues`)

### Corruption
- Vaal Orb: Ergebnisse aus config `vaalOutcomes` (je 25 %, Quelle maxroll/timesaver.gg 0.5.x); nicht mögliche Ergebnisse fallen raus, Rest normiert; Omen of Corruption streicht no_change
  - corrupted_implicit: Enchantment aus Kategorie corrupted (`ModPool.CorruptionEnchantCandidates`, ohne vorhandene Familien) → `ModKind.CorruptedImplicit`
  - reroll_mods: 1-3 Affixe (config) werden je durch einen neuen Mod gleichen Typs ersetzt (ANNAHME, Quelle sagt nur "randomized")
  - add_socket_or_quality: +1 Sockel über Limit (Martial Weapon, Armour-Gruppe, Focus) bzw. Quality bis 23 % (Wand/Staff); Schmuck/Jewel: Ergebnis entfällt
- Orb of Sacrifice: Enchantment → Upgrade (`GameData.CorruptionUpgradeFor`, Name `CorruptionX` → `CorruptionUpgradeX`, 110/115 vorhanden) + zufälligen Mod entfernen
- Architect's Orb: 50 % (config) zweites Enchantment anderer Familie + `Item.TwiceCorrupted`, sonst zerstört
- Import: "(enchant)"-Zeilen auf Corrupted-Items werden als Corruption-Enchantments aufgelöst; "Twice Corrupted" im Footer
- Unrevealed Desecrated im Spieltext = `{ Suffix Modifier "of the Veil" }` + Zeile "Desecrated Suffix" → `ItemMod.MarkUnrevealed` (Parser + `Item.Bind` repariert alte Projekt-Items); Edit behält die Kind eines übernommenen Mods (`SelectedMod.KindOnItem`)
- Neueres Itemtext-Format: "Quality (Caster Modifiers): +34% (augmented)", "{ Enhancement }"-Block ("Allocates X — Unscalable Value" = Instill-Enchant), Desecrated-Header mit Mod, den die Daten nur als normal kennen (Countess' Spirit) → Kind bleibt Desecrated (Test mit Woe Braid)
- ItemDisplay hat oben rechts "⧉ Copy": `ItemTextWriter.ToText` (Display-Tiers) → Zwischenablage (`copyText` in site.js, Fallback execCommand) — bei JEDEM angezeigten Item; Werte mit Range wie im Spiel (`ItemMod.AdvancedText`, `ModText.RenderWithRanges`); App.razor hängt an site.css/site.js `?v=<Dateistand>` (kein alter Browser-Cache nach Updates)
- Minimum Modifier Level (Greater/Perfect): pro Mod-Typ (Tier-Gruppe); wären alle Tiers ausgeschlossen, rollt der höchste Tier ≤ Item Level trotzdem (poe2wiki; `ModPool.AtLeastMinimumLevel`, z. B. Hoarder's Level 47 mit Perfect Augmentation)
- `ModDef.DisplayName`: Enchantments haben in den Daten nur Codes als Namen → in der UI immer DisplayName verwenden
- Nicht modelliert: Unique-Reroll (x0.78-1.22), Jewel-Sonderergebnis (Affix hinzufügen/entfernen), Vaal-Infuser

### Desecration
- Knochen (Op "desecrate", Target weapon_or_quiver/armour/jewellery/jewel) fügen ein `ItemMod { Unrevealed, Kind=Desecrated, Reveal=RevealContext }` hinzu; volle Slots → zufälliger Mod weg, Unrevealed übernimmt dessen Affix-Typ (Annahme)
- Nicht auf Sanctified; nicht wenn schon ein Desecrated-Mod da ist (außer Mark of the Abyssal Lord, der ersetzt wird)
- Omens: Necromancy (P/S), Sovereign/Liege/Blackblooded (Boss-Tag, nur Waffe/Schmuck), Putrefaction (alle Mods → N Unrevealed + Corrupted), Abyssal Echoes (einmal Reveal-Optionen neu würfeln, gehört zur Well of Souls)
- Knochen ergeben IMMER einen Unrevealed-Mod (zählt als Level 1 für Whittling — Quelle nur Community, z. B. dadsofexile.com, NICHT offiziell bestätigt); Preview listet "Possible revealed modifiers" (nicht wählbar)
- Reveal = synthetische Currency **Well of Souls** (`GameData.WellOfSouls`, Op `reveal`, `RevealOperation`, `Consumed=false` → keine Material-Zeile), nimmt Omen of Abyssal Echoes; Preview: welcher Unrevealed (bei mehreren) + "Can be revealed as" (Choose Outcome = beliebiger Mod), UI `RevealOptions` in der Vorschau: N Optionen würfeln (config `revealOptionCount`), mit Echoes einmal neu würfeln, Option wählen. `Engine.RevealPool/RollRevealOptions/Reveal` (Reveal = Execute der Well of Souls); Ancient-Bones: MinModLevel, Altered Collarbone: + breach_otherworldly
- ItemDisplay: Unrevealed-Mod hat "▸ can become" → aufklappbarer Reveal-Pool (gemäß Knochen/Omen, z. B. nur Kurgal)
- Well-of-Souls-Optionen (Mario 14.09.2026, im Spiel gesehen + expertgamereviews "at least one exclusive"): `revealOptionCount` (3), mindestens `revealGuaranteedExclusiveOptions` (1) exklusive Lich-Mods (desecrated-Kategorie), jede weitere Option mit `revealRegularOptionChance` (0.5, KEINE Daten) ein NORMALER Mod desselben Affix-Typs (normale Gewichte, Mindest-Level des Knochens), sonst noch ein Lich-Mod ("genau 1" steht nirgends!); Boss-Omen → ALLE Optionen Mods dieses Lichs (poe2db-Hinweis "may include base modifiers. Unless you use Omen to guarantee named modifiers" + Mario im Spiel: 3× Kurgal; `revealBossOmenOnlyLichModifiers=true`); Vorschau/"can become" zeigen zwei Gruppen (`RevealOffers`: Lich-Mod | andere Optionen, `StepPreview.OtherAdditions`), Knochen-Vorschau nur für mögliche Affix-Typen (freier Suffix → nur Suffixe) — alles ANNAHME in config.json. `RevealPool` = Chance, angeboten zu werden; `Engine.RevealChance` für den Planner; zufälliges Reveal = Optionen würfeln, eine zufällig
- Datenlage: Desecrated-Mods fast alle Level 65, Gewicht 1 (keine poe2db-Schätzung) → Gnawed-Bones auf Waffen finden meist nichts

### Slot-Limits
- NUR über `SimAssumptions.MaxAffixes(item, rarity, type)` bzw. `MaxAffixes(itemClass, rarity, type, modTexts)`: Rarity-Limits aus config, `rareMaxAffixesPerTypeByClass` (Jewel = 2P/2S) + Extra-Slots aus Mods "+1 Prefix/Suffix Modifier allowed" (`ModText.ExtraAffixesAllowed`)

### Liquid Emotions, Runen/Soul Cores, Instill, Hinekora's Lock
- Liquid Emotions auf Juwelen = Essenz-artig: GameData macht aus den op-null-Currencies mit Mods der Kategorie `liquid` synthetische Essenzen (Tier "Liquid", Section "Liquid Emotions"); `EssenceModsFor` liefert ALLE möglichen Mods (Contempt: Prefix ODER Suffix, je 50 %, nur passende rollen)
- Augments: `GameData.Augments` aus `socketable`-Mods (Name = Rune/Soul Core/Idol), synthetische Currencies Op `socket_augment` (Section "Runes & Soul Cores"); `AugmentOperation` füllt freien Sockel, voll → gewählter Sockel wird ersetzt (config, ANNAHME); Effekttext pro Klasse via `GameData.AugmentEffectText`; Bonded-Effekte (nur Shaman) nicht modelliert
- Instill: `data/instills.json` (892 Rezepte, `python tools/poe2db_instills.py` aus research zip); `Engine.CheckInstill/Instill` → Enchant "Allocates X" (nur Amulette, nicht corrupted, ersetzt vorhandenes = ANNAHME); `Item.InstilledNotable`; UI `InstillPanel`/`InstillPicker`; `GameData.EmotionCurrency("Ire")`
- Hinekora's Lock: setzt `Item.Foreseeing` + `ForeseeSeed`; `Engine.Foresee(item, action)` = exaktes Ergebnis pro Aktion (Currency+Omens), `CraftingEngine.ForeseeRng` beim Anwenden → identisch; PreviewPanel zeigt "foreseen result" (ItemDiffList)
- `ItemDiff.Between(before, after)` = Mod-Änderungen (Planner-Guide, Lock-Vorschau)

### Essenzen & Alloys
- `GameData.AllCurrencies`: currencies.json + synthetische CurrencyDef für Essenzen/Alloys (Op="essence", `.Essence`) und Katalysatoren (Op="catalyst") → gleicher Engine-/UI-Pfad; `FindCurrency` sucht in allen
- `GameData.EssenceModFor(essence, base, class)`: garantierter Mod aus Kategorie essence/perfect_essence, Page-Match über Weights-Keys
- Lesser/Normal/Greater: Magic → Rare + Explicit-Mod. Perfect/Corrupted/Alloy: entfernt 1 Mod (gleiche Familie wird ersetzt; ist der Slot-Typ voll, nur dieser Typ) + Crafted-Mod
- Crystallisation-Omens nur für Perfect/Corrupted; `onlyOneCraftedModPerItem` aus config

### Planner
- Ziel-Mod trifft mit seinem Tier ODER besser (`TargetMod.Matches`, gleiche Familie+Stat, Level ≥), abschaltbar (`AllowBetterTiers`)
- Nur noch Pfade von einem bestehenden Item ("Find Paths", Mario, 13.09.2026: Pfade ab leerer Base braucht er nicht) — der alte Template-Planner ab Normal-Base wurde entfernt; eine Normal-Base als Source Item funktioniert trotzdem (Transmute-Moves)
- Wahrscheinlichkeiten kommen direkt aus `CraftingEngine.Preview` (gleiche Regeln wie Simulator)

### Web-Muster
- Culture: en-US global (Program.cs), Zahlen in Markup/CSS über `UiFormat` (invariant)
- Kein Prerendering (`InteractiveServerRenderMode(prerender: false)`), Session lädt das Projekt lazy
- Singletons: GameData, ModPool, CraftingEngine (zustandslos), GuideLibrary, ProjectStore; Scoped: CraftingSession, PlannerState
- Komponenten, die geteilten State ändern: `@inherits ChangingComponentBase` → `Change(() => ...)` ruft OnChanged
- Komponenten ohne Parameter rendern bei Parent-Render NICHT neu → State-Anzeige über `PlannerStateComponentBase` (Changed-Event)
- `@key` in Listen; teure Berechnungen in OnParametersSet cachen (CurrencySelector pro Item-Referenz, ModBrowser pro Base, PreviewPanel pro Action/Item)
- Klickbare Karten/Header sind `<button>` (StrategyCard, Kategorie-/Familien-Header, Step-Toggle, Projekt-Item)

### CraftingSession (Scoped per User)
- Hält das offene Projekt (`Project`), das aktuelle Projekt-Item (`ProjectItem`, eigene History) + CurrentItem, Engine, RNG
- Projekte: `ProjectStore` (Singleton) speichert jedes Projekt als `projects/{Id}.json` (Ordner neben data/, config `ProjectsFolder`, gitignored) — AUTOMATISCH bei jeder Änderung (Commit/Undo/Auswahl/Entfernen); beim Session-Start wird das zuletzt geänderte Projekt geöffnet. KEIN JSON-Download/Upload mehr
- UI `ProjectPanel` (oben im Item-Panel): Projekt-Auswahl, neu (＋), umbenennen (✎), löschen (🗑 → "Delete?"), Item-Liste des Projekts (klicken = öffnen, ✕ → "Remove?")
- ImportItem(): Itemtext parsen (Fehler + Anzahl nicht zuordenbarer Mods in LastError)
- SetItem(item, action): Composer/Import → NEUES Item im Projekt (ohne Projekt wird eines angelegt)
- Execute(): Currency/Omen/Essenz anwenden; InvalidOperationException aus der Engine → LastError (kein Circuit-Crash)

### UI-Layout (Index.razor)
- Desktop (>1100px): App füllt den Viewport; JEDE Spalte/Pane hat genau EINE Scrollbar (`.panel-body`, `.pane`) — keine verschachtelten Scroll-Container (keine max-height+overflow in Listen/Mod-Browser!). Schmal: alles gestapelt, Seite scrollt
- Links: Item-Panel (Import, Compose, ItemDisplay, History); beim Composen wird die linke Spalte breiter (`.crafting-page.composing`)
- Rechts: Segmented Tabs (Simulator | Crafting Planner) + `.workspace` mit zwei Panes
  - Simulator: [InstillPanel + CurrencySelector] | [PreviewPanel] (Reveal ist die Currency "Well of Souls")
  - CurrencySelector: Omens der gewählten Currency als volle Zeile DIREKT unter der gewählten Kachel (im Grid, `grid-auto-flow: dense`); Hinweis "N more currencies … hidden" immer sichtbar (nicht nur beim Suchen)
  - Planner: [ItemBuilder (Target)] | [PlannerPanel (Pfade, pro Schritt aufklappbar "Item after this step")]
- Hauptaktionen unten in der Pane fixiert (`.sticky-actions`, `.action-bar`)
- Choose bei einer Addition (Regal/Exalt/Chaos/Essenz/Reveal): Mod mit Range → Werte-Eingabe unter der Zeile (`ModValueInputs`, Start = Mitte) → "Apply" (`ManualChoice.Values`, Engine prüft Range) oder "Random value"; Divine/Flux nutzen dieselbe Komponente
- PreviewPanel: Choose-Buttons/Werte-/Quality-Eingaben IMMER sichtbar (Mario 14.09.2026: kein "Choose Outcome"-Umschalter mehr); unten nur "Roll (Random)"
- Design-Tokens (Farben, Radien, Schatten) nur in `:root` von site.css

### Currency-/Omen-Infos (CraftItemLink)
- `GameData.FindCraftItem(name)` → `CraftItemInfo` (Kind, IconUrl, Description, Facts wie Min-Mod-Level) für alle Currencies, Essenzen, Alloys, Katalysatoren, Omens
- Icons LOKAL (auch Basis-Items: Slug = `BaseItem.Id`, `GameData.BaseIconUrl`, angezeigt im ItemDisplay-Kopf und in der Projekt-Liste; Skript holt sie von den poe2db-Klassenseiten): `src/POE2Crafting.Web/wwwroot/img/icons/` + `data/icons.json` (Slug → lokaler Pfad), einmalig geladen mit `python tools/poe2db_icons.py` (Art-Pfade von poe2db, Dateien vom RePoE-fork-Mirror; poe2db-CDN blockiert z. B. Essenz-Icons außerhalb poe2db → nie direkt verlinken)
- `CraftItemLink` = markierter Name (Icon + Name, `ShowIcon=false` in Kacheln) → Popover beim HOVER (Mario will Hover, nicht Klick): reines CSS-:hover (kein Server-Roundtrip), `onmouseenter="positionPopover(this)"` platziert es (fixed), 150 ms Schließ-Verzögerung per transition-delay; Klicks gehen durch
- Alle Suchfelder (`TextSearch.Matches`): jedes Wort der Suche muss in einem der Texte vorkommen, Reihenfolge egal ("quality caster" findet Sibilant Catalyst)
- Currency-Suche filtert Name UND Beschreibung (z. B. "life" findet Essence of the Body); versteckte, nicht nutzbare Treffer werden als Hinweis gezählt
- `CraftText` = Text aus Namen mit " + " / " → " (CraftAction.DisplayName, Step-Currency, History) → jeder bekannte Name wird zum Link
- Neue Stellen, an denen Currency-/Omen-Namen angezeigt werden, IMMER über CraftItemLink/CraftText rendern
- ItemDisplay zeigt pro Mod die Roll-Range des Tiers ("(41–45)", `ModText.RangesText`, Tooltip = Tier-Text); Text + Range + Badges umbrechen gemeinsam (`.mod-body`)
- Mod-Level ("ilvl N", Whittling-relevant) überall über `ModLevel`-Komponente: ItemDisplay, Removal-Zeilen (`DistributionRow Mod=`), ModOptionRow, ModBrowser (gewählte Mods + Tier-Buttons), Divine-Werteauswahl

### ItemComposer / ItemBuilder
- Beide halten einen `ItemDraft` und nutzen `BaseItemForm` + `ModBrowser` (OnChanged="StateHasChanged" verdrahten!)
- Klick auf anderen Tier derselben Familie tauscht den Tier
- Mod-Suche durchsucht Prefixe UND Suffixe (Name, Text, Familie, Tags); ohne Suche zeigen die Tabs Prefix/Suffix
- Kategorien: normal, breach_otherworldly, desecrated
- Item-Panel: "Edit" öffnet den Composer mit dem aktuellen Item (Werte übernommen) → "Apply Changes" = `Session.EditItem()` als neuer History-Schritt (Undo geht); nicht editierbare Teile (Implicits, Quality, Sockel, Corruption, Fractured-Flag) bleiben über `ItemDraft`-Template + `Item.WithoutAffixes()` erhalten, Unrevealed-Mods über `ModSelection.Unrevealed` (im ModBrowser sichtbar/entfernbar). "New" = neues Item (History neu)
- Composer → `Session.SetItem()`; Planner (Mario 14.09.2026): **Source item** und **Target item** aus den Items des offenen Projekts wählbar (`PlannerState.SourceItemId`/`TargetItemId`, `Session.FindProjectItem`; Source-Standard = aktuelles Item). Target-Auswahl lädt das Item in den Draft (keine eigene Base-/ilvl-Auswahl, `BaseItemForm ShowBase=false`, nur Target Rarity), "Reload" lädt neu; "Find Paths" nur bei gleicher Base

### Crafting Planner (A → B)
- Source/Target: ItemBuilder mit ItemDraft/ModBrowser; "Source item"/"Target item" wählen Items des Projekts, das Target übernimmt Base, Rarity, ilvl und Mods zum Editieren
- Werte pro Mod-Range (SelectedMod.Values): im Composer = echte Werte, im Planner = Mindestwerte (TargetMod.MinValues)
- "Find Paths" = `CraftingPathFinder.FindPathsFromItem` → `Engine/Planning/FromItemPlanner` → `PlanResult` (Strategies + Problems)
  - `ItemGoal.Compare(item, target)`: Kept / ToRemove / Missing / ValuesUnmet / RarityGap (Distance)
  - Beam Search pro Tool-Policy (Basic, +Omens/Chaos, +Essences, +Desecration, +Fracture): BeamWidth 8, Ranking = Pfadchance × 0.3^Distance × 0.97^Schritte (nur zum Aussortieren); Ergebnis = wahrscheinlichster kompletter Pfad, Gleichstand → weniger verbrauchte Items (`Node.Cost`); Zustände per `Signature` dedupliziert, Pfade unter dem besten fertigen werden abgeschnitten
  - Dadurch sind Setup-Züge ohne direkten Fortschritt möglich: Blocker (`BlockerMoves`: Addition mit sicherem Affix-Typ, irrelevantes Ergebnis), Katalysator-Setup für Omen of Catalysing Exaltation, Essence of the Breach (`MaximumQualityMoves`, nur wenn Katalysator-Quality über dem Maximum nötig ist), Chaos + Omen of Whittling ohne Ziel-Mod (Ergebnis-Typ Prefix/Suffix als getrennte Züge mit ihrer Chance — nie einen Typ annehmen)
  - Moves: Annul (+Sinistral/Dextral/Light), Transmute/Augment/Regal/Exalt (+Omens inkl. Catalysing), Chaos (Removal × Addition, +Whittling), Essenzen/Alloys (+Crystallisation), Desecration + Reveal (+Abyssal Echoes), Fracture, Divine wenn nur Werte fehlen, Katalysatoren bis Werte-Ziele erreicht (`CatalystValueMoves`)
  - Werte-Ziele gelten für EFFEKTIVE Werte (`TargetMod.ValuesSatisfiedBy(item, mod)` → `Item.EffectiveValues`, Katalysator-Quality zählt); `ModDef.StatRanges`/`ItemMod.StatValues`: bei Texten ohne Range die fixen Zahlen (+3 Skills) — ModBrowser (`AllowQualityValues` im Planner) erlaubt Eingaben bis Max × `GameData.HighestCatalystFactor`
  - Previews/Simulationen werden pro Plan gecacht (Policies überlappen); ItemBuilder plant per Task.Run mit "Planning…"-Anzeige
  - Hinzugefügte Mods werden mit Minimalwerten angenommen; Reveal-Chance nimmt gleiche Gewichte an
  - Finishing-Ziele (`TargetItemSpec.MinQuality/QualityType/Augments/InstillNotable`, UI `FinishingTargetForm` + `FinishingTarget`): sichere Schritte (N× Quality-Currency bzw. Katalysator, Artificer's Orb, Rune einsetzen, Instill) — `ItemGoal` zählt sie in `Distance`
  - `CraftStep.Materials` (Name → Anzahl) → Guide-Ansicht (PlannerPanel): Start/Final-Item, Materialliste (pro Lauf + erwartet inkl. Retries), Schritte abhaken, Mod-Änderungen je Schritt, Hinekora-Hinweis bei unsicheren Schritten
  - Unrevealed Desecrated im Target: `ModSelection.Unrevealed` (aus dem Item übernommen, zählt als Slot, im ModBrowser mit ✕), `TargetMod.Unrevealed`/`UnrevealedOf(type)`, `TargetMod.Matches(ItemMod)`; fehlt er, fügt `DesecrationMoves` einen Knochen OHNE Reveal hinzu
  - Ziel-Tier über dem Item Level des Source Items → sofort Problem "needs item level X" (kein Currency hebt das Item Level), keine Suche
  - Quality-Ziel über der Max-Quality: `ItemGoal.MaximumQualityMissing` (+2 Distance) → Essence of the Breach zählt als Fortschritt; `NeededQuality` = max(Werte-Ziele via Katalysator, `MinQuality`), `MaximumQualityMoves` für beide
  - Bekannte Schwäche (14.09.2026, Marios Woe-Braid-Amulett): Pfade werden gefunden, sind aber oft nicht optimal (Reihenfolge Catalysing Exaltation vs. Quality-Ziel, Fracture-Timing, wenig Chance) — Beam Search mit Distanz-Heuristik
  - WICHTIG: Züge, nach denen das Item eine höhere Rarity als das Ziel hat, sind verboten (Rarity kann nicht gesenkt werden; z. B. Essenz macht Magic → Rare). Zentral in `Search` über `ItemGoal.RarityImpossible`; `CraftStep.Result` = Item nach dem Schritt (Test `No_strategy_step_leaves_the_target_rarity`)

### Crafting Guides (Tab "Guides")
- Kuratierte Abfolgen mit Erklärung in `data/guides.json` (`CraftingGuide`: Summary, KeyIdeas, Start-Item, Steps, NextSteps; Modelle in `Data/GuideModels.cs`, geladen als `GameData.Guides`)
- Step: Currency + Omens + Uses + Explanation + OnMiss + `Hit` (was als Treffer zählt: `select` = Filter auf die Removal-Liste der Preview — beim Fracturing Orb der gefracturte Mod —, `add`/`addTag` = hinzugefügter Mod, `outcome` = benanntes Ergebnis; Texte als Teilstring, "*" = beliebig)
- `GuideRunner.Run(guide)` spielt den Guide durch die Engine: Chance aus `Engine.Preview`, Item nach jedem Schritt via Execute mit ManualChoice → `GuideWalkthrough` (Start, `CraftingStrategy`, Problems) — Guides zeigen also immer die aktuellen Regeln/Annahmen
- `OutcomeChance` (Chance gewünschter Additions/Removals, `TryExecute`) teilen sich Planner und Guides
- UI: `GuideList` + `GuideDetail` (Walkthroughs aus dem Singleton `GuideCatalog` = `GuideLibrary` + gespeicherte Guides, Auswahl in `PlannerState`), `StrategyGuide` = gemeinsame Schritt-Ansicht für Planner-Ergebnisse UND Guides, `StrategyCard` = gemeinsame Karte; "Craft along in the Simulator" legt das Start-Item im Projekt an
- Neue Guides: nur JSON ergänzen; Test `Every_guide_plays_through_with_the_current_data` prüft, dass jeder Guide mit den Daten durchläuft
- Vorhanden: Fracture +3 Skills at 1/3 (Abyss mark), +4 Melee Skills Amulet, +4 Spell Skills Amulet (Quelle: YouTube-Video von Mario)
- **Eigene Guides aus dem Simulator** (Mario, 14.09.2026): Button "Save as Guide" unter der Crafting History → Modal (Name, Notes) → `CraftingSession.SaveAsGuide` → `GuideCatalog.Save` (Ordner `saved-guides/` neben data/, config `SavedGuidesFolder`, gitignored) → Guides-Tab "My Guides"
  - `RecordedGuideRunner` spielt NICHT neu, sondern zeigt den aufgezeichneten Weg (Items der History); `HistoryEntry.Action` (= `CraftAction.DisplayName`) wird zurück in Currency + Omens aufgelöst → Materialliste
  - Schritt-Chance = Chance, das Ergebnis wieder zu bekommen (gleicher entfernter/gefracturter Mod × hinzugefügter Mod gleicher Tier-Gruppe mit Tier oder besser, `ModTiers.IsSameOrBetterTier`) aus der aktuellen Preview; benannte Outcomes (Vaal, Knochen) sind nicht zuordenbar → zählt als sicher + Hinweis
  - Nicht-Currency-Schritte: Instill → Emotionen als Material (`GameData.InstillMaterials`); Edited/Well of Souls → ohne Chance/Material, als Problem gemeldet
  - GuideDetail: Basis-Item, Speicherdatum, "Craft along", Löschen (🗑 → "Delete?")
- Export "⤓ HTML" (GuideDetail + PlannerPanel): `GuideExportButton` → `GuideHtmlExport.ToHtml` (eine eigenständige HTML-Datei, Inline-CSS, Item-Icons als Base64 eingebettet, keine Skripte: Start/Final-Item, Materialien, Schritte mit Chance, Erklärung, Mod-Änderungen, aufklappbar "Item after this step", Items mit Tier, Roll-Range und Mod-Level wie im Simulator; KEINE Notes/Annahmen — Mario will die im Export nicht) → `downloadFile` in site.js

### Currency Exchange / Market-Seite (Mario, 15.09.2026)
- Quelle: GGGs öffentliche API `https://web.poecdn.com/api/currency-exchange/poe2/<unix-stunde>` (kein Login): pro abgeschlossener Stunde alle Paare aller Ligen (volume_traded, highest_stock, lowest/highest_ratio). Laufende Stunde → 404 mit `next_change_id` = angefragte Stunde (`ExchangeDigest.IsComplete`). KEIN Live-Orderbook
- Ratios: `{A: x, B: y}` = x A für y B; lowest/highest vergleichen "A pro B" (verifiziert an Chaos|Divine 9:1 / 10:1)
- Items per Metadata-Id: `data/exchange_items.json` (`python tools/poe2_exchange_items.py`: IDs aus den letzten Digests → RePoE-fork base_items.json → Name/Klasse/Icon, Icons lokal; bestehende Einträge bleiben) → `GameData.ExchangeItems`, `ExchangeCatalog` (Name, Icon, Kategorie, `IsCraftingItem` = `FindCraftItem` kennt es; unbekannte IDs → Name aus der ID)
- `MarketDataService`: beim Start fehlende Stunden der letzten `Market:HistoryHours` (24) laden, als kompakte `HourlyMarket`-Listen in `market-cache/{hour}.json` (neben data/, config `MarketCacheFolder`, gitignored), dann alle `PollMinutes` (5) prüfen; "Check now" weckt den Loop; 429 → Fehlertext, nächster Poll
- Preise (`LeagueMarket`, ANNAHMEN, Settings in appsettings "Market"): Average = volumengewichtet über die letzten `RecentHours` (3) Stunden MIT Handel des Paars; Sell ≈ niedrigste Fills, Buy ≈ höchste Fills (je Stunde, volumengewichtet); Fills weiter als ×`OutlierFactor` (1.5) vom Stundenschnitt = Fehlorder → zählen als Schnitt
- Routen: direkt + über Divine/Exalted/Chaos (`ExchangeQuote.Through` multipliziert Sell×Sell/Buy×Buy); Marktpreis = Route mit dem meisten Volumen, `BetterSellRoute`/`BetterBuyRoute` ab 1 % Vorteil (z. B. Divines über Exalts kaufen)
- Row: Stunden-Serie (Sparkline), Change = Schnitt letzte vs. erste `RecentHours` Stunden, Volumen (alle Paare), Stock (letzte Stunde)
- UI: Liga-Auswahl (Standard = meiste Paare), Referenz Divine/Exalted/Chaos, Core-Rate-Karten (`MarketPairCard`), Trade calculator (`MarketConverter`, Routen-Tabelle), Tabelle (`MarketTable`: Kategorien, Suche, "Crafting items only" Standard an, sortierbar, ⇄ → Rechner); Seite abonniert `MarketDataService.Changed`
- Offen: Preise im Planner (Kosten pro Pfad), Gold-Gebühren

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
- Verifizieren: Quality pro Nutzung je Rarity, Katalysator-Menge/Typwechsel, Catalysing-Exaltation-Stärke
- Expedition-/Crest-Mods (Mechanik unklar)
- Abgleich mobalytics (Perra, 06/2026) + forgeofexiles.com (Wiki Stand 0.4.x, teils "unverified"/PoE1-Annahmen) am 13.09.2026 — FEHLT noch:
  - Recombinator (+ Omen of Recombination), Reforging Bench (3→1, Essenz-Upgrade), Salvage
  - Vaal Cultivation Orb / Crystallised Corruption / Core Destabiliser / Ancient Infuser / Double Corruption (Daten da, op null)
  - Currency-Preise (poe.ninja) + Kosten-Schätzung pro Pfad; Omen of the Ancients/Unique-Ergebnisse; Sanctified-Effekt
  - Planner: Plan speichern/teilen; Hinekora's Lock in Wahrscheinlichkeiten einrechnen (derzeit nur Hinweis); Instill-Notables mit Apostroph heißen im Datensatz ohne (Slug "Deserts Scorn")
  - Abweichung: forgeofexiles nennt Greater Transmute/Augment min. Mod-Level 55, poe2db-Daten 44 (wir nutzen poe2db); forgeofexiles sagt "nur 1 Omen aktiv" (Stand 0.4) — Mario spielt mit mehreren aktiven Omens
- Planner: Currency-Kosten berücksichtigen (empfiehlt aktuell z. B. Perfect-Orbs rein nach Wahrscheinlichkeit)
- Essenzen: ob Mod-Level > Item-Level blockiert, ist unverifiziert (derzeit nur Hinweis)
- Weitere Details: siehe Claude-Projekt "POE 2" → poe2-crafting/STATUS.md und KNOWLEDGE_BASE.md
