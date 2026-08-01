using GameOfLife.Api.Game;

namespace GameOfLife.Api.Features.GameControl;

/// <summary><c>POST /step</c> — advance exactly one generation while Paused, broadcasting immediately. X-Admin-Secret gated.</summary>
internal static class StepGameEndpoint
{
    public static Task<IResult> HandleAsync(HttpContext context, GameHost host) =>
        ControlDispatch.RunAsync(host.StepAsync, context);
}
