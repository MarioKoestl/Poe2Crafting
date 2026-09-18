using System.Text.RegularExpressions;

namespace POE2Crafting.Tests;

/// <summary>The in-app knowledge base (data/wiki.json) has to stay consistent with the data it describes.</summary>
public class WikiTests
{
    private static IReadOnlyList<WikiArticle> Articles => TestData.Data!.Wiki;

    [DataFact]
    public void Every_article_has_an_id_a_title_a_category_and_a_summary()
    {
        Assert.NotEmpty(Articles);
        Assert.All(Articles, a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Id), "article without id");
            Assert.False(string.IsNullOrWhiteSpace(a.Title), a.Id);
            Assert.False(string.IsNullOrWhiteSpace(a.Category), a.Id);
            Assert.False(string.IsNullOrWhiteSpace(a.Summary), a.Id);
            Assert.NotEmpty(a.Sections);
        });
        Assert.Equal(Articles.Count, Articles.Select(a => a.Id).Distinct().Count());
    }

    [DataFact]
    public void Related_links_point_at_existing_articles()
    {
        var ids = Articles.Select(a => a.Id).ToHashSet();
        Assert.All(Articles, a => Assert.All(a.Related, r => Assert.True(ids.Contains(r), $"{a.Id} links to unknown article {r}")));
    }

    /// <summary>A [[name]] in the text renders as a hoverable crafting item, so an unknown name would silently lose its link.</summary>
    [DataFact]
    public void Marked_crafting_item_names_are_known()
    {
        var marked = Articles.SelectMany(a => a.SearchTexts)
            .SelectMany(t => Regex.Matches(t, @"\[\[([^\]]+)\]\]").Select(m => m.Groups[1].Value))
            .Distinct().ToList();
        Assert.NotEmpty(marked);
        Assert.All(marked, name => Assert.True(TestData.Data!.FindCraftItem(name) != null, $"unknown crafting item \"{name}\""));
    }

    [DataFact]
    public void Tables_have_a_cell_for_every_column()
    {
        foreach (var article in Articles)
            foreach (var table in article.Sections.Select(s => s.Table).OfType<WikiTable>())
            {
                Assert.NotEmpty(table.Columns);
                Assert.All(table.Rows, row => Assert.Equal(table.Columns.Count, row.Count));
            }
    }

    /// <summary>The mana leech example of the item level article has to match the real data.</summary>
    [DataFact]
    public void The_minimum_modifier_level_example_matches_the_data()
    {
        var article = Assert.Single(Articles, a => a.Id == "item-level-and-minimum-modifier-level");
        var table = Assert.Single(article.Sections.Select(s => s.Table).OfType<WikiTable>().Where(t => t.Columns.Contains("Modifier level")));

        var ring = TestData.NewItem(TestBases.Ring, Rarity.Rare, itemLevel: 100);
        foreach (var row in table.Rows)
        {
            var (name, level, on) = (row[1], int.Parse(row[2]), row[3]);
            var mod = Assert.Single(TestData.Data!.Mods, m => m.Name == name && m.Text.Contains("Physical Attack Damage as Mana"));
            Assert.Equal(level, mod.Level);
            Assert.Equal(on.Contains("ring"), TestData.Pool!.AllForBase(ring).Any(m => m.Id == mod.Id));
        }
    }
}
