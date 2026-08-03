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
    public void Given_an_empty_board_When_encoding_to_packed_bytes_Then_it_produces_1250_zero_bytes()
    {
        var bytes = new SeedBoard().ToPackedBytes();

        Assert.Equal(SeedBoard.ByteLength, bytes.Length);
        Assert.All(bytes, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Given_an_empty_board_When_encoding_to_base64_Then_it_decodes_to_exactly_1250_bytes()
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
    public void Given_a_cell_coordinate_When_setting_it_Then_the_expected_bit_is_set_and_no_other_byte_is_touched(int x, int y, int byteIndex, int mask)
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
    public void Given_live_cells_on_a_board_When_encoded_Then_the_packing_round_trips_with_the_backend_decode()
    {
        var board = new SeedBoard();
        (int X, int Y)[] live = [(0, 0), (5, 3), (99, 99), (0, 99), (99, 0), (42, 17)];
        foreach (var (x, y) in live) board.Set(x, y, true);

        var decoded = Decode(board.ToPackedBytes());

        Assert.Equal(live.ToHashSet(), decoded);
    }

    [Fact]
    public void Given_a_board_When_setting_cells_on_and_off_Then_the_alive_count_tracks_and_setting_is_idempotent()
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
    public void Given_out_of_bounds_coordinates_When_setting_them_Then_they_are_ignored()
    {
        var board = new SeedBoard();
        board.Set(-1, 0, true);
        board.Set(0, -1, true);
        board.Set(SeedBoard.Size, 0, true);
        board.Set(0, SeedBoard.Size, true);

        Assert.Equal(0, board.AliveCount);
    }

    [Fact]
    public void Given_a_board_with_live_cells_When_inverted_and_cleared_Then_the_alive_count_is_maintained()
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
    public void Given_a_pattern_stamped_near_the_edge_When_stamping_Then_cells_outside_the_board_are_clipped()
    {
        var board = new SeedBoard();
        var block = SeedPatterns.All.First(p => p.Name == "Block"); // 2x2 at (0,0),(1,0),(0,1),(1,1)

        // Top-left at (99, 99): only (99,99) lands; the other three are off the board.
        board.Stamp(block, 99, 99);

        Assert.Equal(1, board.AliveCount);
        Assert.True(board.Get(99, 99));
    }

    [Fact]
    public void Given_a_pattern_When_stamped_centered_Then_its_bounding_box_is_placed_in_the_middle()
    {
        var board = new SeedBoard();
        var block = SeedPatterns.All.First(p => p.Name == "Block"); // 2x2

        board.StampCentered(block);

        var ox = (SeedBoard.Size - 2) / 2; // 49
        Assert.Equal(4, board.AliveCount);
        Assert.True(board.Get(ox, ox));
        Assert.True(board.Get(ox + 1, ox + 1));
    }

    [Theory]
    [InlineData(0.0)] // density 0 → no cell alive
    [InlineData(1.0)] // density 1 → every cell alive
    public void Given_an_extreme_density_When_randomizing_Then_the_board_fills_predictably(double density)
    {
        var board = new SeedBoard();
        board.Set(0, 0, true); // pre-existing state must be overwritten

        board.Randomize(new Random(1234), density);

        var expected = density >= 1.0 ? SeedBoard.Size * SeedBoard.Size : 0;
        Assert.Equal(expected, board.AliveCount);
    }

    [Fact]
    public void Given_a_randomized_board_When_counting_alive_cells_Then_the_count_matches_the_encoded_bits()
    {
        var board = new SeedBoard();
        board.Randomize(new Random(42), 0.5);

        // The incrementally-tracked count must equal the actual set bits in the packing.
        var setBits = board.ToPackedBytes().Sum(b => System.Numerics.BitOperations.PopCount(b));
        Assert.Equal(setBits, board.AliveCount);
        Assert.InRange(board.AliveCount, 1, SeedBoard.Size * SeedBoard.Size - 1); // ~half, so neither extreme
    }

    [Fact]
    public void Given_a_packed_board_When_loaded_back_Then_it_is_the_exact_inverse_of_ToPackedBytes()
    {
        var original = new SeedBoard();
        (int X, int Y)[] live = [(0, 0), (5, 3), (99, 99), (0, 99), (99, 0), (42, 17)];
        foreach (var (x, y) in live) original.Set(x, y, true);

        var restored = new SeedBoard();
        restored.LoadPacked(original.ToPackedBytes());

        Assert.Equal(original.AliveCount, restored.AliveCount);
        Assert.Equal(live.Length, restored.AliveCount);
        foreach (var (x, y) in live) Assert.True(restored.Get(x, y));
    }

    [Fact]
    public void Given_a_board_with_existing_cells_When_loading_an_all_dead_packing_Then_the_existing_cells_are_replaced()
    {
        var board = new SeedBoard();
        board.Set(1, 1, true);

        board.LoadPacked(new byte[SeedBoard.ByteLength]); // all-dead packing

        Assert.Equal(0, board.AliveCount);
        Assert.False(board.Get(1, 1));
    }

    [Theory]
    [InlineData(1249)]
    [InlineData(1251)]
    public void Given_a_packing_of_the_wrong_length_When_loading_it_Then_it_throws(int length)
        => Assert.Throws<ArgumentException>(() => new SeedBoard().LoadPacked(new byte[length]));

    [Fact]
    public void Given_a_base64_board_When_try_loaded_Then_it_round_trips_with_ToBase64()
    {
        var original = new SeedBoard();
        original.Set(3, 4, true);
        original.Set(50, 50, true);

        var restored = new SeedBoard();
        var ok = restored.TryLoadBase64(original.ToBase64());

        Assert.True(ok);
        Assert.Equal(2, restored.AliveCount);
        Assert.True(restored.Get(3, 4));
        Assert.True(restored.Get(50, 50));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!!")]      // FormatException path
    [InlineData("YWJj")]                 // valid base64, but only 3 bytes — wrong length
    public void Given_bad_base64_input_When_try_loading_it_Then_it_is_rejected_and_the_board_is_left_untouched(string? input)
    {
        var board = new SeedBoard();
        board.Set(7, 7, true);

        var ok = board.TryLoadBase64(input);

        Assert.False(ok);
        Assert.Equal(1, board.AliveCount);
        Assert.True(board.Get(7, 7));
    }
}
