using GameOfLife.WebClient.Communication.Seeding;

namespace GameOfLife.WebClient.Tests;

public class SeedBoardTests
{
    // Mirror of the backend's SeedGrid.ToCells decode (row-major, MSB-first) so we can assert the
    // client encoder round-trips bit-for-bit with what the server reads back.
    private static HashSet<(int X, int Y)> Decode(byte[] bytes)
    {
        var cells = new HashSet<(int, int)>();
        for (var i = 0; i < SeedBoard.Size * SeedBoard.Size; i++)
        {
            var bit = 7 - (i & 7);
            if ((bytes[i >> 3] & (1 << bit)) != 0)
                cells.Add((i % SeedBoard.Size, i / SeedBoard.Size));
        }
        return cells;
    }

    [Fact]
    public void EmptyBoard_EncodesTo1250ZeroBytes()
    {
        var bytes = new SeedBoard().ToPackedBytes();

        Assert.Equal(SeedBoard.ByteLength, bytes.Length);
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void ToBase64_DecodesToExactly1250Bytes()
    {
        var decoded = Convert.FromBase64String(new SeedBoard().ToBase64());
        Assert.Equal(1250, decoded.Length);
    }

    [Theory]
    [InlineData(0, 0, 0, 0x80)] // bit 0  -> byte 0, MSB
    [InlineData(1, 0, 0, 0x40)] // bit 1  -> byte 0
    [InlineData(7, 0, 0, 0x01)] // bit 7  -> byte 0, LSB
    [InlineData(8, 0, 1, 0x80)] // bit 8  -> byte 1, MSB
    [InlineData(0, 1, 12, 0x08)] // bit 100 -> byte 12, mask 0x80>>4
    public void Set_SetsTheExpectedBit(int x, int y, int byteIndex, int mask)
    {
        var board = new SeedBoard();
        board.Set(x, y, true);

        var bytes = board.ToPackedBytes();
        Assert.Equal(mask, bytes[byteIndex]);
        // no other byte is touched
        for (var i = 0; i < bytes.Length; i++)
            if (i != byteIndex) Assert.Equal(0, bytes[i]);
    }

    [Fact]
    public void Encoder_RoundTripsWithBackendDecode()
    {
        var board = new SeedBoard();
        (int X, int Y)[] live = [(0, 0), (5, 3), (99, 99), (0, 99), (99, 0), (42, 17)];
        foreach (var (x, y) in live) board.Set(x, y, true);

        var decoded = Decode(board.ToPackedBytes());

        Assert.Equal(live.ToHashSet(), decoded);
    }

    [Fact]
    public void Set_TracksAliveCount_AndIsIdempotent()
    {
        var board = new SeedBoard();
        board.Set(1, 1, true);
        board.Set(1, 1, true); // idempotent
        board.Set(2, 2, true);
        Assert.Equal(2, board.AliveCount);

        board.Set(1, 1, false);
        Assert.Equal(1, board.AliveCount);
    }

    [Fact]
    public void Set_OutOfBounds_IsIgnored()
    {
        var board = new SeedBoard();
        board.Set(-1, 0, true);
        board.Set(0, -1, true);
        board.Set(SeedBoard.Size, 0, true);
        board.Set(0, SeedBoard.Size, true);

        Assert.Equal(0, board.AliveCount);
    }

    [Fact]
    public void Clear_And_Invert_MaintainAliveCount()
    {
        var board = new SeedBoard();
        board.Set(0, 0, true);
        board.Set(1, 0, true);

        board.Invert();
        Assert.Equal(SeedBoard.Size * SeedBoard.Size - 2, board.AliveCount);
        Assert.False(board.Get(0, 0));
        Assert.True(board.Get(2, 0));

        board.Clear();
        Assert.Equal(0, board.AliveCount);
    }

    [Fact]
    public void Stamp_ClipsCellsOutsideTheBoard()
    {
        var board = new SeedBoard();
        var block = SeedPatterns.All.First(p => p.Name == "Block"); // 2x2 at (0,0),(1,0),(0,1),(1,1)

        // Top-left at (99, 99): only (99,99) lands; the other three are off the board.
        board.Stamp(block, 99, 99);

        Assert.Equal(1, board.AliveCount);
        Assert.True(board.Get(99, 99));
    }

    [Fact]
    public void StampCentered_PlacesTheBoundingBoxInTheMiddle()
    {
        var board = new SeedBoard();
        var block = SeedPatterns.All.First(p => p.Name == "Block"); // 2x2

        board.StampCentered(block);

        var ox = (SeedBoard.Size - 2) / 2; // 49
        Assert.Equal(4, board.AliveCount);
        Assert.True(board.Get(ox, ox));
        Assert.True(board.Get(ox + 1, ox + 1));
    }

    [Fact]
    public void Load_ReplacesTheBoard()
    {
        var board = new SeedBoard();
        board.Set(0, 0, true);

        board.Load([(5, 5), (6, 6)]);

        Assert.Equal(2, board.AliveCount);
        Assert.False(board.Get(0, 0));
        Assert.True(board.Get(5, 5));
    }
}
