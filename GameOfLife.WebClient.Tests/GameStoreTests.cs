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
    public async Task Given_deltas_race_an_in_flight_attach_snapshot_When_the_snapshot_reconciles_Then_stale_deltas_are_discarded_and_the_chaining_delta_is_applied()
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
        var attach = store.Attach();

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
    public async Task Given_an_attached_store_at_steady_state_When_duplicate_and_out_of_order_deltas_arrive_Then_they_are_discarded()
    {
        var (store, api, stream, _) = NewStore();
        api.EnqueueSnapshot(Snap(5, GameStatus.Running, C(1, 1)));
        await store.Attach();

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
    public async Task Given_an_attached_store_When_a_delta_arrives_with_a_generation_gap_Then_a_resync_snapshot_is_refetched()
    {
        var (store, api, stream, _) = NewStore();
        api.EnqueueSnapshot(Snap(5, GameStatus.Running, C(1, 1)));   // attach bootstrap
        api.EnqueueSnapshot(Snap(8, GameStatus.Running, C(4, 4)));   // resync bootstrap
        await store.Attach();

        // Gap: FromGen 7 cannot chain onto current gen 5 → resync rule trips.
        stream.PushDelta(new Delta(7, 8, [C(9, 9)], []));
        await Task.Yield();

        Assert.Equal(2, api.SnapshotCalls);
        Assert.Equal(8, store.Generation);
        Assert.Equal(new HashSet<Cell> { C(4, 4) }, store.LiveCells.ToHashSet());
    }

    [Fact]
    public async Task Given_a_connected_store_When_status_pushes_arrive_Then_status_updates_and_the_event_raises_only_on_change()
    {
        var (store, _, stream, _) = NewStore();
        await store.Connect();

        var seen = new List<GameStatus>();
        store.StatusChanged += s => seen.Add(s);

        stream.PushStatus(GameStatus.Created);
        stream.PushStatus(GameStatus.Running);
        stream.PushStatus(GameStatus.Running); // no-op repeat — suppressed

        Assert.Equal(GameStatus.Running, store.Status);
        Assert.Equal([GameStatus.Created, GameStatus.Running], seen);
    }

    [Fact]
    public void Given_a_store_subscribed_to_the_stream_When_connection_state_changes_are_pushed_Then_they_are_re_surfaced()
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
    public async Task Given_a_disposed_store_When_a_connection_state_change_is_pushed_Then_it_no_longer_reacts()
    {
        var (store, _, stream, _) = NewStore();
        var seen = 0;
        store.ConnectionStateChanged += _ => seen++;

        await store.DisposeAsync();
        stream.PushConnectionState(StreamConnectionState.Reconnecting);

        Assert.Equal(0, seen);
    }

    [Fact]
    public async Task Given_a_successful_create_When_the_game_is_created_Then_the_admin_secret_is_persisted()
    {
        var (store, api, _, secret) = NewStore();
        api.CreateResult = Result<CreatedGame, GameError>.Ok(
            new CreatedGame("secret-abc", GameStatus.Created, 0, 2.0, "B3/S23"));

        var request = new CreateGameRequest("AAAA", C(0, 0), "B3/S23", 2.0, AutoStart: false);
        var result = await store.CreateGame(request);

        Assert.True(result.IsSuccess);
        Assert.True(secret.HasSecret);
        Assert.Equal("secret-abc", secret.Current);
        Assert.Equal(GameStatus.Created, store.Status);
    }

    [Fact]
    public async Task Given_a_stored_admin_secret_When_a_control_call_is_forbidden_Then_the_stale_secret_is_cleared()
    {
        var (store, api, _, secret) = NewStore();
        await secret.Set("stale-secret");
        api.ControlResult = Result<ControlOutcome, GameError>.Err(new GameError.Forbidden("forbidden"));

        var result = await store.Start();

        Assert.True(result.IsError);
        Assert.False(secret.HasSecret);
    }

    [Fact]
    public async Task Given_a_snapshot_carrying_cells_When_refreshing_status_Then_status_and_generation_are_seeded_without_populating_live_cells()
    {
        var (store, api, _, _) = NewStore();
        // The snapshot carries cells, but the status-seed path must discard them.
        api.EnqueueSnapshot(Snap(7, GameStatus.Running, C(1, 1), C(2, 2)));

        var statusEvents = new List<GameStatus>();
        var snapshotEvents = 0;
        store.StatusChanged += statusEvents.Add;
        store.SnapshotApplied += _ => snapshotEvents++;

        var result = await store.RefreshStatus();

        Assert.True(result.IsSuccess);
        Assert.Equal(GameStatus.Running, result.Value);
        Assert.Equal(GameStatus.Running, store.Status);
        Assert.Equal(7, store.Generation);
        Assert.Empty(store.LiveCells);              // cells discarded — not an attach
        Assert.Equal([GameStatus.Running], statusEvents);
        Assert.Equal(0, snapshotEvents);            // no full-repaint signal — it is not a snapshot adoption
    }

    [Fact]
    public async Task Given_a_404_snapshot_When_refreshing_status_Then_NoGame_is_seeded()
    {
        var (store, api, _, _) = NewStore();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.NoGame("no game")));

        var result = await store.RefreshStatus();

        Assert.True(result.IsError);
        Assert.IsType<GameError.NoGame>(result.Error);
        Assert.Equal(GameStatus.NoGame, store.Status);
    }

    [Fact]
    public async Task Given_a_transport_failure_When_refreshing_status_Then_the_error_is_distinguishable_from_NoGame()
    {
        var (store, api, _, _) = NewStore();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.Transport("offline")));

        var result = await store.RefreshStatus();

        Assert.True(result.IsError);
        Assert.IsType<GameError.Transport>(result.Error); // caller can tell "can't reach server" from NoGame
    }

    [Fact]
    public async Task Given_a_refreshed_status_without_attach_When_a_delta_races_the_seed_Then_it_is_dropped_because_the_buffer_is_not_armed()
    {
        var (store, api, stream, _) = NewStore();
        api.EnqueueSnapshot(Snap(7, GameStatus.Running, C(1, 1)));
        await store.RefreshStatus();

        var applied = 0;
        store.DeltaApplied += _ => applied++;

        // Not observing (no attach) — a delta racing the status seed must be dropped, not buffered/applied.
        stream.PushDelta(new Delta(7, 8, [C(5, 5)], []));

        Assert.Equal(0, applied);
        Assert.Empty(store.LiveCells);
        Assert.Equal(7, store.Generation); // unchanged by the dropped delta
    }

    [Fact]
    public async Task Given_a_store_When_each_control_verb_succeeds_Then_it_delegates_and_applies_status_and_generation()
    {
        var (store, api, _, _) = NewStore();

        // Each verb funnels through the same success path (SetStatus + SetGeneration); walk all five so
        // start/stop/pause/resume/step are each exercised, not just start.
        api.ControlResult = Result<ControlOutcome, GameError>.Ok(new ControlOutcome(GameStatus.Running, 3));
        Assert.True((await store.Start()).IsSuccess);
        Assert.Equal(GameStatus.Running, store.Status);
        Assert.Equal(3, store.Generation);

        api.ControlResult = Result<ControlOutcome, GameError>.Ok(new ControlOutcome(GameStatus.Paused, 3));
        Assert.True((await store.Pause()).IsSuccess);
        Assert.Equal(GameStatus.Paused, store.Status);

        api.ControlResult = Result<ControlOutcome, GameError>.Ok(new ControlOutcome(GameStatus.Running, 3));
        Assert.True((await store.Resume()).IsSuccess);
        Assert.Equal(GameStatus.Running, store.Status);

        api.ControlResult = Result<ControlOutcome, GameError>.Ok(new ControlOutcome(GameStatus.Paused, 4));
        Assert.True((await store.Step()).IsSuccess);
        Assert.Equal(4, store.Generation); // step advances the generation

        api.ControlResult = Result<ControlOutcome, GameError>.Ok(new ControlOutcome(GameStatus.NoGame, 4));
        Assert.True((await store.Stop()).IsSuccess);
        Assert.Equal(GameStatus.NoGame, store.Status);
    }

    [Fact]
    public async Task Given_a_store_When_the_secret_store_gains_a_secret_Then_HasAdminSecret_reflects_it()
    {
        var (store, _, _, secret) = NewStore();

        Assert.False(store.HasAdminSecret);
        await secret.Set("a-secret");
        Assert.True(store.HasAdminSecret);
    }

    [Fact]
    public async Task Given_a_404_bootstrap_When_attach_fails_Then_NoGame_is_seeded_and_the_store_stays_unobserving()
    {
        var (store, api, stream, _) = NewStore();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.NoGame("no game")));

        var snapshotEvents = 0;
        store.SnapshotApplied += _ => snapshotEvents++;

        var result = await store.Attach();

        Assert.True(result.IsError);
        Assert.IsType<GameError.NoGame>(result.Error);
        Assert.Equal(GameStatus.NoGame, store.Status);
        Assert.Empty(store.LiveCells);
        Assert.Equal(0, snapshotEvents); // a failed bootstrap adopts no snapshot

        // The buffer was disarmed and no baseline adopted: a delta racing after the failure is dropped.
        var applied = 0;
        store.DeltaApplied += _ => applied++;
        stream.PushDelta(new Delta(0, 1, [C(1, 1)], []));
        Assert.Equal(0, applied);
    }

    [Fact]
    public async Task Given_an_attached_running_store_When_a_reattach_fails_on_transport_error_Then_NoGame_is_not_seeded()
    {
        var (store, api, _, _) = NewStore();
        // Start from a known non-default status so a spurious NoGame-seed would be observable.
        api.EnqueueSnapshot(Snap(5, GameStatus.Running, C(1, 1)));
        await store.Attach();
        Assert.Equal(GameStatus.Running, store.Status);

        // A resync/attach transport failure must not overwrite the last-known status with NoGame.
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.Transport("offline")));
        var result = await store.Attach();

        Assert.True(result.IsError);
        Assert.IsType<GameError.Transport>(result.Error);
        Assert.Equal(GameStatus.Running, store.Status); // unchanged — transport failure is not "no game"
    }
}
