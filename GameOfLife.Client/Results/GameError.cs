namespace GameOfLife.Client;

/// <summary>
/// The closed union of outcomes that carry UX meaning across the seam. Non-2xx responses that a
/// page must react to are <em>values</em> of this type, not exceptions. One union serves every
/// endpoint (some arms are unreachable for a given verb — accepted for simplicity). The private
/// constructor seals the hierarchy: no case can be added outside this file.
/// </summary>
public abstract record GameError
{
    private GameError() { }

    /// <summary>404 — no game exists (the slot is empty).</summary>
    public sealed record NoGame : GameError
    {
        public static NoGame Instance { get; } = new();
    }

    /// <summary>403 — the admin secret is missing or stale. Callers should clear the secret store.</summary>
    public sealed record Forbidden : GameError
    {
        public static Forbidden Instance { get; } = new();
    }

    /// <summary>409 — the verb is invalid for the current lifecycle state (no-ops are rejected).</summary>
    public sealed record InvalidState : GameError
    {
        public static InvalidState Instance { get; } = new();
    }

    /// <summary>409 on <c>POST /game</c> — a game already exists (first caller won the create race).</summary>
    public sealed record AlreadyExists : GameError
    {
        public static AlreadyExists Instance { get; } = new();
    }

    /// <summary>
    /// The backend rejected the request payload. The client validates seed/rule/tick-rate before
    /// sending, so in practice this is a programming error — but it stays a value for completeness.
    /// </summary>
    public sealed record ValidationRejected(string Details) : GameError;

    /// <summary>An unexpected transport / server failure (network, 5xx, an unforeseen 4xx).</summary>
    public sealed record Transport(string Detail) : GameError;
}
