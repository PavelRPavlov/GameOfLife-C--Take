using GameOfLife.WebClient.Communication;
using MessagePack;
using MessagePack.Resolvers;

namespace GameOfLife.WebClient.Tests;

/// <summary>
/// Guards the client's binary hub contract. The <c>ReceiveDelta</c> push is deserialized by MessagePack's
/// contractless resolver, which builds its formatter via reflection-emit and only accepts a <b>public</b>
/// type. If <see cref="DeltaPushDto"/> slips back to <c>internal</c>, the first delta in the browser throws
/// <c>"Building dynamic formatter only allows public type"</c> — an error no other test exercises, since the
/// REST DTOs travel as JSON (System.Text.Json handles internal types) rather than MessagePack. This
/// round-trips a delta through the exact serializer options <c>SignalRGameStream</c> configures, so the
/// regression fails here on desktop instead of only at runtime in the browser.
/// </summary>
public class HubDeltaSerializationTests
{
    // Must mirror SignalRGameStream's AddMessagePackProtocol options exactly (and the backend's).
    private static readonly MessagePackSerializerOptions HubOptions =
        MessagePackSerializerOptions.Standard
            .WithResolver(ContractlessStandardResolver.Instance)
            .WithSecurity(MessagePackSecurity.UntrustedData)
            .WithCompression(MessagePackCompression.Lz4BlockArray);

    [Fact]
    public void Given_a_delta_push_When_round_tripped_through_the_hub_serializer_Then_it_deserializes_intact()
    {
        // ulong.MaxValue and 0 exercise the torus boundaries that native ulong (not decimal strings) exists to carry.
        var original = new DeltaPushDto(
            FromGen: 41, ToGen: 42,
            BirthsX: [1, 2, ulong.MaxValue], BirthsY: [3, 4, 0],
            DeathsX: [7], DeathsY: [8]);

        // Serialize alone would already throw the "public type" error if DeltaPushDto were internal; the
        // deserialize + field asserts additionally pin the columnar shape end to end.
        var bytes = MessagePackSerializer.Serialize(original, HubOptions);
        var round = MessagePackSerializer.Deserialize<DeltaPushDto>(bytes, HubOptions);

        Assert.Equal(original.FromGen, round.FromGen);
        Assert.Equal(original.ToGen, round.ToGen);
        Assert.Equal(original.BirthsX, round.BirthsX);
        Assert.Equal(original.BirthsY, round.BirthsY);
        Assert.Equal(original.DeathsX, round.DeathsX);
        Assert.Equal(original.DeathsY, round.DeathsY);
    }
}
