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
        var item = TestData.NewItem("Siphoning Wand", Rarity.Magic);
        var action = TestData.Action("Greater Essence of Sorcery");

        Assert.True(TestData.Engine!.Check(item, action).Ok);
        var result = TestData.Engine.Execute(item, action, new Rng(7));

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
        var check = TestData.Engine!.Check(TestData.NewItem("Siphoning Wand", Rarity.Magic), TestData.Action("Lesser Essence of the Body"));
        Assert.False(check.Ok);
        Assert.Contains("no effect", check.Reason);
    }

    [SkippableFact]
    public void Perfect_essence_replaces_a_suffix_when_suffixes_are_full_and_adds_a_crafted_mod()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = TestData.NewItem("Siphoning Wand", Rarity.Rare).WithAffixes(1, 3);
        var action = TestData.Action("Perfect Essence of Sorcery"); // wand: +3 spell skills (suffix)

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
        var check = TestData.Engine!.Check(TestData.NewItem("Siphoning Wand", Rarity.Magic), TestData.Action("Essence of Sorcery", "Omen of Dextral Crystallisation"));
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
        var item = TestData.NewItem("Siphoning Wand", Rarity.Rare).WithAffixes(3, 3);

        // with 3P/3S, a prefix can only be added if a prefix is removed
        var prefix = TestData.Pool!.Candidates(item, AffixType.Prefix).First().Mod;
        for (int seed = 0; seed < 20; seed++)
        {
            var result = TestData.Engine!.Execute(item, TestData.Action("Chaos Orb"), new Rng(seed), new ManualChoice { AddModIds = { prefix.Id } });
            Assert.Contains(result.Item.Affixes, m => m.ModId == prefix.Id);
            Assert.Equal(3, result.Item.PrefixCount);
        }
    }

    [SkippableFact]
    public void Preview_after_a_fixed_removal_only_offers_that_affix_type()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var item = TestData.NewItem("Siphoning Wand", Rarity.Rare).WithAffixes(3, 3);
        int suffixIndex = item.Mods.FindIndex(m => m.Affix == AffixType.Suffix);

        var preview = TestData.Engine!.Preview(item, TestData.Action("Chaos Orb"), suffixIndex);
        Assert.True(preview.TwoStepChoice);
        Assert.All(preview.Additions, a => Assert.Equal(AffixType.Suffix, a.Mod.AffixType));
        Assert.Equal(1.0, preview.Additions.Sum(a => a.Probability), 6);
    }
}

public class PlannerTests
{
    internal static TargetItemSpec Spec(Item baseItem, Rarity rarity, IEnumerable<ModDef> mods, bool better = true) => new()
    {
        BaseName = baseItem.BaseName, ItemClass = baseItem.ItemClass, ItemLevel = baseItem.ItemLevel, TargetRarity = rarity,
        TargetMods = mods.Select(m => new TargetMod
        {
            Family = m.Family!, Tier = m.Tier, AffixType = m.AffixType, ResolvedMod = m, AllowBetterTiers = better, Category = m.Category,
        }).ToList(),
    };

    [SkippableFact]
    public void Better_tiers_count_as_hits()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var baseItem = TestData.NewItem("Siphoning Wand");
        var t3 = TestData.Pool!.AllForBase(baseItem, AffixType.Prefix)
            .First(m => m.Family == "WeaponCasterDamagePrefix" && TestData.Pool.DisplayTier(m, baseItem) == 3);

        double Prob(bool better) => new CraftingPathFinder(TestData.Data!, TestData.Pool)
            .FindPathsFromItem(baseItem, Spec(baseItem, Rarity.Magic, new[] { t3 }, better)).Strategies.Max(s => s.OverallProbability);
        Assert.True(Prob(true) > Prob(false));
    }

    [SkippableFact]
    public void Display_tiers_are_per_base_not_global()
    {
        Skip.If(TestData.Data == null, "Data folder not found");
        var staff = TestData.NewItem("Sanctified Staff");
        var glyphic = TestData.Pool!.AllForBase(staff, AffixType.Prefix).Single(m => m.Name == "Glyphic" && m.Family == "WeaponCasterDamagePrefix");
        Assert.Equal(2, TestData.Pool.DisplayTier(glyphic, staff));
    }
}
