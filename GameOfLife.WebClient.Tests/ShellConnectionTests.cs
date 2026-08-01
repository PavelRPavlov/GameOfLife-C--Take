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
    public void Starts_in_connecting()
    {
        var (shell, _, _, _) = Build();
        Assert.Equal(ShellPhase.Connecting, shell.Phase);
    }

    [Fact]
    public async Task Initialize_reaches_ready_when_a_game_already_runs()
    {
        var (shell, api, stream, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Ok(Snap(GameStatus.Running, gen: 7)));

        var changes = new List<ShellPhase>();
        shell.Changed += () => changes.Add(shell.Phase);

        await shell.InitializeAsync();

        Assert.True(stream.Connected);
        Assert.Equal(ShellPhase.Ready, shell.Phase);
        // Connecting was (re)asserted then Ready — the shell announced both.
        Assert.Contains(ShellPhase.Ready, changes);
    }

    [Fact]
    public async Task NoGame_is_a_resolved_status_so_phase_is_ready()
    {
        var (shell, api, _, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(GameError.NoGame.Instance));

        await shell.InitializeAsync();

        Assert.Equal(ShellPhase.Ready, shell.Phase);
    }

    [Fact]
    public async Task Transport_failure_on_status_lands_disconnected()
    {
        var (shell, api, _, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.Transport("boom")));

        await shell.InitializeAsync();

        Assert.Equal(ShellPhase.Disconnected, shell.Phase);
    }

    [Fact]
    public async Task Reconnecting_push_shows_reconnecting_then_recovers_to_ready()
    {
        var (shell, api, stream, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(GameError.NoGame.Instance));
        await shell.InitializeAsync();
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
    public async Task Closed_push_lands_disconnected()
    {
        var (shell, api, stream, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(GameError.NoGame.Instance));
        await shell.InitializeAsync();

        stream.PushConnectionState(StreamConnectionState.Closed);

        Assert.Equal(ShellPhase.Disconnected, shell.Phase);
    }

    [Fact]
    public async Task Retry_from_disconnected_reaches_ready()
    {
        var (shell, api, _, _) = Build();
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(new GameError.Transport("down")));
        await shell.InitializeAsync();
        Assert.Equal(ShellPhase.Disconnected, shell.Phase);

        // Server is back — the next status fetch resolves.
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(GameError.NoGame.Instance));
        await shell.RetryAsync();

        Assert.Equal(ShellPhase.Ready, shell.Phase);
    }

    [Fact]
    public async Task Reconnect_recovery_catches_a_game_created_while_away()
    {
        var (shell, api, stream, _) = Build();
        // Connected pre-game.
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Err(GameError.NoGame.Instance));
        await shell.InitializeAsync();

        // While disconnected, a game was created; on recovery the re-fetch sees it.
        api.EnqueueSnapshot(Result<Snapshot, GameError>.Ok(Snap(GameStatus.Running, gen: 3)));
        stream.PushConnectionState(StreamConnectionState.Reconnecting);
        stream.PushConnectionState(StreamConnectionState.Reconnected);

        Assert.Equal(ShellPhase.Ready, shell.Phase);
    }
}
