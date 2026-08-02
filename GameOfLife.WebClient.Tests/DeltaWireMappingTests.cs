using GameOfLife.Core;
using GameOfLife.WebClient.Communication;

namespace GameOfLife.WebClient.Tests;

/// <summary>
/// The client-side boundary parse of the hot-path delta: the columnar MessagePack push
/// (<see cref="DeltaPushDto"/>, parallel <c>ulong</c> axis arrays) re-pairs into the seam's domain
/// <see cref="Delta"/> with <c>X[i]</c>/<c>Y[i]</c> preserved and no precision loss.
/// </summary>
public class DeltaWireMappingTests
{
    [Fact]
    public void Given_a_columnar_push_dto_When_mapped_Then_the_axes_re_pair_into_domain_cells_in_order()
    {
        var dto = new DeltaPushDto(
            7, 9,
            BirthsX: [0UL, 1UL << 53, ulong.MaxValue],
            BirthsY: [ulong.MaxValue, 42UL, 0UL],
            DeathsX: [100UL],
            DeathsY: [200UL]);

        var delta = dto.ToDomain();

        Assert.Equal(7, delta.FromGen);
        Assert.Equal(9, delta.ToGen);
        Assert.Equal(
            new[] { new Cell(0, ulong.MaxValue), new Cell(1UL << 53, 42), new Cell(ulong.MaxValue, 0) },
            delta.Births);
        Assert.Equal(new[] { new Cell(100, 200) }, delta.Deaths);
    }

    [Fact]
    public void Given_an_empty_columnar_push_dto_When_mapped_Then_both_cell_lists_are_empty()
    {
        var dto = new DeltaPushDto(3, 4, [], [], [], []);

        var delta = dto.ToDomain();

        Assert.Empty(delta.Births);
        Assert.Empty(delta.Deaths);
        Assert.Equal(3, delta.FromGen);
        Assert.Equal(4, delta.ToGen);
    }
}
