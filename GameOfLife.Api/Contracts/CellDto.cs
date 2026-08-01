using System.Globalization;
using GameOfLife.Core;

namespace GameOfLife.Api.Contracts;

/// <summary>
/// A torus coordinate: a pair of independent <see cref="ulong"/> axes each carried as a decimal
/// <em>string</em> so values above 2^53 survive JSON without precision loss (and so the OpenAPI
/// schema honestly says <c>type: string</c>). One consistent shape for origin, snapshot cells,
/// and delta births/deaths.
/// </summary>
public sealed record CellDto(string X, string Y);

/// <summary>Boundary conversions between the domain <see cref="Cell"/> and the wire <see cref="CellDto"/>.</summary>
public static class CellDtoExtensions
{
    public static CellDto ToDto(this Cell cell) =>
        new(cell.X.ToString(CultureInfo.InvariantCulture),
            cell.Y.ToString(CultureInfo.InvariantCulture));
}
