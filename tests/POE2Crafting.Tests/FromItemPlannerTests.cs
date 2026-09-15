namespace POE2Crafting.Tests;

public class FromItemPlannerTests
{
    private static PlanResult Plan(Item current, Rarity rarity, IEnumerable<ModDef> mods, bool better = true) =>
        TestData.Plan(current, TestData.Spec(rarity, mods, better));

    private static ModDef[] Defs(Item item) => TestData.Defs(item);

    /// <summary>Magic amulet with only a spirit prefix, and the best "Rarity of Items" suffix.</summary>
    private static (Item Current, ModDef Rarity) SpiritAmuletAndRaritySuffix()
    {
        var current = TestData.NewItem(TestBases.MagicAmulet, Rarity.Magic);
        current.AddMod(TestData.Pool!.AllForBase(current, AffixType.Prefix).First(m => m.Text.Contains("Spirit")));
        return (current, TestData.BestMod(current, "Rarity of Items", AffixType.Suffix));
    }

    [DataFact]
    public void Removal_only_target_is_reached_with_annulments()
    {
        var current = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(3, 0);

        var plan = Plan(current, Rarity.Rare, Defs(current).Take(1));
        var basic = plan.Strategies.Single(s => s.Id == "item-basic-currency");
        Assert.All(basic.Steps, s => Assert.Contains("Annul", s.CurrencyName));
        Assert.Equal(2.0 / 3 * 1.0 / 2, basic.OverallProbability, 6);
        Assert.Equal(1.0 / 3, basic.Steps[0].BrickProbability!.Value, 6);
    }

    [DataFact]
    public void Directional_annulment_omen_beats_plain_annulment()
    {
        var current = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(3, 2);
        var target = Defs(current).Where(m => m.AffixType == AffixType.Prefix).Append(Defs(current).First(m => m.AffixType == AffixType.Suffix));

        var plan = Plan(current, Rarity.Rare, target);
        var basic = plan.Strategies.Single(s => s.Id == "item-basic-currency");
        Assert.Equal(1.0 / 5, basic.OverallProbability, 6);
        Assert.Equal(1.0 / 2, plan.Strategies[0].OverallProbability, 6);
        Assert.Contains("Omen", plan.Strategies[0].Steps[0].CurrencyName);
    }

    [DataFact]
    public void Normal_item_starts_with_a_transmutation()
    {
        var current = TestData.NewItem(TestBases.Wand);
        var prefix = Defs(TestData.NewItem(TestBases.Wand).WithAffixes(1, 0));

        var plan = Plan(current, Rarity.Magic, prefix);
        Assert.NotEmpty(plan.Strategies);
        Assert.Contains("Transmutation", plan.Strategies[0].Steps[0].CurrencyName);
        Assert.Empty(plan.Problems);
    }

    [DataFact]
    public void Perfect_essence_with_crystallisation_omen_replaces_the_unwanted_suffix()
    {
        // a perfect essence removes a random mod first; Omen of Dextral Crystallisation makes it the unwanted suffix
        var current = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1);
        var essence = TestData.Currency("Perfect Essence of Sorcery").Essence!;
        var crafted = Assert.Single(TestData.Data!.EssenceModsFor(essence, current.Base, current.ItemClass));

