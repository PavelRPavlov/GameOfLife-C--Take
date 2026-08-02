using GameOfLife.Api.Errors;
using GameOfLife.Api.Game;
using GameOfLife.Shared;

namespace GameOfLife.Api.Features.GetSnapshot;

/// <summary>
/// <c>GET /snapshot</c> — view-only, no secret; the full live set at the last broadcast-aligned
/// generation. Projects the kernel's <c>GameSnapshot</c> read-model onto the wire response; an empty
/// slot returns the shared <c>GAME_NOT_FOUND</c> error envelope.
/// </summary>
internal static class GetSnapshotEndpoint
{
    public static async Task<IResult> HandleAsync(GameHost host)
    {
        var snapshot = await host.GetSnapshotAsync();
        return snapshot is null
            ? ErrorResults.Envelope(StatusCodes.Status404NotFound, ErrorCodes.GameNotFound, ErrorMessages.GameNotFound)
            : Results.Ok(new SnapshotResponse(snapshot.Gen, snapshot.Status, snapshot.TickRate, snapshot.Cells));
    }
}
