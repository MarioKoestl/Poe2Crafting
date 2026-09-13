using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine.Operations;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>
/// Applies crafting currencies (with omens) to items. Each currency operation (Data: CurrencyDef.Op) is a
/// <see cref="CraftOperation"/>; this class runs the generic checks and holds the building blocks all operations share
/// (candidate pools, adding/removing mods, rarity changes). Unknown rules come from SimConfig.Assumptions and are surfaced as notes.
/// </summary>
public sealed class CraftingEngine
{
    private readonly GameData _data;
    private readonly ModPool _pool;
    private readonly Dictionary<string, CraftOperation> _operations;
    private readonly DesecrateOperation _desecrate;

    public CraftingEngine(GameData data, ModPool? pool = null)
    {
        _data = data;
        _pool = pool ?? new ModPool(data);
        _desecrate = new DesecrateOperation(this);
        _operations = new CraftOperation[]
        {
            new AddModOperation(this, "transmute", Rarity.Magic),
            new AddModOperation(this, "augment", Rarity.Magic),
            new AddModOperation(this, "regal", Rarity.Rare),
            new AddModOperation(this, "exalt", Rarity.Rare),
            new AlchemyOperation(this),
            new ChaosOperation(this),
            new AnnulOperation(this),
            new DivineOperation(this),
            new ChanceOperation(this),
            new FractureOperation(this),
            new FlagOperation(this, "mirror"),
            new FlagOperation(this, "identify"),
            new FlagOperation(this, "lock"),
            new EssenceOperation(this),
            _desecrate,
            new VaalOperation(this),
            new SacrificeOperation(this),
            new ArchitectOperation(this),
            new QualityOperation(this, "quality", infuser: false),
            new QualityOperation(this, "vaal_quality", infuser: true),
            new CatalystOperation(this),
            new SocketOperation(this),
            new ExtractOperation(this),
            new FluxOperation(this),
        }.ToDictionary(o => o.Op);
    }

    public GameData Data => _data;
    public ModPool Pool => _pool;
    public SimAssumptions A => _data.Config.Assumptions;

    // ------------------------------------------------------------------ public API

    public Applicability Check(Item item, CraftAction action) => Check(NewContext(item, action));

    /// <param name="forcedRemovalIndex">For two-step manual choices (Chaos): show additions as they would be after removing this mod.</param>
    public StepPreview Preview(Item item, CraftAction action, int? forcedRemovalIndex = null)
    {
        var ctx = NewContext(item, action);
        var app = Check(ctx);
        var notes = new List<string>(app.Notes) { $"Weights: {A.WeightsSource}." };
        if (!app.Ok) return new StepPreview { Applicability = app, Notes = notes };
        var preview = _operations[action.Currency.Op!].Preview(ctx, forcedRemovalIndex);
        preview.Applicability = app;
        preview.Notes.InsertRange(0, notes);
        return preview;
    }

    public CraftResult Execute(Item item, CraftAction action, Rng rng, ManualChoice? choice = null)
    {
        var app = Check(item, action);
        if (!app.Ok) return new CraftResult { Applied = false, Item = item, Summary = app.Reason };
        var result = item.Clone();
        result.Foreseeing = false;
        var ctx = new ExecuteContext { Item = item, Currency = action.Currency, Omens = action.Omens, Result = result, Rng = rng, Choice = choice };
        _operations[action.Currency.Op!].Execute(ctx);
        var summary = action.DisplayName + (ctx.Details.Count > 0 ? ": " + string.Join("; ", ctx.Details.Take(3)) + (ctx.Details.Count > 3 ? " ..." : "") : "");
        return new CraftResult { Applied = true, Item = result, Destroyed = ctx.Destroyed, Summary = summary, Details = ctx.Details };
    }

    // ---- desecrated mods: reveal at the Well of Souls

    /// <summary>What the unrevealed mod at <paramref name="modIndex"/> can become, with probabilities.</summary>
    public List<ModCandidate> RevealPool(Item item, int modIndex) => _desecrate.RevealPool(item, modIndex);

