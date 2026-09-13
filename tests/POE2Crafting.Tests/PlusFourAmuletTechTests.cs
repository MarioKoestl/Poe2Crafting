namespace POE2Crafting.Tests;

/// <summary>The "+4 skills amulet" technique (Abyss mark → desecrate → fracture 1/3, Breach quality + Reaver catalysts, whittle off the Breach mod).</summary>
public class PlusFourAmuletTechTests
{

    [DataFact]
    public void Abyss_essence_with_sinistral_crystallisation_puts_the_mark_on_a_prefix()
    {
        var item = TestData.NewItem(TestBases.Amulet, Rarity.Rare).WithAffixes(2, 2);
        var preview = TestData.Engine!.Preview(item, TestData.Action("Essence of the Abyss", "Omen of Sinistral Crystallisation"));
        Assert.True(preview.Applicability.Ok, preview.Applicability.Reason);
        var addition = Assert.Single(preview.Additions);
        Assert.Equal(AffixType.Prefix, addition.Mod.AffixType);
        Assert.Equal(1.0, addition.Probability, 6);

        for (int seed = 0; seed < 5; seed++)
        {
            var marked = TestData.Apply(item, "Essence of the Abyss", seed: seed, omens: "Omen of Sinistral Crystallisation").Item;
            Assert.Equal(AffixType.Prefix, Assert.Single(marked.Affixes, m => m.Def?.Family == ModFamilies.AbyssMark).Affix);
            Assert.Equal(2, marked.PrefixCount);
        }
    }

    [DataFact]
    public void Desecrated_mods_cannot_be_fractured_so_three_mods_share_the_fracture()
    {
        var item = TestData.NewItem(TestBases.Amulet, Rarity.Rare).WithAffixes(2, 1);
        item.Mods.Add(new ItemMod { Kind = ModKind.Desecrated, Affix = AffixType.Suffix, Unrevealed = true });

        var preview = TestData.Engine!.Preview(item, TestData.Action("Fracturing Orb"));
        Assert.True(preview.Applicability.Ok, preview.Applicability.Reason);
        Assert.Equal(3, preview.Removals.Count);
        Assert.All(preview.Removals, r => Assert.Equal(1.0 / 3, r.Probability, 6));
    }

    [DataFact]
    public void Reaver_quality_of_34_percent_turns_plus_three_melee_skills_into_plus_four()
    {
        var item = TestData.NewItem(TestBases.Amulet, Rarity.Rare);
        var melee = item.AddMod(TestData.BestMod(item, "Level of all Melee Skills"));
        item.QualityType = "Attack";
        item.Quality = 33;
        Assert.Equal("+3 to Level of all Melee Skills", item.EffectiveText(melee));
        item.Quality = 34;
        Assert.Equal("+4 to Level of all Melee Skills", item.EffectiveText(melee));
    }

    [DataFact]
    public void Breach_quality_catalysts_and_whittling_keep_the_extra_quality()
    {
        var item = TestData.NewItem(TestBases.Amulet, Rarity.Rare);
        item.AddMod(TestData.BestMod(item, "Level of all Melee Skills")).Fractured = true;
        item.AddMod(TestData.BestMod(item, "to Spirit"));
        item.AddMod(TestData.Pool!.AllForBase(item, AffixType.Suffix).First(m => m.Text.Contains("Strength")));

        // Essence of the Breach replaces the dead suffix (Dextral Crystallisation) with +20% maximum quality
        var breach = TestData.Apply(item, "Essence of the Breach", omens: "Omen of Dextral Crystallisation").Item;
        Assert.DoesNotContain(breach.Affixes, m => m.Def!.Text.Contains("Strength"));
        Assert.Equal(40, breach.MaxQuality(TestData.Data!.Config.Assumptions.DefaultMaxQuality));

        var quality = breach;
        for (int i = 0; i < 8; i++) quality = TestData.Apply(quality, "Reaver Catalyst").Item;
        Assert.Equal(40, quality.Quality);
        Assert.Equal("+4 to Level of all Melee Skills", quality.EffectiveText(quality.Affixes.Single(m => m.Fractured)));

        // an unrevealed desecrated mod counts as level 1 for whittling, so reveal first; then whittling hits the level-1 Breach mod
        var withUnrevealed = quality.Clone();
        withUnrevealed.Mods.Add(new ItemMod { Kind = ModKind.Desecrated, Affix = AffixType.Suffix, Unrevealed = true });
        var whittle = TestData.Engine!.Preview(withUnrevealed, TestData.Action("Greater Chaos Orb", "Omen of Whittling"));
        Assert.Equal(2, whittle.Removals.Count);

        var whittled = TestData.Apply(quality, "Greater Chaos Orb", omens: "Omen of Whittling").Item;
        Assert.DoesNotContain(whittled.Affixes, m => m.Def?.Family == ModFamilies.MaximumQuality);
        Assert.Equal(40, whittled.Quality);
        Assert.Equal("+4 to Level of all Melee Skills", whittled.EffectiveText(whittled.Affixes.Single(m => m.Fractured)));
    }

