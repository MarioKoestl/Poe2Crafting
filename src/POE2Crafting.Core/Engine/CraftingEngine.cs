using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>
/// Applies crafting currencies (with omens) to items. Covers: Transmutation, Augmentation, Regal, Exalted, Chaos,
/// Alchemy, Annulment, Divine (incl. Greater/Perfect variants and their omens), Orb of Chance, Fracturing Orb, Mirror,
/// Scroll of Wisdom, Hinekora's Lock, Essences (Lesser/Normal/Greater/Perfect/Corrupted) and Alloys.
/// Unknown rules are taken from SimConfig.Assumptions and surfaced as notes.
/// </summary>
public sealed class CraftingEngine
{
    private readonly GameData _data;
    private readonly ModPool _pool;
    public SimAssumptions A => _data.Config.Assumptions;

    public CraftingEngine(GameData data, ModPool? pool = null)
    {
        _data = data;
        _pool = pool ?? new ModPool(data);
    }

    public ModPool Pool => _pool;

    public int MaxPrefixes(Rarity r) => A.MaxPrefixes(r);
    public int MaxSuffixes(Rarity r) => A.MaxSuffixes(r);

    // ------------------------------------------------------------------ applicability

    public Applicability Check(Item item, CraftAction action)
    {
        var c = action.Currency;
        var omen = action.Omen;
        if (c.Op == null) return Applicability.No("This currency is not simulated (yet).");
        if (item.Base == null) return Applicability.No("Unknown base item.");
        if (item.Mirrored) return Applicability.No("Mirrored items cannot be modified.");
        if (omen != null && omen.TargetCurrency != null && !CurrencyMatchesOmen(c, omen))
            return Applicability.No($"{omen.Name} does not affect {c.Name}.");
        if (item.Corrupted && c.Op is not ("sacrifice" or "architect" or "identify"))
            return Applicability.No("Corrupted items cannot be modified with this currency.");
        if (c.RarityIn is { Count: > 0 } && !c.RarityIn.Contains(item.Rarity.ToString()))
            return Applicability.No($"{c.Name} requires a {string.Join(" or ", c.RarityIn)} item (item is {item.Rarity}).");
        if (c.MaxItemLevel is { } maxIlvl && item.ItemLevel > maxIlvl)
            return Applicability.No($"{c.Name} only works on items up to item level {maxIlvl}.");
        if (!item.Identified && c.Op != "identify") return Applicability.No("Item must be identified first.");

        var notes = new List<string>();
        switch (c.Op)
        {
            case "transmute":
                if (item.AffixCount != 0) return Applicability.No("Normal item already has modifiers?");
                break;
            case "augment":
                if (FreeSlots(item, Rarity.Magic, omen) == 0) return NoSlot(omen);
                if (NoCandidates(item, c, omen, Rarity.Magic)) return Applicability.No("No modifier can roll (item level too low or pool exhausted).");
                break;
            case "regal":
                if (FreeSlots(item, Rarity.Rare, omen) == 0) return NoSlot(omen);
                if (NoCandidates(item, c, omen, Rarity.Rare)) return Applicability.No("No modifier can roll (item level too low or pool exhausted).");
                break;
            case "exalt":
                {
                    int need = omen?.Effect == "add_two" ? 2 : 1;
                    if (FreeSlots(item, Rarity.Rare, omen) < need) return NoSlot(omen, need);
                    if (NoCandidates(item, c, omen, Rarity.Rare)) return Applicability.No("No modifier can roll (item level too low, pool exhausted or omen restriction).");
                    if (omen?.Effect == "catalysing") notes.Add("Catalysing Exaltation: catalyst quality bias is not simulated yet (treated as a normal Exalted Orb).");
                    break;
                }
            case "chaos":
                if (Removable(item, omen).Count == 0) return Applicability.No("No modifier can be removed (all fractured or omen restriction).");
                break;
            case "annul":
                {
                    int need = omen?.Effect == "remove_two" ? 2 : 1;
                    if (Removable(item, omen).Count < need) return Applicability.No("Not enough removable modifiers (fractured mods cannot be removed).");
                    break;
                }
            case "alchemy":
                if (item.Rarity == Rarity.Magic)
                    notes.Add(A.AlchemyOnMagicKeepsExistingMods
                        ? "Assumption: Orb of Alchemy on a Magic item keeps its mods and fills up to 4 (config: alchemyOnMagicKeepsExistingMods)."
                        : "Assumption: existing magic mods are rerolled (config: alchemyOnMagicKeepsExistingMods).");
                break;
            case "divine":
                if (!item.Affixes.Any(m => m.Def != null && m.Def.Ranges.Count > 0) && omen?.Effect != "implicits_only")
                    return Applicability.No("No modifier with a value range to reroll.");
                if (omen?.Effect == "sanctify") notes.Add("Sanctify: the item is marked Sanctified (cannot be desecrated); further effects are UNVERIFIED.");
                if (omen?.Effect == "implicits_only") notes.Add("Implicit values are not tracked numerically; the omen only prevents explicit rerolls.");
                break;
            case "fracture":
                if (item.AffixCount < (c.MinMods ?? 4)) return Applicability.No($"Fracturing Orb needs at least {c.MinMods ?? 4} modifiers.");
                if (item.Affixes.All(m => m.Fractured)) return Applicability.No("All modifiers are already fractured.");
                if (item.Affixes.Any(m => m.Fractured)) notes.Add("Assumption: an item can hold more than one fractured modifier (UNVERIFIED).");
                break;
            case "chance":
                notes.Add("Assumption: destroy vs. unique outcome uses config probabilities; the resulting unique is not modelled (item becomes a placeholder unique).");
                break;
            case "mirror":
                if (item.Rarity == Rarity.Unique) return Applicability.No("Uniques cannot be mirrored.");
                break;
            case "identify":
                if (item.Identified) return Applicability.No("Item is already identified.");
                break;
            case "lock":
                if (item.Foreseeing) return Applicability.No("Item already foresees its next result.");
                break;
            case "essence":
                {
                    var essenceCheck = CheckEssence(item, c.Essence, omen, notes);
                    if (essenceCheck != null) return essenceCheck;
                    break;
                }
            default:
                return Applicability.No($"{c.Name} ({c.Op}) is planned for a later stage.");
        }
        if (c.MinModLevel is { } mml) notes.Add($"Minimum Modifier Level {mml}: only modifier tiers with level >= {mml} can be added.");
        return new Applicability { Ok = true, Notes = notes };
    }

