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
        var augment = TestData.Engine!.Preview(current, TestData.Action("Orb of Augmentation")).Additions.Single(a => a.Mod.Id == rarity.Id);

        var plan = Plan(current, Rarity.Magic, Defs(current).Append(rarity), better: false);
        var strategy = Assert.Single(plan.Strategies);
        var step = Assert.Single(strategy.Steps);
        Assert.Contains("Augmentation", step.CurrencyName);
        Assert.Equal(augment.Probability, step.SuccessProbability, 6);
        // the best rarity suffix tier is level 40: Greater (min level 44) and Perfect (70) augmentations cannot roll it, and the step says so
        Assert.Contains(step.Notes, n => n.Contains("Greater Orb of Augmentation") && n.Contains("(minimum modifier level 44: no matching tier can roll)"));
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
    public void Reveal_chance_draws_options_without_replacement(int pool, int good, int options, double expected) =>
        Assert.Equal(expected, FromItemPlanner.RevealChance(pool, good, options), 6);

    [DataFact]
    public void Better_tiers_count_as_hits()
    {
        var baseItem = TestData.NewItem(TestBases.Wand);
        var t3 = TestData.Pool!.AllForBase(baseItem, AffixType.Prefix)
            .First(m => m.Family == "WeaponCasterDamagePrefix" && TestData.Pool.DisplayTier(m, baseItem) == 3);

        double Chance(bool better) => Plan(baseItem, Rarity.Magic, new[] { t3 }, better).Strategies.Max(s => s.OverallProbability);
        Assert.True(Chance(true) > Chance(false));
    }
}
