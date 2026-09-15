using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Desecration with bones (Jawbone, Rib, Collarbone, Cranium; Gnawed/Preserved/Ancient/Altered): adds an Unrevealed Desecrated modifier,
/// removing a random mod first when the slots are full. The mod is revealed later at the Well of Souls by choosing one of several options.
/// Omens: Sinistral/Dextral Necromancy (prefix/suffix only), Sovereign/Liege/Blackblooded (guaranteed Ulaman/Amanamu/Kurgal mod),
/// Putrefaction (replace all mods with unrevealed ones and corrupt). The reveal (with Omen of Abyssal Echoes) is <see cref="RevealOperation"/>.
/// </summary>
internal sealed class DesecrateOperation : CraftOperation
{
    public DesecrateOperation(CraftingEngine engine) : base(engine, CurrencyOps.Desecrate) { }

    /// <summary>Omen of Abyssal Echoes applies to the later reveal (<see cref="RevealOperation"/>), not to the bone.</summary>
    public override bool AcceptsOmen(CraftContext ctx, OmenDef omen) => base.AcceptsOmen(ctx, omen) && omen.Effect != OmenEffects.RerollRevealOnce;

    private static readonly Dictionary<string, string> BossTags = new()
    {
        [OmenEffects.GuaranteeUlaman] = "ulaman_mod",
        [OmenEffects.GuaranteeAmanamu] = "amanamu_mod",
        [OmenEffects.GuaranteeKurgal] = "kurgal_mod",
    };

    /// <summary>Bones whose desecration can take a boss omen.</summary>
    private static readonly string[] BossOmenTargets = { ClassTargets.WeaponOrQuiver, ClassTargets.Jewellery };

    /// <summary>The label of the "unrevealed prefix/suffix" outcome (StepPreview.SpecialOutcomes, ManualChoice.SpecialOutcome).</summary>
    internal static string OutcomeName(AffixType type) => $"Unrevealed {type}";

    private static RevealContext RevealContextFor(CraftContext ctx) =>
        new(ctx.MinModLevel, BossOmen(ctx) is { } boss ? BossTags[boss.Effect!] : null, ctx.Currency.Otherworldly == true);

    private static OmenDef? BossOmen(CraftContext ctx) => ctx.Omens.FirstOrDefault(o => o.Effect != null && BossTags.ContainsKey(o.Effect));

    // ------------------------------------------------------------------ reveal pool

    /// <summary>Desecrated (and for otherworldly bones breach) mods of one affix type that an unrevealed mod with this context can become.</summary>
    private List<ModCandidate> Pool(Item item, AffixType type, RevealContext reveal)
    {
        Func<ModDef, bool>? filter = reveal.RequiredTag is { } tag ? m => m.ModTags.Contains(tag) : null;
        var list = Engine.Pool.Candidates(item, type, reveal.MinModLevel, filter, ModCategories.Desecrated);
        if (reveal.Otherworldly) list.AddRange(Engine.Pool.Candidates(item, type, reveal.MinModLevel, filter, ModCategories.Otherworldly));
        return ModCandidate.Normalised(list);
    }

    /// <summary>Regular modifiers a reveal can offer besides the exclusive ones (none when a boss omen allows only its Lich's modifiers).</summary>
    private List<ModCandidate> RegularPool(Item item, AffixType type, RevealContext reveal) =>
        reveal.RequiredTag != null && Assumptions.RevealBossOmenOnlyLichModifiers ? new() : Engine.Pool.Candidates(item, type, reveal.MinModLevel);

    /// <summary>Everything an unrevealed mod of this type and context can become: exclusive and regular modifiers.</summary>
    private List<ModCandidate> RevealablePool(Item item, AffixType type, RevealContext reveal) =>
        ModCandidate.Normalised(Pool(item, type, reveal).Concat(RegularPool(item, type, reveal)));

