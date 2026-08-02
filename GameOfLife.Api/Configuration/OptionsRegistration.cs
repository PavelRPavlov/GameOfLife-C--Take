namespace GameOfLife.Api.Configuration;

/// <summary>
/// Binds and validates every backend options class. Validation runs at startup
/// (<see cref="OptionsBuilderExtensions.ValidateOnStart{TOptions}"/>): a malformed <c>appsettings</c>
/// (unparseable rule, non-positive interval, empty origins) fails the host at boot with a clear
/// message rather than surfacing deep in a request — consistent with the API's fail-loudly stance.
/// </summary>
public static class OptionsRegistration
{
    public static IServiceCollection AddGameOptions(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GameOptions>()
            .Bind(configuration.GetSection(GameOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<GameOptions>, GameOptionsValidator>();

        services.AddOptions<CorsOptions>()
            .Bind(configuration.GetSection(CorsOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CorsOptions>, CorsOptionsValidator>();

        return services;
    }
}

/// <summary>Fails startup when <see cref="GameOptions"/> holds an unusable value.</summary>
internal sealed class GameOptionsValidator : IValidateOptions<GameOptions>
{
    public ValidateOptionsResult Validate(string? name, GameOptions options)
    {
        var failures = new List<string>();

        if (!Rule.TryParse(options.DefaultRule, out _))
            failures.Add(
                $"{GameOptions.SectionName}:{nameof(GameOptions.DefaultRule)} '{options.DefaultRule}' is not a valid " +
                "B[0-8]*/S[0-8]* rulestring (unique digits per group, no B0).");

        if (options.BroadcastIntervalMs <= 0)
            failures.Add(
                $"{GameOptions.SectionName}:{nameof(GameOptions.BroadcastIntervalMs)} must be greater than 0 " +
                $"(was {options.BroadcastIntervalMs}).");

        // The torus wraps with a single mask only because its size is a power of two — i.e. the range
        // of an unsigned integer type. Reject anything else (signed, floating, or unknown) so the app
        // refuses to start rather than fall back to a costlier, non-free wrapping scheme.
        if (!Universe.TryParseCoordinateType(options.CoordinateType, out _))
            failures.Add(
                $"{GameOptions.SectionName}:{nameof(GameOptions.CoordinateType)} '{options.CoordinateType}' is not a " +
                "wrap-capable unsigned integer type. Use one of: " +
                $"{string.Join(", ", Universe.SupportedCoordinateTypeNames)}.");

        return failures.Count > 0 ? ValidateOptionsResult.Fail(failures) : ValidateOptionsResult.Success;
    }
}

/// <summary>Fails startup when <see cref="CorsOptions"/> lists no usable origin.</summary>
internal sealed class CorsOptionsValidator : IValidateOptions<CorsOptions>
{
    public ValidateOptionsResult Validate(string? name, CorsOptions options)
    {
        if (options.AllowedOrigins.Length == 0 || options.AllowedOrigins.Any(string.IsNullOrWhiteSpace))
            return ValidateOptionsResult.Fail(
                $"{CorsOptions.SectionName}:{nameof(CorsOptions.AllowedOrigins)} must contain at least one non-empty origin.");

        return ValidateOptionsResult.Success;
    }
}
