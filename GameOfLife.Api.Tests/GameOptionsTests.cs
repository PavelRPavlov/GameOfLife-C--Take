using System.Net;
using System.Net.Http.Json;
using GameOfLife.Api.Configuration;
using GameOfLife.Api.Features.CreateGame;
using GameOfLife.Api.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The backend-configuration surface: <c>POST /game</c> falls back to the configured
/// <c>Game:DefaultRule</c> when <c>rule</c> is omitted, and per-environment overrides of the bound
/// options actually take effect. In-memory config is pinned per test so the assertions don't depend
/// on which <c>appsettings.{Environment}.json</c> the host happens to load.
/// </summary>
public class GameOptionsTests
{
    [Fact]
    public async Task Given_a_configured_default_rule_When_a_game_is_created_without_a_rule_Then_the_configured_default_is_applied()
    {
        await using var ctx = ApiTestContext.Create(
            settings: new Dictionary<string, string?> { ["Game:DefaultRule"] = "B3/S23" });

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreateWithoutRule()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(ApiTestContext.Json);
        Assert.Equal("B3/S23", body!.Rule);
    }

    [Fact]
    public async Task Given_an_overridden_configured_default_rule_When_a_game_is_created_without_a_rule_Then_the_override_is_applied()
    {
        // Same request (no rule) under a different configured default — the override is observable in
        // the response, exactly as switching appsettings.{Environment}.json would be.
        await using var ctx = ApiTestContext.Create(
            settings: new Dictionary<string, string?> { ["Game:DefaultRule"] = "B36/S23" });

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreateWithoutRule()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(ApiTestContext.Json);
        Assert.Equal("B36/S23", body!.Rule);
    }

    [Fact]
    public async Task Given_a_configured_default_rule_When_a_game_is_created_with_an_explicit_rule_Then_the_explicit_rule_wins()
    {
        await using var ctx = ApiTestContext.Create(
            settings: new Dictionary<string, string?> { ["Game:DefaultRule"] = "B36/S23" });

        var response = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate(rule: "B3/S23")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreateGameResponse>(ApiTestContext.Json);
        Assert.Equal("B3/S23", body!.Rule);
    }

    [Fact]
    public async Task Given_cors_allowed_origins_in_configuration_When_the_options_are_bound_Then_they_contain_the_configured_origin()
    {
        await using var ctx = ApiTestContext.Create(
            settings: new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "https://cors-bind.test" });

        var cors = ctx.Services.GetRequiredService<IOptions<CorsOptions>>().Value;

        Assert.Contains("https://cors-bind.test", cors.AllowedOrigins);
    }
}
