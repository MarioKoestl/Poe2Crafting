using System.Text;
using POE2Crafting.Core.Builds;

namespace POE2Crafting.Tests;

public class NinjaBuildsTests
{
    // ---- protobuf / dictionary helpers building poe.ninja-shaped bytes

    private static byte[] Varint(ulong value)
    {
        var bytes = new List<byte>();
        do
        {
            byte b = (byte)(value & 0x7F);
            value >>= 7;
            bytes.Add(value != 0 ? (byte)(b | 0x80) : b);
        } while (value != 0);
        return bytes.ToArray();
    }

    private static byte[] Field(int number, ulong value) => Varint((ulong)(number << 3)).Concat(Varint(value)).ToArray();
    private static byte[] Field(int number, byte[] bytes) => Varint((ulong)((number << 3) | 2)).Concat(Varint((ulong)bytes.Length)).Concat(bytes).ToArray();
    private static byte[] Field(int number, string text) => Field(number, Encoding.UTF8.GetBytes(text));
    private static byte[] Message(params byte[][] fields) => fields.SelectMany(f => f).ToArray();

    private static byte[] Dimension(string id, string dictionary, params (int Key, int Count)[] counts) =>
        Message(new[] { Field(1, id), Field(2, dictionary) }.Concat(counts.Select(c => Field(3, Message(c.Key == 0 ? Array.Empty<byte>() : Field(1, (ulong)c.Key), Field(2, (ulong)c.Count))))).ToArray());

    private static byte[] Ndic(params string[] values)
    {
        var lengths = values.SelectMany(v => Varint((ulong)Encoding.UTF8.GetByteCount(v))).ToArray();
        var header = new byte[36];
        "NDIC"u8.CopyTo(header);
        BitConverter.GetBytes(2).CopyTo(header, 4);
        BitConverter.GetBytes(values.Length).CopyTo(header, 12);
        BitConverter.GetBytes(0).CopyTo(header, 28);
        BitConverter.GetBytes(lengths.Length).CopyTo(header, 32);
        return header.Concat(lengths).Concat(values.SelectMany(v => Encoding.UTF8.GetBytes(v))).ToArray();
    }

    [Fact]
    public void Search_result_counts_resolve_through_dictionaries()
    {
        var result = Message(
            Field(1, 1000UL),
            Field(2, Dimension("class", "class", (0, 600), (1, 400))),
            Field(2, Dimension("items", "item", (0, 950), (1, 120))),
            Field(6, Message(Field(1, "class"), Field(2, "hashClass"))),
            Field(6, Message(Field(1, "item"), Field(2, "hashItem"))),
            Field(12, Message(Field(1, "name"), Field(7, "Alice"), Field(7, "Bob"))),
            Field(12, Message(Field(1, "account"), Field(7, "alice-1"), Field(7, "bob-2"))),
            Field(12, Message(Field(1, "class"), Field(6, new byte[] { 0, 1 }), Field(11, "class"), Field(13, 2UL))),
            Field(12, Message(Field(1, "dps.skill"), Field(6, new byte[] { 1, 1 }), Field(11, "gem"), Field(13, 2UL))),
            Field(6, Message(Field(1, "gem"), Field(2, "hashGem"))));
        var search = BuildSearchResult.Parse(Field(1, result));

        Assert.Equal(1000, search.Total);
        Assert.Equal(new[] { new CharacterRef("alice-1", "Alice"), new CharacterRef("bob-2", "Bob") }, search.Characters);

        var blobs = new Dictionary<string, byte[]> { ["hashClass"] = Ndic("Gemling Legionnaire", "Oracle"), ["hashItem"] = Ndic("Rare Gloves", "Headhunter"), ["hashGem"] = Ndic("Frost Bomb", "Twister") };
        var overview = BuildOverview.From(search, h => NinjaDictionary.Values(blobs[h]), _ => new Dictionary<string, IReadOnlyList<string>>());
        Assert.Equal(new[] { new NamedCount("Gemling Legionnaire", 600), new NamedCount("Oracle", 400) }, overview.Classes);
        Assert.Equal("Rare Gloves", Assert.Single(overview.RareSlots).Name);
        Assert.Equal("Headhunter", Assert.Single(overview.Uniques).Name);
        Assert.Equal(new[] { new BuildCount("Twister", "Gemling Legionnaire", 1), new BuildCount("Twister", "Oracle", 1) }, overview.TopBuilds);
    }

    [Fact]
    public void Filters_become_query_parameters()
    {
        Assert.Equal("", BuildFilter.None.Query);
        Assert.Equal("&class=Gemling%20Legionnaire&skills=Twister", new BuildFilter("Gemling Legionnaire", "Twister").Query);
        Assert.Equal("Twister · Gemling Legionnaire", new BuildFilter("Gemling Legionnaire", "Twister").ToString());
        Assert.Equal("&items=Rare%20Staff", new BuildFilter(Item: "Rare Staff").Query);
    }

