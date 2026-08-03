namespace GameOfLife.Api.Errors;

/// <summary>
/// The single server-side home for every user-facing error <em>message</em> (English-only for v1).
/// Copy lives here — deliberately <em>not</em> in <c>GameOfLife.Shared</c> — because the client reads
/// each <c>message</c> verbatim off the wire and never references these constants; the shared library
/// carries only the contract (<see cref="Shared.ErrorCodes"/> + the envelope DTOs). Keeping copy in one
/// place lets it be reworded or localized later without touching the contract or the client.
///
/// <para>
/// Voice: user-presentable (no stack-speak — no types, encodings, byte counts, or regex), calm and
/// blame-neutral, complete sentences, addressing the caller as "you" and our side as "our end".
/// </para>
/// </summary>
internal static class ErrorMessages
{
    // ---- Top-level messages, one per code ----

    public const string ValidationFailed =
        "Some of the values you provided aren't valid. Please check the highlighted fields and try again.";

    public const string MalformedRequestBody =
        "We couldn't read your request. Please try again.";

    public const string GameAlreadyExists =
        "A game already exists. Only one game can run at a time.";

    public const string InvalidAdminSecret =
        "Your admin access isn't valid. You may need to create a new game to get a fresh admin link.";

    public const string GameNotFound =
        "There's no game right now.";

    public const string InternalError =
        "Something went wrong on our end. Please try again.";

    /// <summary>
    /// The one dynamic message: names the <em>current</em> game state in friendly words (the high-value
    /// "why"), not the attempted verb (the user knows which button they pressed).
    /// </summary>
    public static string InvalidStateForVerb(GameStatus current) =>
        $"That action isn't available while the game is {DescribeState(current)}.";

    /// <summary>The current state as a friendly, localizable token.</summary>
    private static string DescribeState(GameStatus status) => status switch
    {
        GameStatus.Created => "waiting to start",
        GameStatus.Running => "running",
        GameStatus.Paused => "paused",
        // Defensive: a wrong-state failure always has a live game, so NoGame is unreachable here.
        _ => "not running",
    };

    // ---- Per-field validation messages (VALIDATION_FAILED). field keys are the camelCase JSON names. ----

    public const string SeedRequired = "A starting grid is required.";
    public const string SeedInvalid =
        "The starting grid isn't in the expected format. Please regenerate it and try again.";

    public const string OriginRequired = "A starting position is required.";
    public const string OriginInvalid = "The starting position must use whole, non-negative numbers.";

    public const string AutoStartRequired =
        "Please choose whether the game should start automatically.";

    public const string RuleInvalid =
        "That rule isn't valid. Use a birth/survival rule like \"B3/S23\" — birth on 0 neighbours isn't allowed.";

    public const string TickRateRequired = "A tick rate is required.";
    public const string TickRateInvalid =
        "The tick rate must be between 60 and 250 generations per second.";
}
