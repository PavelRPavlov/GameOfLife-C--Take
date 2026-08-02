namespace GameOfLife.Api.Game;

/// <summary>
/// Hot-path SignalR push (MessagePack). <see cref="FromGen"/>/<see cref="ToGen"/> make each delta
/// self-describing so a client can detect a gap and trip the single resync rule.
///
/// Coordinates travel as native <see cref="ulong"/> (not the REST <c>CellDto</c> decimal strings):
/// this is a binary protocol, so values above 2^53 and the <c>0</c>/<c>2^64-1</c> boundaries
/// round-trip exactly with no precision loss. The axes are <em>columnar</em> (all X's, then all Y's)
/// rather than interleaved so coordinates sharing high-order bytes sit adjacently — longer runs for
/// the LZ4 compression follow-on. Invariant: <c>BirthsX.Length == BirthsY.Length</c> and
/// <c>DeathsX.Length == DeathsY.Length</c>; <c>X[i]</c> pairs with <c>Y[i]</c>.
/// </summary>
public sealed record DeltaDto(
    long FromGen,
    long ToGen,
    ulong[] BirthsX, ulong[] BirthsY,
    ulong[] DeathsX, ulong[] DeathsY);
