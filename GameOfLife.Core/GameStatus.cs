namespace GameOfLife.Core;

/// <summary>
/// The single game-lifecycle enum, shared by the server (<c>Api</c>) and the browser
/// (<c>WebClient</c>). <c>NoGame</c> means the slot is empty — it surfaces as a
/// <c>GET /snapshot</c> 404 and is never carried in a response body, but is a valid
/// client-side status.
/// </summary>
public enum GameStatus
{
    NoGame,
    Created,
    Running,
    Paused,
}
