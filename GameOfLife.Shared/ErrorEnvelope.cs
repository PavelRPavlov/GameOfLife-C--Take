namespace GameOfLife.Shared;

/// <summary>
/// The one JSON shape every expected client-facing API failure returns — 400 / 403 / 404 / 409 and the
/// redacted 500 alike — so a consumer writes one deserializer and one error-handling path. Serialized
/// camelCase (<c>{ "code", "message", "errors" }</c>) as <c>application/json</c> (deliberately
/// <em>not</em> <c>application/problem+json</c>); carries no <c>traceId</c> and no echoed HTTP status.
/// </summary>
/// <param name="Code">
/// Always present, non-null. The machine-readable discriminant — one of <see cref="ErrorCodes"/>. The
/// client branches on this rather than inferring intent from HTTP status plus endpoint.
/// </param>
/// <param name="Message">
/// Always present, non-null. User-presentable copy the client shows verbatim. <em>Not</em> part of the
/// contract — the server may reword or localize it freely, so clients never switch on it.
/// </param>
/// <param name="Errors">
/// Always present — <c>[]</c> for single-error cases (control verbs, the 500), populated only for
/// <see cref="ErrorCodes.ValidationFailed"/>. Never null, so a consumer iterates without a null-check.
/// </param>
public sealed record ErrorEnvelope(
    string Code,
    string Message,
    IReadOnlyList<FieldError> Errors);

/// <summary>
/// One per-field validation violation inside <see cref="ErrorEnvelope.Errors"/>. A field may repeat
/// (one entry per violation) and there is no per-entry code.
/// </summary>
/// <param name="Field">
/// The camelCase input name (e.g. <c>"tickRate"</c>), or <c>null</c> for an object-level error the
/// consumer renders form-level rather than attributing to one input.
/// </param>
/// <param name="Message">User-presentable copy for this violation, shown verbatim.</param>
public sealed record FieldError(
    string? Field,
    string Message);
