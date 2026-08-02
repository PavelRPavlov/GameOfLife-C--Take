namespace GameOfLife.Api.Features.GetSnapshot;

/// <summary>200 body for <c>GET /snapshot</c> — the full live set at a known generation.</summary>
public sealed record SnapshotResponse(
    long Gen,
    GameStatus Status,
    double TickRate,
    IReadOnlyList<CellDto> Cells);
