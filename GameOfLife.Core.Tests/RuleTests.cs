using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

public class RuleTests
{
    [Theory]
    [InlineData("B3/S23")]
    [InlineData("B36/S23")]   // HighLife
    [InlineData("B/S")]        // empty groups are valid (nothing born, nothing survives)
    [InlineData("B1/S")]
    [InlineData("B12345678/S012345678")]
    public void Parse_accepts_well_formed_rules(string rule)
    {
        var parsed = Rule.Parse(rule);
        Assert.NotNull(parsed);
    }

    [Theory]
    [InlineData("B0/S23")]     // B0 fills the torus — rejected
    [InlineData("B03/S23")]    // B0 anywhere in the birth group — rejected
    [InlineData("B3/S23/")]    // trailing garbage
    [InlineData("b3/s23")]     // lower case
    [InlineData("B3S23")]      // missing slash
    [InlineData("B9/S23")]     // digit out of range
    [InlineData("B33/S23")]    // repeated digit within a group
    [InlineData("B3/S223")]    // repeated digit within survival group
    [InlineData("")]
    [InlineData("garbage")]
    public void Parse_rejects_malformed_or_B0_rules(string rule)
    {
        Assert.Throws<FormatException>(() => Rule.Parse(rule));
        Assert.False(Rule.TryParse(rule, out _));
    }

    [Fact]
    public void TryParse_null_returns_false()
    {
        Assert.False(Rule.TryParse(null, out _));
    }

    [Fact]
    public void B3_S23_birth_and_survival_sets_are_correct()
    {
        var rule = Rule.Parse("B3/S23");

        // Birth only on exactly 3 neighbours.
        for (var n = 0; n <= 8; n++)
            Assert.Equal(n == 3, rule.IsBirth(n));

        // Survival on 2 or 3 neighbours.
        for (var n = 0; n <= 8; n++)
            Assert.Equal(n is 2 or 3, rule.IsSurvival(n));
    }

    [Fact]
    public void ToString_is_canonical_and_ascending()
    {
        Assert.Equal("B3/S23", Rule.Parse("B3/S32").ToString());
        Assert.Equal("B36/S23", Rule.Parse("B63/S23").ToString());
    }
}