    /// <summary>Roll the options offered at the Well of Souls (config: revealOptionCount).</summary>
    public List<ModDef> RollRevealOptions(Item item, int modIndex, Rng rng) => _desecrate.RollRevealOptions(item, modIndex, rng);

    /// <summary>Turn the unrevealed mod into the chosen desecrated mod.</summary>
    public CraftResult Reveal(Item item, int modIndex, string modId, Rng rng) => _desecrate.Reveal(item, modIndex, modId, rng);

    // ------------------------------------------------------------------ generic checks

    private static CraftContext NewContext(Item item, CraftAction action) => new() { Item = item, Currency = action.Currency, Omens = action.Omens };

    private Applicability Check(CraftContext ctx)
    {
        var (item, c) = (ctx.Item, ctx.Currency);
        if (c.Op == null) return Applicability.No("This currency is not simulated (yet).");
        if (!_operations.TryGetValue(c.Op, out var op)) return Applicability.No($"{c.Name} ({c.Op}) is planned for a later stage.");
        if (item.Base == null) return Applicability.No("Unknown base item.");
        if (item.Mirrored) return Applicability.No("Mirrored items cannot be modified.");
        if (ctx.Omens.FirstOrDefault(o => o.TargetCurrency != null && !op.AcceptsOmen(ctx, o)) is { } unrelated)
            return Applicability.No($"{unrelated.Name} does not affect {c.Name}.");
        if (OmenEffects.Conflict(ctx.Omens) is { } conflict) return Applicability.No(conflict);
        if (item.Corrupted && !op.WorksOnCorrupted && !op.RequiresCorrupted) return Applicability.No("Corrupted items cannot be modified with this currency.");
        if (!item.Corrupted && op.RequiresCorrupted) return Applicability.No($"{c.Name} can only be used on Corrupted items.");
        if (c.RarityIn is { Count: > 0 } && !c.RarityIn.Contains(item.Rarity.ToString()))
            return Applicability.No($"{c.Name} requires a {string.Join(" or ", c.RarityIn)} item (item is {item.Rarity}).");
        var classTarget = c.ClassTarget ?? op.DefaultClassTarget;
        if (!_data.ClassMatchesTarget(item.ItemClass, classTarget))
            return Applicability.No($"{c.Name} can only be used on {GameData.TargetDisplayName(classTarget)}.");
        if (c.MaxItemLevel is { } maxIlvl && item.ItemLevel > maxIlvl)
            return Applicability.No($"{c.Name} only works on items up to item level {maxIlvl}.");
        if (!item.Identified && c.Op != "identify") return Applicability.No("Item must be identified first.");

        if (op.Check(ctx) is { } refusal) return refusal;
        if (c.MinModLevel is { } mml) ctx.Notes.Add($"Minimum Modifier Level {mml}: only modifier tiers with level >= {mml} can be added.");
        return new Applicability { Ok = true, Notes = ctx.Notes };
    }

    /// <summary>Default omen routing: the omen's target currency name maps to this operation's id.</summary>
    internal static string? OpOfOmenTarget(string? targetCurrency) => targetCurrency switch
    {
        "Chaos Orb" => "chaos",
        "Exalted Orb" => "exalt",
        "Regal Orb" => "regal",
        "Orb of Alchemy" => "alchemy",
        "Orb of Annulment" => "annul",
        "Divine Orb" => "divine",
        "Orb of Chance" => "chance",
        "Vaal Orb" => "vaal",
        "Essence" => "essence",
        "Desecration" => "desecrate",
        _ => null,
    };

    // ------------------------------------------------------------------ shared building blocks

    internal static Applicability NoSlot(IReadOnlyList<OmenDef> omens, int need = 1)
    {
        var restricted = OmenEffects.RestrictedType(omens);
        if (restricted != null)
            return Applicability.No($"No free {restricted.Value.ToString().ToLower()} slot for {omens.First(o => OmenEffects.RestrictedType(o) != null).Name}.");
        return Applicability.No(need > 1 ? $"Needs {need} free modifier slots." : "No free modifier slot (prefixes and suffixes are full).");
    }