    private Applicability? CheckEssence(Item item, EssenceDef? essence, OmenDef? omen, List<string> notes)
    {
        if (essence == null) return Applicability.No("Essence data missing.");
        var mod = _data.EssenceModFor(essence, item.Base, item.ItemClass);
        if (mod == null) return Applicability.No($"{essence.Name} has no effect on {item.ItemClass}.");
        if (mod.Level > item.ItemLevel)
            notes.Add($"The guaranteed modifier has level {mod.Level} but the item level is {item.ItemLevel}; whether the game blocks this is UNVERIFIED (applied anyway).");

        if (!essence.RemovesRandomModifier)
        {
            if (item.HasFamily(mod.Family)) return Applicability.No($"The item already has a modifier of the same family as \"{mod.Text}\".");
            if (item.CountOf(mod.AffixType) >= A.MaxAffixes(Rarity.Rare, mod.AffixType)) return Applicability.No($"No free {mod.AffixType.ToString().ToLower()} slot.");
            return null;
        }

        if (A.OnlyOneCraftedModPerItem && item.Affixes.Any(m => m.Kind == ModKind.Crafted))
            return Applicability.No("The item already has a crafted modifier (config: onlyOneCraftedModPerItem).");
        if (EssenceRemovals(item, mod, omen).Count == 0)
            return Applicability.No($"No removable modifier would make room for the {mod.AffixType.ToString().ToLower()} \"{mod.Text}\" (fractured mods or omen restriction).");
        if (item.HasFamily(mod.Family))
            notes.Add("Assumption: the existing modifier of the same family is the one that gets replaced (UNVERIFIED).");
        else if (item.CountOf(mod.AffixType) >= A.MaxAffixes(Rarity.Rare, mod.AffixType))
            notes.Add($"Assumption: {mod.AffixType.ToString().ToLower()}es are full, so only a {mod.AffixType.ToString().ToLower()} can be removed (UNVERIFIED).");
        return null;
    }

