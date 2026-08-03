using GameOfLife.Api.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The startup fail-fast validators. These run at boot via <c>ValidateOnStart</c>; here they're
/// exercised directly so a malformed <c>appsettings</c> value is proven to be rejected rather than
/// silently accepted and blowing up later per-request.
/// </summary>
public class OptionsValidationTests
{
    private static readonly GameOptionsValidator GameValidator = new();

    /// <summary>A CORS validator that believes it is running in the given hosting environment.</summary>
    private static CorsOptionsValidator CorsValidatorFor(string environmentName) =>
        new(new FakeHostEnvironment(environmentName));

    [Fact]
    public void Given_valid_game_options_When_validated_Then_validation_succeeds()
    {
        var result = GameValidator.Validate(null, new GameOptions { DefaultRule = "B3/S23" });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("B0/S23")]  // B0 is rejected by the rule parser
    [InlineData("b3/s23")]  // lower case
    [InlineData("B3S23")]   // missing slash
    [InlineData("")]        // empty
    public void Given_an_unparseable_default_rule_When_game_options_are_validated_Then_validation_fails(string rule)
    {
        var result = GameValidator.Validate(null, new GameOptions { DefaultRule = rule });

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
            new GameOptions { DefaultRule = "B3/S23", UniverseAxisSize = coordinateType });

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
            new GameOptions { DefaultRule = "B3/S23", UniverseAxisSize = coordinateType });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Given_empty_allowed_origins_When_cors_options_are_validated_Then_validation_fails()
    {
        var result = CorsValidatorFor(Environments.Production).Validate(null, new CorsOptions { AllowedOrigins = [] });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Given_empty_allowed_origins_in_development_When_cors_options_are_validated_Then_validation_fails()
    {
        // Empty is rejected regardless of environment — even Development needs at least one origin.
        var result = CorsValidatorFor(Environments.Development).Validate(null, new CorsOptions { AllowedOrigins = [] });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Given_a_blank_allowed_origin_When_cors_options_are_validated_Then_validation_fails()
    {
        var result = CorsValidatorFor(Environments.Production)
            .Validate(null, new CorsOptions { AllowedOrigins = ["https://ok.test", "  "] });

        Assert.True(result.Failed);
    }

    [Fact]
    public void Given_valid_allowed_origins_When_cors_options_are_validated_Then_validation_succeeds()
    {
        var result = CorsValidatorFor(Environments.Production)
            .Validate(null, new CorsOptions { AllowedOrigins = ["https://ok.test"] });

        Assert.True(result.Succeeded);
    }

    [Theory]
    [InlineData("https://localhost:7079")]
    [InlineData("http://127.0.0.1:5292")]
    [InlineData("https://[::1]:7079")]
    public void Given_a_localhost_origin_in_production_When_cors_options_are_validated_Then_validation_fails(string origin)
    {
        // A leftover dev origin that slipped into a deployed config must fail startup, not be served.
        var result = CorsValidatorFor(Environments.Production).Validate(null, new CorsOptions { AllowedOrigins = [origin] });

        Assert.True(result.Failed);
    }

    [Theory]
    [InlineData("https://localhost:7079")]
    [InlineData("http://127.0.0.1:5292")]
    [InlineData("https://[::1]:7079")]
    public void Given_a_localhost_origin_in_development_When_cors_options_are_validated_Then_validation_succeeds(string origin)
    {
        // Localhost origins are legitimate in Development, so the loopback rejection is skipped there.
        var result = CorsValidatorFor(Environments.Development).Validate(null, new CorsOptions { AllowedOrigins = [origin] });

        Assert.True(result.Succeeded);
    }

    /// <summary>Minimal <see cref="IHostEnvironment"/> so the CORS validator can be exercised per environment.</summary>
    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "GameOfLife.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
