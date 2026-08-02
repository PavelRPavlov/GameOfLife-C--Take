using GameOfLife.Core;

namespace GameOfLife.Core.Tests;

/// <summary>
/// The <see cref="Universe"/> value object and the engine's use of it: which coordinate type names
/// are admissible, that each yields the right power-of-two wrap mask, and that a narrower universe
/// actually changes the engine's wraparound — a smaller torus is what makes wrapping testable at all.
/// </summary>
public class UniverseTests
{
    [Theory]
    [InlineData("byte", 8)]
    [InlineData("uint8", 8)]
    [InlineData("ushort", 16)]
    [InlineData("UInt16", 16)]
    [InlineData("uint", 32)]
    [InlineData("UInt32", 32)]
    [InlineData("ulong", 64)]
    [InlineData("UInt64", 64)]
    [InlineData("  UInt64  ", 64)] // surrounding whitespace is tolerated
    public void Given_a_wrap_capable_type_name_When_parsed_Then_it_yields_its_bit_width(string typeName, int expectedBitWidth)
    {
        Assert.True(Universe.TryParseCoordinateType(typeName, out var universe));
        Assert.Equal(expectedBitWidth, universe.BitWidth);
    }

    [Theory]
    [InlineData("int")]      // signed
    [InlineData("long")]     // signed
    [InlineData("Int64")]    // signed (CLR name)
    [InlineData("sbyte")]    // signed
    [InlineData("UInt128")]  // wraps, but wider than the ulong coordinate can hold
    [InlineData("float")]    // non-integer
    [InlineData("decimal")]  // non-integer
    [InlineData("string")]   // not a number
    [InlineData("")]         // empty
    [InlineData("   ")]      // blank
    [InlineData(null)]       // missing
    public void Given_a_non_wrap_capable_type_name_When_parsed_Then_it_is_rejected(string? typeName)
    {
        Assert.False(Universe.TryParseCoordinateType(typeName, out _));
    }

    [Theory]
    [InlineData(8, 0xFFUL)]
    [InlineData(16, 0xFFFFUL)]
    [InlineData(32, 0xFFFF_FFFFUL)]
    [InlineData(64, ulong.MaxValue)]
    public void Given_a_universe_of_a_given_bit_width_When_its_wrap_mask_is_read_Then_it_is_two_to_the_bit_width_minus_one(int bitWidth, ulong expectedMask)
    {
        var universe = new Universe(bitWidth);

        Assert.Equal(expectedMask, universe.WrapMask);
        Assert.Equal(0UL, universe.Wrap(0));
        // One past the top edge folds back to the origin — the defining torus behaviour.
        Assert.Equal(0UL, universe.Wrap(universe.WrapMask + 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(63)]
    [InlineData(128)]
    public void Given_a_non_power_of_two_bit_width_When_a_universe_is_constructed_Then_it_is_rejected(int bitWidth)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Universe(bitWidth));
    }

    [Fact]
    public void Given_the_full_universe_When_its_dimensions_are_read_Then_it_is_the_2_to_the_64_torus()
    {
        Assert.Equal(64, Universe.Full.BitWidth);
        Assert.Equal(ulong.MaxValue, Universe.Full.WrapMask);
    }

    // The seed is a horizontal blinker whose three cells are 255, 0, 1 (mod 256) — consecutive ONLY if
    // the axis wraps at 256. On a Byte (2^8) torus it is a valid blinker and rotates to vertical; on
    // the default 2^64 torus 255 and 0 are 255 apart, so the same cells are not adjacent at all and
    // die out. One seed, two outcomes — proof the configured width really drives wraparound.
    [Fact]
    public void Given_a_blinker_across_the_seam_When_advanced_Then_it_wraps_on_a_byte_universe_but_not_on_the_full_one()
    {
        var rule = Rule.Parse("B3/S23");
        Cell[] seamBlinker = [new(255, 10), new(0, 10), new(1, 10)];

        var onByteTorus = new GameEngine(seamBlinker, rule, new Universe(8)).Advance();
        Assert.Equal(
            new HashSet<Cell> { new(0, 9), new(0, 10), new(0, 11) },
            onByteTorus.LiveCells);

        var onFullTorus = new GameEngine(seamBlinker, rule, Universe.Full).Advance();
        Assert.Empty(onFullTorus.LiveCells);
    }
}
