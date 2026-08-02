using System.Net;
using GameOfLife.Api.Tests.Support;
using GameOfLife.Shared;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The global exception handler contract: outside Development, an unhandled exception becomes a generic
/// 500 carrying the uniform error envelope (code INTERNAL_ERROR, a generic message, empty errors) that
/// leaks no exception detail and carries no traceId/status echo; in Development the Developer Exception
/// Page is preserved instead. Driven over real HTTP against a test-only endpoint that throws.
/// </summary>
public class GlobalExceptionHandlingTests
{
    [Fact]
    public async Task Given_a_production_host_When_an_endpoint_throws_an_unhandled_exception_Then_it_returns_a_500_error_envelope()
    {
        await using var ctx = ApiTestContext.Create(environment: "Production", withThrowingEndpoint: true);

        var response = await ctx.Client.GetAsync(ThrowingEndpoint.Route);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        // The redacted 500 speaks the same application/json envelope as every other failure.
        var error = await response.ReadError(ErrorCodes.InternalError);
        Assert.Equal("Something went wrong on our end. Please try again.", error.Message);
        Assert.Empty(error.Errors);

        // No traceId, no echoed status — only the three envelope fields.
        var properties = await response.ReadJsonPropertyNames();
        Assert.Equal(new HashSet<string> { "code", "message", "errors" }, properties.ToHashSet());
    }

    [Fact]
    public async Task Given_a_production_host_When_an_endpoint_throws_an_unhandled_exception_Then_no_exception_detail_is_leaked()
    {
        await using var ctx = ApiTestContext.Create(environment: "Production", withThrowingEndpoint: true);

        var response = await ctx.Client.GetAsync(ThrowingEndpoint.Route);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(ThrowingEndpoint.SensitiveDetail, raw);
        Assert.DoesNotContain(nameof(InvalidOperationException), raw);
        Assert.DoesNotContain("stackTrace", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" at ", raw); // stack-frame lines read "   at Namespace.Method(...)".
    }

    [Fact]
    public async Task Given_a_development_host_When_an_endpoint_throws_an_unhandled_exception_Then_the_developer_exception_page_and_its_detail_are_preserved()
    {
        // The gate's other side: in Development the global handler stays off and the Developer Exception
        // Page keeps surfacing the exception detail locally — the very thing Production must never leak.
        await using var ctx = ApiTestContext.Create(environment: "Development", withThrowingEndpoint: true);

        var response = await ctx.Client.GetAsync(ThrowingEndpoint.Route);
        var raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains(ThrowingEndpoint.SensitiveDetail, raw);
        Assert.Contains(nameof(InvalidOperationException), raw);
    }
}
