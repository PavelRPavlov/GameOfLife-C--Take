using System.Net;
using System.Text;
using System.Text.Json;
using GameOfLife.Core;
using GameOfLife.WebClient.Communication;

namespace GameOfLife.WebClient.Tests;

/// <summary>
/// Exercises <see cref="HttpGameApi"/> against a canned <see cref="HttpMessageHandler"/>: the
/// status-code → <see cref="GameError"/> mapping, wire parsing (decimal-string coordinates,
/// string enums), the transparent <c>X-Admin-Secret</c> header, and the client-side validation
/// that short-circuits before any round-trip.
/// </summary>
public class HttpGameApiTests
{
    // A valid seed: 1250 zero-bytes, base64-encoded (100×100 all-dead grid — allowed).
    private static readonly string ValidSeed = Convert.ToBase64String(new byte[1250]);

    private static CreateGameRequest ValidCreate() =>
        new(ValidSeed, new Cell(10, 20), "B3/S23", 5.0, AutoStart: false);

    private static (HttpGameApi api, StubHandler handler, FakeAdminSecretStore store) NewApi()
    {
        var handler = new StubHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://backend.test/") };
        var store = new FakeAdminSecretStore();
        return (new HttpGameApi(http, store), handler, store);
    }

    [Fact]
    public async Task CreateGame_success_parses_CreatedGame_and_sends_decimal_string_origin()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.Created, """
            {"adminSecret":"the-secret","status":"Created","generation":0,"tickRate":5,"rule":"B3/S23",
             "hubUrl":"/hubs/game","snapshotUrl":"/snapshot"}
            """);

        var result = await api.CreateGameAsync(ValidCreate());

        Assert.True(result.IsSuccess);
        Assert.Equal("the-secret", result.Value.Secret);
        Assert.Equal(GameStatus.Created, result.Value.Status);
        Assert.Equal("B3/S23", result.Value.Rule);

        // Body carries the origin as decimal strings, and no extra properties.
        Assert.Equal("/game", handler.LastPath);
        using var body = JsonDocument.Parse(handler.LastBody!);
        var origin = body.RootElement.GetProperty("origin");
        Assert.Equal("10", origin.GetProperty("x").GetString());
        Assert.Equal("20", origin.GetProperty("y").GetString());
        Assert.Equal(ValidSeed, body.RootElement.GetProperty("seed").GetString());
        Assert.False(body.RootElement.GetProperty("autoStart").GetBoolean());
    }

    [Fact]
    public async Task CreateGame_409_maps_to_AlreadyExists()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.Conflict, "A game already exists.");

        var result = await api.CreateGameAsync(ValidCreate());

        Assert.True(result.IsError);
        Assert.IsType<GameError.AlreadyExists>(result.Error);
    }

    [Fact]
    public async Task CreateGame_400_maps_to_ValidationRejected_with_details()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.BadRequest, "seed is wrong");

        var result = await api.CreateGameAsync(ValidCreate());

        var rejected = Assert.IsType<GameError.ValidationRejected>(result.Error);
        Assert.Contains("seed is wrong", rejected.Details);
    }

    [Theory]
    [InlineData("not-base64!!", "B3/S23", 5.0)]      // bad seed
    [InlineData(null, "B0/S23", 5.0)]                 // B0 rejected
    [InlineData(null, "B3/S23", 0.0)]                 // tick-rate below range
    [InlineData(null, "B3/S23", 201.0)]               // tick-rate above range
    [InlineData(null, "B33/S23", 5.0)]                // repeated digit in a group
    public async Task CreateGame_invalid_request_short_circuits_without_calling_backend(
        string? seed, string rule, double tickRate)
    {
        var (api, handler, _) = NewApi();
        var request = new CreateGameRequest(seed ?? ValidSeed, new Cell(0, 0), rule, tickRate, AutoStart: true);

        var result = await api.CreateGameAsync(request);

        Assert.IsType<GameError.ValidationRejected>(result.Error);
        Assert.Equal(0, handler.CallCount); // never hit the wire
    }

    [Fact]
    public async Task Control_success_parses_outcome_and_attaches_admin_secret_header()
    {
        var (api, handler, store) = NewApi();
        await store.SetAsync("secret-123");
        handler.Respond(HttpStatusCode.OK, """{"status":"Running","generation":42}""");

        var result = await api.StartAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(GameStatus.Running, result.Value.Status);
        Assert.Equal(42, result.Value.Generation);
        Assert.Equal("/start", handler.LastPath);
        Assert.Equal("secret-123", handler.LastAdminSecret);
    }

    [Fact]
    public async Task Control_without_secret_sends_no_admin_header()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.Forbidden, "");

        var result = await api.PauseAsync();

        Assert.IsType<GameError.Forbidden>(result.Error);
        Assert.Null(handler.LastAdminSecret);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, typeof(GameError.NoGame))]
    [InlineData(HttpStatusCode.Forbidden, typeof(GameError.Forbidden))]
    [InlineData(HttpStatusCode.Conflict, typeof(GameError.InvalidState))]
    [InlineData(HttpStatusCode.InternalServerError, typeof(GameError.Transport))]
    public async Task Control_maps_status_codes(HttpStatusCode status, Type expectedError)
    {
        var (api, handler, _) = NewApi();
        handler.Respond(status, "");

        var result = await api.StopAsync();

        Assert.True(result.IsError);
        Assert.IsType(expectedError, result.Error);
    }

    [Fact]
    public async Task GetSnapshot_success_parses_decimal_string_cells()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.OK, """
            {"gen":7,"status":"Paused","tickRate":2.5,
             "cells":[{"x":"18446744073709551615","y":"0"},{"x":"1","y":"2"}]}
            """);

        var result = await api.GetSnapshotAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value.Gen);
        Assert.Equal(GameStatus.Paused, result.Value.Status);
        Assert.Equal(2.5, result.Value.TickRate);
        Assert.Contains(new Cell(ulong.MaxValue, 0), result.Value.Cells);
        Assert.Contains(new Cell(1, 2), result.Value.Cells);
    }

    [Fact]
    public async Task GetSnapshot_404_maps_to_NoGame()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.NotFound, "");

        var result = await api.GetSnapshotAsync();

        Assert.IsType<GameError.NoGame>(result.Error);
    }

    [Fact]
    public async Task Network_failure_maps_to_Transport()
    {
        var (api, handler, _) = NewApi();
        handler.Throw(new HttpRequestException("connection refused"));

        var result = await api.GetSnapshotAsync();

        var transport = Assert.IsType<GameError.Transport>(result.Error);
        Assert.Contains("connection refused", transport.Detail);
    }

    /// <summary>Records the last request and returns a canned response (or throws a canned exception).</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        private HttpStatusCode _status = HttpStatusCode.OK;
        private string _body = "";
        private Exception? _throw;

        public int CallCount { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastBody { get; private set; }
        public string? LastAdminSecret { get; private set; }

        public void Respond(HttpStatusCode status, string body)
        {
            _status = status;
            _body = body;
            _throw = null;
        }

        public void Throw(Exception ex) => _throw = ex;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastPath = request.RequestUri!.AbsolutePath;
            LastAdminSecret = request.Headers.TryGetValues("X-Admin-Secret", out var values)
                ? values.FirstOrDefault()
                : null;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            if (_throw is not null)
                throw _throw;

            return new HttpResponseMessage(_status)
            {
                Content = new StringContent(_body, Encoding.UTF8, "application/json"),
            };
        }
    }
}
