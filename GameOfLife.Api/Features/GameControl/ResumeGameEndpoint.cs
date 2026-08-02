namespace GameOfLife.Api.Features.GameControl;

/// <summary><c>POST /resume</c> — Paused → Running. X-Admin-Secret gated.</summary>
internal static class ResumeGameEndpoint
{
    public static Task<IResult> Handle(HttpContext context, GameHost host) =>
        ControlDispatch.Run(host.Resume, context);
}
