using GameOfLife.Api.Configuration;
using GameOfLife.Api.Contracts;
using GameOfLife.Api.Features.CreateGame;
using GameOfLife.Api.Tests.Support;

namespace GameOfLife.Api.Tests;

/// <summary>
/// Direct unit tests for <see cref="CreateGameRequest.ToParameters"/>. The endpoint always validates a
/// request before projecting it, so the projection's defensive guards are unreachable over HTTP; these
/// exercise them straight, pinning the "called on an unvalidated/invalid request" contract.
/// </summary>
public class CreateGameRequestTests
{
    private static readonly GameOptions Options = new() { DefaultRule = "B3/S23", BroadcastIntervalMs = 50 };

    [Fact]
    public void ToParameters_projects_a_validated_request_into_domain_values()
    {
        var request = new CreateGameRequest
        {
            Seed = TestSeeds.With((0, 0), (1, 1)),
            Origin = new CellDto("2", "3"),
            AutoStart = true,
            Rule = "B36/S23",
            TickRate = 7.5,
        };

        var parameters = request.ToParameters(Options);

        Assert.Equal("B36/S23", parameters.Rule.ToString());
        Assert.Equal(7.5, parameters.TickRate);
        Assert.True(parameters.AutoStart);
        Assert.NotEmpty(parameters.Seed);
    }

    [Fact]
    public void ToParameters_falls_back_to_the_configured_default_rule_when_rule_is_omitted()
    {
        var request = new CreateGameRequest
        {
            Seed = TestSeeds.AllDead(),
            Origin = new CellDto("0", "0"),
            AutoStart = false,
            Rule = null, // omitted → server default
            TickRate = 10,
        };

        var parameters = request.ToParameters(Options);

        Assert.Equal("B3/S23", parameters.Rule.ToString());
    }

    [Fact]
    public void ToParameters_throws_when_the_seed_is_undecodable()
    {
        var request = new CreateGameRequest
        {
            Seed = "!!not-base64!!",
            Origin = new CellDto("0", "0"),
            AutoStart = false,
            TickRate = 10,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => request.ToParameters(Options));
        Assert.Contains("seed", ex.Message);
    }

    [Fact]
    public void ToParameters_throws_when_the_origin_is_unparseable()
    {
        var request = new CreateGameRequest
        {
            Seed = TestSeeds.AllDead(), // seed is fine, so the origin guard is the one that trips
            Origin = new CellDto("not-a-number", "0"),
            AutoStart = false,
            TickRate = 10,
        };

        var ex = Assert.Throws<InvalidOperationException>(() => request.ToParameters(Options));
        Assert.Contains("origin", ex.Message);
    }
}
