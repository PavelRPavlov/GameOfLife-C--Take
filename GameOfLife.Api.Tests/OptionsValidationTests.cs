using GameOfLife.Api.Configuration;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The startup fail-fast validators. These run at boot via <c>ValidateOnStart</c>; here they're
/// exercised directly so a malformed <c>appsettings</c> value is proven to be rejected rather than
/// silently accepted and blowing up later per-request.
/// </summary>
public class OptionsValidationTests
{
    private static readonly GameOptionsValidator GameValidator = new();
    private static readonly CorsOptionsValidator CorsValidator = new();

    [Fact]
    public void Valid_game_options_pass()
    {
        var result = GameValidator.Validate(null, new GameOptions { DefaultRule = "B3/S23", BroadcastIntervalMs = 100 });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("B0/S23")]  // B0 is rejected by the rule parser
    [InlineData("b3/s23")]  // lower case
    [InlineData("B3S23")]   // missing slash
    [InlineData("")]        // empty
    public void Unparseable_default_rule_fails(string rule)
    {
        var result = GameValidator.Validate(null, new GameOptions { DefaultRule = rule, BroadcastIntervalMs = 100 });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_broadcast_interval_fails(int intervalMs)
    {
        var result = GameValidator.Validate(null, new GameOptions { DefaultRule = "B3/S23", BroadcastIntervalMs = intervalMs });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("UInt64")]
    [InlineData("ulong")]
    [InlineData("UInt32")]
    [InlineData("uint")]
    [InlineData("UInt16")]
    [InlineData("ushort")]
    [InlineData("Byte")]
    [InlineData("byte")]
    public void Wrap_capable_coordinate_type_passes(string coordinateType)
    {
        var result = GameValidator.Validate(null,
            new GameOptions { DefaultRule = "B3/S23", BroadcastIntervalMs = 100, CoordinateType = coordinateType });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("Int64")]    // signed
    [InlineData("long")]     // signed
    [InlineData("int")]      // signed
    [InlineData("UInt128")]  // wider than the ulong coordinate can hold
    [InlineData("decimal")]  // non-integer
    [InlineData("float")]    // non-integer
    [InlineData("string")]   // not a number
    [InlineData("")]         // empty
    [InlineData("nonsense")] // unknown
    public void Non_wrap_capable_coordinate_type_fails(string coordinateType)
    {
        var result = GameValidator.Validate(null,
            new GameOptions { DefaultRule = "B3/S23", BroadcastIntervalMs = 100, CoordinateType = coordinateType });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Empty_allowed_origins_fails()
    {
        var result = CorsValidator.Validate(null, new CorsOptions { AllowedOrigins = [] });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Blank_allowed_origin_fails()
    {
        var result = CorsValidator.Validate(null, new CorsOptions { AllowedOrigins = ["https://ok.test", "  "] });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Valid_allowed_origins_pass()
    {
        var result = CorsValidator.Validate(null, new CorsOptions { AllowedOrigins = ["https://ok.test"] });

        Assert.True(result.Succeeded);
    }
}
