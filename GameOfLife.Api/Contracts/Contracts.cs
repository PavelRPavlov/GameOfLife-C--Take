using GameOfLife.Core;

namespace GameOfLife.Api.Contracts;

// The wire contract for the Game of Life backend. These types are Api-internal (per the #2
// structure decision); the REST shapes reach clients through a generated OpenAPI/nswag SDK,
// the SignalR shapes through a bare push-only connection. JSON is the contract of record.

/// <summary>The single game-lifecycle enum. <c>NoGame</c> is only ever a 404, never a body value.</summary>
public enum GameStatus
{
    NoGame,
    Created,
    Running,
    Paused,
}

/// <summary>
/// A torus coordinate: a pair of independent <see cref="ulong"/> axes each carried as a decimal
/// <em>string</em> so values above 2^53 survive JSON without precision loss (and so the OpenAPI
/// schema honestly says <c>type: string</c>). One consistent shape for origin, snapshot cells,
/// and delta births/deaths.
/// </summary>
public sealed record CellDto(string X, string Y);

/// <summary>201 response for <c>POST /game</c>. <see cref="AdminSecret"/> is returned here once only.</summary>
public sealed record CreateGameResponse(
    string AdminSecret,
    GameStatus Status,
    long Generation,
    double TickRate,
    string Rule,
    string HubUrl,
    string SnapshotUrl);

/// <summary>Uniform 200 body shared by all five control verbs. Errors carry no body.</summary>
public sealed record ControlResponse(
    GameStatus Status,
    long Generation);

/// <summary>200 body for <c>GET /snapshot</c> — the full live set at a known generation.</summary>
public sealed record SnapshotResponse(
    long Gen,
    GameStatus Status,
    double TickRate,
    IReadOnlyList<CellDto> Cells);

/// <summary>
/// Hot-path SignalR push. <see cref="FromGen"/>/<see cref="ToGen"/> make each delta self-describing
/// so a client can detect a gap and trip the single resync rule.
/// </summary>
public sealed record DeltaDto(
    long FromGen,
    long ToGen,
    IReadOnlyList<CellDto> Births,
    IReadOnlyList<CellDto> Deaths);

/// <summary>
/// The two server→client pushes the hub invokes. The hub exposes no client-callable methods;
/// used as <c>Hub&lt;IGameClient&gt;</c> for compile-checked strongly-typed pushes.
/// </summary>
public interface IGameClient
{
    Task ReceiveDelta(DeltaDto delta);
    Task ReceiveStatus(GameStatus status);
}

/// <summary>Boundary conversions between the domain <see cref="Cell"/> and the wire <see cref="CellDto"/>.</summary>
public static class CellDtoExtensions
{
    public static CellDto ToDto(this Cell cell) =>
        new(cell.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
            cell.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
