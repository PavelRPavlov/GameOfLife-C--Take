namespace GameOfLife.Api.Tests.Support;

/// <summary>Builders for the 100×100 base64 seed grid used by the create endpoint.</summary>
public static class TestSeeds
{
    public const int Size = 100;
    public const int ByteLength = Size * Size / 8; // 1250

    /// <summary>An all-dead seed (1250 zero bytes) — accepted, starts from an empty world.</summary>
    public static string AllDead() => Convert.ToBase64String(new byte[ByteLength]);

    /// <summary>A seed with the given (row, col) cells alive (row-major, MSB-first).</summary>
    public static string With(params (int row, int col)[] cells)
    {
        var bytes = new byte[ByteLength];
        foreach (var (row, col) in cells)
        {
            var i = row * Size + col;
            bytes[i >> 3] |= (byte)(1 << (7 - (i & 7)));
        }
        return Convert.ToBase64String(bytes);
    }

    /// <summary>A horizontal blinker centred at grid (row, col), i.e. cells at cols col-1, col, col+1.</summary>
    public static string HorizontalBlinker(int row, int col) =>
        With((row, col - 1), (row, col), (row, col + 1));
}
