namespace GameOfLife.Api.Game;

/// <summary>
/// The two server→client pushes the hub invokes. The hub exposes no client-callable methods;
/// used as <c>Hub&lt;IGameClient&gt;</c> for compile-checked strongly-typed pushes.
/// </summary>
public interface IGameClient
{
    Task ReceiveDelta(DeltaDto delta);
    Task ReceiveStatus(GameStatus status);
}
