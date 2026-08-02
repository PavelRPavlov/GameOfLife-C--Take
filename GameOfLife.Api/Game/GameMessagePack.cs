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
/// </list>
/// </summary>
internal static class GameMessagePack
{
    public static readonly MessagePackSerializerOptions SerializerOptions =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithSecurity(MessagePackSecurity.UntrustedData);
}
