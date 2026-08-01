using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace GameOfLife.Api.Tests.Support;

/// <summary>
/// A test-only fault injector: exposes <c>GET /__throw</c>, whose handler throws an exception carrying
/// a sentinel message, so an unhandled exception can be driven through the real HTTP pipeline. Wired
/// only into the test host (never the shipped API) via <see cref="StartupFilter"/>, which appends a
/// terminal middleware. That middleware runs only for the otherwise-unmatched <see cref="Route"/> and,
/// because it sits downstream of the whole pipeline, its throw is caught by the global exception
/// handler exactly as a real endpoint's would be.
/// </summary>
public static class ThrowingEndpoint
{
    /// <summary>The exception message; tests assert this never appears in the client response.</summary>
    public const string SensitiveDetail = "SENSITIVE-EXCEPTION-DETAIL-should-never-reach-a-client";

    public const string Route = "/__throw";

    public sealed class StartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);

            // Appended last: reached only when no real endpoint matched (e.g. Route), and still inside
            // the exception handler registered earlier in the pipeline.
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == Route)
                    throw new InvalidOperationException(SensitiveDetail);
                await nextMiddleware();
            });
        };
    }
}
