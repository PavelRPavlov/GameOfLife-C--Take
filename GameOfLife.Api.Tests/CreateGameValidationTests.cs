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
    public async Task Valid_request_creates_the_game_and_returns_201_with_a_secret()
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/snapshot", response.Headers.Location?.ToString());

        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(ApiTestContext.Json);
        Assert.NotNull(body);
        Assert.True(Guid.TryParse(body!.AdminSecret, out _));
        Assert.Equal(GameStatus.Created, body.Status);
        Assert.Equal(0, body.Generation);
        Assert.Equal("B3/S23", body.Rule);
        Assert.Equal(10, body.TickRate);
        Assert.Equal("/hubs/game", body.HubUrl);
        Assert.Equal("/snapshot", body.SnapshotUrl);
    }

    [Fact]
    public async Task AutoStart_true_reports_Running()
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
    public async Task Empty_or_malformed_body_maps_to_MALFORMED_REQUEST_BODY(string json, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadErrorAsync(ErrorCodes.MalformedRequestBody);
        Assert.Empty(error.Errors); // a body we couldn't read has no per-field breakdown
    }

    [Fact]
    public async Task Unknown_property_maps_to_MALFORMED_REQUEST_BODY()
    {
        await using var ctx = new ApiTestContext();
        var json = Requests.ValidCreate().TrimEnd().TrimEnd('}') + ", \"surprise\": 1 }";

        var response = await ctx.Client.PostAsync("/game", Requests.Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await response.ReadErrorAsync(ErrorCodes.MalformedRequestBody);
    }

    [Fact]
    public async Task Empty_object_reports_VALIDATION_FAILED_for_every_missing_field()
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadErrorAsync(ErrorCodes.ValidationFailed);
        var fields = error.Errors.Select(e => e.Field).ToHashSet();
        // rule is optional (falls back to the configured default), so it never appears as missing.
        Assert.Equal(new HashSet<string?> { "seed", "origin", "autoStart", "tickRate" }, fields);
    }

    [Theory]
    [InlineData("\"seed\"", "seed")]
    [InlineData("\"origin\"", "origin")]
    [InlineData("\"autoStart\"", "autoStart")]
    [InlineData("\"tickRate\"", "tickRate")]
    public async Task Missing_required_field_maps_to_VALIDATION_FAILED_with_that_field(string quotedField, string expectedField)
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
        var error = await response.ReadErrorAsync(ErrorCodes.ValidationFailed);
        Assert.Contains(error.Errors, e => e.Field == expectedField && !string.IsNullOrWhiteSpace(e.Message));
    }

    [Theory]
    [InlineData("not-base64!!", "not base64")]
    [InlineData("AAAA", "base64 but wrong length")]
    public async Task Bad_seed_maps_to_VALIDATION_FAILED_on_seed(string seed, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(seed: seed)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadErrorAsync(ErrorCodes.ValidationFailed);
        Assert.Contains(error.Errors, e => e.Field == "seed");
    }

    [Theory]
    [InlineData("-1", "0", "negative x")]
    [InlineData("0", "abc", "non-numeric y")]
    [InlineData("18446744073709551616", "0", "x above ulong max")]
    public async Task Unparseable_origin_coordinate_maps_to_VALIDATION_FAILED_on_origin(string x, string y, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(originX: x, originY: y)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadErrorAsync(ErrorCodes.ValidationFailed);
        // origin stays a single object-level entry — not split into x/y.
        Assert.Contains(error.Errors, e => e.Field == "origin");
        Assert.DoesNotContain(error.Errors, e => e.Field is "x" or "y");
    }

    [Theory]
    [InlineData("B0/S23", "B0 fills the torus")]
    [InlineData("B3/S9", "digit out of range")]
    [InlineData("b3/s23", "lower case")]
    [InlineData("B3S23", "missing slash")]
    public async Task Bad_rule_maps_to_VALIDATION_FAILED_on_rule(string rule, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(rule: rule)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadErrorAsync(ErrorCodes.ValidationFailed);
        Assert.Contains(error.Errors, e => e.Field == "rule");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(200.1)]
    [InlineData(1000)]
    public async Task Out_of_range_tick_rate_maps_to_VALIDATION_FAILED_on_tickRate(double tickRate)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(tickRate: tickRate)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.ReadErrorAsync(ErrorCodes.ValidationFailed);
        Assert.Contains(error.Errors, e => e.Field == "tickRate");
    }

    [Theory]
    [InlineData(0.1)]   // MinTickRate boundary
    [InlineData(200.0)] // MaxTickRate boundary — raised from 60 for high-speed testing
    public async Task Boundary_tick_rate_is_accepted_201(double tickRate)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(tickRate: tickRate)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Second_create_while_a_game_exists_maps_to_GAME_ALREADY_EXISTS()
    {
        await using var ctx = new ApiTestContext();
        var first = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate()));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate()));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var error = await second.ReadErrorAsync(ErrorCodes.GameAlreadyExists);
        Assert.Empty(error.Errors);
    }
}
