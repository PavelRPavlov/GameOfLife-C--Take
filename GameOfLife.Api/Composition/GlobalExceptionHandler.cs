using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace GameOfLife.Api.Composition;

/// <summary>
/// The last-resort safety net for <em>unexpected</em> exceptions: any exception that reaches the HTTP
/// pipeline is turned into a generic <c>500</c> <see cref="ProblemDetails"/> (RFC 9457) carrying only a
/// status, a generic title, and a correlation <c>traceId</c> — never the exception type, message, or
/// stack. Expected failures (validation, conflict, auth, wrong state) are still handled explicitly at
/// each endpoint and never reach here; reaching this handler always means an unforeseen fault, which is
/// always a <c>500</c>. The full exception is logged at <c>Error</c> by the framework's
/// <c>ExceptionHandlerMiddleware</c>, so this type deliberately does not log — it only shapes the
/// safe response. It is gated to non-Development environments so the Developer Exception Page keeps
/// serving full stack traces locally (see <see cref="ApiSurfaceRegistration.UseGameApiPipeline"/>).
/// </summary>
internal sealed class GlobalExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        // TryWriteAsync applies the registered ProblemDetails customizations — the generic
        // status-derived title/type and the traceId extension — and writes application/problem+json.
        // The exception is intentionally not passed in, so no detail can leak into the body.
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = { Status = StatusCodes.Status500InternalServerError },
        });
    }
}
