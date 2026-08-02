namespace GameOfLife.Core;

/// <summary>
/// A single cell on the torus. Each axis is an independent <see cref="ulong"/> coordinate — so a
/// cell is a full 128-bit position, never a single number. The torus extent is a power of two chosen
/// by the configured <see cref="Universe"/> (2^64 by default); wraparound is a single mask against
/// <see cref="Universe.WrapMask"/> — for the default 2^64 universe that mask is a no-op, so unchecked
/// <see cref="ulong"/> arithmetic already wraps for free and no explicit <c>%</c> is ever needed.
/// </summary>
public readonly record struct Cell(ulong X, ulong Y);