    private static bool CurrencyMatchesOmen(CurrencyDef c, OmenDef omen) => omen.TargetCurrency switch
    {
        "Chaos Orb" => c.Op == "chaos",
        "Exalted Orb" => c.Op == "exalt",
        "Regal Orb" => c.Op == "regal",
        "Orb of Alchemy" => c.Op == "alchemy",
        "Orb of Annulment" => c.Op == "annul",
        "Divine Orb" => c.Op == "divine",
        "Orb of Chance" => c.Op == "chance",
        "Vaal Orb" => c.Op == "vaal",
        // "next Perfect or Corrupted Essence"
        "Essence" => c.Op == "essence" && c.Essence?.Tier is "Perfect" or "Corrupted",
        "Desecration" => c.Op == "desecrate",
        _ => false,
    };

    private static Applicability NoSlot(OmenDef? omen, int need = 1)
    {
        var restricted = RestrictedType(omen);
        if (restricted != null) return Applicability.No($"No free {restricted.Value.ToString().ToLower()} slot for {omen!.Name}.");
        return Applicability.No(need > 1 ? $"Needs {need} free modifier slots." : "No free modifier slot (prefixes and suffixes are full).");
    }

    private bool NoCandidates(Item item, CurrencyDef c, OmenDef? omen, Rarity targetRarity)
    {
        var (pre, suf) = AdditionCandidates(item, c.MinModLevel ?? 0, omen, targetRarity);
        return pre.Count + suf.Count == 0;
    }

    // ------------------------------------------------------------------ helpers

    private static AffixType? RestrictedType(OmenDef? omen) => omen?.Effect switch
    {
        "add_prefix_only" or "remove_prefix_only" or "max_prefixes" => AffixType.Prefix,
        "add_suffix_only" or "remove_suffix_only" or "max_suffixes" => AffixType.Suffix,
        _ => null,
    };

    /// <summary>Free affix slots for the target rarity, honouring a prefix/suffix restriction.</summary>
    private int FreeSlots(Item item, Rarity target, OmenDef? omen)
    {
        int freeP = Math.Max(0, MaxPrefixes(target) - item.PrefixCount);
        int freeS = Math.Max(0, MaxSuffixes(target) - item.SuffixCount);
        return RestrictedType(omen) switch { AffixType.Prefix => freeP, AffixType.Suffix => freeS, _ => freeP + freeS };
    }

    private static List<RemovalCandidate> Removable(Item item, OmenDef? omen)
    {
        var restricted = RestrictedType(omen);
        var list = new List<RemovalCandidate>();
        for (int i = 0; i < item.Mods.Count; i++)
        {
            var m = item.Mods[i];
            if (!m.IsAffix || m.Fractured) continue;
            if (restricted != null && m.Affix != restricted) continue;
            if (omen?.Effect == "remove_desecrated_only" && m.Kind != ModKind.Desecrated) continue;
            list.Add(new RemovalCandidate { Index = i, Mod = m });
        }
        if (omen?.Effect == "remove_lowest_level" && list.Count > 0)
        {
            int lowest = list.Min(r => r.Mod.Def?.Level ?? int.MaxValue);
            list = list.Where(r => (r.Mod.Def?.Level ?? int.MaxValue) == lowest).ToList();
        }
        foreach (var r in list) r.Probability = 1.0 / list.Count;
        return list;
    }

    /// <summary>
    /// Mods a Perfect/Corrupted essence or alloy may remove so that its guaranteed mod fits:
    /// a mod of the same family is replaced; if that affix type is full, only that type can go.
    /// </summary>
    private List<RemovalCandidate> EssenceRemovals(Item item, ModDef mod, OmenDef? omen)
    {
        var list = Removable(item, omen);
        if (item.HasFamily(mod.Family))
            list = list.Where(r => r.Mod.Def?.Family == mod.Family).ToList();
        else if (item.CountOf(mod.AffixType) >= A.MaxAffixes(Rarity.Rare, mod.AffixType))
            list = list.Where(r => r.Mod.Affix == mod.AffixType).ToList();
        foreach (var r in list) r.Probability = 1.0 / list.Count;
        return list;
    }

