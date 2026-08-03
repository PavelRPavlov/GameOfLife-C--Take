namespace GameOfLife.WebClient.Communication.Seeding;

/// <summary>
/// The mutable 100×100 seed-authoring bitmap and its encoder — the client counterpart to the
/// backend's decode (<c>Api.Features.CreateGame.SeedGrid</c>). The create form paints, stamps
/// presets, and pastes RLE into it, then hands <see cref="ToBase64"/> straight to <c>POST /game</c>
/// as <c>CreateGameRequest.Seed</c>.
/// </summary>
/// <remarks>
/// Encoding is <b>row-major, MSB-first</b>: cell <c>(x = col, y = row)</c> occupies bit
/// <c>i = y·100 + x</c>, byte <c>i / 8</c>, mask <c>0x80 &gt;&gt; (i % 8)</c> — bit-for-bit what the
/// backend's <c>SeedGrid.ToCells</c> reads back (verified against it). Framework-free, so the
/// encoder, stamping, and out-of-bounds clamping are unit-tested off the Wasm host.
/// </remarks>
public sealed class SeedBoard
{
    public const int Size = 100;
    public const int ByteLength = Size * Size / 8; // 1250

    // Row-major: index = y * Size + x, matching the backend's bit order.
    private readonly bool[] _cells = new bool[Size * Size];

    /// <summary>Live-cell count, kept incrementally so the UI can show it without a scan.</summary>
    public int AliveCount { get; private set; }

    /// <summary>Reads a cell; out-of-bounds coordinates read as dead.</summary>
    public bool Get(int x, int y) => InBounds(x, y) && _cells[Index(x, y)];

    /// <summary>Sets a cell alive/dead; out-of-bounds coordinates are ignored.</summary>
    public void Set(int x, int y, bool alive)
    {
        if (!InBounds(x, y)) return;
        ref var slot = ref _cells[Index(x, y)];
        if (slot == alive) return;
        slot = alive;
        AliveCount += alive ? 1 : -1;
    }

    /// <summary>Empties the board.</summary>
    public void Clear()
    {
        Array.Clear(_cells);
        AliveCount = 0;
    }

    /// <summary>Flips every cell.</summary>
    public void Invert()
    {
        for (var i = 0; i < _cells.Length; i++) _cells[i] = !_cells[i];
        AliveCount = _cells.Length - AliveCount;
    }

    /// <summary>Fills the board randomly at the given alive-<paramref name="density"/> (0..1).</summary>
    public void Randomize(Random rng, double density)
    {
        var count = 0;
        for (var i = 0; i < _cells.Length; i++)
        {
            var alive = rng.NextDouble() < density;
            _cells[i] = alive;
            if (alive) count++;
        }
        AliveCount = count;
    }

    /// <summary>
    /// Replaces the board from the row-major, MSB-first packing produced by <see cref="ToPackedBytes"/>
    /// (the exact inverse). Used to restore a seed persisted as base64 (save/load to local storage).
    /// </summary>
    /// <exception cref="ArgumentException">The span is not exactly <see cref="ByteLength"/> bytes.</exception>
    public void LoadPacked(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != ByteLength)
            throw new ArgumentException($"Expected {ByteLength} bytes, got {bytes.Length}.", nameof(bytes));

        var count = 0;
        for (var i = 0; i < _cells.Length; i++)
        {
            var alive = (bytes[i >> 3] & (0x80 >> (i & 7))) != 0;
            _cells[i] = alive;
            if (alive) count++;
        }
        AliveCount = count;
    }

    /// <summary>
    /// Restores the board from a base64 <see cref="ToBase64"/> string, returning <c>false</c> (leaving the
    /// board untouched) for null/empty, non-base64, or wrong-length input — a lenient load for whatever
    /// happens to sit in local storage.
    /// </summary>
    public bool TryLoadBase64(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return false;

        byte[] bytes;
        try { bytes = Convert.FromBase64String(base64); }
        catch (FormatException) { return false; }

        if (bytes.Length != ByteLength) return false;

        LoadPacked(bytes);
        return true;
    }

    /// <summary>Stamps <paramref name="pattern"/> with its top-left at <c>(ox, oy)</c>, clipping OOB cells.</summary>
    public void Stamp(SeedPattern pattern, int ox, int oy)
    {
        foreach (var (x, y) in pattern.Cells) Set(ox + x, oy + y, true);
    }

    /// <summary>Stamps <paramref name="pattern"/> centred on the board (bounding box centred).</summary>
    public void StampCentered(SeedPattern pattern)
        => Stamp(pattern, (Size - pattern.Width) / 2, (Size - pattern.Height) / 2);

    /// <summary>Row-major, MSB-first packing into exactly <see cref="ByteLength"/> bytes.</summary>
    public byte[] ToPackedBytes()
    {
        var bytes = new byte[ByteLength];
        for (var i = 0; i < _cells.Length; i++)
            if (_cells[i]) bytes[i >> 3] |= (byte)(0x80 >> (i & 7));
        return bytes;
    }

    /// <summary>The seed as the backend wants it: base64 of the 1250-byte packing.</summary>
    public string ToBase64() => Convert.ToBase64String(ToPackedBytes());

    private static bool InBounds(int x, int y) => (uint)x < Size && (uint)y < Size;

    private static int Index(int x, int y) => y * Size + x;
}
