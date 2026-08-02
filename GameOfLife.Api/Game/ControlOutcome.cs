namespace GameOfLife.Api.Game;

/// <summary>How a control verb (<c>start/stop/pause/resume/step</c>) resolved.</summary>
public enum ControlResult
{
    /// <summary>200 — transition applied.</summary>
    Ok,

    /// <summary>404 — no game exists (slot empty).</summary>
    NoGame,

    /// <summary>403 — bad or missing admin secret.</summary>
    Forbidden,

    /// <summary>409 — the verb is invalid for the current state (no-ops are rejected, not ignored).</summary>
    InvalidState,
}

/// <summary>
/// The kernel's outcome of a control verb, carrying the resulting status/generation on success.
/// This is the kernel's return contract; the <c>GameControl</c> slice projects it to the HTTP response.
/// </summary>
public readonly record struct ControlOutcome(ControlResult Result, GameStatus Status, long Generation)
{
    public static ControlOutcome NoGame { get; } = new(ControlResult.NoGame, GameStatus.NoGame, 0);
    public static ControlOutcome Forbidden { get; } = new(ControlResult.Forbidden, GameStatus.NoGame, 0);

    /// <summary>
    /// A wrong-state rejection carrying the <em>current</em> state, so the HTTP edge can name it in the
    /// error message (there is always a live game when this outcome is produced).
    /// </summary>
    public static ControlOutcome InvalidState(GameStatus current) =>
        new(ControlResult.InvalidState, current, 0);

    public static ControlOutcome Ok(GameStatus status, long generation) =>
        new(ControlResult.Ok, status, generation);
}
