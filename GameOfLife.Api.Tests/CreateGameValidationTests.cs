using System.Net;
using System.Net.Http.Json;
using GameOfLife.Api.Contracts;
using GameOfLife.Api.Tests.Support;

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
    [InlineData("{}", "empty body")]
    [InlineData("", "no body at all")]
    [InlineData("not json", "malformed json")]
    public async Task Empty_or_malformed_body_is_rejected_400(string json, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_property_is_rejected_400()
    {
        await using var ctx = new ApiTestContext();
        var json = Requests.ValidCreate().TrimEnd().TrimEnd('}') + ", \"surprise\": 1 }";

        var response = await ctx.Client.PostAsync("/game", Requests.Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("\"seed\"", "missing seed")]
    [InlineData("\"origin\"", "missing origin")]
    [InlineData("\"autoStart\"", "missing autoStart")]
    [InlineData("\"rule\"", "missing rule")]
    [InlineData("\"tickRate\"", "missing tickRate")]
    public async Task Missing_required_field_is_rejected_400(string quotedField, string _)
    {
        await using var ctx = new ApiTestContext();
        // Remove the named field's line from an otherwise-valid body.
        var lines = Requests.ValidCreate().Split('\n')
            .Where(l => !l.Contains(quotedField.Trim('"')))
            .ToArray();
        // Re-join and repair a possible trailing comma before the closing brace.
        var json = string.Join('\n', lines).Replace(",\n}", "\n}");

        var response = await ctx.Client.PostAsync("/game", Requests.Json(json));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("not-base64!!", "not base64")]
    [InlineData("AAAA", "base64 but wrong length")]
    public async Task Bad_seed_is_rejected_400(string seed, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(seed: seed)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("-1", "0", "negative x")]
    [InlineData("0", "abc", "non-numeric y")]
    [InlineData("18446744073709551616", "0", "x above ulong max")]
    public async Task Unparseable_origin_coordinate_is_rejected_400(string x, string y, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(originX: x, originY: y)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("B0/S23", "B0 fills the torus")]
    [InlineData("B3/S9", "digit out of range")]
    [InlineData("b3/s23", "lower case")]
    [InlineData("B3S23", "missing slash")]
    public async Task Bad_rule_is_rejected_400(string rule, string _)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(rule: rule)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.05)]
    [InlineData(60.1)]
    [InlineData(1000)]
    public async Task Out_of_range_tick_rate_is_rejected_400(double tickRate)
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(tickRate: tickRate)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Second_create_while_a_game_exists_is_rejected_409()
    {
        await using var ctx = new ApiTestContext();
        var first = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate()));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate()));

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }
}
