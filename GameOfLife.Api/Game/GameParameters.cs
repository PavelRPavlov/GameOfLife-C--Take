namespace GameOfLife.Api.Game;

/// <summary>The validated domain inputs for creating a game — the kernel's create-entry contract.</summary>
public sealed record GameParameters(
    IReadOnlyCollection<Cell> Seed,
    Rule Rule,
    double TickRate,
    bool AutoStart);
