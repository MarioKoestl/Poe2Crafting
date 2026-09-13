using System.Globalization;
using System.Text.RegularExpressions;

namespace POE2Crafting.Core.Items;

/// <summary>
/// All handling of numbers inside modifier texts: templates ("(105-119)% increased Spell Damage"),
/// rolled item lines ("112(105-119)% increased Spell Damage") and plain lines ("112% increased Spell Damage").
/// </summary>
public static class ModText
{
    private const string Num = @"-?\d+(?:\.\d+)?";

    /// <summary>"(105-119)" in a template.</summary>
    private static readonly Regex RangeRx = new($@"\((?<a>{Num})\s*-\s*(?<b>{Num})\)", RegexOptions.Compiled);
    /// <summary>"112(105-119)" or "112" in an item line.</summary>
    private static readonly Regex RolledRx = new($@"(?<v>{Num})(?:\((?<a>{Num})\s*-\s*(?<b>{Num})\))?", RegexOptions.Compiled);
    /// <summary>A range or a fixed number in a template.</summary>
    private static readonly Regex TemplateTokenRx = new($@"\((?<a>{Num})\s*-\s*(?<b>{Num})\)|(?<v>{Num})", RegexOptions.Compiled);
    /// <summary>Any number representation: rolled value with range, bare range, bare number (sign stays in the text).</summary>
    private static readonly Regex AnyNumberRx = new($@"{Num}\({Num}\s*-\s*{Num}\)|\({Num}\s*-\s*{Num}\)|\d+(?:\.\d+)?", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRx = new(@"\s+", RegexOptions.Compiled);

    public readonly record struct RolledToken(double Value, double[]? Range);

    /// <summary>Replace each "(a-b)" range with the corresponding rolled value. Missing values keep the range text.</summary>
    public static string Render(string template, IReadOnlyList<double> values)
    {
        int i = 0;
        return RangeRx.Replace(template, m => i < values.Count ? FormatValue(values[i++]) : m.Value);
    }

    public static string FormatValue(double v) =>
        v == Math.Floor(v) ? ((long)v).ToString(CultureInfo.InvariantCulture) : v.ToString("0.##", CultureInfo.InvariantCulture);

    public static List<double[]> ParseRanges(string template) =>
        RangeRx.Matches(template).Select(m => new[] { ParseNumber(m.Groups["a"].Value), ParseNumber(m.Groups["b"].Value) }).ToList();

    /// <summary>All numbers of an item text in order, each with its "(min-max)" range when the advanced item text shows one.</summary>
    public static List<RolledToken> RolledTokens(string text) =>
        RolledRx.Matches(text).Select(m => new RolledToken(
            ParseNumber(m.Groups["v"].Value),
            m.Groups["a"].Success ? new[] { ParseNumber(m.Groups["a"].Value), ParseNumber(m.Groups["b"].Value) } : null)).ToList();

    /// <summary>For each number in a template: true for a rollable range, false for a fixed number.</summary>
    public static List<bool> TemplateTokenIsRange(string template) =>
        TemplateTokenRx.Matches(template).Select(m => m.Groups["a"].Success).ToList();

    /// <summary>"+(209-248) to maximum Mana", "+238(209-248) to maximum Mana" and "+238 to maximum Mana" all become "+# to maximum Mana".</summary>
    public static string StripNumbers(string text) => WhitespaceRx.Replace(AnyNumberRx.Replace(text, "#"), " ").Trim();

    /// <summary>
    /// The stat a mod grants with all numbers removed, lower-cased, e.g. "+# to level of all physical spell skills".
    /// Used to match item lines to templates, and for tiers: one family can hold several stats (Physical/Fire/... Spell Skill levels).
    /// </summary>
    public static string StatSignature(string text) => StripNumbers(text).ToLowerInvariant();

    public static bool IsIntegerRange(double min, double max) => min == Math.Floor(min) && max == Math.Floor(max);

    /// <summary>Middle of a range; integer ranges give integers (rounded away from zero), fractional ranges two decimals.</summary>
    public static double MidValue(double[] range)
    {
        double mid = (range[0] + range[1]) / 2;
        return IsIntegerRange(range[0], range[1]) ? Math.Round(mid, MidpointRounding.AwayFromZero) : Math.Round(mid, 2);
    }

    /// <summary>Map rolled values to other ranges keeping each value's relative position (e.g. 38 in 36-40 → 22 in 20-23).</summary>
    public static List<double> RescaleValues(IReadOnlyList<double> values, IReadOnlyList<double[]> from, IReadOnlyList<double[]> to) =>
        to.Select((range, i) =>
        {
            if (i >= values.Count || i >= from.Count || from[i][1] == from[i][0]) return MidValue(range);
            double position = (values[i] - from[i][0]) / (from[i][1] - from[i][0]);
            double v = range[0] + position * (range[1] - range[0]);
            return IsIntegerRange(range[0], range[1]) ? Math.Round(v, MidpointRounding.AwayFromZero) : Math.Round(v, 2);
        }).ToList();

    /// <summary>A template with every range replaced by its middle value (e.g. a base implicit for display).</summary>
    public static string RenderMid(string template) => Render(template, ParseRanges(template).Select(MidValue).ToList());

    private static double ParseNumber(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
