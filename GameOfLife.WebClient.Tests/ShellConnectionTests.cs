using GameOfLife.Core;
using GameOfLife.WebClient.Communication;

namespace GameOfLife.WebClient.Tests;

/// <summary>
/// Drives the app-shell state machine (<see cref="ShellConnection"/>) over the fake seam — the same
/// off-host testability the rest of <c>Communication</c> buys. Verifies the Connecting… → Ready →
/// Reconnecting… → Disconnected/Retry transitions and that every (re)connect re-gates status from truth.
/// </summary>
public class ShellConnectionTests
{
    private static Snapshot Snap(GameStatus status, long gen = 0) =>
        new(gen, status, TickRate: 1.0, Cells: Array.Empty<Cell>());

    private static (ShellConnection shell, FakeGameApi api, FakeGameStream stream, FakeAdminSecretStore secret) Build()
    {
        var api = new FakeGameApi();
        var stream = new FakeGameStream();
        var secret = new FakeAdminSecretStore();
        var store = new GameStore(api, stream, secret);
        return (new ShellConnection(store), api, stream, secret);
    }

    [Fact]
    public void Given_a_new_shell_When_it_is_built_Then_it_starts_in_connecting()
    {
        var (shell, _, _, _) = Build();
        Assert.Equal(ShellPhase.Connecting, shell.Phase);
    }

    [Fact]
    public async Task Given_a_game_already_running_When_the_shell_initializes_Then_it_reaches_ready()
    {
        var (shell, api, stream, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Ok(Snap(GameStatus.Running, gen: 7)));

        var changes = new List<ShellPhase>();
        shell.Changed += () => changes.Add(shell.Phase);

        await shell.Initialize();

        Assert.True(stream.Connected);
        Assert.Equal(ShellPhase.Ready, shell.Phase);
        // Connecting was (re)asserted then Ready — the shell announced both.
        Assert.Contains(ShellPhase.Ready, changes);
    }

    [Fact]
    public async Task Given_a_NoGame_status_When_the_shell_initializes_Then_the_phase_is_ready()
    {
        var (shell, api, _, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.NoGame("no game")));

        await shell.Initialize();

        Assert.Equal(ShellPhase.Ready, shell.Phase);
    }

    [Fact]
    public async Task Given_a_transport_failure_on_status_When_the_shell_initializes_Then_it_lands_disconnected()
    {
        var (shell, api, _, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.Transport("boom")));

        await shell.Initialize();

        Assert.Equal(ShellPhase.Disconnected, shell.Phase);
    }

    [Fact]
    public async Task Given_a_ready_shell_When_the_transport_drops_and_recovers_Then_it_shows_reconnecting_then_returns_to_ready()
    {
        var (shell, api, stream, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.NoGame("no game")));
        await shell.Initialize();
        Assert.Equal(ShellPhase.Ready, shell.Phase);

        // The transport drops...
        stream.PushConnectionState(StreamConnectionState.Reconnecting);
        Assert.Equal(ShellPhase.Reconnecting, shell.Phase);

        // ...and comes back: the shell re-fetches status (fakes complete synchronously) and returns to Ready.
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Ok(Snap(GameStatus.Running)));
        stream.PushConnectionState(StreamConnectionState.Reconnected);
        Assert.Equal(ShellPhase.Ready, shell.Phase);
    }

    [Fact]
    public async Task Given_a_ready_shell_When_a_closed_connection_state_is_pushed_Then_it_lands_disconnected()
    {
        var (shell, api, stream, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.NoGame("no game")));
        await shell.Initialize();

        stream.PushConnectionState(StreamConnectionState.Closed);

        Assert.Equal(ShellPhase.Disconnected, shell.Phase);
    }

    [Fact]
    public async Task Given_a_disconnected_shell_When_retried_and_the_server_is_back_Then_it_reaches_ready()
    {
        var (shell, api, _, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.Transport("down")));
        await shell.Initialize();
        Assert.Equal(ShellPhase.Disconnected, shell.Phase);

        // Server is back — the next status fetch resolves.
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.NoGame("no game")));
        await shell.Retry();

        Assert.Equal(ShellPhase.Ready, shell.Phase);
    }

    [Fact]
    public async Task Given_a_stream_that_cannot_connect_When_the_shell_initializes_Then_it_lands_disconnected_without_fetching_status()
    {
        var (shell, api, stream, _) = Build();
        stream.ConnectException = new InvalidOperationException("transport unavailable");
        // A status result is enqueued but must never be consulted — the connect fails first.
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.NoGame("no game")));

        await shell.Initialize();

        Assert.Equal(ShellPhase.Disconnected, shell.Phase);
        Assert.Equal(0, api.SnapshotCalls); // never reached the status fetch
    }

    [Fact]
    public async Task Given_a_disposed_shell_When_a_connection_state_change_is_pushed_Then_it_stops_reacting()
    {
        var (shell, api, stream, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.NoGame("no game")));
        await shell.Initialize();
        Assert.Equal(ShellPhase.Ready, shell.Phase);

        shell.Dispose();

        // After disposal a dropped transport must not move the shell off Ready.
        stream.PushConnectionState(StreamConnectionState.Reconnecting);
        Assert.Equal(ShellPhase.Ready, shell.Phase);
    }

    [Fact]
    public async Task Given_a_game_created_while_disconnected_When_the_shell_reconnects_Then_the_recovery_re_fetch_catches_it()
    {
        var (shell, api, stream, _) = Build();
        // Connected pre-game.
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.NoGame("no game")));
        await shell.Initialize();

        // While disconnected, a game was created; on recovery the re-fetch sees it.
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Ok(Snap(GameStatus.Running, gen: 3)));
        stream.PushConnectionState(StreamConnectionState.Reconnecting);
        stream.PushConnectionState(StreamConnectionState.Reconnected);

        Assert.Equal(ShellPhase.Ready, shell.Phase);
    }
}
