using GameOfLife.Api.Game;

namespace GameOfLife.Api.Features.GetSnapshot;

/// <summary>
/// <c>GET /snapshot</c> — view-only, no secret; the full live set at the last broadcast-aligned
/// generation. Projects the kernel's <c>GameSnapshot</c> read-model onto the wire response.
/// </summary>
internal static class GetSnapshotEndpoint
{
    public static async Task<IResult> HandleAsync(GameHost host)
    {
        var snapshot = await host.GetSnapshotAsync();
        return snapshot is null
            ? Results.NotFound()
            : Results.Ok(new SnapshotResponse(snapshot.Gen, snapshot.Status, snapshot.TickRate, snapshot.Cells));
    }
}
