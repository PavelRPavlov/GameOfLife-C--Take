using GameOfLife.Api.Game;

namespace GameOfLife.Api.Features.GameControl;

/// <summary>
/// Shared control-verb dispatch for the five verb endpoints: read the <c>X-Admin-Secret</c> header,
/// run the verb on the kernel, and map the resulting <see cref="ControlOutcome"/> to the uniform HTTP
/// contract — a 200 <see cref="ControlResponse"/> on success, bodyless 404/403/409 otherwise. The
/// existence → auth → state ordering itself lives in <see cref="GameHost"/>; this is only the HTTP edge.
/// </summary>
internal static class ControlDispatch
{
    public static async Task<IResult> RunAsync(Func<string?, Task<ControlOutcome>> verb, HttpContext context)
    {
        var secret = context.Request.Headers["X-Admin-Secret"].ToString();
        var outcome = await verb(secret);
        return ToResult(outcome);
    }

    private static IResult ToResult(ControlOutcome outcome) => outcome.Result switch
    {
        ControlResult.Ok => Results.Ok(new ControlResponse(outcome.Status, outcome.Generation)),
        ControlResult.NoGame => Results.NotFound(),
        ControlResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
        ControlResult.InvalidState => Results.Conflict(),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };
}