    private static readonly AffixType[] AffixTypes = { AffixType.Prefix, AffixType.Suffix };

    /// <summary>Free slots of one affix type for the target rarity; 0 when an omen restricts additions to the other type.</summary>
    internal int FreeSlots(Item item, AffixType type, Rarity target, AffixType? restricted = null) =>
        restricted != null && restricted != type ? 0 : Math.Max(0, A.MaxAffixes(target, type) - item.CountOf(type));

    /// <summary>Free affix slots of both types for the target rarity, honouring a prefix/suffix restriction.</summary>
    internal int FreeSlots(Item item, Rarity target, AffixType? restricted) => AffixTypes.Sum(t => FreeSlots(item, t, target, restricted));

    /// <summary>Non-fractured affixes that a removal can hit, honouring prefix/suffix, desecrated-only and lowest-level omens.</summary>
    internal static List<RemovalCandidate> Removable(Item item, IReadOnlyList<OmenDef> omens)
    {
        var restricted = OmenEffects.RestrictedType(omens);
        var list = item.Mods.Select((m, i) => new RemovalCandidate { Index = i, Mod = m })
            .Where(r => r.Mod.IsAffix && !r.Mod.Fractured)
            .Where(r => restricted == null || r.Mod.Affix == restricted)
            .Where(r => !omens.Has(OmenEffects.RemoveDesecratedOnly) || r.Mod.Kind == ModKind.Desecrated)
            .ToList();
        if (omens.Has(OmenEffects.RemoveLowestLevel) && list.Count > 0)
        {
            int lowest = list.Min(r => r.Mod.Def?.Level ?? int.MaxValue);
            list = list.Where(r => (r.Mod.Def?.Level ?? int.MaxValue) == lowest).ToList();
        }
        return RemovalCandidate.Uniform(list);
    }

    /// <summary>Prefix and suffix candidate lists for adding one mod (before choosing the affix type).</summary>
    internal (List<ModCandidate> prefixes, List<ModCandidate> suffixes) AdditionCandidates(Item item, int minLevel, IReadOnlyList<OmenDef> omens, Rarity targetRarity,
        string category = ModPool.NormalCategory, Func<ModDef, bool>? filter = null)
    {
        if (omens.Has(OmenEffects.Homogenising))
        {
            var existingTags = item.Affixes.Where(m => m.Def != null).SelectMany(m => m.Def!.ModTags).ToHashSet();
            if (existingTags.Count > 0) filter = Both(filter, mod => mod.ModTags.Any(existingTags.Contains));
        }
        var restricted = OmenEffects.RestrictedType(omens);
        List<ModCandidate> For(AffixType type) =>
            FreeSlots(item, type, targetRarity, restricted) > 0 ? CatalystBias(item, omens, _pool.Candidates(item, type, minLevel, filter, category)) : new List<ModCandidate>();
        return (For(AffixType.Prefix), For(AffixType.Suffix));
    }

    /// <summary>Omen of Catalysing Exaltation: mods with the catalyst quality's tag get their weight multiplied (config catalysingWeightBonusPerQuality).</summary>
    private List<ModCandidate> CatalystBias(Item item, IReadOnlyList<OmenDef> omens, List<ModCandidate> candidates)
    {
        if (!omens.Has(OmenEffects.Catalysing) || item.QualityTag is not { } tag) return candidates;
        double factor = 1 + item.Quality * A.CatalysingWeightBonusPerQuality;
        var biased = candidates.Select(c => c.Mod.ModTags.Contains(tag) ? new ModCandidate { Mod = c.Mod, Weight = (int)Math.Round(c.Weight * factor) } : c).ToList();
        ModPool.Normalise(biased);
        return biased;
    }

    private static Func<ModDef, bool> Both(Func<ModDef, bool>? a, Func<ModDef, bool> b) => a == null ? b : m => a(m) && b(m);

