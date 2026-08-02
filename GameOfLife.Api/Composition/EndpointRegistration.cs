namespace GameOfLife.Api.Composition;

/// <summary>
/// The single endpoint-mapping step: wires every route — and the SignalR hub — to its slice handler.
/// Routing is centralized here so the whole HTTP + push surface is auditable in one place, while each
/// slice owns its handler body. Adding a feature is one folder plus one line here.
/// </summary>
public static class EndpointRegistration
{
    public static void MapGameEndpoints(this IEndpointRouteBuilder app)
    {
        // Lifecycle: create + seed + configure the single game; issues the one-time admin secret.
        app.MapPost("/game", CreateGameEndpoint.HandleAsync);

        // Control verbs — X-Admin-Secret gated; bodyless 404/403/409 in existence → auth → state order.
        app.MapPost("/start", StartGameEndpoint.HandleAsync);
        app.MapPost("/stop", StopGameEndpoint.HandleAsync);
        app.MapPost("/pause", PauseGameEndpoint.HandleAsync);
        app.MapPost("/resume", ResumeGameEndpoint.HandleAsync);
        app.MapPost("/step", StepGameEndpoint.HandleAsync);

        // View-only, no secret; the full live set at the last broadcast-aligned generation.
        app.MapGet("/snapshot", GetSnapshotEndpoint.HandleAsync);

        // The observer push channel (delta + status broadcasts).
        app.MapHub<GameHub>(GameHost.HubUrl);
    }
}