    // ---- the planner finds the technique on its own

    /// <summary>Rare Gold Amulet: fractured +3 melee skills and spirit, plus the given extra mods; target: the same two with melee skills at +4.</summary>
    private static (Item Current, PlanResult Plan) PlanPlusFour(bool withDeadSuffix)
    {
        var item = TestData.NewItem(TestBases.Amulet, Rarity.Rare);
        var melee = TestData.BestMod(item, "Level of all Melee Skills");
        var spirit = TestData.BestMod(item, "to Spirit");
        item.AddMod(melee).Fractured = true;
        item.AddMod(spirit);
        if (withDeadSuffix) item.AddMod(TestData.Pool!.AllForBase(item, AffixType.Suffix).First(m => m.Text.Contains("Strength")));

        var spec = TestData.Spec(Rarity.Rare, new[] { melee, spirit });
        spec.TargetMods[0].MinValues = new List<double?> { 4 };
        return (item, TestData.Plan(item, spec));
    }

    private static void AssertPlusFour(CraftingStrategy strategy)
    {
        var final = strategy.Steps.Last().Result!;
        Assert.Equal("+4 to Level of all Melee Skills", final.EffectiveText(final.Affixes.Single(m => m.Fractured)));
        Assert.DoesNotContain(final.Affixes, m => m.Def?.Family == ModFamilies.MaximumQuality);
        Assert.Equal(2, final.AffixCount);
    }

    [DataFact]
    public void Planner_raises_maximum_quality_with_breach_applies_attack_catalysts_and_removes_the_breach_mod()
    {
        var (_, plan) = PlanPlusFour(withDeadSuffix: true);

        Assert.True(plan.Strategies.Count > 0, string.Join(" / ", plan.Problems));
        var best = plan.Strategies[0];
        var names = best.Steps.Select(s => s.CurrencyName).ToList();
        int breach = names.FindIndex(n => n.Contains("Essence of the Breach"));
        int catalyst = names.FindIndex(n => n.Contains("Catalyst"));
        Assert.True(breach >= 0 && catalyst > breach, string.Join(" → ", names));
        Assert.True(best.Steps[catalyst].Result!.Quality >= 34);
        AssertPlusFour(best);
    }

    [DataFact]
    public void Planner_adds_a_blocker_when_the_breach_essence_has_no_mod_to_replace()
    {
        var (_, plan) = PlanPlusFour(withDeadSuffix: false);

        Assert.True(plan.Strategies.Count > 0, string.Join(" / ", plan.Problems));
        var best = plan.Strategies[0];
        var descriptions = best.Steps.Select(s => $"{s.CurrencyName}: {s.Description}").ToList();
        Assert.Contains(best.Steps, s => s.Description.Contains("blocker"));
        Assert.True(descriptions.FindIndex(d => d.Contains("blocker")) < descriptions.FindIndex(d => d.Contains("Essence of the Breach")), string.Join(" → ", descriptions));
        AssertPlusFour(best);
    }

    [DataFact]
    public void Planner_uses_catalysing_exaltation_when_catalyst_quality_favours_the_missing_mod()
    {
        var item = TestData.NewItem(TestBases.Amulet, Rarity.Rare);
        var spirit = TestData.BestMod(item, "to Spirit");
        item.AddMod(spirit);
        var life = TestData.Pool!.AllForBase(item, AffixType.Prefix).Where(m => m.Text.Contains("to maximum Life")).MaxBy(m => m.Level)!;
        var spec = TestData.Spec(Rarity.Rare, new[] { spirit, life }, better: false);

        var plan = TestData.Plan(item, spec);
        var plain = plan.Strategies.Single(s => s.Id == "item-basic-currency");
        var best = plan.Strategies[0];
        Assert.Contains(best.Steps, s => s.CurrencyName == "Flesh Catalyst");
        Assert.Contains(best.Steps, s => s.CurrencyName.Contains("Omen of Catalysing Exaltation"));
        Assert.True(best.OverallProbability > plain.OverallProbability);
    }
}