    /// <summary>Combined distribution over prefixes and suffixes according to the affix-type selection rule.</summary>
    internal List<ModCandidate> Combined(List<ModCandidate> pre, List<ModCandidate> suf, out double pPrefix)
    {
        double wp = pre.Sum(x => (double)x.Weight), ws = suf.Sum(x => (double)x.Weight);
        if (pre.Count == 0 && suf.Count == 0) { pPrefix = 0; return new List<ModCandidate>(); }
        pPrefix = A.AffixTypeSelection == "equal" && pre.Count > 0 && suf.Count > 0 ? 0.5 : wp / (wp + ws);
        double p = pPrefix;
        return pre.Select(x => new ModCandidate { Mod = x.Mod, Weight = x.Weight, Probability = p * x.Weight / wp })
            .Concat(suf.Select(x => new ModCandidate { Mod = x.Mod, Weight = x.Weight, Probability = (1 - p) * x.Weight / ws }))
            .OrderByDescending(x => x.Probability).ToList();
    }

    /// <summary>Add one mod (random or chosen). Returns false when nothing could be added.</summary>
    internal bool AddOne(ExecuteContext ctx, AffixType? forcedType, ManualChoice? choice, IReadOnlyList<OmenDef> omens)
    {
        var item = ctx.Result;
        var target = item.Rarity == Rarity.Normal ? Rarity.Magic : item.Rarity;
        var (pre, suf) = AdditionCandidates(item, ctx.MinModLevel, omens, target);
        if (forcedType == AffixType.Prefix) suf.Clear();
        if (forcedType == AffixType.Suffix) pre.Clear();
        var combined = Combined(pre, suf, out _);
        if (combined.Count == 0) return false;

        ModCandidate pick = choice?.AddModIds.Count > 0
            ? combined.FirstOrDefault(x => x.Mod.Id == choice.AddModIds[0]) ?? throw new InvalidOperationException("The chosen modifier cannot roll on this item right now.")
            : combined[ctx.Rng.PickWeighted(combined.Select(x => x.Probability).ToList())];
        var values = choice?.Values.Count > 0 ? choice.Values[0] : null;
        var added = item.AddMod(pick.Mod, ModKind.Explicit, values ?? RollValues(pick.Mod, ctx.Rng));
        ctx.Details.Add($"Added {Describe(added, item)}");
        return true;
    }

    /// <summary>Add <paramref name="count"/> mods one after another, honouring the omens' prefix/suffix restriction.</summary>
    internal void AddMods(ExecuteContext ctx, int count, IReadOnlyList<OmenDef> omens)
    {
        for (int i = 0; i < count; i++)
            if (!AddOne(ctx, OmenEffects.RestrictedType(omens), ChoiceAt(ctx.Choice, i), omens)) { ctx.Details.Add("No modifier could be added."); break; }
    }

    /// <summary>Remove one mod from the candidates: the chosen one (by item index) or a random pick.</summary>
    internal RemovalCandidate RemoveOne(ExecuteContext ctx, List<RemovalCandidate> candidates, ItemMod? chosen)
    {
        var pick = chosen != null
            ? candidates.FirstOrDefault(x => ReferenceEquals(x.Mod, chosen)) ?? throw new InvalidOperationException("The chosen modifier cannot be removed.")
            : candidates[ctx.Rng.PickWeighted(candidates.Select(x => x.Probability).ToList())];
        ctx.Details.Add($"Removed {Describe(pick.Mod, ctx.Result)}");
        ctx.Result.Mods.RemoveAt(pick.Index);
        return pick;
    }

    /// <summary>The mod instances chosen for removal, resolved from indices into the item before any removal.</summary>
    internal static List<ItemMod> ChosenRemovals(ExecuteContext ctx) =>
        ctx.Choice?.RemoveIndices.Where(i => i >= 0 && i < ctx.Result.Mods.Count).Select(i => ctx.Result.Mods[i]).ToList() ?? new();

    /// <summary>Additions (with prefix probability) on the item after removing the mod at <paramref name="removalIndex"/>.</summary>
    internal (List<ModCandidate> Additions, double PrefixProbability) AdditionsAfterRemoval(Item item, int removalIndex, int minModLevel)
    {
        var after = item.Clone();
        after.Mods.RemoveAt(removalIndex);
        var (pre, suf) = AdditionCandidates(after, minModLevel, OmenEffects.None, Rarity.Rare);
        return (Combined(pre, suf, out var pP), pP);
    }

