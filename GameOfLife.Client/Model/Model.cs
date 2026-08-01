namespace GameOfLife.Client;

/// <summary>
/// The single game-lifecycle enum, mirroring the backend contract exactly.
/// <c>NoGame</c> means the slot is empty (a <c>GET /snapshot</c> 404); it is never
/// carried in a snapshot body but is a valid client-side status.
/// </summary>
public enum GameStatus
{
    NoGame,
    Created,
    Running,
    Paused,
}

/// <summary>
/// A torus coordinate: a pair of independent <see cref="ulong"/> axes. The "128-bit torus"
/// is the <em>pair</em>, not a 128-bit axis. On the wire each axis travels as a decimal
/// string (<c>CellDto</c>); that string concern is confined to the API implementation, so
/// consumers of the seam only ever see parsed <see cref="Cell"/>s. A <c>readonly record
/// struct</c> so it is a cheap, value-equal key for the live-cell <see cref="System.Collections.Generic.HashSet{T}"/>.
/// </summary>
public readonly record struct Cell(ulong X, ulong Y);

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
/// rulestring, a tick rate (0.1..60 gen/sec), and whether to auto-start. Settings are fixed at
/// create — the backend has no runtime-reconfigure endpoint.
/// </summary>
public sealed record CreateGameRequest(
    string Seed,
    Cell Origin,
    string Rule,
    double TickRate,
    bool AutoStart);
