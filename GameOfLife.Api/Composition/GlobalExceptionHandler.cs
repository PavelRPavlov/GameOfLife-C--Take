namespace GameOfLife.Api.Composition;

/// <summary>
/// The last-resort safety net for <em>unexpected</em> exceptions: any exception that reaches the HTTP
/// pipeline is turned into a generic <c>500</c> carrying the same bespoke <see cref="ErrorEnvelope"/> as
/// every other failure — <c>{ code: "INTERNAL_ERROR", message: &lt;generic&gt;, errors: [] }</c> as
/// <c>application/json</c> — never the exception type, message, or stack, and never a <c>traceId</c> or
/// echoed status. Expected failures (validation, conflict, auth, wrong state) are handled explicitly at
/// each endpoint and never reach here; reaching this handler always means an unforeseen fault, always a
/// <c>500</c>. The full exception is still logged at <c>Error</c> by the framework's
/// <c>ExceptionHandlerMiddleware</c> before this runs, so this handler deliberately does not log — it
/// only shapes the safe response. It is wired only for non-Development environments so the Developer
/// Exception Page keeps serving full stack traces locally (see
/// <see cref="ApiSurfaceRegistration.UseGameApiPipeline"/>).
/// </summary>
internal static class GlobalExceptionHandler
{
    /// <summary>
    /// The <c>UseExceptionHandler</c> fallback delegate: writes the redacted 500 envelope. The exception
    /// is never touched, so no detail can reach the body. <c>WriteAsJsonAsync</c> applies the
    /// application-wide HTTP JSON options (camelCase, string enums) and writes <c>application/json</c>.
    /// </summary>
    public static async Task WriteRedactedResponseAsync(HttpContext httpContext)
    {
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            new ErrorEnvelope(ErrorCodes.InternalError, ErrorMessages.InternalError, []));
    }
}