    /// <summary>
    /// The two pools of a reveal and how many of the offered options are exclusive: <c>RevealGuaranteedExclusiveOptions</c> always, each further option a
    /// regular modifier with <c>RevealRegularOptionChance</c> (config) — as a distribution over the number of exclusive options. When one pool is empty,
    /// all options come from the other.
    /// </summary>
    private sealed record RevealPools(List<ModCandidate> Exclusive, List<ModCandidate> Regular, int Options, List<(int ExclusiveOptions, double Chance)> Splits);

    private RevealPools PoolsFor(Item item, int modIndex)
    {
        var mod = item.Mods[modIndex];
        if (!mod.Unrevealed) throw new InvalidChoiceException("This modifier is already revealed.");
        var others = item.Clone();
        others.Mods.RemoveAt(modIndex);
        return PoolsFor(others, mod.Affix, mod.Reveal ?? new RevealContext());
    }

    /// <summary>The pools of a reveal of <paramref name="type"/> on an item (without the unrevealed mod itself).</summary>
    private RevealPools PoolsFor(Item others, AffixType type, RevealContext reveal)
    {
        var exclusive = Pool(others, type, reveal);
        var regular = RegularPool(others, type, reveal);
        int options = Assumptions.RevealOptionCount;
        List<(int, double)> splits;
        if (regular.Count == 0) splits = new() { (options, 1) };
        else if (exclusive.Count == 0) splits = new() { (0, 1) };
        else
        {
            int guaranteed = Math.Min(Assumptions.RevealGuaranteedExclusiveOptions, options), free = options - guaranteed;
            double p = Assumptions.RevealRegularOptionChance;
            splits = Enumerable.Range(0, free + 1).Select(regularOptions => (options - regularOptions, Binomial(free, regularOptions, p))).ToList();
        }
        return new RevealPools(exclusive, regular, options, splits);
    }

    private static double Binomial(int n, int k, double p)
    {
        double coefficient = 1;
        for (int i = 0; i < k; i++) coefficient = coefficient * (n - i) / (i + 1);
        return coefficient * Math.Pow(p, k) * Math.Pow(1 - p, n - k);
    }

    /// <summary>The exclusive and the regular modifiers of a reveal, each with its chance to be among the offered options, most likely first.</summary>
    private static (List<ModCandidate> Exclusive, List<ModCandidate> Regular) Offers(RevealPools pools)
    {
        List<ModCandidate> Offered(List<ModCandidate> pool, Func<int, int> slots) => pool
            .Select(c => new ModCandidate
            {
                Mod = c.Mod, Weight = c.Weight,
                Probability = pools.Splits.Sum(s => s.Chance * OfferChance(pool, x => x.Mod.Id == c.Mod.Id, slots(s.ExclusiveOptions))),
            })
            .OrderByDescending(c => c.Probability).ToList();
        return (Offered(pools.Exclusive, e => e), Offered(pools.Regular, e => pools.Options - e));
    }

    /// <summary>The exclusive and regular modifiers the unrevealed mod at <paramref name="modIndex"/> can become, with their chance to be offered.</summary>
    public (List<ModCandidate> Exclusive, List<ModCandidate> Regular) RevealOffers(Item item, int modIndex) => Offers(PoolsFor(item, modIndex));

    /// <summary>What the unrevealed mod at <paramref name="modIndex"/> can become, each with its chance to be among the offered options, most likely first.</summary>
    public List<ModCandidate> RevealPool(Item item, int modIndex)
    {
        var (exclusive, regular) = RevealOffers(item, modIndex);
        return exclusive.Concat(regular).OrderByDescending(c => c.Probability).ToList();
    }

