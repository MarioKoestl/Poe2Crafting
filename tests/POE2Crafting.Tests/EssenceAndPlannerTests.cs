using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Items;
using Xunit;

namespace POE2Crafting.Tests;

public class EssenceTests
{
    [SkippableFact]
    public void Greater_essence_turns_magic_into_rare_with_guaranteed_mod()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var engine = TestData.Engine!;
        var item = TestData.NewItem("Siphoning Wand", Rarity.Magic);
        var action = new CraftAction { Currency = TestData.Currency("Greater Essence of Sorcery") };

        Assert.True(engine.Check(item, action).Ok);
        var result = engine.Execute(item, action, new Rng(7));

        Assert.Equal(Rarity.Rare, result.Item.Rarity);
        var added = Assert.Single(result.Item.Affixes);
        Assert.Equal("(75-89)% increased Spell Damage", added.Def!.Text);
        Assert.Equal(ModKind.Explicit, added.Kind);
        Assert.InRange(added.Values[0], 75, 89);
    }

    [SkippableFact]
    public void Essence_without_mod_for_the_class_is_not_applicable()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = TestData.NewItem("Siphoning Wand", Rarity.Magic);
        var check = TestData.Engine!.Check(item, new CraftAction { Currency = TestData.Currency("Lesser Essence of the Body") });
        Assert.False(check.Ok);
        Assert.Contains("no effect", check.Reason);
    }

    [SkippableFact]
    public void Perfect_essence_replaces_a_suffix_when_suffixes_are_full_and_adds_a_crafted_mod()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var pool = TestData.Pool!;
        var item = TestData.NewItem("Siphoning Wand", Rarity.Rare);
        foreach (var s in pool.Candidates(item, AffixType.Suffix).GroupBy(c => c.Mod.Family).Take(3).Select(g => g.First().Mod))
            item.Mods.Add(TestData.ModOf(s));
        item.Mods.Add(TestData.ModOf(pool.Candidates(item, AffixType.Prefix).First().Mod));
        Skip.If(item.SuffixCount != 3);

        var action = new CraftAction { Currency = TestData.Currency("Perfect Essence of Sorcery") }; // wand: +3 spell skills (suffix)
        var preview = TestData.Engine!.Preview(item, action);
        Assert.True(preview.Applicability.Ok, preview.Applicability.Reason);
        Assert.All(preview.Removals, r => Assert.Equal(AffixType.Suffix, r.Mod.Affix));

        var result = TestData.Engine.Execute(item, action, new Rng(3));
        Assert.Equal(3, result.Item.SuffixCount);
        Assert.Equal(1, result.Item.PrefixCount);
        Assert.Contains(result.Item.Mods, m => m.Kind == ModKind.Crafted && m.SourceName == "Perfect Essence of Sorcery");
    }

    [SkippableFact]
    public void Crystallisation_omen_only_targets_perfect_and_corrupted_essences()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var omen = TestData.Data!.FindOmen("Omen of Dextral Crystallisation")!;
        var item = TestData.NewItem("Siphoning Wand", Rarity.Magic);
        var check = TestData.Engine!.Check(item, new CraftAction { Currency = TestData.Currency("Essence of Sorcery"), Omen = omen });
        Assert.False(check.Ok);
        Assert.Contains("does not affect", check.Reason);
    }
}

public class ChaosChoiceTests
{
    [SkippableFact]
    public void Choosing_only_the_added_mod_rolls_a_compatible_removal_instead_of_throwing()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var pool = TestData.Pool!;
        var engine = TestData.Engine!;
        var item = TestData.NewItem("Siphoning Wand", Rarity.Rare);
        foreach (var type in new[] { AffixType.Prefix, AffixType.Suffix })
            foreach (var m in pool.Candidates(item, type).GroupBy(c => c.Mod.Family).Take(3).Select(g => g.First().Mod))
                item.Mods.Add(TestData.ModOf(m));

        // with 3P/3S, a prefix can only be added if a prefix is removed
        var prefix = pool.Candidates(item, AffixType.Prefix).First().Mod;
        var chaos = new CraftAction { Currency = TestData.Currency("Chaos Orb") };
        for (int seed = 0; seed < 20; seed++)
        {
            var result = engine.Execute(item, chaos, new Rng(seed), new ManualChoice { AddModIds = new() { prefix.Id } });
            Assert.Contains(result.Item.Affixes, m => m.ModId == prefix.Id);
            Assert.Equal(3, result.Item.PrefixCount);
        }
    }

    [SkippableFact]
    public void Preview_after_a_fixed_removal_only_offers_that_affix_type()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var pool = TestData.Pool!;
        var item = TestData.NewItem("Siphoning Wand", Rarity.Rare);
        foreach (var type in new[] { AffixType.Prefix, AffixType.Suffix })
            foreach (var m in pool.Candidates(item, type).GroupBy(c => c.Mod.Family).Take(3).Select(g => g.First().Mod))
                item.Mods.Add(TestData.ModOf(m));

        int suffixIndex = item.Mods.FindIndex(m => m.Affix == AffixType.Suffix);
        var preview = TestData.Engine!.Preview(item, new CraftAction { Currency = TestData.Currency("Chaos Orb") }, suffixIndex);
        Assert.True(preview.TwoStepChoice);
        Assert.All(preview.Additions, a => Assert.Equal(AffixType.Suffix, a.Mod.AffixType));
        Assert.Equal(1.0, preview.Additions.Sum(a => a.Probability), 6);
    }
}

