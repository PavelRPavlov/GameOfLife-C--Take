using System.Text.RegularExpressions;

namespace GameOfLife.Core;

/// <summary>
/// A Conway-family birth/survival rule (e.g. <c>B3/S23</c>) applied over the 8-cell Moore
/// neighbourhood. Birth counts turn a dead cell alive; survival counts keep a live cell alive.
/// A rule is immutable for the life of a game.
/// </summary>
/// <remarks>
/// <c>B0</c> is rejected: a rule that births cells with zero live neighbours would instantly
/// fill the entire 2^128-cell torus and blow up the sparse store.
/// </remarks>
public sealed partial class Rule
{
    private readonly bool[] _birth = new bool[9];
    private readonly bool[] _survival = new bool[9];

    private Rule(IReadOnlySet<int> birth, IReadOnlySet<int> survival)
    {
        Birth = birth;
        Survival = survival;
        foreach (var n in birth) _birth[n] = true;
        foreach (var n in survival) _survival[n] = true;
    }

    /// <summary>Neighbour counts (0..8) that bring a dead cell to life. Never contains 0.</summary>
    public IReadOnlySet<int> Birth { get; }

    /// <summary>Neighbour counts (0..8) that keep a live cell alive.</summary>
    public IReadOnlySet<int> Survival { get; }

    /// <summary>True if a dead cell with <paramref name="neighbours"/> live neighbours is born.</summary>
    public bool IsBirth(int neighbours) => (uint)neighbours <= 8 && _birth[neighbours];

    /// <summary>True if a live cell with <paramref name="neighbours"/> live neighbours survives.</summary>
    public bool IsSurvival(int neighbours) => (uint)neighbours <= 8 && _survival[neighbours];

    /// <summary>
    /// Parses a B/S rulestring. Format <c>^B[0-8]*/S[0-8]*$</c>, digits unique within each group.
    /// </summary>
    /// <exception cref="FormatException">
    /// The string is malformed, has a repeated digit within a group, or contains <c>B0</c>.
    /// </exception>
    public static Rule Parse(string rule)
    {
        if (!TryParse(rule, out var parsed))
            throw new FormatException(
                $"Invalid rule '{rule}'. Expected B[0-8]*/S[0-8]* with unique digits per group and no B0.");
        return parsed;
    }

    /// <summary>Non-throwing counterpart to <see cref="Parse"/>.</summary>
    public static bool TryParse(string? rule, out Rule parsed)
    {
        parsed = null!;
        if (rule is null) return false;

        var match = RuleRegex().Match(rule);
        if (!match.Success) return false;

        if (!TryReadGroup(match.Groups["b"].Value, out var birth)) return false;
        if (!TryReadGroup(match.Groups["s"].Value, out var survival)) return false;

        if (birth.Contains(0)) return false; // B0 is rejected outright.

        parsed = new Rule(birth, survival);
        return true;
    }

    private static bool TryReadGroup(string digits, out HashSet<int> set)
    {
        set = new HashSet<int>();
        foreach (var c in digits)
        {
            // Regex already guarantees [0-8]; uniqueness within the group is enforced here.
            if (!set.Add(c - '0')) return false;
        }
        return true;
    }

    /// <summary>Canonical rulestring, digits ascending (e.g. <c>B3/S23</c>).</summary>
    public override string ToString() =>
        $"B{string.Concat(Birth.OrderBy(n => n))}/S{string.Concat(Survival.OrderBy(n => n))}";

    [GeneratedRegex(@"^B(?<b>[0-8]*)/S(?<s>[0-8]*)$")]
    private static partial Regex RuleRegex();
}
