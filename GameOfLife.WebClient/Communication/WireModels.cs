using System.Text.Json;
using System.Text.Json.Serialization;
using GameOfLife.Core;

namespace GameOfLife.WebClient.Communication;

// The private wire contract for the REST backend. These DTOs never leave the API implementation:
// HttpGameApi parses them into the seam's domain types (Cell, Snapshot, Delta, ...) at the boundary,
// so no consumer of the seam ever sees a decimal-string coordinate or a JSON shape. Coordinates
// travel as decimal strings (128-bit torus, never one ulong) and enums as their member names.

/// <summary>Shared JSON options: Web defaults (camelCase, case-insensitive) plus string enums, matching the backend.</summary>
internal static class WireJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>A torus coordinate on the wire — each axis a decimal string so values above 2^53 survive JSON.</summary>
internal sealed record CellDto(string X, string Y);

/// <summary>The <c>POST /game</c> request body. Field names map (camelCase) to the backend's required set.</summary>
internal sealed record CreateGameRequestDto(
    string Seed,
    CellDto Origin,
    string Rule,
    double TickRate,
    bool AutoStart);

/// <summary>The <c>201</c> body of <c>POST /game</c>. Extra fields (hubUrl, snapshotUrl) are ignored here.</summary>
internal sealed record CreateGameResponseDto(
    string AdminSecret,
    GameStatus Status,
    long Generation,
    double TickRate,
    string Rule);

/// <summary>The uniform <c>200</c> body of the five control verbs.</summary>
internal sealed record ControlResponseDto(
    GameStatus Status,
    long Generation);

/// <summary>The <c>200</c> body of <c>GET /snapshot</c>.</summary>
internal sealed record SnapshotResponseDto(
    long Gen,
    GameStatus Status,
    double TickRate,
    IReadOnlyList<CellDto> Cells);