public class PlannerTests
{
    private static TargetMod Target(ModDef mod, ModPool pool, Item baseItem, bool better = true) => new()
    {
        Family = mod.Family!, Tier = mod.Tier, AffixType = mod.AffixType, ResolvedMod = mod,
        DisplayTier = pool.DisplayTier(mod, baseItem), AllowBetterTiers = better,
    };

    [SkippableFact]
    public void Better_tiers_count_as_hits()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var data = TestData.Data!; var pool = TestData.Pool!;
        var baseItem = TestData.NewItem("Siphoning Wand");
        var t3 = pool.AllForBase(baseItem, AffixType.Prefix).First(m => m.Family == "WeaponCasterDamagePrefix" && pool.DisplayTier(m, baseItem) == 3);

        double Prob(bool better) => new CraftingPathFinder(data, pool).FindPaths(new TargetItemSpec
        {
            BaseName = baseItem.BaseName, ItemClass = baseItem.ItemClass, ItemLevel = 82, TargetRarity = Rarity.Magic,
            TargetMods = new() { Target(t3, pool, baseItem, better) },
        }).Single().OverallProbability;

        Assert.True(Prob(true) > Prob(false));
    }

    [SkippableFact]
    public void Two_prefixes_and_a_suffix_still_produce_a_transmute_path()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var data = TestData.Data!; var pool = TestData.Pool!;
        var baseItem = TestData.NewItem("Siphoning Wand");
        var prefixes = pool.Candidates(baseItem, AffixType.Prefix).GroupBy(c => c.Mod.Family).Take(2).Select(g => g.First().Mod).ToList();
        var suffix = pool.Candidates(baseItem, AffixType.Suffix).First().Mod;

        var spec = new TargetItemSpec
        {
            BaseName = baseItem.BaseName, ItemClass = baseItem.ItemClass, ItemLevel = 82, TargetRarity = Rarity.Rare,
            TargetMods = prefixes.Append(suffix).Select(m => Target(m, pool, baseItem)).ToList(),
        };
        var strategy = new CraftingPathFinder(data, pool).FindPaths(spec).Single(s => s.Id == "transmute-regal");

        Assert.True(strategy.OverallProbability > 0);
        Assert.Contains("Transmutation", strategy.Steps[0].CurrencyName);
        Assert.Contains("Augmentation", strategy.Steps[1].CurrencyName);
        Assert.Contains("Regal", strategy.Steps[2].CurrencyName);
    }

    [SkippableFact]
    public void Essence_path_is_offered_when_an_essence_guarantees_a_target_mod()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var data = TestData.Data!; var pool = TestData.Pool!;
        var baseItem = TestData.NewItem("Siphoning Wand");
        var spellDamage = pool.AllForBase(baseItem, AffixType.Prefix)
            .Where(m => m.Family == "WeaponCasterDamagePrefix").OrderBy(m => m.Level).First(m => m.Level >= 33);
        var suffix = pool.Candidates(baseItem, AffixType.Suffix).First().Mod;

        var spec = new TargetItemSpec
        {
            BaseName = baseItem.BaseName, ItemClass = baseItem.ItemClass, ItemLevel = 82, TargetRarity = Rarity.Rare,
            TargetMods = new() { Target(spellDamage, pool, baseItem), Target(suffix, pool, baseItem) },
        };
        var strategies = new CraftingPathFinder(data, pool).FindPaths(spec);
        var essence = Assert.Single(strategies, s => s.Id.StartsWith("essence-"));
        Assert.Contains(essence.Steps, s => s.CurrencyName.Contains("Essence of Sorcery") && s.SuccessProbability == 1.0);
    }

    [SkippableFact]
    public void Display_tiers_are_per_base_not_global()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var pool = TestData.Pool!;
        var staff = TestData.NewItem("Sanctified Staff");
        var glyphicStaff = pool.AllForBase(staff, AffixType.Prefix).Single(m => m.Name == "Glyphic" && m.Family == "WeaponCasterDamagePrefix");
        Assert.Equal(2, pool.DisplayTier(glyphicStaff, staff));
    }
}
