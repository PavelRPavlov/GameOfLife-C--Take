namespace GameOfLife.Api.Game;

/// <summary>
/// Registers the game kernel: the single in-memory <see cref="GameHost"/>, its broadcast loop, and
/// the SignalR hub infrastructure (enum-as-string payloads). Streaming is kernel infrastructure, so
/// the hub and its JSON protocol are registered here rather than in the REST API surface.
/// </summary>
public static class GameServiceCollectionExtensions
{
    public static IServiceCollection AddGame(this IServiceCollection services)
    {
        // Enums cross the SignalR wire as strings ("Running", "Created", ...).
        services.AddSignalR()
            .AddJsonProtocol(options =>
                options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

        // The torus every game runs on, resolved once from the (already startup-validated) coordinate
        // type. Validation guarantees the parse succeeds; Universe.Full is an unreachable safety net.
        // Non-generic registration: Universe is a value type, which the generic AddSingleton<T> (which
        // constrains T to a reference type) cannot accept. The factory boxes it; DI unboxes on inject.
        services.AddSingleton(typeof(Universe), sp =>
            Universe.TryParseCoordinateType(
                sp.GetRequiredService<IOptions<GameOptions>>().Value.CoordinateType, out var universe)
                ? universe
                : Universe.Full);

        services.AddSingleton<GameHost>();
        services.AddHostedService<BroadcastLoopService>();

        return services;
    }
}
