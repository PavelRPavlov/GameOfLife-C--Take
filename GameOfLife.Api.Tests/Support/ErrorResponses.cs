using System.Net.Http.Json;
using System.Text.Json;
using GameOfLife.Shared;

namespace GameOfLife.Api.Tests.Support;

/// <summary>
/// Test helpers for asserting on the uniform error envelope at the HTTP edge. Every expected failure
/// parses to <see cref="ErrorEnvelope"/> with a non-null <c>code</c>, a non-empty <c>message</c>, and a
/// present (possibly empty) <c>errors</c> array — the cross-cutting shape contract.
/// </summary>
public static class ErrorResponses
{
    /// <summary>
    /// Reads and shape-checks the error envelope: the body is <c>application/json</c>, and the parsed
    /// envelope has a non-empty code/message and a non-null errors list. Optionally asserts the code.
    /// </summary>
    public static async Task<ErrorEnvelope> ReadError(this HttpResponseMessage response, string? expectedCode = null)
    {
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var envelope = await response.Content.ReadFromJsonAsync<ErrorEnvelope>(ApiTestContext.Json);

        Assert.NotNull(envelope);
        Assert.False(string.IsNullOrWhiteSpace(envelope!.Code), "code must be present and non-empty");
        Assert.False(string.IsNullOrWhiteSpace(envelope.Message), "message must be present and non-empty");
        Assert.NotNull(envelope.Errors); // present, possibly empty — never null

        if (expectedCode is not null)
            Assert.Equal(expectedCode, envelope.Code);

        return envelope;
    }

    /// <summary>
    /// Parses the raw response body as a JSON object and returns its top-level property names, so a test
    /// can assert the exact camelCase wire keys and the absence of <c>traceId</c>/<c>status</c>.
    /// </summary>
    public static async Task<IReadOnlyCollection<string>> ReadJsonPropertyNames(this HttpResponseMessage response)
    {
        var raw = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
    }
}
