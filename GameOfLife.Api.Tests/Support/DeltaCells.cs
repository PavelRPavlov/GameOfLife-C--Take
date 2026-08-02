using System.Globalization;
using GameOfLife.Core;
using GameOfLife.Api.Contracts;
using GameOfLife.Api.Game;

namespace GameOfLife.Api.Tests.Support;

/// <summary>
/// Test-side re-pairing of the columnar wire shapes back into domain <see cref="Cell"/>s: the delta's
/// parallel <c>ulong</c> axis arrays and the REST snapshot's decimal-string <see cref="CellDto"/>s.
/// Lets a test assert births/deaths and reconstructed live sets as plain <see cref="Cell"/> sets.
/// </summary>
public static class DeltaCells
{
    public static IReadOnlyList<Cell> BirthCells(this DeltaDto delta) => Zip(delta.BirthsX, delta.BirthsY);

    public static IReadOnlyList<Cell> DeathCells(this DeltaDto delta) => Zip(delta.DeathsX, delta.DeathsY);

    public static Cell ToCell(this CellDto dto) =>
        new(ulong.Parse(dto.X, NumberStyles.None, CultureInfo.InvariantCulture),
            ulong.Parse(dto.Y, NumberStyles.None, CultureInfo.InvariantCulture));

    private static IReadOnlyList<Cell> Zip(ulong[] xs, ulong[] ys)
    {
        var cells = new Cell[xs.Length];
        for (var i = 0; i < xs.Length; i++)
            cells[i] = new Cell(xs[i], ys[i]);
        return cells;
    }
}
