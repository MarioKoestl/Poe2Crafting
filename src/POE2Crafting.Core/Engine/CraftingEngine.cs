using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine.Operations;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine;

/// <summary>
/// Applies crafting currencies (with omens) to items. Each currency operation (<see cref="CurrencyDef.Op"/>) is a
/// CraftOperation; this class runs the generic checks and holds the building blocks all operations share
/// (candidate pools, adding/removing mods, rarity changes). Unknown rules come from <see cref="SimAssumptions"/> and are surfaced as notes.
/// </summary>
public sealed class CraftingEngine
{
    private readonly GameData _data;
    private readonly ModPool _pool;
    private readonly Dictionary<string, CraftOperation> _operations;
    private readonly DesecrateOperation _desecrate;
    private readonly RevealOperation _reveal;

    public CraftingEngine(GameData data, ModPool? pool = null)
    {
        _data = data;
        _pool = pool ?? new ModPool(data);
        _desecrate = new DesecrateOperation(this);
        _reveal = new RevealOperation(this);
        _operations = new CraftOperation[]
        {
            new AddModOperation(this, CurrencyOps.Transmute, Rarity.Magic, requiresNoAffixes: true),
            new AddModOperation(this, CurrencyOps.Augment, Rarity.Magic),
            new AddModOperation(this, CurrencyOps.Regal, Rarity.Rare),
            new AddModOperation(this, CurrencyOps.Exalt, Rarity.Rare),
            new AlchemyOperation(this),
            new ChaosOperation(this),
            new AnnulOperation(this),
            new DivineOperation(this),
            new ChanceOperation(this),
            new FractureOperation(this),
            FlagOperation.Mirror(this),
            FlagOperation.Identify(this),
            FlagOperation.Lock(this),
            new EssenceOperation(this),
            _desecrate,
            _reveal,
            new VaalOperation(this),
            new SacrificeOperation(this),
            new ArchitectOperation(this),
            new QualityOperation(this, CurrencyOps.Quality, infuser: false),
            new QualityOperation(this, CurrencyOps.VaalQuality, infuser: true),
            new CatalystOperation(this),
            new SocketOperation(this),
            new ExtractOperation(this),
            new FluxOperation(this),
            new AugmentOperation(this),
        }.ToDictionary(o => o.Op);
    }

    public GameData Data => _data;
    public ModPool Pool => _pool;
    public SimAssumptions Assumptions => _data.Config.Assumptions;

    /// <summary>Whether an operation id is simulated.</summary>
    public bool HasOperation(string op) => _operations.ContainsKey(op);

    // ------------------------------------------------------------------ public API

    public Applicability Check(Item item, CraftAction action) => Check(NewContext(item, action));

    /// <param name="forcedRemovalIndex">For two-step manual choices (Chaos): show additions as they would be after removing this mod.</param>
    public StepPreview Preview(Item item, CraftAction action, int? forcedRemovalIndex = null)
    {
        var ctx = NewContext(item, action);
        var app = Check(ctx);
        var weights = $"Weights: {Assumptions.WeightsSource}.";
        if (!app.Ok) return new StepPreview { Applicability = app, WeightsNote = weights };
        return _operations[action.Currency.Op!].Preview(ctx, forcedRemovalIndex).WithApplicability(app, weights);
    }

    /// <summary>
    /// Hinekora's Lock: the exact result the action will have on a foreseeing item (every action — currency plus omens — has its own fixed outcome).
    /// Applying the action with <see cref="Execute(Item, CraftAction, Rng, ManualChoice?)"/> and <see cref="ForeseeRng"/> gives the same result.
    /// </summary>
    public CraftResult Foresee(Item item, CraftAction action) =>
        item.Foreseeing ? Execute(item, action, ForeseeRng(item, action)) : throw new InvalidOperationException("The item does not foresee (use Hinekora's Lock first).");

    /// <summary>The random source that fixes the foreseen outcome of an action on a foreseeing item.</summary>
    public static Rng ForeseeRng(Item item, CraftAction action) => new(Rng.Derive(item.ForeseeSeed, action.DisplayName));

