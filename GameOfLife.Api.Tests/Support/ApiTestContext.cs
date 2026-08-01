using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOfLife.Core;
using GameOfLife.Api.Features.CreateGame;
using GameOfLife.Api.Game;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace GameOfLife.Api.Tests.Support;

/// <summary>
/// A fresh in-memory API host for one test: real HTTP over <see cref="HttpClient"/> and real
/// SignalR observer connections over the test server, with no internal mocks. Each context starts
/// with an Empty <c>GameHost</c> slot.
/// </summary>
public sealed class ApiTestContext : IAsyncDisposable
{
    public static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly WebApplicationFactory<Program> _factory;
    private readonly List<HubConnection> _connections = [];

    /// <summary>Parameterless entry point used both directly and by the Reqnroll BoDi container.</summary>
    public ApiTestContext() : this(null, false) { }

    /// <summary>
    /// Builds a context pinned to a specific environment and, optionally, with the test-only throwing
    /// route wired in — used by exception-handling tests. A static factory (not a public constructor)
    /// so the BoDi container keeps seeing only the parameterless constructor it can resolve.
    /// </summary>
    public static ApiTestContext Create(string? environment = null, bool withThrowingEndpoint = false) =>
        new(environment, withThrowingEndpoint);

    private ApiTestContext(string? environment, bool withThrowingEndpoint)
    {
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // The global exception handler is gated to non-Development, so exception-path tests pin the
            // environment explicitly (e.g. "Production") rather than depending on the host default.
            if (environment is not null)
                builder.UseEnvironment(environment);

            // A test-only route that throws, injected so an unhandled exception can be driven through
            // the real pipeline. Never added to the shipped API.
            if (withThrowingEndpoint)
                builder.ConfigureServices(services =>
                    services.AddSingleton<IStartupFilter, ThrowingEndpoint.StartupFilter>());
        });
        Client = _factory.CreateClient();
    }

    public HttpClient Client { get; }

    /// <summary>Creates a game via <c>POST /game</c>, asserting success, and returns the response.</summary>
    public async Task<CreateGameResponse> CreateGameAsync(string? body = null)
    {
        var response = await Client.PostAsync("/game", Requests.Json(body ?? Requests.ValidCreate()));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateGameResponse>(Json))!;
    }

    /// <summary>POSTs a control verb (e.g. "start"), optionally with an X-Admin-Secret header.</summary>
    public Task<HttpResponseMessage> ControlAsync(string verb, string? secret)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/" + verb);
        if (secret is not null)
            request.Headers.Add("X-Admin-Secret", secret);
        return Client.SendAsync(request);
    }

    /// <summary>Fetches <c>GET /snapshot</c>.</summary>
    public Task<HttpResponseMessage> GetSnapshotAsync() => Client.GetAsync("/snapshot");

    /// <summary>Opens an observer SignalR connection over the in-memory server (long polling transport).</summary>
    public async Task<ObserverClient> ConnectObserverAsync()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, GameHost.HubUrl.TrimStart('/')), options =>
            {
                options.Transports = HttpTransportType.LongPolling;
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        var observer = new ObserverClient(connection);
        _connections.Add(connection);
        await connection.StartAsync();
        return observer;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var connection in _connections)
            await connection.DisposeAsync();
        Client.Dispose();
        await _factory.DisposeAsync();
    }
}

/// <summary>Buffers the two server→client pushes so a test can assert what an observer saw.</summary>
public sealed class ObserverClient
{
    private readonly HubConnection _connection;
    private readonly List<DeltaDto> _deltas = [];
    private readonly List<GameStatus> _statuses = [];
    private readonly Lock _sync = new();

    public ObserverClient(HubConnection connection)
    {
        _connection = connection;
        connection.On<DeltaDto>(nameof(IGameClient.ReceiveDelta), delta =>
        {
            lock (_sync) _deltas.Add(delta);
        });
        connection.On<GameStatus>(nameof(IGameClient.ReceiveStatus), status =>
        {
            lock (_sync) _statuses.Add(status);
        });
    }

    public IReadOnlyList<DeltaDto> Deltas
    {
        get { lock (_sync) return _deltas.ToList(); }
    }

    public IReadOnlyList<GameStatus> Statuses
    {
        get { lock (_sync) return _statuses.ToList(); }
    }

    /// <summary>Waits until <paramref name="predicate"/> holds over the buffered pushes, or times out.</summary>
    public async Task<bool> WaitForAsync(Func<ObserverClient, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (DateTime.UtcNow < deadline)
        {
            if (predicate(this)) return true;
            await Task.Delay(25);
        }
        return predicate(this);
    }
}
