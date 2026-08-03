using System.ComponentModel.DataAnnotations;
using GameOfLife.Api.Configuration;
using GameOfLife.Api.Contracts;
using GameOfLife.Api.Features.CreateGame;
using GameOfLife.Api.Tests.Support;

namespace GameOfLife.Api.Tests;

/// <summary>
/// Direct unit tests for <see cref="CreateGameRequest.ToParameters"/>. Projection now reuses the seed
/// and origin that <see cref="CreateGameRequest.Validate"/> decoded, so the happy-path tests validate a
/// request first (as the endpoint does) before projecting. The single remaining guard — a request that
/// never passed validation carries no decoded seed/origin and so throws — is pinned straight here, since
/// the endpoint's validate-first flow makes it unreachable over HTTP.
/// </summary>
public class CreateGameRequestTests
{
    private static readonly GameOptions Options = new() { DefaultRule = "B3/S23" };

    /// <summary>Runs the request through the same DataAnnotations validation the endpoint uses, so the
    /// decoded seed/origin ToParameters consumes are populated. Asserts the request is in fact valid.</summary>
    private static CreateGameRequest Validated(CreateGameRequest request)
    {
        var results = new List<ValidationResult>();
        Assert.True(
            Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true),
            "expected the request to pass validation");
        return request;
    }

    [Fact]
    public void Given_a_validated_create_request_When_ToParameters_is_called_Then_it_projects_into_domain_values()
    {
        var request = Validated(new CreateGameRequest
        {
            Seed = TestSeeds.With((0, 0), (1, 1)),
            Origin = new CellDto("2", "3"),
            AutoStart = true,
            Rule = "B36/S23",
            TickRate = 75,
        });

        var parameters = request.ToParameters(Options);

        Assert.Equal("B36/S23", parameters.Rule.ToString());
        Assert.Equal(75, parameters.TickRate);
        Assert.True(parameters.AutoStart);
        Assert.NotEmpty(parameters.Seed);
    }

    [Fact]
    public void Given_a_validated_create_request_with_no_rule_When_ToParameters_is_called_Then_it_falls_back_to_the_configured_default_rule()
    {
        var request = Validated(new CreateGameRequest
        {
            Seed = TestSeeds.AllDead(),
            Origin = new CellDto("0", "0"),
            AutoStart = false,
            Rule = null, // omitted → server default
            TickRate = 100,
        });

        var parameters = request.ToParameters(Options);

        Assert.Equal("B3/S23", parameters.Rule.ToString());
    }

    [Fact]
    public void Given_a_request_that_has_not_passed_validation_When_ToParameters_is_called_Then_it_throws()
    {
        // A well-formed request, but ToParameters is called without validating first, so the decoded
        // seed/origin were never memoised.
        var request = new CreateGameRequest
        {
            Seed = TestSeeds.AllDead(),
            Origin = new CellDto("0", "0"),
            AutoStart = false,
            TickRate = 100,
        };

        Assert.Throws<InvalidOperationException>(() => request.ToParameters(Options));
    }

    [Fact]
    public void Given_a_request_with_an_undecodable_seed_When_validated_and_projected_Then_ToParameters_throws()
    {
        var request = new CreateGameRequest
        {
            Seed = "!!not-base64!!",
            Origin = new CellDto("0", "0"),
            AutoStart = false,
            TickRate = 100,
        };

        // Validation fails, so the seed is never decoded; ToParameters then refuses to project.
        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true));
        Assert.Throws<InvalidOperationException>(() => request.ToParameters(Options));
    }

    [Fact]
    public void Given_a_request_with_an_unparseable_origin_When_validated_and_projected_Then_ToParameters_throws()
    {
        var request = new CreateGameRequest
        {
            Seed = TestSeeds.AllDead(), // seed is fine, so the origin is the field that stays undecoded
            Origin = new CellDto("not-a-number", "0"),
            AutoStart = false,
            TickRate = 100,
        };

        var results = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), results, validateAllProperties: true));
        Assert.Throws<InvalidOperationException>(() => request.ToParameters(Options));
    }
}
