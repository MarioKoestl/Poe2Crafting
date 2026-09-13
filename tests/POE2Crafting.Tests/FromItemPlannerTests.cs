using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;
using Xunit;

namespace POE2Crafting.Tests;

public class FromItemPlannerTests
{
    private static PlanResult Plan(Item current, Rarity rarity, IEnumerable<ModDef> mods, bool better = true) =>
        new CraftingPathFinder(TestData.Data!, TestData.Pool!).FindPathsFromItem(current, PlannerTests.Spec(current, rarity, mods, better));

    private static ModDef[] Defs(Item item) => item.Affixes.Select(m => m.Def!).ToArray();

    [SkippableFact]
    public void Removal_only_target_is_reached_with_annulments()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var current = TestData.NewItem("Siphoning Wand", Rarity.Rare).WithAffixes(3, 0);

        var plan = Plan(current, Rarity.Rare, Defs(current).Take(1));
        var basic = plan.Strategies.Single(s => s.Id == "item-basic-currency");
        Assert.All(basic.Steps, s => Assert.Contains("Annul", s.CurrencyName));
        Assert.Equal(2.0 / 3 * 1.0 / 2, basic.OverallProbability, 6);
        Assert.Equal(1.0 / 3, basic.Steps[0].BrickProbability!.Value, 6);
    }

    [SkippableFact]
    public void Directional_annulment_omen_beats_plain_annulment()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var current = TestData.NewItem("Siphoning Wand", Rarity.Rare).WithAffixes(3, 2);
        var target = Defs(current).Where(m => m.AffixType == AffixType.Prefix).Append(Defs(current).First(m => m.AffixType == AffixType.Suffix));

        var plan = Plan(current, Rarity.Rare, target);
        var basic = plan.Strategies.Single(s => s.Id == "item-basic-currency");
        Assert.Equal(1.0 / 5, basic.OverallProbability, 6);
        Assert.Equal(1.0 / 2, plan.Strategies[0].OverallProbability, 6);
        Assert.Contains("Omen", plan.Strategies[0].Steps[0].CurrencyName);
    }

    [SkippableFact]
    public void Normal_item_starts_with_a_transmutation()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var current = TestData.NewItem("Siphoning Wand");
        var prefix = Defs(TestData.NewItem("Siphoning Wand").WithAffixes(1, 0));

        var plan = Plan(current, Rarity.Magic, prefix);
        Assert.NotEmpty(plan.Strategies);
        Assert.Contains("Transmutation", plan.Strategies[0].Steps[0].CurrencyName);
        Assert.Empty(plan.Problems);
    }

    [SkippableFact]
    public void Perfect_essence_with_crystallisation_omen_replaces_the_unwanted_suffix()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        // a perfect essence removes a random mod first; Omen of Dextral Crystallisation makes it the unwanted suffix
        var current = TestData.NewItem("Siphoning Wand", Rarity.Rare).WithAffixes(1, 1);
        var essence = TestData.Currency("Perfect Essence of Sorcery").Essence!;
        var crafted = TestData.Data!.EssenceModFor(essence, current.Base, current.ItemClass)!;

        var plan = Plan(current, Rarity.Rare, Defs(current).Where(m => m.AffixType == AffixType.Prefix).Append(crafted));
        Assert.Contains(plan.Strategies, s => s.Steps.Count == 1 && s.Steps[0].CurrencyName.Contains("Perfect Essence of Sorcery") && s.Steps[0].SuccessProbability == 1.0);
    }

    [SkippableFact]
    public void Desecrated_target_is_planned_with_a_bone_and_a_reveal()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var current = TestData.NewItem("Siphoning Wand", Rarity.Rare).WithAffixes(1, 0);
        var desecrated = TestData.Pool!.AllForBaseByCategory(current, ModCategories.Desecrated, AffixType.Suffix).First(m => m.Level <= current.ItemLevel);

        var plan = Plan(current, Rarity.Rare, Defs(current).Append(desecrated));
        var strategy = Assert.Single(plan.Strategies.Where(s => s.Steps.Any(step => step.Description.Contains("reveal"))).Take(1));
        Assert.InRange(strategy.OverallProbability, 0.0001, 1);
    }

    [SkippableFact]
    public void Divine_is_planned_when_only_values_are_too_low()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var current = TestData.NewItem("Siphoning Wand", Rarity.Rare);
        var mod = TestData.Pool!.AllForBase(current, AffixType.Prefix).First(m => m.Family == "WeaponCasterDamagePrefix" && m.Ranges.Count > 0 && m.Ranges[0][0] < m.Ranges[0][1]);
        var range = mod.Ranges[0];
        current.AddMod(mod, ModKind.Explicit, mod.Ranges.Select(r => r[0]).ToList());
        var spec = PlannerTests.Spec(current, Rarity.Rare, new[] { mod });
        spec.TargetMods[0].MinValues = new List<double?> { range[1] };

        var plan = new CraftingPathFinder(TestData.Data!, TestData.Pool).FindPathsFromItem(current, spec);
        var step = Assert.Single(plan.Strategies[0].Steps);
        Assert.Contains("Divine", step.CurrencyName);
        Assert.Equal(FromItemPlanner.ChanceAtLeast(range, range[1]), step.SuccessProbability, 6);
    }

    [SkippableFact]
    public void Magic_target_only_uses_moves_that_keep_the_item_magic()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        // magic amulet with a spirit prefix, target adds a rarity suffix: an essence would make the item rare
        var current = TestData.NewItem("Crimson Amulet", Rarity.Magic);
        current.AddMod(TestData.Pool!.AllForBase(current, AffixType.Prefix).First(m => m.Text.Contains("Spirit")));
        var rarity = TestData.Pool.AllForBase(current, AffixType.Suffix).Where(m => m.Text.Contains("Rarity of Items")).MaxBy(m => m.Level)!;
        var augment = TestData.Engine!.Preview(current, TestData.Action("Orb of Augmentation")).Additions.Single(a => a.Mod.Id == rarity.Id);

        var plan = Plan(current, Rarity.Magic, Defs(current).Append(rarity), better: false);
        var strategy = Assert.Single(plan.Strategies);
        var step = Assert.Single(strategy.Steps);
        Assert.Contains("Augmentation", step.CurrencyName);
        Assert.Equal(augment.Probability, step.SuccessProbability, 6);
        // the best rarity suffix tier is level 40: Greater (min level 44) and Perfect (70) augmentations cannot roll it, and the step says so
        Assert.Contains(step.Notes, n => n.Contains("Greater Orb of Augmentation") && n.Contains("(minimum modifier level 44: no matching tier can roll)"));
    }

    [SkippableFact]
    public void Magic_prefix_item_to_magic_suffix_item_annuls_and_augments_without_essences()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        // Mario's case: magic amulet with only a spirit prefix → magic amulet with only a rarity suffix
        var current = TestData.NewItem("Crimson Amulet", Rarity.Magic);
        current.AddMod(TestData.Pool!.AllForBase(current, AffixType.Prefix).First(m => m.Text.Contains("Spirit")));
        var rarity = TestData.Pool.AllForBase(current, AffixType.Suffix).Where(m => m.Text.Contains("Rarity of Items")).MaxBy(m => m.Level)!;

        var plan = Plan(current, Rarity.Magic, new[] { rarity });
        Assert.NotEmpty(plan.Strategies);
        Assert.All(plan.Strategies, s =>
        {
            Assert.DoesNotContain(s.Steps, step => step.CurrencyName.Contains("Essence") || step.CurrencyName.Contains("Regal") || step.CurrencyName.Contains("Alloy"));
            Assert.Contains("Annulment", s.Steps[0].CurrencyName);
            Assert.Equal(1.0, s.Steps[0].SuccessProbability, 6);
        });
        Assert.Contains("Augmentation", plan.Strategies[0].Steps[1].CurrencyName);
    }

    [SkippableFact]
    public void No_strategy_step_leaves_the_target_rarity()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        // magic targets from normal and magic starts: after every step of every strategy the item is still at most magic
        foreach (var (baseName, prefixes, suffixes) in new[] { ("Crimson Amulet", 1, 0), ("Crimson Amulet", 0, 1), ("Siphoning Wand", 1, 1), ("Siphoning Wand", 0, 0) })
        {
            var current = TestData.NewItem(baseName, prefixes + suffixes > 0 ? Rarity.Magic : Rarity.Normal).WithAffixes(prefixes, suffixes);
            var wanted = TestData.NewItem(baseName, Rarity.Magic).WithAffixes(suffixes > 0 ? 0 : 1, prefixes > 0 ? 0 : 1);
            var plan = Plan(current, Rarity.Magic, Defs(wanted));
            Assert.NotEmpty(plan.Strategies);
            Assert.All(plan.Strategies.SelectMany(s => s.Steps), step => Assert.True(step.Result!.Rarity <= Rarity.Magic, $"{step.CurrencyName} makes the item {step.Result.Rarity}"));
        }
    }

    [SkippableFact]
    public void Lower_rarity_target_is_reported_as_a_problem()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var current = TestData.NewItem("Siphoning Wand", Rarity.Rare).WithAffixes(1, 0);
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

    [Fact]
    public void Chance_at_least_handles_integer_and_decimal_ranges()
    {
        Assert.Equal(6.0 / 11, FromItemPlanner.ChanceAtLeast(new[] { 10.0, 20.0 }, 15), 6);
        Assert.Equal(0.5, FromItemPlanner.ChanceAtLeast(new[] { 1.0, 2.0 }, 1.5), 6);
        Assert.Equal(1.0, FromItemPlanner.ChanceAtLeast(new[] { 10.0, 20.0 }, 5), 6);
        Assert.Equal(0.0, FromItemPlanner.ChanceAtLeast(new[] { 10.0, 20.0 }, 21), 6);
    }
}
