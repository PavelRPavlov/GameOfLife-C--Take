using CorsInfrastructureOptions = Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions;

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

    public static IServiceCollection AddGameApiSurface(this IServiceCollection services)
    {
        services.AddOpenApi();

        // Enums cross the REST wire as strings ("Running", "Created", ...).
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // The Blazor Wasm client is served from a separate origin, so it needs CORS to reach this API
        // and, later, the SignalR hub. The allowed origins come from the "Cors" section, but binding and
        // validation live once in OptionsRegistration (validated non-empty, and Production-unsafe at
        // startup by CorsOptionsValidator); here we consume the already-validated IOptions<CorsOptions>
        // rather than re-reading the section. Configuring the CORS infrastructure options through the
        // options system lets the policy resolve those bound values from DI. AllowCredentials (needed for
        // the SignalR websocket) forbids a wildcard origin, so the origins are listed explicitly;
        // AllowAnyHeader lets the X-Admin-Secret control header through.
        services.AddCors();
        services.AddOptions<CorsInfrastructureOptions>()
            .Configure<IOptions<CorsOptions>>((cors, gameCors) =>
                cors.AddPolicy(WasmCorsPolicy, policy => policy
                    .WithOrigins(gameCors.Value.AllowedOrigins)
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
        // and guarantees no exception detail leaves the process — it writes the redacted error envelope
        // directly (no ProblemDetails), supplied here as the middleware's exception-handling delegate.
        if (!app.Environment.IsDevelopment())
            app.UseExceptionHandler(new ExceptionHandlerOptions
            {
                ExceptionHandler = GlobalExceptionHandler.WriteRedactedResponse,
            });

        if (app.Environment.IsDevelopment())
            app.MapOpenApi();

        app.UseCors(WasmCorsPolicy);

        return app;
    }
}