    /// <summary>Apply the action (random, or with a manual choice). Throws <see cref="InvalidChoiceException"/> when the choice is impossible.</summary>
    public CraftResult Execute(Item item, CraftAction action, Rng rng, ManualChoice? choice = null)
    {
        var app = Check(item, action);
        if (!app.Ok) return NotApplied(item, app);
        var result = ResultCopy(item);
        var ctx = new ExecuteContext { Item = item, Currency = action.Currency, Omens = action.Omens, Result = result, Rng = rng, Choice = choice };
        _operations[action.Currency.Op!].Execute(ctx);
        var summary = action.DisplayName + (ctx.Details.Count > 0 ? ": " + string.Join("; ", ctx.Details.Take(3)) + (ctx.Details.Count > 3 ? " ..." : "") : "");
        return new CraftResult { Applied = true, Item = result, Destroyed = ctx.Destroyed, Summary = summary, Details = ctx.Details };
    }

    private static CraftResult NotApplied(Item item, Applicability app) => new() { Applied = false, Item = item, Summary = app.Reason };

    /// <summary>The copy an action modifies; any change of the item ends Hinekora's Lock.</summary>
    private static Item ResultCopy(Item item)
    {
        var result = item.Clone();
        result.Foreseeing = false;
        return result;
    }

    // ---- instilling amulets with Liquid Emotions

    /// <summary>Whether the recipe's notable can be instilled on the item (amulets that are not corrupted or mirrored).</summary>
    public Applicability CheckInstill(Item item, InstillRecipe recipe)
    {
        if (!GameData.CanInstill(item.ItemClass)) return Applicability.No("Only amulets can be instilled.");
        if (item.Corrupted) return Applicability.No("Corrupted amulets cannot be instilled.");
        if (item.Mirrored) return Applicability.No(MirroredReason);
        var notes = new List<string> { $"Needs {string.Join(" → ", recipe.Emotions)} (in this order)." };
        if (item.InstilledNotable is { } existing)
        {
            if (!Assumptions.InstillReplacesExisting) return Applicability.No($"The amulet is already instilled ({existing.DisplayText()}).");
            notes.Add($"Replaces {existing.DisplayText()} ({Assumptions.AugmentInstillNote}).");
        }
        return new Applicability { Ok = true, Notes = notes };
    }

    /// <summary>Instill the recipe's notable: the amulet gains the enchantment "Allocates Notable" (replacing an existing instill).</summary>
    public CraftResult Instill(Item item, InstillRecipe recipe)
    {
        var app = CheckInstill(item, recipe);
        if (!app.Ok) return NotApplied(item, app);
        var result = ResultCopy(item);
        if (result.InstilledNotable is { } existing) result.Mods.Remove(existing);
        result.Mods.Add(new ItemMod { ModId = "instill", Kind = ModKind.Enchant, Affix = AffixType.Other, RawText = recipe.EnchantText, SourceName = string.Join(", ", recipe.Emotions) });
        var detail = $"Instilled {recipe.Notable} ({string.Join(" → ", recipe.Emotions)})";
        return new CraftResult { Applied = true, Item = result, Summary = detail, Details = { detail } };
    }

    // ---- desecrated mods: reveal at the Well of Souls

    /// <summary>What the unrevealed mod at <paramref name="modIndex"/> can become, with probabilities.</summary>
    public List<ModCandidate> RevealPool(Item item, int modIndex) => _desecrate.RevealPool(item, modIndex);

    /// <summary>Roll the options offered at the Well of Souls (config: revealOptionCount).</summary>
    public List<ModDef> RollRevealOptions(Item item, int modIndex, Rng rng) => _desecrate.RollOptions(item, modIndex, rng);

    /// <summary>The exclusive and regular modifiers an unrevealed mod can become, with their chance to be offered.</summary>
    public (List<ModCandidate> Exclusive, List<ModCandidate> Regular) RevealOffers(Item item, int modIndex) => _desecrate.RevealOffers(item, modIndex);

    /// <summary>Heading of the exclusive reveal options of an unrevealed mod ("Kurgal modifiers — all 3 options" with a boss omen).</summary>
    public string RevealExclusiveLabel(Item item, int modIndex) => _desecrate.ExclusiveLabel(item, modIndex);

    /// <summary>Chance that a wanted modifier is among the options offered for the unrevealed mod.</summary>
    public double RevealChance(Item item, int modIndex, Func<ModDef, bool> wanted) => _desecrate.RevealChance(item, modIndex, wanted);

    /// <summary>Turn the unrevealed mod into the chosen desecrated mod (the Well of Souls with a manual choice).</summary>
    public CraftResult Reveal(Item item, int modIndex, string modId, Rng rng) =>
        Execute(item, CraftAction.Of(GameData.WellOfSouls), rng, new ManualChoice { RemoveIndices = { modIndex }, AddModIds = { modId } });

