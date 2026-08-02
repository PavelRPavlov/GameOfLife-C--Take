namespace GameOfLife.Api.Features.GameControl;

/// <summary><c>POST /stop</c> — any existing state → torn down, freeing the slot. X-Admin-Secret gated.</summary>
internal static class StopGameEndpoint
{
    public static Task<IResult> HandleAsync(HttpContext context, GameHost host) =>
        ControlDispatch.RunAsync(host.StopAsync, context);
}