    /// <summary>Labels of the two groups of reveal options.</summary>
    /// <summary>
    /// Heading of the exclusive group: "at least N of the options" when regular modifiers can be offered too, otherwise every option is one — with a
    /// boss omen named after its Lich ("Kurgal modifiers — all 3 options").
    /// </summary>
    private string ExclusiveLabel(RevealContext reveal, bool withRegular)
    {
        int options = Assumptions.RevealOptionCount;
        var group = reveal.RequiredTag is { } tag ? $"{System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(tag.Replace("_mod", ""))} modifiers" : "Lich modifiers";
        return withRegular
            ? $"{group} — at least {Math.Min(Assumptions.RevealGuaranteedExclusiveOptions, options)} of the {options} options (chance to be offered)"
            : $"{group} — all {options} options (chance to be offered)";
    }

    /// <summary>The heading of the exclusive group for the unrevealed mod at <paramref name="modIndex"/>.</summary>
    public string ExclusiveLabel(Item item, int modIndex) =>
        ExclusiveLabel(item.Mods[modIndex].Reveal ?? new RevealContext(), PoolsFor(item, modIndex).Regular.Count > 0);
    internal const string RegularLabel = "Other options — regular modifiers (chance to be offered)";

    /// <summary>Chance that a modifier the planner wants is among the options offered for the unrevealed mod.</summary>
    public double RevealChance(Item item, int modIndex, Func<ModDef, bool> wanted)
    {
        var pools = PoolsFor(item, modIndex);
        return pools.Splits.Sum(s =>
        {
            double exclusive = OfferChance(pools.Exclusive, c => wanted(c.Mod), s.ExclusiveOptions);
            double regular = OfferChance(pools.Regular, c => wanted(c.Mod), pools.Options - s.ExclusiveOptions);
            return s.Chance * (1 - (1 - exclusive) * (1 - regular));
        });
    }

    /// <summary>The options offered for the unrevealed mod: how many are exclusive is rolled (config), then distinct modifiers by weight.</summary>
    public List<ModDef> RollOptions(Item item, int modIndex, Rng rng)
    {
        var pools = PoolsFor(item, modIndex);
        int exclusiveOptions = pools.Splits[rng.PickWeighted(pools.Splits.Select(s => s.Chance).ToList())].ExclusiveOptions;
        List<ModDef> Sample(List<ModCandidate> pool, int count) =>
            rng.SampleWeighted(pool.Select(c => c.Probability).ToList(), count).Select(i => pool[i].Mod).ToList();
        var exclusive = Sample(pools.Exclusive, exclusiveOptions);
        var regular = Sample(pools.Regular, pools.Options - exclusive.Count);
        // too few regular modifiers: more exclusive ones fill the options
        return exclusive.Concat(regular)
            .Concat(Sample(pools.Exclusive.Where(c => !exclusive.Contains(c.Mod)).ToList(), pools.Options - exclusive.Count - regular.Count))
            .ToList();
    }

    /// <summary>
    /// Chance that at least one wanted candidate is among <paramref name="slots"/> distinct draws from the pool: exact for equal weights
    /// (all desecrated mods), an estimate treating the draws as independent otherwise.
    /// </summary>
    internal static double OfferChance(List<ModCandidate> pool, Func<ModCandidate, bool> wanted, int slots)
    {
        int good = pool.Count(wanted);
        if (slots <= 0 || good == 0) return 0;
        if (slots >= pool.Count) return 1;
        if (pool.All(c => c.Weight == pool[0].Weight))
        {
            double none = 1;
            for (int i = 0; i < slots; i++) none *= Math.Max(0, pool.Count - good - i) / (double)(pool.Count - i);
            return 1 - none;
        }
        return 1 - Math.Pow(1 - pool.Where(wanted).Sum(c => c.Probability), slots);
    }

    // ------------------------------------------------------------------ desecration plan

    /// <summary>Which mod may be removed and which affix type the unrevealed mod gets (with probabilities).</summary>
    private sealed record Plan(List<RemovalCandidate> Removals, Dictionary<AffixType, double> Types);

