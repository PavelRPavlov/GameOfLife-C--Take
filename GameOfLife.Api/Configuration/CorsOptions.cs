namespace GameOfLife.Api.Configuration;

/// <summary>
/// Cross-origin policy bound from the <c>"Cors"</c> configuration section. The Blazor Wasm client is
/// served from a separate origin, so the allowed origins differ per environment (dev localhost URLs
/// vs the deployed origin) and live in <c>appsettings.{Environment}.json</c> rather than in code.
/// Validated non-empty at startup (see <see cref="OptionsRegistration"/>).
/// </summary>
public sealed class CorsOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Cors";

    /// <summary>Origins allowed to reach the API and the SignalR hub. Must contain at least one entry.</summary>
    public string[] AllowedOrigins { get; init; } = [];
}
