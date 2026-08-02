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
        app.MapPost("/game", CreateGameEndpoint.Handle);

        // Control verbs — X-Admin-Secret gated; bodyless 404/403/409 in existence → auth → state order.
        app.MapPost("/start", StartGameEndpoint.Handle);
        app.MapPost("/stop", StopGameEndpoint.Handle);
        app.MapPost("/pause", PauseGameEndpoint.Handle);
        app.MapPost("/resume", ResumeGameEndpoint.Handle);
        app.MapPost("/step", StepGameEndpoint.Handle);

        // View-only, no secret; the full live set at the last broadcast-aligned generation.
        app.MapGet("/snapshot", GetSnapshotEndpoint.Handle);

        // The observer push channel (delta + status broadcasts).
        app.MapHub<GameHub>(GameHost.HubUrl);
    }
}
