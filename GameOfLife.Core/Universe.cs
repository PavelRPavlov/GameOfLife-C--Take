namespace GameOfLife.Core;

/// <summary>
/// The torus the game lives on: a 2^<see cref="BitWidth"/> × 2^<see cref="BitWidth"/> wraparound
/// grid. The whole reason wraparound is free is that the size is a power of two, so wrapping is a
/// single <c>&amp;</c> against <see cref="WrapMask"/> — exactly as cheap as <see cref="ulong"/>'s
/// native mod-2^64 arithmetic, never a <c>%</c>. <see cref="BitWidth"/> is therefore always the bit
/// width of an unsigned integer coordinate type (8/16/32/64 for byte/ushort/uint/ulong).
/// </summary>
/// <remarks>
/// Coordinates are always stored as <see cref="ulong"/> (see <see cref="Cell"/>); a narrower universe
/// simply constrains them to the low <see cref="BitWidth"/> bits. The default <see cref="Full"/>
/// universe (2^64) leaves every <see cref="ulong"/> value untouched, so it is behaviourally identical
/// to unchecked <see cref="ulong"/> arithmetic.
/// </remarks>
public readonly record struct Universe
{
    /// <summary>The full 2^64 torus — <see cref="ulong"/>'s native range, wrapping for free.</summary>
    public static Universe Full { get; } = new(64);

    // Coordinate type names accepted in configuration → their bit width. Only unsigned integer types
    // wrap cleanly under unchecked arithmetic, so only they are admissible; signed and non-integer
    // types are absent by design and rejected at startup. Both the C# keyword and the CLR name are
    // accepted, case-insensitively (see TryParseCoordinateType).
    private static readonly IReadOnlyDictionary<string, int> BitWidthsByTypeName =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["byte"] = 8, ["uint8"] = 8,
            ["ushort"] = 16, ["uint16"] = 16,
            ["uint"] = 32, ["uint32"] = 32,
            ["ulong"] = 64, ["uint64"] = 64,
        };

    /// <summary>The coordinate type names accepted by <see cref="TryParseCoordinateType"/>, for diagnostics.</summary>
    public static IEnumerable<string> SupportedCoordinateTypeNames => BitWidthsByTypeName.Keys;

    /// <summary>Creates a torus whose axes wrap mod 2^<paramref name="bitWidth"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="bitWidth"/> is not one of 8, 16, 32, or 64 — the only widths that correspond to
    /// an unsigned integer type and so wrap with a single mask.
    /// </exception>
    public Universe(int bitWidth)
    {
        if (bitWidth is not (8 or 16 or 32 or 64))
            throw new ArgumentOutOfRangeException(nameof(bitWidth), bitWidth,
                "Universe bit width must be 8, 16, 32, or 64 (the widths of byte/ushort/uint/ulong).");

        BitWidth = bitWidth;
        // 2^BitWidth - 1, computed without overflowing at width 64 (1UL << 64 is undefined).
        WrapMask = bitWidth == 64 ? ulong.MaxValue : (1UL << bitWidth) - 1;
    }

    /// <summary>The bit width of the coordinate axis — the torus is 2^<see cref="BitWidth"/> cells wide.</summary>
    public int BitWidth { get; }

    /// <summary>The mask that performs wraparound: <c>coordinate &amp; WrapMask</c> == coordinate mod 2^BitWidth.</summary>
    public ulong WrapMask { get; }

    /// <summary>Wraps a coordinate onto the torus with a single mask — the entire wraparound mechanism.</summary>
    public ulong Wrap(ulong coordinate) => coordinate & WrapMask;

    /// <summary>
    /// Maps a coordinate type name (e.g. <c>"UInt64"</c>, <c>"ulong"</c>, <c>"byte"</c>) to its
    /// <see cref="Universe"/>. Only unsigned integer types are admissible because they are the only
    /// ones whose unchecked arithmetic wraps mod 2^N for free; any other name (signed, floating, or
    /// unknown) yields <see langword="false"/>. The match is case-insensitive and accepts both the C#
    /// keyword (<c>ulong</c>) and the CLR name (<c>UInt64</c>).
    /// </summary>
    public static bool TryParseCoordinateType(string? typeName, out Universe universe)
    {
        if (typeName is not null && BitWidthsByTypeName.TryGetValue(typeName.Trim(), out var bitWidth))
        {
            universe = new Universe(bitWidth);
            return true;
        }

        universe = default;
        return false;
    }
}