    /// <summary>Prefix and suffix candidate lists for adding one mod (before choosing the affix type).</summary>
    private (List<ModCandidate> prefixes, List<ModCandidate> suffixes) AdditionCandidates(Item item, int minLevel, OmenDef? omen, Rarity targetRarity)
    {
        Func<ModDef, bool>? filter = null;
        if (omen?.Effect == "homogenising")
        {
            var existingTags = item.Affixes.Where(m => m.Def != null).SelectMany(m => m.Def!.ModTags).ToHashSet();
            filter = existingTags.Count == 0 ? null : (mod => mod.ModTags.Any(existingTags.Contains));
        }
        var restricted = RestrictedType(omen);
        bool canP = item.PrefixCount < MaxPrefixes(targetRarity) && restricted != AffixType.Suffix;
        bool canS = item.SuffixCount < MaxSuffixes(targetRarity) && restricted != AffixType.Prefix;
        var pre = canP ? _pool.Candidates(item, AffixType.Prefix, minLevel, filter) : new List<ModCandidate>();
        var suf = canS ? _pool.Candidates(item, AffixType.Suffix, minLevel, filter) : new List<ModCandidate>();
        return (pre, suf);
    }

    /// <summary>Combined distribution over prefixes and suffixes according to the affix-type selection rule.</summary>
    private List<ModCandidate> Combined(List<ModCandidate> pre, List<ModCandidate> suf, out double pPrefix)
    {
        double wp = pre.Sum(x => (double)x.Weight), ws = suf.Sum(x => (double)x.Weight);
        if (pre.Count == 0 && suf.Count == 0) { pPrefix = 0; return new List<ModCandidate>(); }
        if (A.AffixTypeSelection == "equal" && pre.Count > 0 && suf.Count > 0) pPrefix = 0.5;
        else pPrefix = wp + ws > 0 ? wp / (wp + ws) : 0;
        var list = new List<ModCandidate>();
        foreach (var x in pre) list.Add(new ModCandidate { Mod = x.Mod, Weight = x.Weight, Probability = wp > 0 ? pPrefix * x.Weight / wp : 0 });
        foreach (var x in suf) list.Add(new ModCandidate { Mod = x.Mod, Weight = x.Weight, Probability = ws > 0 ? (1 - pPrefix) * x.Weight / ws : 0 });
        return list.OrderByDescending(x => x.Probability).ToList();
    }

    /// <summary>
    /// Distribution of one randomly added mod on <paramref name="item"/> as if the item had <paramref name="targetRarity"/>
    /// (slot limits of that rarity apply). Used by the planner so it shares the engine's affix-selection rule.
    /// </summary>
    public List<ModCandidate> AdditionDistribution(Item item, Rarity targetRarity, int minModLevel = 0, AffixType? onlyType = null)
    {
        var (pre, suf) = AdditionCandidates(item, minModLevel, null, targetRarity);
        if (onlyType == AffixType.Prefix) suf.Clear();
        if (onlyType == AffixType.Suffix) pre.Clear();
        return Combined(pre, suf, out _);
    }

    // ------------------------------------------------------------------ preview

