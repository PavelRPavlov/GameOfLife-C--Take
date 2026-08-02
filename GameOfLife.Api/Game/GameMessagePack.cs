using MessagePack;
using MessagePack.Resolvers;

namespace GameOfLife.Api.Game;

/// <summary>
/// The one MessagePack serializer configuration the game hub uses, pinned so the server, the
/// WebClient, and the test harness all agree on the wire format. The WebClient (which cannot
/// reference this assembly) mirrors these exact settings on its own side.
///
/// <list type="bullet">
/// <item><b>Contractless resolver</b> — plain records (<see cref="DeltaDto"/>'s columnar
/// <see cref="ulong"/> arrays) serialize by member name with no <c>[MessagePackObject]</c>/<c>[Key]</c>
/// attributes.</item>
/// <item><b>Enum by value</b> — the standard resolver carries <c>GameStatus</c> as its underlying
/// integer (member order is stable), so no JSON string-enum converter is involved.</item>
/// <item><b>UntrustedData security</b> — matches SignalR's own default MessagePack posture.</item>
/// <item><b>LZ4BlockArray compression</b> — the columnar <see cref="ulong"/> arrays put cells that
/// are near each other on the torus into contiguous, shared-high-order-byte runs, which LZ4 packs
/// down further on large deltas. Compression is part of the options and <em>must</em> match on both
/// ends, so it lives here; tiny deltas fall under MessagePack's internal LZ4 threshold and pass
/// through uncompressed with negligible overhead.</item>
/// </list>
/// </summary>
internal static class GameMessagePack
{
    public static readonly MessagePackSerializerOptions SerializerOptions =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithSecurity(MessagePackSecurity.UntrustedData)
            .WithCompression(MessagePackCompression.Lz4BlockArray);
}