        var plan = Plan(current, Rarity.Rare, Defs(current).Where(m => m.AffixType == AffixType.Prefix).Append(crafted));
        Assert.Contains(plan.Strategies, s => s.Steps.Count == 1 && s.Steps[0].CurrencyName.Contains("Perfect Essence of Sorcery") && s.Steps[0].SuccessProbability == 1.0);
    }

    [DataFact]
    public void Desecrated_target_is_planned_with_a_bone_and_a_reveal()
    {
        var current = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 0);
        var desecrated = TestData.Pool!.AllForBaseByCategory(current, ModCategories.Desecrated, AffixType.Suffix).First(m => m.Level <= current.ItemLevel);

        var plan = Plan(current, Rarity.Rare, Defs(current).Append(desecrated));
        var strategy = Assert.Single(plan.Strategies.Where(s => s.Steps.Any(step => step.Description.Contains("reveal"))).Take(1));
        Assert.InRange(strategy.OverallProbability, 0.0001, 1);
    }

    [DataFact]
    public void Divine_is_planned_when_only_values_are_too_low()
    {
        var current = TestData.NewItem(TestBases.Wand, Rarity.Rare);
        var mod = TestData.Pool!.AllForBase(current, AffixType.Prefix).First(m => m.Family == "WeaponCasterDamagePrefix" && m.Ranges.Count > 0 && m.Ranges[0][0] < m.Ranges[0][1]);
        var range = mod.Ranges[0];
        current.AddMod(mod, ModKind.Explicit, mod.Ranges.Select(r => r[0]).ToList());
        var spec = TestData.Spec(Rarity.Rare, new[] { mod });
        spec.TargetMods[0].MinValues = new List<double?> { range[1] };

        var plan = TestData.Plan(current, spec);
        var step = Assert.Single(plan.Strategies[0].Steps);
        Assert.Contains("Divine", step.CurrencyName);
        Assert.Equal(ModText.ChanceAtLeast(range, range[1]), step.SuccessProbability, 6);
    }

    [DataFact]
    public void Magic_target_only_uses_moves_that_keep_the_item_magic()
    {
        // magic amulet with a spirit prefix, target adds a rarity suffix: an essence would make the item rare
        var (current, rarity) = SpiritAmuletAndRaritySuffix();

        var plan = Plan(current, Rarity.Magic, Defs(current).Append(rarity), better: false);
        var strategy = Assert.Single(plan.Strategies);
        var step = Assert.Single(strategy.Steps);
        Assert.Contains("Augmentation", step.CurrencyName);
        var chosen = TestData.Engine!.Preview(current, TestData.Action(step.CurrencyName)).Additions.Single(a => a.Mod.Id == rarity.Id);
        Assert.Equal(chosen.Probability, step.SuccessProbability, 6);
        // the best rarity suffix tier (level 40) is below the minimum modifier level of Greater (44) and Perfect (70) orbs, but as the highest tier
        // of its type it still rolls — and more likely, since the low tiers of the other types are gone
        var normal = TestData.Engine.Preview(current, TestData.Action("Orb of Augmentation")).Additions.Single(a => a.Mod.Id == rarity.Id);
        Assert.True(step.SuccessProbability > normal.Probability);
    }

    [DataFact]
    public void Magic_prefix_item_to_magic_suffix_item_annuls_and_augments_without_essences()
    {
        // Mario's case: magic amulet with only a spirit prefix → magic amulet with only a rarity suffix
        var (current, rarity) = SpiritAmuletAndRaritySuffix();

        var plan = Plan(current, Rarity.Magic, new[] { rarity });
        Assert.NotEmpty(plan.Strategies);
        Assert.All(plan.Strategies, s =>
            Assert.DoesNotContain(s.Steps, step => step.CurrencyName.Contains("Essence") || step.CurrencyName.Contains("Regal") || step.CurrencyName.Contains("Alloy")));

        // basic currency: annul the prefix, then augment
        var basic = plan.Strategies.Single(s => s.Id == "item-basic-currency");
        Assert.Contains("Annulment", basic.Steps[0].CurrencyName);
        Assert.Equal(1.0, basic.Steps[0].SuccessProbability, 6);
        Assert.Contains("Augmentation", basic.Steps[1].CurrencyName);

        // with omens the spirit prefix is a blocker: augmenting first can only add a suffix, then Omen of Sinistral Annulment removes the prefix
        var best = plan.Strategies[0];
        Assert.Contains("Augmentation", best.Steps[0].CurrencyName);
        Assert.Contains("Sinistral", best.Steps[1].CurrencyName);
        Assert.True(best.OverallProbability > basic.OverallProbability);
    }

    [DataFact]
    public void No_strategy_step_leaves_the_target_rarity()
    {
        // magic targets from normal and magic starts: after every step of every strategy the item is still at most magic
        foreach (var (baseName, prefixes, suffixes) in new[] { (TestBases.MagicAmulet, 1, 0), (TestBases.MagicAmulet, 0, 1), (TestBases.Wand, 1, 1), (TestBases.Wand, 0, 0) })
        {
            var current = TestData.NewItem(baseName, prefixes + suffixes > 0 ? Rarity.Magic : Rarity.Normal).WithAffixes(prefixes, suffixes);
            var wanted = TestData.NewItem(baseName, Rarity.Magic).WithAffixes(suffixes > 0 ? 0 : 1, prefixes > 0 ? 0 : 1);
            var plan = Plan(current, Rarity.Magic, Defs(wanted));
            Assert.NotEmpty(plan.Strategies);
            Assert.All(plan.Strategies.SelectMany(s => s.Steps), step => Assert.True(step.Result!.Rarity <= Rarity.Magic, $"{step.CurrencyName} makes the item {step.Result.Rarity}"));
        }
    }

    [DataFact]
    public void Quality_augments_and_instill_targets_become_certain_finishing_steps()
    {
        var finder = TestData.PathFinder;

        // normal armour: quality first (5% per use on Normal), two sockets, two runes
        var boots = TestData.NewItem(TestBases.Boots);
        boots.Sockets = 0;
        var bootsSpec = TestData.Spec(Rarity.Normal, Array.Empty<ModDef>());
        bootsSpec.MinQuality = 20;
        bootsSpec.Augments.Add(new AugmentTarget { Name = "Desert Rune" });
        var bootsPlan = finder.FindPathsFromItem(boots, bootsSpec);
        var bootsSteps = Assert.Single(bootsPlan.Strategies).Steps;
        Assert.All(bootsSteps, s => Assert.Equal(1.0, s.SuccessProbability));
        var quality = bootsSteps.Single(s => s.CurrencyName == "Armourer's Scrap");
        Assert.Equal(4, quality.Materials["Armourer's Scrap"]);
        Assert.Contains(bootsSteps, s => s.CurrencyName == "Artificer's Orb");
        Assert.Contains("+14% to Fire Resistance", bootsSteps.Last().Result!.Runes);

        // amulet: catalyst quality of a type and an instilled notable
        var amulet = TestData.NewItem(TestBases.Amulet, Rarity.Rare).WithAffixes(1, 1);
        var amuletSpec = TestData.Spec(Rarity.Rare, Defs(amulet));
        amuletSpec.MinQuality = 20;
        amuletSpec.QualityType = "Life";
        amuletSpec.InstillNotable = "Flamekeeper";
        var amuletSteps = Assert.Single(finder.FindPathsFromItem(amulet, amuletSpec).Strategies).Steps;
        Assert.Equal(4, amuletSteps.Single(s => s.CurrencyName == "Flesh Catalyst").Materials["Flesh Catalyst"]);
        var instill = amuletSteps.Single(s => s.CurrencyName == "Instill Flamekeeper");
        Assert.Equal(2, instill.Materials["Diluted Liquid Ire"]);
        Assert.Equal("Allocates Flamekeeper", amuletSteps.Last().Result!.InstilledNotable!.DisplayText());
    }

    [DataFact]
    public void Lower_rarity_target_is_reported_as_a_problem()
    {
        var current = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 0);
        var plan = Plan(current, Rarity.Magic, Defs(current));
        Assert.Empty(plan.Strategies);
        Assert.Contains(plan.Problems, p => p.Contains("rarity cannot be lowered"));
    }

    [Theory]
    [InlineData(10, 1, 3, 0.3)]
    [InlineData(3, 1, 3, 1.0)]
    [InlineData(10, 0, 3, 0.0)]
    public void Offer_chance_draws_equally_weighted_options_without_replacement(int pool, int good, int options, double expected)
    {
        var candidates = Enumerable.Range(0, pool).Select(i => new ModCandidate { Mod = new ModDef { Id = i.ToString() }, Weight = 1, Probability = 1.0 / pool }).ToList();
        Assert.Equal(expected, POE2Crafting.Core.Engine.Operations.DesecrateOperation.OfferChance(candidates, c => int.Parse(c.Mod.Id) < good, options), 6);
    }

    [DataFact]
    public void Better_tiers_count_as_hits()
    {
        var baseItem = TestData.NewItem(TestBases.Wand);
        var t3 = TestData.Pool!.AllForBase(baseItem, AffixType.Prefix)
            .First(m => m.Family == "WeaponCasterDamagePrefix" && TestData.Pool.DisplayTier(m, baseItem) == 3);

        double Chance(bool better) => Plan(baseItem, Rarity.Magic, new[] { t3 }, better).Strategies.Max(s => s.OverallProbability);
        Assert.True(Chance(true) > Chance(false));
    }

    [DataFact]
    public void Target_tier_above_the_item_level_is_reported_at_once()
    {
        var current = TestData.NewItem(TestBases.Amulet, Rarity.Rare, itemLevel: 79);
        var best = TestData.Pool!.AllForBase(TestData.NewItem(TestBases.Amulet, Rarity.Rare, itemLevel: 100), AffixType.Suffix)
            .Where(m => m.Family == "ColdResistance").MaxBy(m => m.Level)!;
        Assert.True(best.Level > 79);

        var plan = Plan(current, Rarity.Rare, new[] { best });
        Assert.Empty(plan.Strategies);
        var problem = Assert.Single(plan.Problems);
        Assert.Contains($"needs item level {best.Level}", problem);
        Assert.Contains("item level 79", problem);
    }

    [DataFact]
    public void Quality_target_above_the_maximum_uses_essence_of_the_breach_and_removes_its_modifier_again()
    {
        // rare amulet: +3 spell skills and a junk suffix; target: the skills with 34% caster quality (maximum without Breach: 20%)
        var current = TestData.NewItem(TestBases.Amulet, Rarity.Rare, itemLevel: 82);
        var skills = TestData.BestMod(current, "Level of all Spell Skills");
        current.AddMod(skills);
        current.AddMod(TestData.BestMod(current, "Lightning Resistance", AffixType.Suffix));
        var spec = TestData.Spec(Rarity.Rare, new[] { skills });
        spec.MinQuality = 34;
        spec.QualityType = "Caster";

        var plan = TestData.Plan(current, spec);
        Assert.True(plan.Strategies.Count > 0, string.Join(" / ", plan.Problems));
        var steps = plan.Strategies[0].Steps;
        Assert.Contains(steps, s => s.CurrencyName.Contains("Essence of the Breach"));
        var final = steps.Last().Result!;
        Assert.True(final.Quality >= 34);
        Assert.Equal("Caster", final.QualityType);
        Assert.DoesNotContain(final.Affixes, m => m.Def?.Family == ModFamilies.MaximumQuality);
    }

    [DataFact]
    public void Minimum_modifier_level_keeps_the_highest_tier_of_a_type_that_would_be_excluded()
    {
        // Mario's case: magic Gold Amulet "of the Sorcerer" + Perfect Orb of Augmentation can give "Hoarder's" (rarity prefix, level 47)
        var amulet = TestData.NewItem(TestBases.Amulet, Rarity.Magic, itemLevel: 81);
        amulet.AddMod(TestData.BestMod(amulet, "Level of all Spell Skills"));
        var additions = TestData.Engine!.Preview(amulet, TestData.Action("Perfect Orb of Augmentation")).Additions;

        var rarity = Assert.Single(additions, a => a.Mod.Family == "ItemFoundRarityIncreasePrefix");
        Assert.Equal("Hoarder's", rarity.Mod.Name);
        // types with tiers of level 70+ keep only those
        Assert.All(additions.Where(a => a.Mod.Level < 70), a =>
            Assert.Equal(a.Mod.Level, additions.Where(b => ModTiers.TierGroupKey(b.Mod) == ModTiers.TierGroupKey(a.Mod)).Max(b => b.Mod.Level)));
    }

    [DataFact]
    public void Unrevealed_desecrated_mod_of_the_target_is_kept_or_added_without_a_reveal()
    {
        var wand = TestData.NewItem(TestBases.Wand, Rarity.Rare).WithAffixes(1, 1);
        var withUnrevealed = TestData.Apply(wand, "Preserved Jawbone", new ManualChoice { SpecialOutcome = "Unrevealed Suffix" }).Item;

        // the target taken over from the item keeps the unrevealed suffix (counts as a slot)
        var draft = new POE2Crafting.Core.Drafting.ItemDraft(TestData.Data!);
        draft.LoadFrom(withUnrevealed, copyValues: false);
        Assert.Single(draft.Selection.Unrevealed);
        Assert.Equal(2, draft.Selection.Count(AffixType.Suffix));
        Assert.Single(draft.BuildItem()!.UnrevealedMods);
        var spec = new TargetItemSpec { TargetRarity = Rarity.Rare, TargetMods = draft.Selection.ToTargetMods(TestData.Pool!, withUnrevealed, true) };

        // same item as source: nothing to do (the unrevealed mod is not an unwanted one)
        var done = TestData.Plan(withUnrevealed, spec);
        Assert.Equal("already-done", Assert.Single(done.Strategies).Id);

        // without it: a bone adds an unrevealed suffix, no reveal step
        var plan = TestData.Plan(wand, spec);
        Assert.True(plan.Strategies.Count > 0, string.Join(" / ", plan.Problems));
        var final = plan.Strategies[0].Steps.Last().Result!;
        Assert.Contains(final.UnrevealedMods, u => u.Mod.Affix == AffixType.Suffix);
        Assert.DoesNotContain(plan.Strategies[0].Steps, s => s.CurrencyName.Contains("Well of Souls"));
    }
}
