using POE2Crafting.Core.Data;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Core.Engine.Operations;

/// <summary>
/// Desecration with bones (Jawbone, Rib, Collarbone, Cranium; Gnawed/Preserved/Ancient/Altered): adds an Unrevealed Desecrated modifier,
/// removing a random mod first when the slots are full. The mod is revealed later at the Well of Souls by choosing one of several options.
/// Omens: Sinistral/Dextral Necromancy (prefix/suffix only), Sovereign/Liege/Blackblooded (guaranteed Ulaman/Amanamu/Kurgal mod),
/// Putrefaction (replace all mods with unrevealed ones and corrupt), Abyssal Echoes (reroll the reveal options once, handled by the caller).
/// </summary>
public sealed class DesecrateOperation : CraftOperation
{
    /// <summary>Family of "Mark of the Abyssal Lord" (Essence of the Abyss): desecration replaces this mod.</summary>
    private const string AbyssMarkFamily = "EssenceAbyss";
    private const string UnrevealedModId = "unrevealed_desecrated";

    public DesecrateOperation(CraftingEngine engine) : base(engine, "desecrate") { }

    /// <summary>Omen of Abyssal Echoes applies to the later reveal, not to the bone.</summary>
    public override bool AcceptsOmen(CraftContext ctx, OmenDef omen) => base.AcceptsOmen(ctx, omen) && omen.Effect != OmenEffects.RerollRevealOnce;

    private static readonly Dictionary<string, string> BossTags = new()
    {
        [OmenEffects.GuaranteeUlaman] = "ulaman_mod",
        [OmenEffects.GuaranteeAmanamu] = "amanamu_mod",
        [OmenEffects.GuaranteeKurgal] = "kurgal_mod",
    };

    private static string OutcomeName(AffixType type) => $"Unrevealed {type}";

    private static RevealContext RevealContextFor(CraftContext ctx) =>
        new(ctx.MinModLevel, BossOmen(ctx) is { } boss ? BossTags[boss.Effect!] : null, ctx.Currency.Otherworldly == true);

    private static OmenDef? BossOmen(CraftContext ctx) => ctx.Omens.FirstOrDefault(o => o.Effect != null && BossTags.ContainsKey(o.Effect));

    // ------------------------------------------------------------------ reveal pool

    /// <summary>Desecrated (and for otherworldly bones breach) mods of one affix type that an unrevealed mod with this context can become.</summary>
    private List<ModCandidate> Pool(Item item, AffixType type, RevealContext reveal)
    {
        Func<ModDef, bool>? filter = reveal.RequiredTag is { } tag ? m => m.ModTags.Contains(tag) : null;
        var list = Engine.Pool.Candidates(item, type, reveal.MinModLevel, filter, ModPool.DesecratedCategory);
        if (reveal.Otherworldly) list.AddRange(Engine.Pool.Candidates(item, type, reveal.MinModLevel, filter, ModPool.OtherworldlyCategory));
        ModPool.Normalise(list);
        return list;
    }

    public List<ModCandidate> RevealPool(Item item, int modIndex)
    {
        var mod = item.Mods[modIndex];
        if (!mod.Unrevealed) throw new InvalidOperationException("This modifier is already revealed.");
        var others = item.Clone();
        others.Mods.RemoveAt(modIndex);
        return Pool(others, mod.Affix, mod.Reveal ?? new RevealContext()).OrderByDescending(c => c.Probability).ToList();
    }

    public List<ModDef> RollRevealOptions(Item item, int modIndex, Rng rng)
    {
        var pool = RevealPool(item, modIndex);
        return rng.SampleWeighted(pool.Select(c => c.Probability).ToList(), Engine.A.RevealOptionCount).Select(i => pool[i].Mod).ToList();
    }

