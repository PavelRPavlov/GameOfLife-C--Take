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
    public void Given_valid_game_options_When_validated_Then_validation_succeeds()
    {
        var result = GameValidator.Validate(null, new GameOptions { DefaultRule = "B3/S23", BroadcastIntervalMs = 100 });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("B0/S23")]  // B0 is rejected by the rule parser
    [InlineData("b3/s23")]  // lower case
    [InlineData("B3S23")]   // missing slash
    [InlineData("")]        // empty
    public void Given_an_unparseable_default_rule_When_game_options_are_validated_Then_validation_fails(string rule)
    {
        var result = GameValidator.Validate(null, new GameOptions { DefaultRule = rule, BroadcastIntervalMs = 100 });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Given_a_non_positive_broadcast_interval_When_game_options_are_validated_Then_validation_fails(int intervalMs)
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
    public void Given_a_wrap_capable_coordinate_type_When_game_options_are_validated_Then_validation_succeeds(string coordinateType)
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
    public void Given_a_non_wrap_capable_coordinate_type_When_game_options_are_validated_Then_validation_fails(string coordinateType)
    {
        var result = GameValidator.Validate(null,
            new GameOptions { DefaultRule = "B3/S23", BroadcastIntervalMs = 100, CoordinateType = coordinateType });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Given_empty_allowed_origins_When_cors_options_are_validated_Then_validation_fails()
    {
        var result = CorsValidator.Validate(null, new CorsOptions { AllowedOrigins = [] });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Given_a_blank_allowed_origin_When_cors_options_are_validated_Then_validation_fails()
    {
        var result = CorsValidator.Validate(null, new CorsOptions { AllowedOrigins = ["https://ok.test", "  "] });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Given_valid_allowed_origins_When_cors_options_are_validated_Then_validation_succeeds()
    {
        var result = CorsValidator.Validate(null, new CorsOptions { AllowedOrigins = ["https://ok.test"] });

        Assert.True(result.Succeeded);
    }
}