    /// <summary>Pick a named outcome: the manual choice if it is one of the outcomes, otherwise weighted random.</summary>
    internal static string PickOutcome(ExecuteContext ctx, IReadOnlyDictionary<string, double> outcomes)
    {
        if (ctx.Choice?.SpecialOutcome is { } chosen && outcomes.ContainsKey(chosen)) return chosen;
        var keys = outcomes.Keys.ToList();
        return keys[ctx.Rng.PickWeighted(keys.Select(k => outcomes[k]).ToList())];
    }

    /// <summary>Normalise outcome weights to probabilities, dropping outcomes with weight 0.</summary>
    internal static Dictionary<string, double> NormaliseOutcomes(IEnumerable<KeyValuePair<string, double>> weights)
    {
        var positive = weights.Where(kv => kv.Value > 0).ToList();
        double total = positive.Sum(kv => kv.Value);
        return positive.ToDictionary(kv => kv.Key, kv => kv.Value / total);
    }

    /// <summary>Add a random (or the chosen) corruption enchantment from the base's pool; false when none can be added.</summary>
    internal bool AddCorruptionEnchant(ExecuteContext ctx, string? chosenModId)
    {
        var candidates = _pool.CorruptionEnchantCandidates(ctx.Result);
        if (candidates.Count == 0) return false;
        var pick = chosenModId != null
            ? candidates.FirstOrDefault(c => c.Mod.Id == chosenModId) ?? throw new InvalidOperationException("The chosen enchantment cannot be added to this item.")
            : candidates[ctx.Rng.PickWeighted(candidates.Select(c => c.Probability).ToList())];
        var added = ctx.Result.AddMod(pick.Mod, ModKind.CorruptedImplicit, RollValues(pick.Mod, ctx.Rng), ctx.Currency.Name);
        ctx.Details.Add($"Added corruption enchantment: {added.DisplayText()}");
        return true;
    }

    internal static void MakeRare(Item item, Rng rng)
    {
        item.Rarity = Rarity.Rare;
        item.Name ??= RareName(rng);
    }

    internal static ManualChoice? ChoiceAt(ManualChoice? choice, int i)
    {
        if (choice == null || i >= choice.AddModIds.Count) return null;
        return new ManualChoice { AddModIds = new() { choice.AddModIds[i] }, Values = new() { i < choice.Values.Count ? choice.Values[i] : null } };
    }

    internal string Describe(ItemMod m, Item item)
    {
        var tier = m.Def != null && m.Kind == ModKind.Explicit && _pool.TryDisplayTier(m.Def, item) is { } t ? $" (T{t})" : "";
        var kind = m.Kind is ModKind.Crafted or ModKind.Desecrated ? m.Kind.ToString().ToLower() + " " : "";
        var name = m.Def != null ? $" \"{m.Def.DisplayName}\"" : "";
        return $"{kind}{m.Affix.ToString().ToLower()}{name}{tier}: {m.DisplayText()}";
    }

    public static List<double> RollValues(ModDef mod, Rng rng) => mod.Ranges.Select(r => rng.RollRange(r[0], r[1])).ToList();

    static readonly string[] NamePrefixes = { "Dusk", "Grim", "Storm", "Soul", "Blood", "Vortex", "Ghoul", "Doom", "Rune", "Spirit", "Dread", "Sol", "Chimeric", "Corpse", "Empyrean", "Torment", "Glyph", "Phoenix", "Viper", "Havoc" };
    static readonly string[] NameSuffixes = { "Spire", "Song", "Bane", "Roar", "Whisper", "Call", "Grasp", "Beacon", "Weaver", "Cry", "Hold", "Bite", "Knell", "Ward", "Mark", "Blow", "Tear", "Brand", "Coil", "Spell" };
    public static string RareName(Rng rng) => $"{NamePrefixes[rng.Next(NamePrefixes.Length)]} {NameSuffixes[rng.Next(NameSuffixes.Length)]}";
}
