using System.Text.Json;
using GameOfLife.Api.Tests.Support;
using GameOfLife.Shared;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The cross-cutting wire-shape contract, asserted once at the HTTP edge: every failure envelope is
/// camelCase <c>application/json</c> with exactly <c>{ code, message, errors }</c> — no <c>traceId</c>,
/// no echoed <c>status</c> — and each <c>errors[]</c> entry is <c>{ field, message }</c>.
/// </summary>
public class ErrorEnvelopeContractTests
{
    [Fact]
    public async Task Given_failing_requests_across_the_status_range_When_the_response_body_is_read_Then_it_has_exactly_the_camelCase_envelope_keys()
    {
        await using var ctx = new ApiTestContext();
        await ctx.CreateGame(); // occupy the slot so a second create conflicts

        // A representative sample across the status range: 400 (validation) and 409 (conflict).
        var validation = await ctx.Client.PostAsync("/game", Requests.Json("{}"));
        var conflict = await ctx.Client.PostAsync("/game", Requests.Json(Requests.ValidCreate()));

        foreach (var response in new[] { validation, conflict })
        {
            var names = (await response.ReadJsonPropertyNames()).ToHashSet();
            Assert.Equal(new HashSet<string> { "code", "message", "errors" }, names);
        }
    }

    [Fact]
    public async Task Given_a_validation_failure_When_the_error_entries_are_read_Then_each_uses_camelCase_field_and_message_keys()
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Client.PostAsync("/game", Requests.Json("{}"));

        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var errors = doc.RootElement.GetProperty("errors");

        Assert.True(errors.GetArrayLength() > 0);
        foreach (var entry in errors.EnumerateArray())
        {
            var keys = entry.EnumerateObject().Select(p => p.Name).ToHashSet();
            Assert.Equal(new HashSet<string> { "field", "message" }, keys);
        }
    }

    [Fact]
    public async Task Given_a_control_verb_against_no_game_When_the_not_found_failure_is_read_Then_it_has_the_envelope_keys()
    {
        await using var ctx = new ApiTestContext();

        var response = await ctx.Control("start", secret: null);

        await response.ReadError(ErrorCodes.GameNotFound);
        var names = (await response.ReadJsonPropertyNames()).ToHashSet();
        Assert.Equal(new HashSet<string> { "code", "message", "errors" }, names);
    }
}
