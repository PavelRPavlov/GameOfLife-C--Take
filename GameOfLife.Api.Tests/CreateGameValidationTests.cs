using System.Net;
using System.Net.Http.Json;
using GameOfLife.Core;
using GameOfLife.Api.Features.CreateGame;
using GameOfLife.Api.Tests.Support;
using GameOfLife.Shared;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The <c>POST /game</c> validation matrix: every malformed request is rejected with 400
/// <em>before</em> any game is created, so a bad request never half-creates or claims the slot.
/// </summary>
public class CreateGameValidationTests
{
    [Fact]
    public async Task Given_a_valid_create_request_When_posted_Then_the_game_is_created_and_201_is_returned_with_a_secret()
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/snapshot", response.Headers.Location?.ToString());

        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(ApiTestContext.Json);
        Assert.NotNull(body);
        // A 256-bit CSPRNG token, base64url-encoded: 32 bytes → 43 chars, no padding.
        Assert.NotNull(body!.AdminSecret);
        Assert.Equal(43, body.AdminSecret.Length);
        Assert.Equal(GameStatus.Created, body.Status);
        Assert.Equal(0, body.Generation);
        Assert.Equal("B3/S23", body.Rule);
        Assert.Equal(100, body.TickRate);
        Assert.Equal("/hubs/game", body.HubUrl);
        Assert.Equal("/snapshot", body.SnapshotUrl);
    }

    [Fact]
    public async Task Given_a_create_request_with_autostart_true_When_posted_Then_the_game_reports_running()
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(autoStart: true)));
        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(ApiTestContext.Json);

        Assert.Equal(GameStatus.Running, body!.Status);
    }

    [Theory]
    [InlineData("", "no body at all")]
    [InlineData("not json", "malformed json")]
    [InlineData("null", "explicit json null deserializes to a null request")]
    public async Task Given_an_empty_or_malformed_body_When_posted_Then_the_result_is_400_malformed_request_body(string json, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadError(ErrorCodes.MalformedRequestBody);
        Assert.Empty(error.Errors); // a body we couldn't read has no per-field breakdown
    }

    [Fact]
    public async Task Given_a_body_with_an_unknown_property_When_posted_Then_the_result_is_400_malformed_request_body()
    {
        await using var ctx = new ApiTestContext();
        var json = Requests.ValidCreate().TrimEnd().TrimEnd('}') + ", \"surprise\": 1 }";

        var response = await ctx.Client.PostAsync("/game", Requests.Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await response.ReadError(ErrorCodes.MalformedRequestBody);
    }

    [Fact]
    public async Task Given_an_empty_json_object_When_posted_Then_validation_fails_for_every_missing_field()
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadError(ErrorCodes.ValidationFailed);
        var fields = error.Errors.Select(e => e.Field).ToHashSet();
        // rule is optional (falls back to the configured default), so it never appears as missing.
        Assert.Equal(new HashSet<string?> { "seed", "origin", "autoStart", "tickRate" }, fields);
    }

    [Theory]
    [InlineData("\"seed\"", "seed")]
    [InlineData("\"origin\"", "origin")]
    [InlineData("\"autoStart\"", "autoStart")]
    [InlineData("\"tickRate\"", "tickRate")]
    public async Task Given_a_body_missing_a_required_field_When_posted_Then_validation_fails_naming_that_field(string quotedField, string expectedField)
    {
        await using var ctx = new ApiTestContext();
        // Remove the named field's line from an otherwise-valid body.
        var lines = Requests.ValidCreate().Split('\n')
            .Where(l => !l.Contains(quotedField.Trim('"')))
            .ToArray();
        // Re-join and repair a trailing comma before the closing brace (newline-agnostic: removing the
        // last field otherwise leaves "...,\r?\n}", which would parse as malformed rather than missing).
        var json = System.Text.RegularExpressions.Regex.Replace(string.Join('\n', lines), @",(\s*})", "$1");

        var response = await ctx.Client.PostAsync("/game", Requests.Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadError(ErrorCodes.ValidationFailed);
        Assert.Contains(error.Errors, e => e.Field == expectedField && !string.IsNullOrWhiteSpace(e.Message));
    }

    [Theory]
    [InlineData("not-base64!!", "not base64")]
    [InlineData("AAAA", "base64 but wrong length")]
    public async Task Given_a_bad_seed_When_posted_Then_validation_fails_on_seed(string seed, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(seed: seed)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadError(ErrorCodes.ValidationFailed);
        Assert.Contains(error.Errors, e => e.Field == "seed");
    }

    [Theory]
    [InlineData("-1", "0", "negative x")]
    [InlineData("0", "abc", "non-numeric y")]
    [InlineData("18446744073709551616", "0", "x above ulong max")]
    public async Task Given_an_unparseable_origin_coordinate_When_posted_Then_validation_fails_on_origin(string x, string y, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(originX: x, originY: y)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadError(ErrorCodes.ValidationFailed);
        // origin stays a single object-level entry — not split into x/y.
        Assert.Contains(error.Errors, e => e.Field == "origin");
        Assert.DoesNotContain(error.Errors, e => e.Field is "x" or "y");
    }

    [Theory]
    [InlineData("B0/S23", "B0 fills the torus")]
    [InlineData("B3/S9", "digit out of range")]
    [InlineData("b3/s23", "lower case")]
    [InlineData("B3S23", "missing slash")]
    public async Task Given_a_bad_rule_When_posted_Then_validation_fails_on_rule(string rule, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(rule: rule)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadError(ErrorCodes.ValidationFailed);
        Assert.Contains(error.Errors, e => e.Field == "rule");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(59.9)]   // just below the MinTickRate boundary
    [InlineData(250.1)]  // just above the MaxTickRate boundary
    [InlineData(1000)]
    public async Task Given_an_out_of_range_tick_rate_When_posted_Then_validation_fails_on_tickRate(double tickRate)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(tickRate: tickRate)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadError(ErrorCodes.ValidationFailed);
        Assert.Contains(error.Errors, e => e.Field == "tickRate");
    }

    [Theory]
    [InlineData(60.0)]  // MinTickRate boundary
    [InlineData(250.0)] // MaxTickRate boundary — capped so delivery stays faithful to every generation
    public async Task Given_a_boundary_tick_rate_When_posted_Then_the_game_is_created_with_201(double tickRate)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(tickRate: tickRate)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Given_a_game_already_exists_When_a_second_create_is_posted_Then_the_result_is_409_game_already_exists()
    {
        await using var ctx = new ApiTestContext();
        var first = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate()));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate()));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.ReadError(ErrorCodes.GameAlreadyExists);
        Assert.Empty(error.Errors);
    }
}
