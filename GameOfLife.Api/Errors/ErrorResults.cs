using GameOfLife.Shared;

namespace GameOfLife.Api.Errors;

/// <summary>
/// The single place that shapes an <see cref="ErrorEnvelope"/> into an <see cref="IResult"/>: every
/// expected client-facing failure at the minimal-API edge goes through here, so the wire shape and
/// media type stay uniform. <see cref="Results.Json{TValue}"/> serializes with the application-wide
/// HTTP JSON options (camelCase, string enums), yielding <c>application/json</c> — never
/// <c>problem+json</c>. The redacted 500 path writes its own envelope directly from the exception
/// handler, outside the minimal-API result pipeline.
/// </summary>
internal static class ErrorResults
{
    /// <summary>
    /// A failure response carrying the uniform envelope. <paramref name="errors"/> defaults to an empty
    /// list (the single-error case), so <c>errors</c> is always present on the wire.
    /// </summary>
    public static IResult Envelope(
        int statusCode,
        string code,
        string message,
        IReadOnlyList<FieldError>? errors = null) =>
        Results.Json(new ErrorEnvelope(code, message, errors ?? []), statusCode: statusCode);
}
