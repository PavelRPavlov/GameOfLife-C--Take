namespace GameOfLife.Api.Game;

/// <summary>
/// The observer pipe: an internal push-only hub with zero client-callable methods. Clients connect
/// and receive <see cref="IGameClient.ReceiveDelta"/> / <see cref="IGameClient.ReceiveStatus"/>
/// pushes to <c>Clients.All</c> (implicit subscribe-on-connect); all control is done over HTTP.
/// </summary>
public sealed class GameHub : Hub<IGameClient>;
