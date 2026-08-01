using GameOfLife.Api.Game;

namespace GameOfLife.Api.Features.GameControl;

/// <summary><c>POST /start</c> — Created → Running. X-Admin-Secret gated.</summary>
internal static class StartGameEndpoint
{
    public static Task<IResult> HandleAsync(HttpContext context, GameHost host) =>
        ControlDispatch.RunAsync(host.StartAsync, context);
}