    private Plan MakePlan(CraftContext ctx)
    {
        var item = ctx.Item;
        var reveal = RevealContextFor(ctx);

        var mark = CraftingEngine.Removable(item, OmenEffects.None).Where(r => r.Mod.Def?.Family == ModFamilies.AbyssMark).ToList();
        if (mark.Count > 0) return PlanWithRemoval(item, mark, reveal);

        if (Engine.FreeSlots(item, Rarity.Rare, ctx.RestrictedType) == 0)
            return PlanWithRemoval(item, CraftingEngine.Removable(item, ctx.Omens), reveal);

        List<ModCandidate> PoolIfFree(AffixType type) =>
            Engine.FreeSlots(item, type, Rarity.Rare, ctx.RestrictedType) > 0 ? RevealablePool(item, type, reveal) : new();
        var prefixes = PoolIfFree(AffixType.Prefix);
        var suffixes = PoolIfFree(AffixType.Suffix);
        Engine.Combined(prefixes, suffixes, out var pPrefix);
        var types = new Dictionary<AffixType, double>();
        if (prefixes.Count > 0) types[AffixType.Prefix] = pPrefix;
        if (suffixes.Count > 0) types[AffixType.Suffix] = 1 - pPrefix;
        return new Plan(new(), types);
    }

    /// <summary>The new unrevealed mod takes the removed mod's slot; only removals after which something can be revealed count.</summary>
    private Plan PlanWithRemoval(Item item, IEnumerable<RemovalCandidate> removals, RevealContext reveal)
    {
        var usable = RemovalCandidate.Uniform(removals.Where(r =>
        {
            var after = item.Clone();
            after.Mods.RemoveAt(r.Index);
            return RevealablePool(after, r.Mod.Affix, reveal).Count > 0;
        }));
        var types = usable.GroupBy(r => r.Mod.Affix).ToDictionary(g => g.Key, g => g.Sum(r => r.Probability));
        return new Plan(usable, types);
    }

    // ------------------------------------------------------------------ operation

