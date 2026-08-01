using GameOfLife.Api.Contracts;
using GameOfLife.Core;

namespace GameOfLife.Api.Game;

/// <summary>
/// The kernel's view-only read-model at a broadcast boundary: the generation, status, tick rate, and
/// full live set. It is the kernel's return contract for a snapshot; the <c>GetSnapshot</c> slice
/// projects it to the wire <c>SnapshotResponse</c>, keeping the kernel free of HTTP-shape concerns.
/// </summary>
internal sealed record GameSnapshot(
    long Gen,
    GameStatus Status,
    double TickRate,
    IReadOnlyList<CellDto> Cells);
