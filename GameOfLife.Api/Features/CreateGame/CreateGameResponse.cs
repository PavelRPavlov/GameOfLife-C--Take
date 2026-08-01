using GameOfLife.Core;

namespace GameOfLife.Api.Features.CreateGame;

/// <summary>201 response for <c>POST /game</c>. <see cref="AdminSecret"/> is returned here once only.</summary>
public sealed record CreateGameResponse(
    string AdminSecret,
    GameStatus Status,
    long Generation,
    double TickRate,
    string Rule,
    string HubUrl,
    string SnapshotUrl);
