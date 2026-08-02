namespace GameOfLife.Api.Features.GameControl;

/// <summary><c>POST /start</c> — Created → Running. X-Admin-Secret gated.</summary>
internal static class StartGameEndpoint
{
    public static Task<IResult> Handle(HttpContext context, GameHost host) =>
        ControlDispatch.Run(host.Start, context);
}
