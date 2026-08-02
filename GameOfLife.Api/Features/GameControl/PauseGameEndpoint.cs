namespace GameOfLife.Api.Features.GameControl;

/// <summary><c>POST /pause</c> — Running → Paused. X-Admin-Secret gated.</summary>
internal static class PauseGameEndpoint
{
    public static Task<IResult> Handle(HttpContext context, GameHost host) =>
        ControlDispatch.Run(host.Pause, context);
}
