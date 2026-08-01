using GameOfLife.Api.Game;

namespace GameOfLife.Api.Features.GameControl;

/// <summary><c>POST /resume</c> — Paused → Running. X-Admin-Secret gated.</summary>
internal static class ResumeGameEndpoint
{
    public static Task<IResult> HandleAsync(HttpContext context, GameHost host) =>
        ControlDispatch.RunAsync(host.ResumeAsync, context);
}
