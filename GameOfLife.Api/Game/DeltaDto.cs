using GameOfLife.Api.Contracts;

namespace GameOfLife.Api.Game;

/// <summary>
/// Hot-path SignalR push. <see cref="FromGen"/>/<see cref="ToGen"/> make each delta self-describing
/// so a client can detect a gap and trip the single resync rule.
/// </summary>
public sealed record DeltaDto(
    long FromGen,
    long ToGen,
    IReadOnlyList<CellDto> Births,
    IReadOnlyList<CellDto> Deaths);