    public CraftResult Reveal(Item item, int modIndex, string modId, Rng rng)
    {
        var pick = RevealPool(item, modIndex).FirstOrDefault(c => c.Mod.Id == modId)
                   ?? throw new InvalidOperationException("The chosen modifier cannot be revealed from this desecrated modifier.");
        var result = item.Clone();
        var source = result.Mods[modIndex].SourceName;
        result.Mods.RemoveAt(modIndex);
        var revealed = result.AddMod(pick.Mod, ModKind.Desecrated, CraftingEngine.RollValues(pick.Mod, rng), source, modIndex);
        var detail = $"Revealed {Engine.Describe(revealed, result)}";
        return new CraftResult { Applied = true, Item = result, Summary = $"Well of Souls: {detail}", Details = { detail } };
    }

    // ------------------------------------------------------------------ desecration plan

    /// <summary>Which mod may be removed and which affix type the unrevealed mod gets (with probabilities).</summary>
    private sealed record Plan(List<RemovalCandidate> Removals, Dictionary<AffixType, double> Types);

    private Plan MakePlan(CraftContext ctx)
    {
        var item = ctx.Item;
        var reveal = RevealContextFor(ctx);

        var mark = CraftingEngine.Removable(item, OmenEffects.None).Where(r => r.Mod.Def?.Family == AbyssMarkFamily).ToList();
        if (mark.Count > 0) return PlanWithRemoval(item, RemovalCandidate.Uniform(mark), reveal);

        if (Engine.FreeSlots(item, Rarity.Rare, ctx.RestrictedType) == 0)
            return PlanWithRemoval(item, CraftingEngine.Removable(item, ctx.Omens), reveal);

        List<ModCandidate> PoolIfFree(AffixType type) =>
            Engine.FreeSlots(item, type, Rarity.Rare, ctx.RestrictedType) > 0 ? Pool(item, type, reveal) : new();
        var prefixes = PoolIfFree(AffixType.Prefix);
        var suffixes = PoolIfFree(AffixType.Suffix);
        Engine.Combined(prefixes, suffixes, out var pPrefix);
        var types = new Dictionary<AffixType, double>();
        if (prefixes.Count > 0) types[AffixType.Prefix] = pPrefix;
        if (suffixes.Count > 0) types[AffixType.Suffix] = 1 - pPrefix;
        return new Plan(new(), types);
    }

    /// <summary>The new unrevealed mod takes the removed mod's slot; only removals after which something can be revealed count.</summary>
    private Plan PlanWithRemoval(Item item, List<RemovalCandidate> removals, RevealContext reveal)
    {
        var usable = RemovalCandidate.Uniform(removals.Where(r =>
        {
            var after = item.Clone();
            after.Mods.RemoveAt(r.Index);
            return Pool(after, r.Mod.Affix, reveal).Count > 0;
        }).ToList());
        var types = usable.GroupBy(r => r.Mod.Affix).ToDictionary(g => g.Key, g => g.Sum(r => r.Probability));
        return new Plan(usable, types);
    }

    // ------------------------------------------------------------------ operation

    public override Applicability? Check(CraftContext ctx)
    {
        var item = ctx.Item;
        if (item.Sanctified) return Applicability.No("Sanctified items cannot be desecrated.");
        bool hasMark = item.Affixes.Any(m => m.Def?.Family == AbyssMarkFamily);
        if (item.HasDesecratedMod && !hasMark) return Applicability.No("Items with Desecrated modifiers cannot be desecrated again.");
        if (BossOmen(ctx) is { } boss && ctx.Currency.Target is not ("weapon_or_quiver" or "jewellery"))
            return Applicability.No($"{boss.Name} only works on Weapon or Jewellery desecration.");

        if (ctx.OmenIs(OmenEffects.Putrefaction))
        {
            ctx.Notes.Add($"Assumption: Putrefaction replaces every non-fractured modifier with {Engine.A.PutrefactionUnrevealedCount} unrevealed modifiers (\"up to 6\", config: putrefactionUnrevealedCount) and corrupts the item.");
            return null;
        }

        if (MakePlan(ctx).Types.Count == 0)
            return Applicability.No("No desecrated modifier from the data store can roll here (item level, omen restriction or fractured mods).");
        if (hasMark) ctx.Notes.Add("Mark of the Abyssal Lord is replaced by the unrevealed modifier.");
        else if (Engine.FreeSlots(item, Rarity.Rare, ctx.RestrictedType) == 0)
            ctx.Notes.Add("Modifiers are full: a random modifier is removed and the unrevealed modifier takes its slot (assumption: same affix type).");
        ctx.Notes.Add($"Reveal at the Well of Souls offers {Engine.A.RevealOptionCount} options (config: revealOptionCount). Desecrated weights in the data are all equal (poe2db has no estimates).");
        return null;
    }

