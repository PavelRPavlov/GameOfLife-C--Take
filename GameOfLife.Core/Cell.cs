namespace GameOfLife.Core;

/// <summary>
/// A single cell on the 2^64 × 2^64 torus. Each axis is an independent <see cref="ulong"/>
/// that wraps mod 2^64 — so a cell is a full 128-bit coordinate, never a single number.
/// Torus wraparound is free: unchecked <see cref="ulong"/> arithmetic is already mod 2^64,
/// so no explicit <c>%</c> is ever needed.
/// </summary>
public readonly record struct Cell(ulong X, ulong Y);
