namespace GameOfLife.Api.Game;

/// <summary>
/// Registers the game kernel: the single in-memory <see cref="GameHost"/>, its broadcast loop, and
/// the SignalR hub infrastructure (binary MessagePack payloads). Streaming is kernel infrastructure,
/// so the hub and its wire protocol are registered here rather than in the REST API surface.
/// </summary>
public static class GameServiceCollectionExtensions
{
    public static IServiceCollection AddGame(this IServiceCollection services)
    {
        // The hot-path delta and status pushes cross the wire as binary MessagePack: the delta's
        // native ulong coordinates round-trip exactly and the payload shrinks materially versus JSON.
        // The serializer options are pinned so server, client, and test harness agree (GameMessagePack).
        services.AddSignalR()
            .AddMessagePackProtocol(options =>
                options.SerializerOptions = GameMessagePack.SerializerOptions);

        // The torus every game runs on, resolved once from the (already startup-validated) coordinate
        // type. Validation guarantees the parse succeeds; Universe.Full is an unreachable safety net.
        // Non-generic registration: Universe is a value type, which the generic AddSingleton<T> (which
        // constrains T to a reference type) cannot accept. The factory boxes it; DI unboxes on inject.
        services.AddSingleton(typeof(Universe), sp =>
            Universe.TryParseCoordinateType(
                sp.GetRequiredService<IOptions<GameOptions>>().Value.UniverseAxisSize, out var universe)
                ? universe
                : Universe.Full);

        services.AddSingleton<GameHost>();
        // The broadcast loop drives the host through the IBroadcaster seam (same singleton instance),
        // which keeps the loop testable in isolation without the hub-backed host.
        services.AddSingleton<IBroadcaster>(sp => sp.GetRequiredService<GameHost>());
        services.AddHostedService<BroadcastLoopService>();

        return services;
    }
}
