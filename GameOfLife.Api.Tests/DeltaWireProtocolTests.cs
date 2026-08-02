using System.Text.Json;
using MessagePack;
using GameOfLife.Core;
using GameOfLife.Api.Contracts;
using GameOfLife.Api.Game;
using GameOfLife.Api.Tests.Support;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace GameOfLife.Api.Tests;

/// <summary>
/// The hot-path delta wire contract as it crosses a <em>real</em> MessagePack hub connection: native
/// <see cref="ulong"/> coordinates (above 2^53 and the torus boundaries) round-trip exactly, the
/// <see cref="GameStatus"/> enum round-trips by value, and the binary payload is materially smaller
/// than the decimal-string JSON shape it replaces.
/// </summary>
public class DeltaWireProtocolTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Given_a_delta_with_coordinates_above_2_53_and_at_the_torus_boundaries_When_pushed_over_MessagePack_Then_the_observer_decodes_the_exact_cell_set()
    {
        await using var ctx = new ApiTestContext();
        var observer = await ctx.ConnectObserver();

        // Exactly the values the REST decimal-string CellDto existed to protect: above 2^53, and the
        // 0 / 2^64-1 boundaries of the default uint64 torus. Native ulong must carry them losslessly.
        Cell[] births =
        [
            new(0UL, ulong.MaxValue),
            new(ulong.MaxValue, 0UL),
            new((1UL << 53) + 1, (1UL << 63) + 12345),
            new(9_007_199_254_740_993UL, 18_446_744_073_709_551_615UL),
        ];
        Cell[] deaths = [new(1UL, 2UL), new(ulong.MaxValue - 1, ulong.MaxValue)];

        var delta = new DeltaDto(
            41, 42,
            [.. births.Select(c => c.X)], [.. births.Select(c => c.Y)],
            [.. deaths.Select(c => c.X)], [.. deaths.Select(c => c.Y)]);

        var hub = ctx.Services.GetRequiredService<IHubContext<GameHub, IGameClient>>();
        await hub.Clients.All.ReceiveDelta(delta);

        Assert.True(await observer.WaitFor(o => o.Deltas.Any(d => d.ToGen == 42)),
            "expected the crafted delta to arrive over the MessagePack connection");

        var received = observer.Deltas.First(d => d.ToGen == 42);
        Assert.Equal(41, received.FromGen);
        Assert.Equal(births, received.BirthCells());
        Assert.Equal(deaths, received.DeathCells());
    }

    [Fact]
    public async Task Given_every_GameStatus_member_When_pushed_over_MessagePack_Then_each_round_trips_by_value()
    {
        await using var ctx = new ApiTestContext();
        var observer = await ctx.ConnectObserver();
        var hub = ctx.Services.GetRequiredService<IHubContext<GameHub, IGameClient>>();

        var all = Enum.GetValues<GameStatus>();
        foreach (var status in all)
            await hub.Clients.All.ReceiveStatus(status);

        Assert.True(await observer.WaitFor(o => o.Statuses.Count >= all.Length),
            $"expected all {all.Length} statuses; saw [{string.Join(", ", observer.Statuses)}]");
        Assert.Equal(all, observer.Statuses);
    }

    [Fact]
    public void Given_a_fixed_sample_delta_When_serialized_Then_MessagePack_is_materially_smaller_than_the_JSON_baseline()
    {
        // 500 births + 500 deaths, mixed-magnitude coordinates (the ticket's sample).
        var delta = SampleDelta(500);

        var messagePackBytes = MessagePackSerializer.Serialize(delta, GameMessagePack.SerializerOptions).Length;

        // Baseline: the shape this replaces — the old decimal-string CellDto delta over the Web-JSON
        // protocol (camelCase). CellDto is still the REST coordinate type, so this is a faithful before.
        var jsonBaseline = new
        {
            fromGen = delta.FromGen,
            toGen = delta.ToGen,
            births = delta.BirthCells().Select(c => c.ToDto()).ToList(),
            deaths = delta.DeathCells().Select(c => c.ToDto()).ToList(),
        };
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(jsonBaseline, ApiTestContext.Json).Length;

        output.WriteLine(
            $"Delta (500 births + 500 deaths): MessagePack = {messagePackBytes} bytes, " +
            $"JSON baseline = {jsonBytes} bytes, ratio = {(double)messagePackBytes / jsonBytes:P1}.");

        Assert.True(messagePackBytes < jsonBytes / 2,
            $"expected MessagePack ({messagePackBytes} B) to be under half the JSON baseline ({jsonBytes} B)");
    }

    [Fact]
    public void Given_the_large_sample_delta_When_LZ4_compression_is_enabled_Then_the_payload_is_materially_smaller_than_uncompressed_MessagePack()
    {
        // Same 500+500 sample as the JSON-baseline test — the ticket's fixed large delta.
        var delta = SampleDelta(500);

        var uncompressed = MessagePackSerializer.Serialize(delta, Uncompressed).Length;
        var compressed = MessagePackSerializer.Serialize(delta, GameMessagePack.SerializerOptions).Length;

        output.WriteLine(
            $"Delta (500 births + 500 deaths): uncompressed MessagePack = {uncompressed} bytes, " +
            $"LZ4BlockArray = {compressed} bytes, ratio = {(double)compressed / uncompressed:P1}.");

        // "Materially smaller", not merely a byte smaller: hold LZ4 to at least a 10% cut on the large
        // sample (the ticket expects 2–4× on big deltas; this mixed-magnitude sample runs ~1.5×).
        Assert.True(compressed < uncompressed * 0.9,
            $"expected LZ4 compression ({compressed} B) to be materially smaller than the uncompressed MessagePack baseline ({uncompressed} B)");
    }

    [Fact]
    public void Given_the_smallest_delta_When_LZ4_compression_is_enabled_Then_there_is_no_meaningful_size_regression()
    {
        // 1 birth / 0 deaths — well under MessagePack's internal LZ4 threshold, so the compressed
        // encoding must pass the raw bytes through rather than pay a compression penalty.
        var tiny = new DeltaDto(1000, 1001, [7UL], [123_456_789UL], [], []);

        var uncompressed = MessagePackSerializer.Serialize(tiny, Uncompressed).Length;
        var compressed = MessagePackSerializer.Serialize(tiny, GameMessagePack.SerializerOptions).Length;

        output.WriteLine(
            $"Smallest delta (1 birth / 0 deaths): uncompressed MessagePack = {uncompressed} bytes, " +
            $"LZ4BlockArray = {compressed} bytes.");

        Assert.True(compressed <= uncompressed,
            $"expected the tiny delta to pass through with no regression, but LZ4 ({compressed} B) exceeded uncompressed ({uncompressed} B)");
    }

    // The exact GameMessagePack options minus compression — the honest before for the compression
    // size comparisons. Derived from the real options (not hand-copied) so resolver and security stay
    // guaranteed-identical and only LZ4 differs, even if that shared posture ever changes.
    private static readonly MessagePackSerializerOptions Uncompressed =
        GameMessagePack.SerializerOptions.WithCompression(MessagePackCompression.None);

    // A deterministic delta spanning tiny to near-2^64 magnitudes, so the size measurement reflects a
    // realistic mix rather than a best case.
    private static DeltaDto SampleDelta(int n)
    {
        ulong[] bx = new ulong[n], by = new ulong[n], dx = new ulong[n], dy = new ulong[n];
        for (var i = 0; i < n; i++)
        {
            var k = (ulong)i;
            bx[i] = k * 2_654_435_761UL;             // scattered across the range
            by[i] = ulong.MaxValue - k * 40_503UL;   // near the top boundary
            dx[i] = (k + 1) << 40;                    // high-order bytes set
            dy[i] = k;                                // small magnitude
        }
        return new DeltaDto(1000, 1001, bx, by, dx, dy);
    }
}
