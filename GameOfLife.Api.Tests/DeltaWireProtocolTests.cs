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
