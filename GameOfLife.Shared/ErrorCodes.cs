namespace GameOfLife.Shared;

/// <summary>
/// The single, framework-free source of truth for the API's machine-readable error <c>code</c>
/// values — one per distinct failure <em>reason</em>, not per HTTP status. Shared by the server
/// (<c>Api</c>) and the browser (<c>WebClient</c>) so both agree on the same compile-time contract:
/// the server tags every failure envelope with one of these, the client branches on it.
///
/// <para>
/// The C# identifier is <c>PascalCase</c>; its <em>value</em> is the wire token
/// (<c>SCREAMING_SNAKE_CASE</c>). A shipped value is frozen for life — <strong>additive-only</strong>:
/// a new failure reason gets a brand-new code, and existing codes are never renamed or repurposed.
/// Clients must tolerate an unrecognized code by falling back to the envelope's <c>message</c>, so
/// the server can add codes with zero client coordination.
/// </para>
/// </summary>
public static class ErrorCodes
{
    /// <summary>400 — field validation failed; the specifics are in <c>errors[]</c>. (<c>POST /game</c>)</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>400 — the request body was unparseable / not JSON / had an unknown property / was empty. (<c>POST /game</c>)</summary>
    public const string MalformedRequestBody = "MALFORMED_REQUEST_BODY";

    /// <summary>409 — a game already exists (only one runs at a time). (<c>POST /game</c>)</summary>
    public const string GameAlreadyExists = "GAME_ALREADY_EXISTS";

    /// <summary>403 — the admin secret is bad or missing. (control verbs)</summary>
    public const string InvalidAdminSecret = "INVALID_ADMIN_SECRET";

    /// <summary>409 — the verb is illegal for the current lifecycle state. (control verbs)</summary>
    public const string InvalidStateForVerb = "INVALID_STATE_FOR_VERB";

    /// <summary>404 — no game exists. (control verbs, <c>GET /snapshot</c>)</summary>
    public const string GameNotFound = "GAME_NOT_FOUND";

    /// <summary>500 — an unexpected fault, redacted so no exception detail leaks. (any endpoint)</summary>
    public const string InternalError = "INTERNAL_ERROR";
}
