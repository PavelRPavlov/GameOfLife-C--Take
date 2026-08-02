using MessagePack;
using MessagePack.Resolvers;

namespace GameOfLife.WebClient.Communication;

/// <summary>
/// The real <see cref="IGameStream"/> — a thin wrapper over a SignalR <see cref="HubConnection"/> to the
/// backend hub (<c>/hubs/game</c>). The hub is push-only and subscribe-on-connect (no groups, no
/// client-callable methods), so this type only wires the two server pushes and the connection lifecycle:
/// it parses the wire DTOs into the seam's domain <see cref="Delta"/>/<see cref="GameStatus"/> at the
/// boundary and re-raises them raw. It holds <em>no</em> domain state and does no reconcile — buffering,
/// snapshot bootstrap and the gap/resync rule all live in <see cref="GameStore"/>.
///
/// Auto-reconnect is delegated to <see cref="HubConnectionBuilderExtensions.WithAutomaticReconnect(IHubConnectionBuilder, TimeSpan[])"/>
/// (a finite policy — never a hand-rolled loop); its lifecycle surfaces through
/// <see cref="ConnectionStateChanged"/> so the shell can drive Connecting… → Reconnecting… → Retry.
/// </summary>
public sealed class SignalRGameStream : IGameStream
{
    // The default auto-reconnect schedule (0s, 2s, 10s, 30s) made explicit: four finite attempts, then
    // the connection fires Closed and the shell falls back to a manual Retry.
    private static readonly TimeSpan[] ReconnectDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30)];

    private readonly HubConnection _hub;

    public event Action<Delta>? DeltaReceived;
    public event Action<GameStatus>? StatusReceived;
    public event Action<StreamConnectionState>? ConnectionStateChanged;

    public SignalRGameStream(string hubUrl)
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(hubUrl)
            .WithAutomaticReconnect(ReconnectDelays)
            // Match the server's binary MessagePack protocol (GameMessagePack on the backend): contractless
            // records, GameStatus by value, coordinates as native ulong, LZ4BlockArray compression. These
            // options must stay in lockstep with the server's (a compression mismatch fails deserialization)
            // — see GameOfLife.Api/Game/GameMessagePack.cs.
            .AddMessagePackProtocol(options =>
                options.SerializerOptions = MessagePackSerializerOptions.Standard
                    .WithResolver(ContractlessStandardResolver.Instance)
                    .WithSecurity(MessagePackSecurity.UntrustedData)
                    .WithCompression(MessagePackCompression.Lz4BlockArray))
            .Build();

        _hub.On<DeltaPushDto>("ReceiveDelta", dto => DeltaReceived?.Invoke(dto.ToDomain()));
        _hub.On<GameStatus>("ReceiveStatus", status => StatusReceived?.Invoke(status));

        _hub.Reconnecting += _ => Raise(StreamConnectionState.Reconnecting);
        _hub.Reconnected += _ => Raise(StreamConnectionState.Reconnected);
        _hub.Closed += _ => Raise(StreamConnectionState.Closed);
    }

    public Task Connect(CancellationToken ct = default) => _hub.StartAsync(ct);

    public async ValueTask DisposeAsync() => await _hub.DisposeAsync();

    private Task Raise(StreamConnectionState state)
    {
        ConnectionStateChanged?.Invoke(state);
        return Task.CompletedTask;
    }
}