    /// <param name="forcedRemovalIndex">For two-step manual choices (Chaos, Perfect Essence): show additions as they would be after removing this mod.</param>
    public StepPreview Preview(Item item, CraftAction action, int? forcedRemovalIndex = null)
    {
        var app = Check(item, action);
        var notes = new List<string>(app.Notes) { $"Weights: {A.WeightsSource}." };
        if (!app.Ok) return new StepPreview { Applicability = app, Notes = notes };
        var c = action.Currency; var omen = action.Omen;
        switch (c.Op)
        {
            case "transmute":
            case "augment":
            case "regal":
            case "exalt":
                {
                    var target = c.Op is "transmute" or "augment" ? Rarity.Magic : Rarity.Rare;
                    var (pre, suf) = AdditionCandidates(item, c.MinModLevel ?? 0, omen, target);
                    var combined = Combined(pre, suf, out var pP);
                    if (A.AffixTypeSelection == "weighted") notes.Add("Assumption: prefix vs. suffix is chosen proportionally to the total weight of each pool (config: affixTypeSelection).");
                    return new StepPreview { Applicability = app, AddCount = omen?.Effect == "add_two" ? 2 : 1, Additions = combined, PrefixProbability = pP, SuffixProbability = 1 - pP, Notes = notes };
                }
            case "alchemy":
                {
                    var (pre, suf) = AdditionCandidates(item, c.MinModLevel ?? 0, omen, Rarity.Rare);
                    var combined = Combined(pre, suf, out var pP);
                    notes.Add($"Orb of Alchemy adds {A.AlchemyModCount} modifiers one after another; the distribution shown is for the first modifier.");
                    if (omen?.Effect is "max_prefixes" or "max_suffixes") notes.Add($"{omen.Name}: result will have 3 {(omen.Effect == "max_prefixes" ? "prefixes" : "suffixes")}.");
                    return new StepPreview { Applicability = app, AddCount = A.AlchemyModCount, Additions = combined, PrefixProbability = pP, SuffixProbability = 1 - pP, Notes = notes };
                }
            case "chaos":
                {
                    var removals = Removable(item, omen);
                    var considered = forcedRemovalIndex is { } fi ? removals.Where(r => r.Index == fi).ToList() : removals;
                    // marginal distribution of the added mod over the (considered) removal outcomes
                    var acc = new Dictionary<string, ModCandidate>();
                    double pPsum = 0, weightSum = considered.Sum(r => r.Probability);
                    foreach (var r in considered)
                    {
                        var after = item.Clone(); after.Mods.RemoveAt(r.Index);
                        var (pre, suf) = AdditionCandidates(after, c.MinModLevel ?? 0, null, Rarity.Rare);
                        var combined = Combined(pre, suf, out var pP);
                        double pr = weightSum > 0 ? r.Probability / weightSum : 0;
                        pPsum += pP * pr;
                        foreach (var x in combined)
                        {
                            if (!acc.TryGetValue(x.Mod.Id, out var e)) acc[x.Mod.Id] = e = new ModCandidate { Mod = x.Mod, Weight = x.Weight, Probability = 0 };
                            e.Probability += x.Probability * pr;
                        }
                    }
                    if (omen?.Effect == "remove_lowest_level") notes.Add("Omen of Whittling: removes the modifier with the lowest modifier level (assumption: ties are broken randomly).");
                    return new StepPreview { Applicability = app, RemoveCount = 1, AddCount = 1, Removals = removals, Additions = acc.Values.OrderByDescending(x => x.Probability).ToList(), PrefixProbability = pPsum, SuffixProbability = 1 - pPsum, Notes = notes, TwoStepChoice = true };
                }
            case "annul":
                return new StepPreview { Applicability = app, RemoveCount = omen?.Effect == "remove_two" ? 2 : 1, Removals = Removable(item, omen), Notes = notes };
            case "divine":
                notes.Add("Every modifier value is rerolled uniformly inside its tier range.");
                return new StepPreview { Applicability = app, Notes = notes };
            case "chance":
                {
                    double pUnique = 0.05, pDestroy = 0.95;
                    if (omen?.Effect == "no_destroy") { pDestroy = 0; notes.Add("Omen of Chance: the item is never destroyed; on failure it stays unchanged."); }
                    if (omen?.Effect == "random_unique_of_class") notes.Add("Omen of the Ancients: result is a random unique of the item class.");
                    notes.Add("Assumption: 5% unique chance (UNVERIFIED).");
                    var outcomes = new Dictionary<string, double> { ["Upgrade to Unique"] = pUnique };
                    if (pDestroy > 0) outcomes["Item destroyed"] = pDestroy; else outcomes["No change"] = 1 - pUnique;
                    return new StepPreview { Applicability = app, SpecialOutcomes = outcomes, Notes = notes };
                }
            case "fracture":
                {
                    var cands = item.Mods.Select((m, i) => (m, i)).Where(t => t.m.IsAffix && !t.m.Fractured)
                        .Select(t => new RemovalCandidate { Index = t.i, Mod = t.m }).ToList();
                    foreach (var r in cands) r.Probability = 1.0 / cands.Count;
                    notes.Add("One random (non-fractured) modifier becomes fractured and can no longer be removed or changed.");
                    return new StepPreview { Applicability = app, Removals = cands, Notes = notes, RemovalLabel = "Fracture Target" };
                }
            case "essence":
                {
                    var essence = c.Essence!;
                    var mod = _data.EssenceModFor(essence, item.Base, item.ItemClass)!;
                    var removals = essence.RemovesRandomModifier ? EssenceRemovals(item, mod, omen) : new List<RemovalCandidate>();
                    notes.Add(essence.AddsCraftedMod
                        ? "The guaranteed modifier is added as a Crafted modifier (shown as \"Crafted\" in the item text)."
                        : "The item becomes Rare and gains the guaranteed modifier; its value is rolled inside the essence's range.");
                    return new StepPreview
                    {
                        Applicability = app,
                        RemoveCount = removals.Count > 0 ? 1 : 0,
                        AddCount = 1,
                        Removals = removals,
                        Additions = new() { new ModCandidate { Mod = mod, Weight = 1, Probability = 1 } },
                        PrefixProbability = mod.IsPrefix ? 1 : 0,
                        SuffixProbability = mod.IsSuffix ? 1 : 0,
                        Notes = notes,
                    };
                }
            default:
                return new StepPreview { Applicability = app, Notes = notes };
        }
    }