    // ------------------------------------------------------------------ generic checks

    private const string MirroredReason = "Mirrored items cannot be modified.";

    private static CraftContext NewContext(Item item, CraftAction action) => new() { Item = item, Currency = action.Currency, Omens = action.Omens };

    private Applicability Check(CraftContext ctx)
    {
        var (item, c) = (ctx.Item, ctx.Currency);
        if (c.Op == null) return Applicability.No("This currency is not simulated (yet).");
        if (!_operations.TryGetValue(c.Op, out var op)) return Applicability.No($"{c.Name} ({c.Op}) is planned for a later stage.");
        if (item.Base == null) return Applicability.No("Unknown base item.");
        if (item.Mirrored) return Applicability.No(MirroredReason);
        if (ctx.Omens.FirstOrDefault(o => o.TargetCurrency != null && !op.AcceptsOmen(ctx, o)) is { } unrelated)
            return Applicability.No($"{unrelated.Name} does not affect {c.Name}.");
        if (OmenEffects.Conflict(ctx.Omens) is { } conflict) return Applicability.No(conflict);
        if (item.Corrupted && !op.WorksOnCorrupted && !op.RequiresCorrupted) return Applicability.No("Corrupted items cannot be modified with this currency.");
        if (!item.Corrupted && op.RequiresCorrupted) return Applicability.No($"{c.Name} can only be used on Corrupted items.");
        if (c.RarityIn is { Count: > 0 } && !c.RarityIn.Contains(item.Rarity.ToString()))
            return Applicability.No($"{c.Name} requires a {string.Join(" or ", c.RarityIn)} item (item is {item.Rarity}).");
        var classTarget = c.ClassTarget ?? op.DefaultClassTarget;
        if (!_data.ClassMatchesTarget(item.ItemClass, classTarget))
            return Applicability.No($"{c.Name} can only be used on {ClassTargets.DisplayName(classTarget)}.");
        if (c.MaxItemLevel is { } maxIlvl && item.ItemLevel > maxIlvl)
            return Applicability.No($"{c.Name} only works on items up to item level {maxIlvl}.");
        if (!item.Identified && c.Op != CurrencyOps.Identify) return Applicability.No("Item must be identified first.");

        if (op.Check(ctx) is { } refusal) return refusal;
        if (c.MinModLevel is { } mml) ctx.Notes.Add($"Minimum Modifier Level {mml}: only modifier tiers with level >= {mml} can be added.");
        return new Applicability { Ok = true, Notes = ctx.Notes };
    }

    // ------------------------------------------------------------------ shared building blocks

    /// <summary>Maximum quality of the item (base or default maximum plus "+% to Maximum Quality" modifiers).</summary>
    internal int MaxQuality(Item item) => item.MaxQuality(Assumptions.DefaultMaxQuality);

    internal static Applicability NoSlot(IReadOnlyList<OmenDef> omens, int need = 1)
    {
        if (OmenEffects.RestrictingOmen(omens) is { } omen)
            return Applicability.No($"No free {OmenEffects.RestrictedType(omen)!.Value.Lower()} slot for {omen.Name}.");
        return Applicability.No(need > 1 ? $"Needs {need} free modifier slots." : "No free modifier slot (prefixes and suffixes are full).");
    }

    /// <summary>Free slots of one affix type for the target rarity; 0 when an omen restricts additions to the other type.</summary>
    internal int FreeSlots(Item item, AffixType type, Rarity target, AffixType? restricted = null) =>
        restricted != null && restricted != type ? 0 : Math.Max(0, Assumptions.MaxAffixes(item, target, type) - item.CountOf(type));

    /// <summary>Free affix slots of both types for the target rarity, honouring a prefix/suffix restriction.</summary>
    internal int FreeSlots(Item item, Rarity target, AffixType? restricted) => AffixTypeExtensions.Both.Sum(t => FreeSlots(item, t, target, restricted));

    /// <summary>Non-fractured affixes that a removal can hit, honouring prefix/suffix, desecrated-only and lowest-level omens.</summary>
    internal static List<RemovalCandidate> Removable(Item item, IReadOnlyList<OmenDef> omens)
    {
        var restricted = OmenEffects.RestrictedType(omens);
        var list = RemovalCandidate.UniformOf(item, m => m.IsAffix && !m.Fractured
            && (restricted == null || m.Affix == restricted)
            && (!omens.Has(OmenEffects.RemoveDesecratedOnly) || m.Kind == ModKind.Desecrated));
        if (omens.Has(OmenEffects.RemoveLowestLevel) && list.Count > 0)
        {
            int lowest = list.Min(r => ModLevel(r.Mod));
            list = RemovalCandidate.Uniform(list.Where(r => ModLevel(r.Mod) == lowest));
        }
        return list;
    }

