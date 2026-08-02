namespace GameOfLife.Api.Features.CreateGame;

/// <summary>
/// <c>POST /game</c> — create + seed + configure the single game; issues the one-time admin secret;
/// no auth. Validation is stateless and runs to completion <em>before</em> any game is created, so a
/// bad request never half-creates or claims the slot.
/// </summary>
internal static class CreateGameEndpoint
{
    public static async Task<IResult> Handle(HttpContext context, GameHost host, IOptions<GameOptions> gameOptions)
    {
        CreateGameRequest? request;
        try
        {
            // ReadFromJsonAsync honours [JsonUnmappedMemberHandling(Disallow)]: unknown properties,
            // an empty body, and malformed JSON all surface as JsonException → MALFORMED_REQUEST_BODY.
            request = await context.Request.ReadFromJsonAsync<CreateGameRequest>();
        }
        catch (JsonException)
        {
            return MalformedBody();
        }

        // An empty body deserializes to null (no JsonException) — also unreadable, so the same code.
        if (request is null)
            return MalformedBody();

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, validateAllProperties: true))
            return ErrorResults.Envelope(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationFailed,
                ErrorMessages.ValidationFailed,
                ToFieldErrors(validationResults));

        // Validation passed (stateless) before any game is created — a bad request never claims the slot.
        var session = await host.TryCreate(request.ToParameters(gameOptions.Value));
        if (session is null)
            return ErrorResults.Envelope(
                StatusCodes.Status409Conflict,
                ErrorCodes.GameAlreadyExists,
                ErrorMessages.GameAlreadyExists);

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

    private static IResult MalformedBody() => ErrorResults.Envelope(
        StatusCodes.Status400BadRequest,
        ErrorCodes.MalformedRequestBody,
        ErrorMessages.MalformedRequestBody);

    /// <summary>
    /// Flattens the validation results into the envelope's <c>errors[]</c>: one entry per violation,
    /// its <c>field</c> the camelCase JSON name (or null for an object-level result — the old <c>"_"</c>
    /// sentinel is retired). The member name is camelCased with the same <see cref="JsonNamingPolicy"/>
    /// the serializer uses, so a field key here always matches the property's wire name. A field may
    /// repeat when it fails more than one rule.
    /// </summary>
    private static IReadOnlyList<FieldError> ToFieldErrors(IEnumerable<ValidationResult> results) =>
        results
            .SelectMany(r => r.MemberNames.Any()
                ? r.MemberNames.Select(member => new FieldError(
                    JsonNamingPolicy.CamelCase.ConvertName(member), r.ErrorMessage ?? "Invalid value."))
                : [new FieldError(null, r.ErrorMessage ?? "Invalid value.")])
            .ToList();
}
