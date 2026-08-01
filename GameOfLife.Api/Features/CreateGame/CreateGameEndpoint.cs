using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using GameOfLife.Api.Game;

namespace GameOfLife.Api.Features.CreateGame;

/// <summary>
/// <c>POST /game</c> — create + seed + configure the single game; issues the one-time admin secret;
/// no auth. Validation is stateless and runs to completion <em>before</em> any game is created, so a
/// bad request never half-creates or claims the slot.
/// </summary>
internal static class CreateGameEndpoint
{
    public static async Task<IResult> HandleAsync(HttpContext context, GameHost host)
    {
        CreateGameRequest? request;
        try
        {
            // ReadFromJsonAsync honours [JsonUnmappedMemberHandling(Disallow)]: unknown properties,
            // an empty body, and malformed JSON all surface as JsonException → 400.
            request = await context.Request.ReadFromJsonAsync<CreateGameRequest>();
        }
        catch (JsonException)
        {
            return Results.BadRequest("Request body is not valid JSON or contains unknown properties.");
        }

        if (request is null)
            return Results.BadRequest("Request body is required.");

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, validateAllProperties: true))
            return Results.ValidationProblem(ToErrorDictionary(validationResults));

        // Validation passed (stateless) before any game is created — a bad request never claims the slot.
        var session = await host.TryCreateAsync(request.ToParameters());
        if (session is null)
            return Results.Conflict("A game already exists.");

        var response = new CreateGameResponse(
            AdminSecret: session.AdminSecret.ToString(),
            Status: session.Status,
            Generation: session.Current.Number,
            TickRate: session.TickRate,
            Rule: session.Rule.ToString(),
            HubUrl: GameHost.HubUrl,
            SnapshotUrl: GameHost.SnapshotUrl);

        return Results.Created(GameHost.SnapshotUrl, response);
    }

    private static Dictionary<string, string[]> ToErrorDictionary(IEnumerable<ValidationResult> results)
    {
        return results
            .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : ["_"])
                .Select(member => (member, message: r.ErrorMessage ?? "Invalid value.")))
            .GroupBy(x => x.member, x => x.message)
            .ToDictionary(g => g.Key, g => g.ToArray());
    }
}