    // ------------------------------------------------------------------ execute

    public CraftResult Execute(Item item, CraftAction action, Rng rng, ManualChoice? choice = null)
    {
        var app = Check(item, action);
        if (!app.Ok) return new CraftResult { Applied = false, Item = item, Summary = app.Reason };
        var c = action.Currency; var omen = action.Omen;
        var result = item.Clone();
        var details = new List<string>();
        result.Foreseeing = false;
        switch (c.Op)
        {
            case "transmute":
                result.Rarity = Rarity.Magic;
                AddMods(result, c, omen, 1, rng, choice, details);
                break;
            case "augment":
                AddMods(result, c, omen, 1, rng, choice, details);
                break;
            case "regal":
                result.Rarity = Rarity.Rare;
                result.Name ??= RareName(rng);
                AddMods(result, c, omen, 1, rng, choice, details);
                break;
            case "exalt":
                AddMods(result, c, omen, omen?.Effect == "add_two" ? 2 : 1, rng, choice, details);
                break;
            case "alchemy":
                {
                    if (result.Rarity == Rarity.Magic && !A.AlchemyOnMagicKeepsExistingMods) result.Mods.RemoveAll(m => m.IsAffix);
                    result.Rarity = Rarity.Rare;
                    result.Name ??= RareName(rng);
                    int toAdd = Math.Max(0, A.AlchemyModCount - result.AffixCount);
                    // Sinistral/Dextral Alchemy: force the maximum of one type first
                    if (omen?.Effect is "max_prefixes" or "max_suffixes")
                    {
                        var forced = omen.Effect == "max_prefixes" ? AffixType.Prefix : AffixType.Suffix;
                        int forcedCount = Math.Min(A.MaxAffixes(Rarity.Rare, forced) - result.CountOf(forced), toAdd);
                        for (int i = 0; i < forcedCount; i++) AddOne(result, c, forced, rng, ChoiceAt(choice, i), details, null);
                        for (int i = 0; i < toAdd - forcedCount; i++) AddOne(result, c, null, rng, ChoiceAt(choice, forcedCount + i), details, null);
                    }
                    else AddMods(result, c, null, toAdd, rng, choice, details);
                    break;
                }
            case "chaos":
                {
                    int? removeIdx = choice?.RemoveIndices.Count > 0 ? choice.RemoveIndices[0] : null;
                    // "choose outcome" with only the added mod picked: remove a random mod that leaves room for it
                    if (removeIdx == null && choice?.AddModIds.Count > 0)
                        removeIdx = PickRemovalCompatibleWith(result, omen, c, choice.AddModIds[0], rng);
                    RemoveMods(result, omen, 1, rng, removeIdx is { } ri ? new List<int> { ri } : null, details);
                    AddMods(result, c, null, 1, rng, choice, details);
                    break;
                }
            case "annul":
                RemoveMods(result, omen, omen?.Effect == "remove_two" ? 2 : 1, rng, choice?.RemoveIndices, details);
                if (result.AffixCount == 0 && result.Rarity != Rarity.Normal) details.Add("Item has no modifiers left (rarity unchanged).");
                break;
            case "divine":
                if (omen?.Effect != "implicits_only")
                {
                    for (int i = 0; i < result.Mods.Count; i++)
                    {
                        var m = result.Mods[i];
                        if (!m.IsAffix || m.Def == null || m.Def.Ranges.Count == 0) continue;
                        var before = m.DisplayText();
                        if (choice?.Rerolls != null && choice.Rerolls.TryGetValue(i, out var vals)) m.Values = vals.ToList();
                        else m.Values = RollValues(m.Def, rng);
                        details.Add($"{before}  ->  {m.DisplayText()}");
                    }
                }
                else details.Add("Implicit modifier values rerolled (not tracked numerically).");
                if (omen?.Effect == "sanctify") { result.Sanctified = true; details.Add("Item is now Sanctified."); }
                break;
            case "chance":
                {
                    var preview = Preview(item, action);
                    var keys = preview.SpecialOutcomes.Keys.ToList();
                    string outcome = choice?.SpecialOutcome ?? keys[rng.PickWeighted(keys.Select(k => preview.SpecialOutcomes[k]).ToList())];
                    if (outcome == "Item destroyed") return new CraftResult { Applied = true, Item = result, Destroyed = true, Summary = $"{c.Name}: item destroyed", Details = new() { "The Orb of Chance destroyed the item." } };
                    if (outcome == "Upgrade to Unique") { result.Rarity = Rarity.Unique; result.Name = "(random Unique)"; details.Add("Upgraded to a Unique (placeholder, unique mods are not modelled)."); }
                    else details.Add("No change.");
                    break;
                }
            case "fracture":
                {
                    var cands = result.Mods.Select((m, i) => (m, i)).Where(t => t.m.IsAffix && !t.m.Fractured).ToList();
                    var picked = choice?.RemoveIndices.Count > 0 ? cands.FirstOrDefault(t => t.i == choice.RemoveIndices[0]) : cands[rng.Next(cands.Count)];
                    if (picked.m == null) throw new InvalidOperationException("Chosen modifier cannot be fractured.");
                    picked.m.Fractured = true;
                    details.Add($"Fractured: {picked.m.DisplayText()}");
                    break;
                }
            case "mirror":
                result.Mirrored = true; details.Add("Item is now Mirrored (this is the copy; both copies are locked).");
                break;
            case "identify":
                result.Identified = true; details.Add("Item identified.");
                break;
            case "lock":
                result.Foreseeing = true; details.Add("The item now foresees its next currency result (use Preview, then decide).");
                break;
            case "essence":
                ApplyEssence(result, c.Essence!, omen, rng, choice, details);
                break;
        }
        return new CraftResult { Applied = true, Item = result, Summary = Summarise(action, details), Details = details };
    }