    public override Applicability? Check(CraftContext ctx)
    {
        var item = ctx.Item;
        if (item.Sanctified) return Applicability.No("Sanctified items cannot be desecrated.");
        bool hasMark = item.HasFamily(ModFamilies.AbyssMark);
        if (item.HasDesecratedMod && !hasMark) return Applicability.No("Items with Desecrated modifiers cannot be desecrated again.");
        if (BossOmen(ctx) is { } boss && !BossOmenTargets.Contains(ctx.Currency.Target))
            return Applicability.No($"{boss.Name} only works on Weapon or Jewellery desecration.");

        if (ctx.OmenIs(OmenEffects.Putrefaction))
        {
            ctx.Notes.Add($"Assumption: Putrefaction replaces every non-fractured modifier with {Assumptions.PutrefactionUnrevealedCount} unrevealed modifiers (\"up to 6\", config: putrefactionUnrevealedCount) and corrupts the item.");
            return null;
        }

        if (MakePlan(ctx).Types.Count == 0)
            return Applicability.No("No modifier can be revealed here (item level, omen restriction or fractured mods)."); 
        if (hasMark) ctx.Notes.Add("Mark of the Abyssal Lord is replaced by the unrevealed modifier.");
        else if (Engine.FreeSlots(item, Rarity.Rare, ctx.RestrictedType) == 0)
            ctx.Notes.Add("Modifiers are full: a random modifier is removed and the unrevealed modifier takes its slot (assumption: same affix type).");
        ctx.Notes.Add($"The modifier stays unrevealed (community-reported: counts as level 1 for Omen of Whittling, not officially confirmed) until the Well of Souls reveals it: {Assumptions.RevealOptionCount} options, at least {Assumptions.RevealGuaranteedExclusiveOptions} of them an exclusive Lich modifier, the others regular modifiers or further Lich modifiers (assumption: {Assumptions.RevealRegularOptionChance:P0} regular each, config revealRegularOptionChance). Desecrated weights in the data are all equal (poe2db has no estimates).");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var reveal = RevealContextFor(ctx);
        bool putrefaction = ctx.OmenIs(OmenEffects.Putrefaction);
        var plan = putrefaction ? null : MakePlan(ctx);
        // only the affix types the unrevealed mod can get (a free suffix slot → suffixes; a full item → the type of the removed mod)
        var types = plan?.Types.Keys.ToList() ?? AffixTypeExtensions.Both.ToList();
        var offers = types.Select(type => Offers(PoolsFor(ctx.Item, type, reveal))).ToList();
        var exclusive = offers.SelectMany(o => o.Exclusive).OrderByDescending(c => c.Probability).ToList();
        var regular = offers.SelectMany(o => o.Regular).OrderByDescending(c => c.Probability).ToList();

        if (plan == null)
            return new StepPreview
            {
                Removals = CraftingEngine.Removable(ctx.Item, OmenEffects.None),
                RemovalLabel = "Replaced (all)",
                SpecialOutcomes = { [$"{Assumptions.PutrefactionUnrevealedCount} unrevealed modifiers, item corrupted"] = 1 },
                Additions = exclusive, AdditionsChoosable = false, AdditionLabel = ExclusiveLabel(reveal, regular.Count > 0),
                OtherAdditions = regular, OtherAdditionLabel = RegularLabel,
            };

        double planPrefix = plan.Types.GetValueOrDefault(AffixType.Prefix);
        return new StepPreview
        {
            RemoveCount = plan.Removals.Count > 0 ? 1 : 0,
            Removals = plan.Removals,
            SpecialOutcomes = plan.Types.ToDictionary(kv => OutcomeName(kv.Key), kv => kv.Value),
            Additions = exclusive, AdditionsChoosable = false, AdditionLabel = ExclusiveLabel(reveal, regular.Count > 0),
            OtherAdditions = regular, OtherAdditionLabel = RegularLabel,
            PrefixProbability = planPrefix, SuffixProbability = 1 - planPrefix,
        };
    }

    public override void Execute(ExecuteContext ctx)
    {
        var reveal = RevealContextFor(ctx);
        if (ctx.OmenIs(OmenEffects.Putrefaction))
        {
            Putrefy(ctx, reveal);
            return;
        }

        var plan = MakePlan(ctx);
        var type = plan.Removals.Count > 0
            ? Engine.RemoveOne(ctx, plan.Removals, CraftingEngine.ChosenRemovals(ctx).FirstOrDefault()).Mod.Affix
            : CraftingEngine.PickOutcome(ctx, plan.Types, OutcomeName);
        AddUnrevealed(ctx, type, reveal);
    }

    private void Putrefy(ExecuteContext ctx, RevealContext reveal)
    {
        var item = ctx.Result;
        int removed = item.Mods.RemoveAll(m => m.IsAffix && !m.Fractured);
        ctx.Details.Add($"Removed {removed} modifier(s).");
        for (int i = 0; i < Assumptions.PutrefactionUnrevealedCount; i++)
        {
            var type = item.PrefixCount <= item.SuffixCount ? AffixType.Prefix : AffixType.Suffix;
            if (Engine.FreeSlots(item, type, Rarity.Rare) == 0) type = type.Opposite();
            if (Engine.FreeSlots(item, type, Rarity.Rare) == 0) break;
            AddUnrevealed(ctx, type, reveal);
        }
        ctx.Corrupt();
    }

    private static void AddUnrevealed(ExecuteContext ctx, AffixType type, RevealContext reveal)
    {
        ctx.Result.Mods.Add(new ItemMod
        {
            ModId = ItemMod.UnrevealedDesecratedId, Kind = ModKind.Desecrated, Affix = type, Unrevealed = true, Reveal = reveal, SourceName = ctx.Currency.Name,
        });
        ctx.Details.Add($"Added an unrevealed desecrated {type.Lower()}.");
    }
}
