using GameOfLife.Core;
using GameOfLife.WebClient.Communication;

namespace GameOfLife.WebClient.Tests;

public sealed class GameStoreTests
{
    private static Cell C(ulong x, ulong y) => new(x, y);

    private static Result<Snapshot, GameError> Snap(long gen, GameStatus status, params Cell[] cells) =>
        Result<Snapshot, GameError>.Ok(new Snapshot(gen, status, 1.0, cells));

    private static (GameStore store, FakeGameApi api, FakeGameStream stream, FakeAdminSecretStore secret) NewStore()
    {
        var api = new FakeGameApi();
        var stream = new FakeGameStream();
        var secret = new FakeAdminSecretStore();
        return (new GameStore(api, stream, secret), api, stream, secret);
    }

    [Fact]
    public async Task Attach_reconciles_at_B_discarding_deltas_up_to_B_and_applying_the_rest()
    {
        var (store, api, stream, _) = NewStore();
        api.EnqueueSnapshot(Snap(5, GameStatus.Running, C(1, 1), C(2, 2)));
        var gate = new TaskCompletionSource();
        api.SnapshotGate = gate;

        var snapshotEvents = 0;
        var deltaEvents = 0;
        store.SnapshotApplied += _ => snapshotEvents++;
        store.DeltaApplied += _ => deltaEvents++;

        // Attach arms buffering, connects, then blocks on the gated snapshot fetch.
        var attach = store.AttachAsync();

        // These race the in-flight fetch and must be buffered.
        stream.PushDelta(new Delta(3, 4, [C(9, 9)], []));   // ToGen 4 ≤ 5 → discarded at reconcile
        stream.PushDelta(new Delta(5, 6, [C(3, 3)], []));    // FromGen 5 == B → applied after reconcile

        gate.SetResult();
        var result = await attach;

        Assert.True(result.IsSuccess);
        Assert.Equal(6, store.Generation);
        Assert.Equal(GameStatus.Running, store.Status);
        Assert.Equal(new HashSet<Cell> { C(1, 1), C(2, 2), C(3, 3) }, store.LiveCells.ToHashSet());
        Assert.Equal(1, snapshotEvents);
        Assert.Equal(1, deltaEvents); // only the chaining delta applied; the stale one was discarded
    }

    [Fact]
    public async Task Steady_state_discards_duplicate_and_out_of_order_deltas()
    {
        var (store, api, stream, _) = NewStore();
        api.EnqueueSnapshot(Snap(5, GameStatus.Running, C(1, 1)));
        await store.AttachAsync();

        var applied = 0;
        store.DeltaApplied += _ => applied++;

        stream.PushDelta(new Delta(5, 6, [C(2, 2)], []));   // applies → gen 6
        stream.PushDelta(new Delta(5, 6, [C(7, 7)], []));   // duplicate (ToGen 6 ≤ 6) → discarded
        stream.PushDelta(new Delta(4, 5, [C(8, 8)], []));   // out-of-order stale (ToGen 5 ≤ 6) → discarded

        Assert.Equal(6, store.Generation);
        Assert.Equal(1, applied);
        Assert.Equal(new HashSet<Cell> { C(1, 1), C(2, 2) }, store.LiveCells.ToHashSet());
    }

    [Fact]
    public async Task A_gap_delta_triggers_a_resync_snapshot_refetch()
    {
        var (store, api, stream, _) = NewStore();
        api.EnqueueSnapshot(Snap(5, GameStatus.Running, C(1, 1)));   // attach bootstrap
        api.EnqueueSnapshot(Snap(8, GameStatus.Running, C(4, 4)));   // resync bootstrap
        await store.AttachAsync();

        // Gap: FromGen 7 cannot chain onto current gen 5 → resync rule trips.
        stream.PushDelta(new Delta(7, 8, [C(9, 9)], []));
        await Task.Yield();

        Assert.Equal(2, api.SnapshotCalls);
        Assert.Equal(8, store.Generation);
        Assert.Equal(new HashSet<Cell> { C(4, 4) }, store.LiveCells.ToHashSet());
    }

    [Fact]
    public async Task Status_pushes_update_status_and_raise_only_on_change()
    {
        var (store, _, stream, _) = NewStore();
        await store.ConnectAsync();

        var seen = new List<GameStatus>();
        store.StatusChanged += s => seen.Add(s);

        stream.PushStatus(GameStatus.Created);
        stream.PushStatus(GameStatus.Running);
        stream.PushStatus(GameStatus.Running); // no-op repeat — suppressed

        Assert.Equal(GameStatus.Running, store.Status);
        Assert.Equal([GameStatus.Created, GameStatus.Running], seen);
    }