    private const string GlovesJson = """
        {"account":"a-1","name":"Hero","league":"Forbidden Rites","level":100,"class":"Gemling Legionnaire",
         "items":[{"itemData":{"inventoryId":"Gloves","rarity":"Rare","frameType":2,"name":"Phoenix Claw","typeLine":"Runeforged Secured Wraps",
           "baseType":"Runeforged Secured Wraps","ilvl":80,"corrupted":false,"identified":true,
           "explicitMods":["36% increased [Projectile] Speed","+2 to Level of all [Projectile] Skills","+22% to [Resistances|Chaos Resistance]"],
           "fracturedMods":["Adds 4 to 67 [Lightning] damage to [Attack|Attacks]"],
           "desecratedMods":["36% increased [Projectile] Damage"],
           "runeMods":["Can roll Marksman modifiers"]}},
          {"itemData":{"inventoryId":"Belt","rarity":"Unique","frameType":3,"name":"Headhunter","baseType":"Heavy Belt","ilvl":79,
           "explicitMods":["+42 to maximum Life"]}}],
         "jewels":[],"flasks":[]}
        """;

    [Fact]
    public void Markup_of_the_official_item_json_is_removed()
    {
        Assert.Equal("+22% to Chaos Resistance", PoeItemJson.StripMarkup("+22% to [Resistances|Chaos Resistance]"));
        Assert.Equal("36% increased Projectile Speed", PoeItemJson.StripMarkup("36% increased [Projectile] Speed"));
    }

    [DataFact]
    public void Character_items_parse_like_imported_items()
    {
        var character = NinjaCharacter.Parse(GlovesJson);
        Assert.Equal(("Hero", "Gemling Legionnaire", 100), (character.Name, character.Class, character.Level));

        var gloves = character.CraftableItems.First().ToItem(TestData.Data!)!;
        Assert.Equal(("Gloves", Rarity.Rare, 80), (gloves.ItemClass, gloves.Rarity, gloves.ItemLevel));
        Assert.Equal(5, gloves.Affixes.Count());
        Assert.All(gloves.Affixes, m => Assert.NotNull(m.Def));
        Assert.Contains(gloves.Affixes, m => m.Fractured && m.DisplayText() == "Adds 4 to 67 Lightning damage to Attacks");
        Assert.Contains(gloves.Affixes, m => m.Kind == ModKind.Desecrated && m.DisplayText() == "36% increased Projectile Damage");
        // runes are augments, not crafted modifiers
        Assert.DoesNotContain(gloves.Mods, m => m.DisplayText().Contains("Marksman"));
    }

    [DataFact]
    public void Analysis_groups_rare_items_by_class_with_modifier_shares()
    {
        var character = NinjaCharacter.Parse(GlovesJson);
        var reference = new CharacterRef(character.Account, character.Name);
        var items = character.CraftableItems.Select(i => i.ToItem(TestData.Data!)).OfType<Item>()
            .Select(item => new SampledItem(reference, character.Class, character.Level, item)).ToList();

        var analysis = RareItemAnalysis.Build(items, 1, TestData.Pool!);

        var gloves = Assert.Single(analysis.Classes); // the unique belt is not counted
        Assert.Equal(("Gloves", 1, 1), (gloves.ItemClass, gloves.Characters, gloves.Items.Count));
        Assert.Equal("Runeforged Secured Wraps", Assert.Single(gloves.Bases).Name);
        var damage = gloves.Mods.Single(m => m.Text == "Adds # to # Lightning damage to Attacks");
        Assert.Equal((1, 1), (damage.Count, damage.Fractured));
        Assert.Equal(35.5, Assert.Single(damage.Values));
        Assert.NotEmpty(damage.Tiers);
        Assert.Equal(1, gloves.Desecrated);
    }

    [DataFact]
    public void Analysis_of_one_base_counts_only_that_base()
    {
        var alice = new CharacterRef("alice-1", "Alice");
        var bob = new CharacterRef("bob-2", "Bob");
        // two rings of the same class but different bases, each with a different modifier
        var iron = TestData.NewItem(TestBases.Ring, Rarity.Rare);
        iron.AddMod(TestData.BestMod(iron, "to maximum Life", AffixType.Prefix));
        var gold = TestData.NewItem("Gold Ring", Rarity.Rare);
        gold.AddMod(TestData.BestMod(gold, "to Fire Resistance", AffixType.Suffix));
        var items = new[]
        {
            new SampledItem(alice, "Gemling Legionnaire", 100, iron),
            new SampledItem(bob, "Gemling Legionnaire", 96, gold),
        };

        var ring = Assert.Single(RareItemAnalysis.Build(items, 2, TestData.Pool!).Classes);
        Assert.Equal(2, ring.Items.Count);

        var onlyGold = ring.ForBase("Gold Ring", TestData.Pool!)!;
        Assert.Equal("Gold Ring", Assert.Single(onlyGold.Items).Item.BaseName);
        Assert.Equal(1, onlyGold.Characters);
        Assert.Contains(onlyGold.Mods, m => m.Text.Contains("Fire Resistance"));
        Assert.DoesNotContain(onlyGold.Mods, m => m.Text.Contains("maximum Life"));
        // the base list stays complete so the filter can be switched
        Assert.Equal(ring.Bases, onlyGold.Bases);
        Assert.Null(ring.ForBase("Prismatic Ring", TestData.Pool!));
    }
}
