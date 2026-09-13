namespace POE2Crafting.Tests;

public class ModPoolTests
{
    [DataFact]
    public void Display_tiers_are_per_base_not_global()
    {
        var staff = TestData.NewItem(TestBases.Staff);
        var glyphic = TestData.Pool!.AllForBase(staff, AffixType.Prefix).Single(m => m.Name == "Glyphic" && m.Family == "WeaponCasterDamagePrefix");
        Assert.Equal(2, TestData.Pool.DisplayTier(glyphic, staff));
    }

    [DataFact]
    public void Candidate_probabilities_sum_to_one_and_previews_do_not_share_mutable_candidates()
    {
        var item = TestData.NewItem(TestBases.Wand, Rarity.Rare);
        var candidates = TestData.Pool!.Candidates(item, AffixType.Prefix);
        Assert.Equal(1.0, candidates.Sum(c => c.Probability), 6);
        Assert.NotSame(candidates[0], TestData.Pool.Candidates(item, AffixType.Prefix)[0]);
    }
}
