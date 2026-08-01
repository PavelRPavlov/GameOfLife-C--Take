using System.Net;
using System.Text.Json;
using GameOfLife.Api.Tests.Support;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The global exception handler contract: outside Development, an unhandled exception becomes a generic
/// 500 ProblemDetails that leaks no exception detail and carries a correlation traceId; in Development
/// the Developer Exception Page is preserved instead. Driven over real HTTP against a test-only
/// endpoint that throws.
/// </summary>
public class GlobalExceptionHandlingTests
{
    [Fact]
    public async Task Unhandled_exception_outside_development_returns_500_problem_details_with_traceId()
    {
        await using var ctx = ApiTestContext.Create(environment: "Production", withThrowingEndpoint: true);

        var response = await ctx.Client.GetAsync(ThrowingEndpoint.Route);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        Assert.Equal(500, root.GetProperty("status").GetInt32());
        Assert.True(root.TryGetProperty("title", out var title) && !string.IsNullOrWhiteSpace(title.GetString()));
        Assert.True(root.TryGetProperty("traceId", out var traceId) && !string.IsNullOrWhiteSpace(traceId.GetString()));
    }

    [Fact]
    public async Task Unhandled_exception_never_leaks_exception_detail()
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
    public async Task Development_preserves_the_developer_exception_page_and_its_detail()
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
