using System.Text.Json.Serialization;

namespace GameOfLife.Api.Composition;

/// <summary>
/// The REST/OpenAPI edge of the API surface: OpenAPI document generation, enum-as-string HTTP JSON,
/// and CORS for the separate-origin Blazor Wasm client, plus the development request pipeline. The
/// game kernel (host, broadcast loop, SignalR hub) registers itself via <c>AddGame()</c>; this covers
/// only the cross-cutting HTTP surface.
/// </summary>
public static class ApiSurfaceRegistration
{
    private const string WasmCorsPolicy = "wasm-client";

    public static IServiceCollection AddGameApiSurface(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOpenApi();

        // Global safety net for unexpected exceptions: a generic 500 ProblemDetails (with a traceId,
        // no exception detail). Registered here but only activated outside Development — see
        // UseGameApiPipeline. AddProblemDetails supplies the traceId extension and status-derived title.
        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // Enums cross the REST wire as strings ("Running", "Created", ...).
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // The Blazor Wasm client is served from a separate origin, so it needs CORS to reach this API
        // and, later, the SignalR hub. Origins are configurable ("Cors:AllowedOrigins"); the defaults
        // are the WebClient dev URLs. AllowCredentials (needed for the SignalR websocket) forbids a
        // wildcard origin, so the origins are listed explicitly; AllowAnyHeader lets the
        // X-Admin-Secret control header through.
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? ["http://localhost:5292", "https://localhost:7079"];

        services.AddCors(options =>
            options.AddPolicy(WasmCorsPolicy, policy => policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));

        return services;
    }

    public static WebApplication UseGameApiPipeline(this WebApplication app)
    {
        // Must be the first middleware so it wraps everything downstream. Gated off in Development: the
        // host auto-registers the Developer Exception Page there, and we keep it so local developers
        // still get full stack traces in the browser. Everywhere else the global handler owns faults
        // and guarantees no exception detail leaves the process.
        if (!app.Environment.IsDevelopment())
            app.UseExceptionHandler();

        if (app.Environment.IsDevelopment())
            app.MapOpenApi();

        app.UseCors(WasmCorsPolicy);

        return app;
    }
}
