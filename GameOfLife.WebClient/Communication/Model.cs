namespace GameOfLife.WebClient.Communication;

/// <summary>
/// Success payload of <c>POST /game</c>. The <see cref="Secret"/> is the admin capability,
/// returned exactly once by the backend; <see cref="GameStore"/> persists it via
/// <see cref="IAdminSecretStore"/> and never surfaces it to component code.
/// </summary>
public sealed record CreatedGame(
    string Secret,
    GameStatus Status,
    long Generation,
    double TickRate,
    string Rule);

/// <summary>The uniform success payload of the five control verbs (start/stop/pause/resume/step).</summary>
public sealed record ControlOutcome(
    GameStatus Status,
    long Generation);

/// <summary>The full live set at a known generation — the bootstrap for the attach protocol.</summary>
public sealed record Snapshot(
    long Gen,
    GameStatus Status,
    double TickRate,
    IReadOnlyList<Cell> Cells);

/// <summary>
/// A steady-state change from <see cref="FromGen"/> to <see cref="ToGen"/>. Self-describing
/// generations let <see cref="GameStore"/> detect duplicates, out-of-order arrivals, and gaps.
/// </summary>
public sealed record Delta(
    long FromGen,
    long ToGen,
    IReadOnlyList<Cell> Births,
    IReadOnlyList<Cell> Deaths);

/// <summary>
/// The client-side <c>POST /game</c> request. Mirrors the backend body: a base64 100×100 seed
/// (1250 bytes, row-major, MSB-first), an <see cref="Origin"/> placing it on the torus, a B/S
/// rulestring, a tick rate (0.1..200 gen/sec), and whether to auto-start. Settings are fixed at
/// create — the backend has no runtime-reconfigure endpoint.
/// </summary>
public sealed record CreateGameRequest(
    string Seed,
    Cell Origin,
    string Rule,
    double TickRate,
    bool AutoStart);