    private void ApplyEssence(Item item, EssenceDef essence, OmenDef? omen, Rng rng, ManualChoice? choice, List<string> details)
    {
        var mod = _data.EssenceModFor(essence, item.Base, item.ItemClass)!;
        if (essence.RemovesRandomModifier)
        {
            var cands = EssenceRemovals(item, mod, omen);
            var pick = choice?.RemoveIndices.Count > 0
                ? cands.FirstOrDefault(x => x.Index == choice.RemoveIndices[0]) ?? throw new InvalidOperationException("Chosen modifier cannot be removed by this essence.")
                : cands[rng.PickWeighted(cands.Select(x => x.Probability).ToList())];
            details.Add($"Removed {Describe(pick.Mod, item)}");
            item.Mods.RemoveAt(pick.Index);
        }
        item.Rarity = Rarity.Rare;
        item.Name ??= RareName(rng);
        var values = choice?.Values.Count > 0 ? choice.Values[0] : null;
        var im = new ItemMod
        {
            ModId = mod.Id, Def = mod, Affix = mod.AffixType,
            Kind = essence.AddsCraftedMod ? ModKind.Crafted : ModKind.Explicit,
            Values = values ?? RollValues(mod, rng),
            SourceName = essence.Name,
        };
        item.Mods.Add(im);
        details.Add($"Added {(essence.AddsCraftedMod ? "crafted " : "")}{mod.AffixType.ToString().ToLower()} from {essence.Name}: {im.DisplayText()}");
    }

    private int? PickRemovalCompatibleWith(Item item, OmenDef? omen, CurrencyDef c, string modId, Rng rng)
    {
        var compatible = Removable(item, omen).Where(r =>
        {
            var after = item.Clone(); after.Mods.RemoveAt(r.Index);
            var (pre, suf) = AdditionCandidates(after, c.MinModLevel ?? 0, null, Rarity.Rare);
            return pre.Concat(suf).Any(x => x.Mod.Id == modId);
        }).ToList();
        if (compatible.Count == 0) throw new InvalidOperationException("The chosen modifier cannot be added after any possible removal.");
        return compatible[rng.Next(compatible.Count)].Index;
    }

    private static ManualChoice? ChoiceAt(ManualChoice? choice, int i)
    {
        if (choice == null || i >= choice.AddModIds.Count) return null;
        return new ManualChoice { AddModIds = new() { choice.AddModIds[i] }, Values = new() { i < choice.Values.Count ? choice.Values[i] : null } };
    }

    private void AddMods(Item item, CurrencyDef c, OmenDef? omen, int count, Rng rng, ManualChoice? choice, List<string> details)
    {
        for (int i = 0; i < count; i++)
            if (!AddOne(item, c, RestrictedType(omen), rng, ChoiceAt(choice, i), details, omen)) { details.Add("No modifier could be added."); break; }
    }

