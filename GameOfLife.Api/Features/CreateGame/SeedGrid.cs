namespace GameOfLife.Api.Features.CreateGame;

/// <summary>
/// Decoding of the 100×100 seed grid. The seed is base64 of exactly 1250 bytes = 10,000 bits,
/// row-major, MSB-first, 1 = alive. Cell <c>(row, col)</c> maps to torus
/// <c>(x = origin.x + col, y = origin.y + row)</c> with free <see cref="ulong"/> wraparound.
/// </summary>
public static class SeedGrid
{
    public const int Size = 100;
    public const int ByteLength = Size * Size / 8; // 1250

    /// <summary>
    /// Decodes <paramref name="base64"/> to its raw bytes, requiring exactly <see cref="ByteLength"/>.
    /// Returns false for malformed base64 or a wrong-length payload.
    /// </summary>
    public static bool TryDecode(string base64, out byte[] bytes)
    {
        bytes = [];
        try
        {
            var decoded = Convert.FromBase64String(base64);
            if (decoded.Length != ByteLength) return false;
            bytes = decoded;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    /// <summary>Maps a validated <paramref name="seedBytes"/> grid to live cells at <c>(originX, originY)</c>.</summary>
    public static IReadOnlyCollection<Cell> ToCells(byte[] seedBytes, ulong originX, ulong originY)
    {
        var cells = new List<Cell>();
        for (var i = 0; i < Size * Size; i++)
        {
            var byteIndex = i >> 3;
            var bit = 7 - (i & 7); // MSB-first
            if ((seedBytes[byteIndex] & (1 << bit)) == 0) continue;

            var row = i / Size;
            var col = i % Size;
            cells.Add(new Cell(
                unchecked(originX + (ulong)col),
                unchecked(originY + (ulong)row)));
        }
        return cells;
    }
}