    [Fact]
    public void Connection_state_changes_are_re_surfaced_from_the_stream()
    {
        var (store, _, stream, _) = NewStore();

        var seen = new List<StreamConnectionState>();
        store.ConnectionStateChanged += s => seen.Add(s);

        stream.PushConnectionState(StreamConnectionState.Reconnecting);
        stream.PushConnectionState(StreamConnectionState.Reconnected);
        stream.PushConnectionState(StreamConnectionState.Closed);

        Assert.Equal(
            [StreamConnectionState.Reconnecting, StreamConnectionState.Reconnected, StreamConnectionState.Closed],
            seen);
    }

    [Fact]
    public async Task Disposing_the_store_unsubscribes_from_stream_connection_state()
    {
        var (store, _, stream, _) = NewStore();
        var seen = 0;
        store.ConnectionStateChanged += _ => seen++;

        await store.DisposeAsync();
        stream.PushConnectionState(StreamConnectionState.Reconnecting);

        Assert.Equal(0, seen);
    }

    [Fact]
    public async Task Create_success_persists_the_admin_secret()
    {
        var (store, api, _, secret) = NewStore();
        api.CreateResult = Result<CreatedGame, GameError>.Ok(
            new CreatedGame("secret-abc", GameStatus.Created, 0, 2.0, "B3/S23"));

        var request = new CreateGameRequest("AAAA", C(0, 0), "B3/S23", 2.0, AutoStart: false);
        var result = await store.CreateGameAsync(request);

        Assert.True(result.IsSuccess);
        Assert.True(secret.HasSecret);
        Assert.Equal("secret-abc", secret.Current);
        Assert.Equal(GameStatus.Created, store.Status);
    }

    [Fact]
    public async Task A_forbidden_control_response_clears_the_stale_secret()
    {
        var (store, api, _, secret) = NewStore();
        await secret.SetAsync("stale-secret");
        api.ControlResult = Result<ControlOutcome, GameError>.Err(GameError.Forbidden.Instance);

        var result = await store.StartAsync();

        Assert.True(result.IsError);
        Assert.False(secret.HasSecret);
    }

    [Fact]
    public async Task RefreshStatus_seeds_status_and_generation_without_populating_live_cells()
    {
        var (store, api, _, _) = NewStore();
        // The snapshot carries cells, but the status-seed path must discard them.
        api.EnqueueSnapshot(Snap(7, GameStatus.Running, C(1, 1), C(2, 2)));

        var statusEvents = new List<GameStatus>();
        var snapshotEvents = 0;
        store.StatusChanged += statusEvents.Add;
        store.SnapshotApplied += _ => snapshotEvents++;

        var result = await store.RefreshStatusAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(GameStatus.Running, result.Value);
        Assert.Equal(GameStatus.Running, store.Status);
        Assert.Equal(7, store.Generation);
        Assert.Empty(store.LiveCells);              // cells discarded — not an attach
        Assert.Equal([GameStatus.Running], statusEvents);
        Assert.Equal(0, snapshotEvents);            // no full-repaint signal — it is not a snapshot adoption
    }

    [Fact]
    public async Task RefreshStatus_on_404_seeds_NoGame()
    {
        var (store, api, _, _) = NewStore();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(GameError.NoGame.Instance));

        var result = await store.RefreshStatusAsync();

        Assert.True(result.IsError);
        Assert.IsType<GameError.NoGame>(result.Error);
        Assert.Equal(GameStatus.NoGame, store.Status);
    }

    [Fact]
    public async Task RefreshStatus_transport_failure_is_distinguishable_from_NoGame()
    {
        var (store, api, _, _) = NewStore();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.Transport("offline")));

        var result = await store.RefreshStatusAsync();

        Assert.True(result.IsError);
        Assert.IsType<GameError.Transport>(result.Error); // caller can tell "can't reach server" from NoGame
    }

    [Fact]
    public async Task RefreshStatus_does_not_arm_the_attach_buffer_so_deltas_stay_dropped()
    {
        var (store, api, stream, _) = NewStore();
        api.EnqueueSnapshot(Snap(7, GameStatus.Running, C(1, 1)));
        await store.RefreshStatusAsync();

        var applied = 0;
        store.DeltaApplied += _ => applied++;

        // Not observing (no attach) — a delta racing the status seed must be dropped, not buffered/applied.
        stream.PushDelta(new Delta(7, 8, [C(5, 5)], []));

        Assert.Equal(0, applied);
        Assert.Empty(store.LiveCells);
        Assert.Equal(7, store.Generation); // unchanged by the dropped delta
    }
}
