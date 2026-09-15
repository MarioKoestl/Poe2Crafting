using System.Net;
using System.Text;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Engine.Planning;
using POE2Crafting.Core.Items;

namespace POE2Crafting.Web.Services;

/// <summary>The content of an exported guide: a curated or saved guide, or a planner strategy.</summary>
public sealed record GuideExport(string Title, string? Summary, Item Start, CraftingStrategy Strategy)
{
    public IReadOnlyList<string> KeyIdeas { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> NextSteps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();
    public string? Source { get; init; }
}

/// <summary>
/// A crafting guide as one self-contained HTML file (inline styles and embedded item icons, no scripts or external files) to send to someone: starting and final item,
/// materials, every step with chance, explanation, mod changes and the item after it (no rule/assumption notes).
/// </summary>
public static class GuideHtmlExport
{
    public static string FileName(string title)
    {
        var safe = new string(title.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (safe.Contains("--")) safe = safe.Replace("--", "-");
        return (safe.Length > 0 ? safe : "crafting-guide") + ".html";
    }

    /// <param name="itemIcon">The embeddable image (data URI) of an item's base, or null.</param>
    public static string ToHtml(GuideExport guide, ModPool pool, Func<Item, string?> itemIcon)
    {
        var steps = guide.Strategy.Steps;
        var html = new StringBuilder();
        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>").Append(E(guide.Title)).Append("</title><style>").Append(Css).Append("</style></head><body><main>");

        html.Append("<h1>").Append(E(guide.Title)).Append("</h1>");
        if (!string.IsNullOrWhiteSpace(guide.Summary)) html.Append("<p class=\"summary\">").Append(E(guide.Summary)).Append("</p>");
        html.Append("<p class=\"meta\">Base: <strong>").Append(E(guide.Start.BaseName)).Append("</strong> (")
            .Append(guide.Start.Rarity).Append(", item level ").Append(guide.Start.ItemLevel).Append(") · ")
            .Append(UiFormat.Plural(steps.Count, "step")).Append(" · overall chance ").Append(UiFormat.Probability(guide.Strategy.OverallProbability)).Append("</p>");
        if (guide.Source != null) html.Append("<p class=\"meta\">Source: ").Append(E(guide.Source)).Append("</p>");
        List(html, "Problems", guide.Problems, "problems");
        List(html, "How it works", guide.KeyIdeas);

        html.Append("<div class=\"items\"><section><h2>Starting item</h2>");
        ItemBox(html, guide.Start, pool, itemIcon);
        html.Append("</section><section><h2>Final item</h2>");
        ItemBox(html, steps.LastOrDefault()?.Result ?? guide.Start, pool, itemIcon);
        html.Append("</section></div>");

        html.Append("<h2>Materials</h2><table><thead><tr><th>Item</th><th>Per run</th><th>≈ with retries</th></tr></thead><tbody>");
        foreach (var material in steps.Materials())
            html.Append("<tr><td>").Append(E(material.Name)).Append("</td><td>").Append(material.PerRun)
                .Append("</td><td>").Append(E(UiFormat.Attempts(material.Expected))).Append("</td></tr>");
        html.Append("</tbody></table>");

        html.Append("<h2>Steps</h2><ol class=\"steps\">");
        for (int i = 0; i < steps.Count; i++)
        {
            var step = steps[i];
            var before = i == 0 ? guide.Start : steps[i - 1].Result;
            html.Append("<li><div class=\"step-head\"><span class=\"currency\">").Append(E(step.CurrencyName)).Append("</span><span class=\"chance\">")
                .Append(UiFormat.Probability(step.SuccessProbability)).Append("</span></div>");
            html.Append("<div class=\"desc\">").Append(E(step.Description)).Append("</div>");
            if (step.Explanation != null) html.Append("<p>").Append(E(step.Explanation)).Append("</p>");
            if (step.BrickProbability is > 0) html.Append("<p class=\"warn\">⚠ ").Append(UiFormat.Probability(step.BrickProbability.Value)).Append(" chance to destroy a wanted mod</p>");
            if (before != null && step.Result != null)
            {
                var diff = ItemDiff.Between(before, step.Result);
                if (diff.Count > 0)
                {
                    html.Append("<ul class=\"diff\">");
                    foreach (var line in diff)
                        html.Append("<li class=\"").Append(line.Kind.ToString().ToLowerInvariant()).Append("\">")
                            .Append(line.Kind switch { DiffKind.Removed => "− ", DiffKind.Added => "+ ", _ => "~ " }).Append(E(line.Text)).Append("</li>");
                    html.Append("</ul>");
                }
            }
            if (step.RestartLabel != null && step.SuccessProbability < 1) html.Append("<p class=\"note\">On fail: ").Append(E(step.RestartLabel)).Append("</p>");
            if (step.Result != null)
            {
                html.Append("<details><summary>Item after this step</summary>");
                ItemBox(html, step.Result, pool, itemIcon);
                html.Append("</details>");
            }
            html.Append("</li>");
        }
        html.Append("</ol>");
        List(html, "Afterwards", guide.NextSteps);

        html.Append("<footer>Exported from the POE2 Crafting Simulator on ").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Append(". Chances use the simulator's rules and poe2db weight estimates.</footer></main></body></html>");
        return html.ToString();
    }

    private static void ItemBox(StringBuilder html, Item item, ModPool pool, Func<Item, string?> itemIcon)
    {
        html.Append("<div class=\"item ").Append(item.Rarity.ToString().ToLowerInvariant()).Append("\">");
        if (itemIcon(item) is { } icon) html.Append("<img class=\"icon\" alt=\"\" src=\"").Append(icon).Append("\">");
        if (item.Name != null) html.Append("<div class=\"name\">").Append(E(item.Name)).Append("</div>");
        html.Append("<div class=\"name\">").Append(E(item.BaseName)).Append("</div><div class=\"props\">Item Level ").Append(item.ItemLevel);
        if (item.Quality > 0) html.Append(" · Quality ").Append(E(item.QualityText));
        html.Append("</div>");
        foreach (var mod in item.Mods.Where(m => m.Kind.IsImplicitLine())) html.Append("<div class=\"implicit\">").Append(E(mod.DisplayText())).Append("</div>");
        foreach (var mod in item.Affixes.OrderBy(m => m.Affix))
        {
            var tier = mod.Def != null && mod.Kind != ModKind.Crafted && pool.TryDisplayTier(mod.Def, item) is { } t ? $"T{t} " : "";
            var flags = string.Join(", ", new[] { mod.Fractured ? "fractured" : null, mod.Kind is ModKind.Desecrated or ModKind.Crafted ? mod.Kind.ToString().ToLowerInvariant() : null }.OfType<string>());
            html.Append("<div class=\"mod ").Append(mod.Affix.ToString().ToLowerInvariant()).Append("\"><span class=\"tag\">")
                .Append(mod.Affix.ToString()[0]).Append("</span><span class=\"text\">")
                .Append(E(tier + item.EffectiveText(mod)));
            if (!mod.Unrevealed && mod.Def != null && ModText.RangesText(mod.Def.Ranges) is { } range) html.Append(" <span class=\"range\">(").Append(E(range)).Append(")</span>");
            if (flags.Length > 0) html.Append(" <em>(").Append(E(flags)).Append(")</em>");
            html.Append("</span>");
            if (mod.Def != null) html.Append("<span class=\"level\">ilvl ").Append(mod.Def.Level).Append("</span>");
            html.Append("</div>");
        }
        html.Append("</div>");
    }

    private static void List(StringBuilder html, string title, IReadOnlyList<string> items, string? css = null)
    {
        if (items.Count == 0) return;
        html.Append("<h2>").Append(E(title)).Append("</h2><ul").Append(css != null ? $" class=\"{css}\"" : "").Append('>');
        foreach (var item in items) html.Append("<li>").Append(E(item)).Append("</li>");
        html.Append("</ul>");
    }

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private const string Css = """
        body{margin:0;background:#0f1117;color:#d3d0c7;font:15px/1.5 'Segoe UI',system-ui,sans-serif}
        main{max-width:860px;margin:0 auto;padding:24px 16px 48px}
        h1{color:#e3c27e;font-size:1.6rem;margin:0 0 .4rem}h2{color:#f2efe7;font-size:1.05rem;margin:1.6rem 0 .5rem;border-bottom:1px solid #313746;padding-bottom:.25rem}
        .summary{font-size:1rem}.meta{color:#8a877c;margin:.2rem 0}
        .items{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:16px}.items h2{margin-top:1rem}
        .item{background:#0a0b10;border:1px solid #313746;border-top:2px solid #c8c4b8;border-radius:8px;padding:12px;font-size:.9rem}
        .item .icon{display:block;width:48px;height:48px;object-fit:contain;margin:0 auto .3rem}
        .item.magic{border-top-color:#7b95ff}.item.rare{border-top-color:#f5d65a}.item .name{text-align:center;font-weight:600;color:#f2efe7}
        .item .props{text-align:center;color:#8a877c;font-size:.8rem;margin-bottom:.4rem}.implicit{color:#6f93db;text-align:center}
        .mod{display:flex;gap:.4rem;align-items:baseline}.mod .text{flex:1}.mod.prefix{color:#7b9be0}.mod.suffix{color:#6fcf94}.mod .tag{width:1.2em;flex-shrink:0;color:#8a877c;font-weight:700}
        .mod em{color:#b983e0;font-style:normal;font-size:.8rem}.mod .range,.mod .level{color:#8a877c;font-size:.75rem}.mod .level{white-space:nowrap}
        table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:4px 8px;border-bottom:1px solid #232733}th{color:#8a877c;font-size:.8rem;text-transform:uppercase}
        .steps{padding-left:1.4rem}.steps>li{margin:0 0 14px;padding:10px 12px;background:#171a22;border:1px solid #232733;border-radius:8px}
        .step-head{display:flex;justify-content:space-between;gap:12px;font-weight:600}.currency{color:#53b8dd}.chance{color:#e3c27e}.desc{color:#f2efe7}
        .diff{list-style:none;padding:0;margin:.4rem 0}.diff .added{color:#9ad07a}.diff .removed{color:#f08a8a}.diff .changed{color:#e3c27e}
        .note{color:#8a877c;font-size:.85rem;margin:.2rem 0}.warn{color:#d99440}.problems li{color:#d99440}
        details{margin-top:.5rem}summary{cursor:pointer;color:#8a877c;font-size:.85rem}details .item{max-width:420px;margin-top:.4rem}
        footer{margin-top:2rem;color:#8a877c;font-size:.8rem}
        """;
}
