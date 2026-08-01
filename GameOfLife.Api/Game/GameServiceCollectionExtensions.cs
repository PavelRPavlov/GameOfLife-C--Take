using System.Text.Json.Serialization;

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

        services.AddSingleton<GameHost>();
        services.AddHostedService<BroadcastLoopService>();

        return services;
    }
}
