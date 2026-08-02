namespace GameOfLife.WebClient.Communication;

/// <summary>
/// The closed union of outcomes that carry UX meaning across the seam. Non-2xx responses that a page
/// must react to are <em>values</em> of this type, not exceptions. Each arm is chosen by the server's
/// machine-readable error <c>code</c>; the server also owns the display copy, carried verbatim in
/// <see cref="Message"/> (always non-empty) and shown as-is. Behavior branches on the arm, text comes
/// from <see cref="Message"/>. The private constructor seals the hierarchy: no case can be added
/// outside this file.
/// </summary>
public abstract record GameError
{
    /// <summary>User-presentable text, supplied by the server and shown verbatim. Always non-empty.</summary>
    public string Message { get; }

    // Private ctor seals the hierarchy to the arms declared below (nested types may call it; external
    // assemblies cannot derive).
    private GameError(string message) => Message = message;

    /// <summary>404 — no game exists (the slot is empty).</summary>
    public sealed record NoGame(string Message) : GameError(Message);

    /// <summary>403 — the admin secret is missing or stale. Callers should clear the secret store.</summary>
    public sealed record Forbidden(string Message) : GameError(Message);

    /// <summary>409 — the verb is invalid for the current lifecycle state (no-ops are rejected).</summary>
    public sealed record InvalidState(string Message) : GameError(Message);

    /// <summary>409 on <c>POST /game</c> — a game already exists (first caller won the create race).</summary>
    public sealed record AlreadyExists(string Message) : GameError(Message);

    /// <summary>
    /// 400 — the backend rejected the request payload. <see cref="Errors"/> carries the per-field
    /// breakdown (possibly empty), rendered as a form-level summary alongside <see cref="Message"/>.
    /// </summary>
    public sealed record ValidationRejected(string Message, IReadOnlyList<FieldError> Errors) : GameError(Message);

    /// <summary>
    /// An unexpected transport / server failure (network, 5xx, malformed body, an unforeseen or unknown
    /// code). <see cref="Message"/> is the server's message when one arrived, or the client-owned
    /// no-envelope fallback string otherwise.
    /// </summary>
    public sealed record Transport(string Message) : GameError(Message);
}