    public override StepPreview Preview(CraftContext ctx, int? forcedRemovalIndex)
    {
        var reveal = RevealContextFor(ctx);
        List<ModCandidate> PoolIfAllowed(AffixType type) => ctx.RestrictedType is { } only && only != type ? new() : Pool(ctx.Item, type, reveal);
        var possible = Engine.Combined(PoolIfAllowed(AffixType.Prefix), PoolIfAllowed(AffixType.Suffix), out var pPrefix);
        if (ctx.OmenIs(OmenEffects.Putrefaction))
            return new StepPreview
            {
                Removals = CraftingEngine.Removable(ctx.Item, OmenEffects.None),
                RemovalLabel = "Replaced (all)",
                SpecialOutcomes = { [$"{Engine.A.PutrefactionUnrevealedCount} unrevealed modifiers, item corrupted"] = 1 },
                Additions = possible, AdditionsChoosable = false, AdditionLabel = "Possible revealed modifiers",
                PrefixProbability = pPrefix, SuffixProbability = 1 - pPrefix,
            };

        var plan = MakePlan(ctx);
        double planPrefix = plan.Types.GetValueOrDefault(AffixType.Prefix);
        return new StepPreview
        {
            RemoveCount = plan.Removals.Count > 0 ? 1 : 0,
            Removals = plan.Removals,
            SpecialOutcomes = plan.Types.ToDictionary(kv => OutcomeName(kv.Key), kv => kv.Value),
            Additions = possible, AdditionsChoosable = false, AdditionLabel = "Possible revealed modifiers",
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
        AffixType type;
        if (plan.Removals.Count > 0)
            type = Engine.RemoveOne(ctx, plan.Removals, CraftingEngine.ChosenRemovals(ctx).FirstOrDefault()).Mod.Affix;
        else
        {
            var outcome = CraftingEngine.PickOutcome(ctx, plan.Types.ToDictionary(kv => OutcomeName(kv.Key), kv => kv.Value));
            type = plan.Types.Keys.First(t => OutcomeName(t) == outcome);
        }
        AddUnrevealed(ctx, type, reveal);
    }

    private void Putrefy(ExecuteContext ctx, RevealContext reveal)
    {
        var item = ctx.Result;
        int removed = item.Mods.RemoveAll(m => m.IsAffix && !m.Fractured);
        ctx.Details.Add($"Removed {removed} modifier(s).");
        for (int i = 0; i < Engine.A.PutrefactionUnrevealedCount; i++)
        {
            var type = item.PrefixCount <= item.SuffixCount ? AffixType.Prefix : AffixType.Suffix;
            if (Engine.FreeSlots(item, type, Rarity.Rare) == 0) type = type == AffixType.Prefix ? AffixType.Suffix : AffixType.Prefix;
            if (Engine.FreeSlots(item, type, Rarity.Rare) == 0) break;
            AddUnrevealed(ctx, type, reveal);
        }
        item.Corrupted = true;
        ctx.Details.Add("Item is now Corrupted.");
    }

    private void AddUnrevealed(ExecuteContext ctx, AffixType type, RevealContext reveal)
    {
        ctx.Result.Mods.Add(new ItemMod
        {
            ModId = UnrevealedModId, Kind = ModKind.Desecrated, Affix = type, Unrevealed = true, Reveal = reveal, SourceName = ctx.Currency.Name,
        });
        ctx.Details.Add($"Added an unrevealed desecrated {type.ToString().ToLower()}.");
    }
}
