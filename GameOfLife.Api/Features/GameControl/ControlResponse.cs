namespace GameOfLife.Api.Features.GameControl;

/// <summary>Uniform 200 body shared by all five control verbs. Errors carry no body.</summary>
public sealed record ControlResponse(
    GameStatus Status,
    long Generation);