    /// <summary>Add one mod (random or chosen). Returns false when nothing could be added.</summary>
    private bool AddOne(Item item, CurrencyDef c, AffixType? forcedType, Rng rng, ManualChoice? choice, List<string> details, OmenDef? omen)
    {
        var target = item.Rarity == Rarity.Normal ? Rarity.Magic : item.Rarity;
        var (pre, suf) = AdditionCandidates(item, c.MinModLevel ?? 0, omen, target);
        if (forcedType == AffixType.Prefix) suf.Clear();
        if (forcedType == AffixType.Suffix) pre.Clear();
        var combined = Combined(pre, suf, out _);
        if (combined.Count == 0) return false;
        ModDef mod;
        List<double>? values = null;
        if (choice != null && choice.AddModIds.Count > 0)
        {
            var wanted = choice.AddModIds[0];
            var cand = combined.FirstOrDefault(x => x.Mod.Id == wanted) ?? throw new InvalidOperationException("The chosen modifier cannot roll on this item right now.");
            mod = cand.Mod;
            values = choice.Values.Count > 0 ? choice.Values[0] : null;
        }
        else mod = combined[rng.PickWeighted(combined.Select(x => x.Probability).ToList())].Mod;
        var im = new ItemMod { ModId = mod.Id, Def = mod, Affix = mod.AffixType, Kind = ModKind.Explicit, Values = values ?? RollValues(mod, rng) };
        item.Mods.Add(im);
        details.Add($"Added {Describe(im, item)}");
        return true;
    }

    private static void RemoveMods(Item item, OmenDef? omen, int count, Rng rng, IReadOnlyList<int>? chosenIndices, List<string> details)
    {
        // indices refer to the item before any removal; resolve them to mod instances first
        var chosen = chosenIndices?.Where(i => i >= 0 && i < item.Mods.Count).Select(i => item.Mods[i]).ToList();
        for (int i = 0; i < count; i++)
        {
            var cands = Removable(item, omen);
            if (cands.Count == 0) { details.Add("No modifier could be removed."); return; }
            RemovalCandidate pick;
            if (chosen != null && i < chosen.Count)
                pick = cands.FirstOrDefault(x => ReferenceEquals(x.Mod, chosen[i])) ?? throw new InvalidOperationException("The chosen modifier cannot be removed.");
            else pick = cands[rng.PickWeighted(cands.Select(x => x.Probability).ToList())];
            details.Add($"Removed {DescribeNoTier(pick.Mod)}");
            item.Mods.RemoveAt(pick.Index);
        }
    }

    private string Describe(ItemMod m, Item item)
    {
        var tier = m.Def != null && m.Kind == ModKind.Explicit && _pool.TryDisplayTier(m.Def, item) is { } t ? $" (T{t})" : "";
        return $"{m.Affix.ToString().ToLower()} \"{m.Def?.Name}\"{tier}: {m.DisplayText()}";
    }

    private static string DescribeNoTier(ItemMod m) => $"{m.Affix.ToString().ToLower()} \"{m.Def?.Name}\": {m.DisplayText()}";

    public static List<double> RollValues(ModDef mod, Rng rng) => mod.Ranges.Select(r => rng.RollRange(r[0], r[1])).ToList();

    private static string Summarise(CraftAction action, List<string> details) =>
        action.DisplayName + (details.Count > 0 ? ": " + string.Join("; ", details.Take(3)) + (details.Count > 3 ? " ..." : "") : "");

    static readonly string[] NamePrefixes = { "Dusk", "Grim", "Storm", "Soul", "Blood", "Vortex", "Ghoul", "Doom", "Rune", "Spirit", "Dread", "Sol", "Chimeric", "Corpse", "Empyrean", "Torment", "Glyph", "Phoenix", "Viper", "Havoc" };
    static readonly string[] NameSuffixes = { "Spire", "Song", "Bane", "Roar", "Whisper", "Call", "Grasp", "Beacon", "Weaver", "Cry", "Hold", "Bite", "Knell", "Ward", "Mark", "Blow", "Tear", "Brand", "Coil", "Spell" };
    public static string RareName(Rng rng) => $"{NamePrefixes[rng.Next(NamePrefixes.Length)]} {NameSuffixes[rng.Next(NameSuffixes.Length)]}";
}
