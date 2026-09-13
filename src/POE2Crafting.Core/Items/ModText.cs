using System.Globalization;
using System.Text.RegularExpressions;
using POE2Crafting.Core.Data;

namespace POE2Crafting.Core.Items;

/// <summary>
/// All handling of numbers inside modifier texts: templates ("(105-119)% increased Spell Damage"),
/// rolled item lines ("112(105-119)% increased Spell Damage") and plain lines ("112% increased Spell Damage"),
/// plus the rules for rolling and scaling values inside a range.
/// </summary>
public static class ModText
{
    private const string Num = @"-?\d+(?:\.\d+)?";
    private const string Unsigned = @"\d+(?:\.\d+)?";
    /// <summary>"(a-b)" with capture groups a and b.</summary>
    private const string Range = $@"\((?<a>{Num})\s*-\s*(?<b>{Num})\)";

    /// <summary>"(105-119)" in a template.</summary>
    private static readonly Regex RangeRx = new(Range, RegexOptions.Compiled);
    /// <summary>"112(105-119)" or "112" in an item line.</summary>
    private static readonly Regex RolledRx = new($@"(?<v>{Num})(?:{Range})?", RegexOptions.Compiled);
    /// <summary>A range or a fixed number in a template.</summary>
    private static readonly Regex TemplateTokenRx = new($@"{Range}|(?<v>{Num})", RegexOptions.Compiled);
    /// <summary>Any number representation: rolled value with range, bare range, bare number (the sign stays in the text: "+#").</summary>
    private static readonly Regex AnyNumberRx = new($@"{Num}{Range}|{Range}|{Unsigned}", RegexOptions.Compiled);
    /// <summary>A bare number without sign.</summary>
    private static readonly Regex PlainNumberRx = new(Unsigned, RegexOptions.Compiled);
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

    // ------------------------------------------------------------------ ranges and rolls

    public static bool IsIntegerRange(double min, double max) => min == Math.Floor(min) && max == Math.Floor(max);

    /// <summary>The range with its bounds in ascending order (negative ranges are stored as "(-10--20)").</summary>
    public static (double Lo, double Hi) Bounds(double[] range) => (Math.Min(range[0], range[1]), Math.Max(range[0], range[1]));

    /// <summary>A value rounded like rolls of the range: integer ranges give integers (away from zero), fractional ranges two decimals.</summary>
    private static double RoundForRange(double v, double[] range) =>
        IsIntegerRange(range[0], range[1]) ? Math.Round(v, MidpointRounding.AwayFromZero) : Math.Round(v, 2);

    /// <summary>Middle of a range, rounded like its rolls.</summary>
    public static double MidValue(double[] range) => RoundForRange((range[0] + range[1]) / 2, range);

    /// <summary>Every value a roll of the range can have, ascending: whole numbers, or hundredths for fractional ranges.</summary>
    public static IEnumerable<double> PossibleRolls(double[] range)
    {
        var (lo, hi) = Bounds(range);
        double step = IsIntegerRange(lo, hi) ? 1 : 0.01;
        for (int k = 0; lo + k * step <= hi + 1e-9; k++) yield return Math.Round(lo + k * step, 2);
    }

    /// <summary>Chance that a uniform roll in the range is at least <paramref name="min"/> (integer ranges roll whole numbers).</summary>
    public static double ChanceAtLeast(double[] range, double min)
    {
        var (lo, hi) = Bounds(range);
        if (min <= lo) return 1;
        if (min > hi) return 0;
        return IsIntegerRange(lo, hi) ? (hi - Math.Ceiling(min) + 1) / (hi - lo + 1) : (hi - min) / (hi - lo);
    }

    /// <summary>Map rolled values to other ranges keeping each value's relative position (e.g. 38 in 36-40 → 22 in 20-23).</summary>
    public static List<double> RescaleValues(IReadOnlyList<double> values, IReadOnlyList<double[]> from, IReadOnlyList<double[]> to) =>
        to.Select((range, i) =>
        {
            if (i >= values.Count || i >= from.Count || from[i][1] == from[i][0]) return MidValue(range);
            double position = (values[i] - from[i][0]) / (from[i][1] - from[i][0]);
            return RoundForRange(range[0] + position * (range[1] - range[0]), range);
        }).ToList();

    /// <summary>A template with every range replaced by its middle value (e.g. a base implicit for display).</summary>
    public static string RenderMid(string template) => Render(template, ParseRanges(template).Select(MidValue).ToList());

    // ------------------------------------------------------------------ catalyst scaling

    /// <summary>
    /// Every number of a rendered line multiplied by <paramref name="factor"/> (catalyst quality: "+3 to Level of all Melee Skills" at 34% → +4).
    /// Whole numbers round down (3 × 1.33 = 3.99 stays 3, 3 × 1.34 = 4.02 becomes 4), fractions keep two decimals.
    /// </summary>
    public static string ScaleNumbers(string text, double factor) =>
        PlainNumberRx.Replace(text, m => FormatValue(ScaleValue(ParseNumber(m.Value), factor)));

    /// <summary>Values multiplied by <paramref name="factor"/> with the rounding of <see cref="ScaleNumbers"/>.</summary>
    public static List<double> ScaleValues(IEnumerable<double> values, double factor) => values.Select(v => ScaleValue(v, factor)).ToList();

    private static double ScaleValue(double value, double factor)
    {
        double v = Math.Round(value * factor, 6);
        return value == Math.Floor(value) ? Math.Floor(v) : Math.Floor(v * 100) / 100;
    }

    // ------------------------------------------------------------------ special lines

    /// <summary>"+1 Suffix Modifier allowed" (Potent Liquid Contempt).</summary>
    private static readonly Regex ExtraAffixRx = new(@"^\+(?<n>\d+) (?<type>Prefix|Suffix) Modifiers? allowed$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Extra affix slots of <paramref name="type"/> that a mod line grants ("+1 Suffix Modifier allowed" → 1 for suffixes).</summary>
    public static int ExtraAffixesAllowed(string text, AffixType type) =>
        ExtraAffixRx.Match(text.Trim()) is { Success: true } m && m.Groups["type"].Value.Equals(type.ToString(), StringComparison.OrdinalIgnoreCase)
            ? int.Parse(m.Groups["n"].Value, CultureInfo.InvariantCulture)
            : 0;

    private static double ParseNumber(string s) => double.Parse(s, CultureInfo.InvariantCulture);
}