    /// <summary>Modifier level for Omen of Whittling: an unrevealed desecrated mod counts as level 1 (community-reported, e.g. dadsofexile.com; not confirmed by GGG — ties with real level-1 mods are split randomly), unknown lines as highest.</summary>
    internal static int ModLevel(ItemMod mod) => mod.Unrevealed ? 1 : mod.Def?.Level ?? int.MaxValue;

    /// <summary>Prefix and suffix candidate lists for adding one mod (before choosing the affix type).</summary>
    internal (List<ModCandidate> prefixes, List<ModCandidate> suffixes) AdditionCandidates(Item item, int minLevel, IReadOnlyList<OmenDef> omens, Rarity targetRarity,
        string category = ModCategories.Normal, Func<ModDef, bool>? filter = null)
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
        if (!omens.Has(OmenEffects.Catalysing) || item.QualityTag == null) return candidates;
        double factor = 1 + item.Quality * Assumptions.CatalysingWeightBonusPerQuality;
        return ModCandidate.Normalised(candidates.Select(c =>
            item.QualityEnhances(c.Mod) ? new ModCandidate { Mod = c.Mod, Weight = (int)Math.Round(c.Weight * factor) } : c));
    }

    private static Func<ModDef, bool> Both(Func<ModDef, bool>? a, Func<ModDef, bool> b) => a == null ? b : m => a(m) && b(m);

    /// <summary>Combined distribution over prefixes and suffixes according to the affix-type selection rule.</summary>
    internal List<ModCandidate> Combined(List<ModCandidate> pre, List<ModCandidate> suf, out double pPrefix)
    {
        double wp = pre.Sum(x => (double)x.Weight), ws = suf.Sum(x => (double)x.Weight);
        if (pre.Count == 0 && suf.Count == 0) { pPrefix = 0; return new List<ModCandidate>(); }
        pPrefix = Assumptions.AffixTypeSelection == AffixTypeSelection.Equal && pre.Count > 0 && suf.Count > 0 ? 0.5 : wp / (wp + ws);
        double p = pPrefix;
        return pre.Select(x => new ModCandidate { Mod = x.Mod, Weight = x.Weight, Probability = p * x.Weight / wp })
            .Concat(suf.Select(x => new ModCandidate { Mod = x.Mod, Weight = x.Weight, Probability = (1 - p) * x.Weight / ws }))
            .OrderByDescending(x => x.Probability).ToList();
    }

    /// <summary>The chosen candidate (when <paramref name="isChosen"/> is given), otherwise a weighted random one.</summary>
    /// <exception cref="InvalidChoiceException">The choice is not among the candidates.</exception>
    internal static T Pick<T>(Rng rng, IReadOnlyList<T> candidates, Func<T, double> probability, Func<T, bool>? isChosen, string invalidChoice) where T : class =>
        isChosen != null
            ? candidates.FirstOrDefault(isChosen) ?? throw new InvalidChoiceException(invalidChoice)
            : candidates[rng.PickWeighted(candidates.Select(probability).ToList())];

    /// <summary>The chosen values of the first added mod, or a random roll.</summary>
    internal static List<double> ValuesFor(ManualChoice? choice, ModDef mod, Rng rng) =>
        choice?.Values.Count > 0 && choice.Values[0] is { } values ? CheckedValues(mod, values) : RollValues(mod, rng);

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

        var chosenId = choice?.AddModIds.FirstOrDefault();
        var pick = Pick(ctx.Rng, combined, x => x.Probability, chosenId != null ? x => x.Mod.Id == chosenId : null, "The chosen modifier cannot roll on this item right now.");
        var added = item.AddMod(pick.Mod, ModKind.Explicit, ValuesFor(choice, pick.Mod, ctx.Rng));
        ctx.Details.Add($"Added {Describe(added, item)}");
        return true;
    }

    /// <summary>Add <paramref name="count"/> mods one after another; <paramref name="forcedTypeAt"/> gives the affix type of the i-th mod (default: the omens' restriction).</summary>
    internal void AddMods(ExecuteContext ctx, int count, IReadOnlyList<OmenDef> omens, Func<int, AffixType?>? forcedTypeAt = null)
    {
        for (int i = 0; i < count; i++)
            if (!AddOne(ctx, forcedTypeAt != null ? forcedTypeAt(i) : OmenEffects.RestrictedType(omens), ChoiceAt(ctx.Choice, i), omens))
            {
                ctx.Details.Add("No modifier could be added.");
                break;
            }
    }

    /// <summary>Remove one mod from the candidates: the chosen one (by item index) or a random pick.</summary>
    internal RemovalCandidate RemoveOne(ExecuteContext ctx, List<RemovalCandidate> candidates, ItemMod? chosen)
    {
        var pick = Pick(ctx.Rng, candidates, x => x.Probability, chosen != null ? x => ReferenceEquals(x.Mod, chosen) : null, "The chosen modifier cannot be removed.");
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

    /// <summary>Pick a named outcome: the manual choice if it is one of the outcomes' labels, otherwise weighted random.</summary>
    internal static TKey PickOutcome<TKey>(ExecuteContext ctx, IReadOnlyDictionary<TKey, double> outcomes, Func<TKey, string> label) where TKey : notnull
    {
        var keys = outcomes.Keys.ToList();
        if (ctx.Choice?.SpecialOutcome is { } chosen)
            foreach (var key in keys)
                if (label(key) == chosen) return key;
        return keys[ctx.Rng.PickWeighted(keys.Select(k => outcomes[k]).ToList())];
    }

    /// <summary>Pick an outcome whose key is its label.</summary>
    internal static string PickOutcome(ExecuteContext ctx, IReadOnlyDictionary<string, double> outcomes) => PickOutcome(ctx, outcomes, key => key);

    /// <summary>Normalise outcome weights to probabilities, dropping outcomes with weight 0.</summary>
    internal static Dictionary<TKey, double> NormaliseOutcomes<TKey>(IEnumerable<KeyValuePair<TKey, double>> weights) where TKey : notnull
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
        var pick = Pick(ctx.Rng, candidates, c => c.Probability, chosenModId != null ? c => c.Mod.Id == chosenModId : null, "The chosen enchantment cannot be added to this item.");
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
        var kind = m.Kind is ModKind.Crafted or ModKind.Desecrated ? m.Kind.ToString().ToLowerInvariant() + " " : "";
        var name = m.Def != null ? $" \"{m.Def.DisplayName}\"" : "";
        return $"{kind}{m.Affix.Lower()}{name}{tier}: {m.DisplayText()}";
    }

    internal static List<double> RollValues(ModDef mod, Rng rng) => mod.Ranges.Select(rng.RollRange).ToList();

    /// <summary>New values of the mod at <paramref name="index"/> with the ranges of <paramref name="mod"/>: the manually chosen ones (checked) or a random roll.</summary>
    internal static List<double> RerolledValues(ExecuteContext ctx, int index, ModDef mod)
    {
        return ctx.Choice?.Rerolls?.TryGetValue(index, out var values) == true ? CheckedValues(mod, values) : RollValues(mod, ctx.Rng);
    }

    /// <summary>Manually chosen values of a mod: one per range, each inside its range.</summary>
    private static List<double> CheckedValues(ModDef mod, IReadOnlyList<double> values)
    {
        if (values.Count != mod.Ranges.Count) throw new InvalidChoiceException($"{mod.Text} needs {mod.Ranges.Count} value(s).");
        for (int r = 0; r < mod.Ranges.Count; r++)
        {
            var (lo, hi) = ModText.Bounds(mod.Ranges[r]);
            if (values[r] < lo || values[r] > hi) throw new InvalidChoiceException($"{values[r]} is outside the range {lo}–{hi} of {mod.Text}.");
        }
        return values.ToList();
    }

    private static readonly string[] NamePrefixes = { "Dusk", "Grim", "Storm", "Soul", "Blood", "Vortex", "Ghoul", "Doom", "Rune", "Spirit", "Dread", "Sol", "Chimeric", "Corpse", "Empyrean", "Torment", "Glyph", "Phoenix", "Viper", "Havoc" };
    private static readonly string[] NameSuffixes = { "Spire", "Song", "Bane", "Roar", "Whisper", "Call", "Grasp", "Beacon", "Weaver", "Cry", "Hold", "Bite", "Knell", "Ward", "Mark", "Blow", "Tear", "Brand", "Coil", "Spell" };
    private static string RareName(Rng rng) => $"{NamePrefixes[rng.Next(NamePrefixes.Length)]} {NameSuffixes[rng.Next(NameSuffixes.Length)]}";
}
