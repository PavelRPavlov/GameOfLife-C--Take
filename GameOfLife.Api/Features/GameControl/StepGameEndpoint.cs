namespace GameOfLife.Api.Features.GameControl;

/// <summary><c>POST /step</c> — advance exactly one generation while Paused, broadcasting immediately. X-Admin-Secret gated.</summary>
internal static class StepGameEndpoint
{
    public static Task<IResult> Handle(HttpContext context, GameHost host) =>
        ControlDispatch.Run(host.Step, context);
}
