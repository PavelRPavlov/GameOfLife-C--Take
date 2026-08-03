using System.Net;
using System.Text;
using System.Text.Json;
using GameOfLife.Core;
using GameOfLife.Shared;
using GameOfLife.WebClient.Communication;

namespace GameOfLife.WebClient.Tests;

/// <summary>
/// Exercises <see cref="HttpGameApi"/> against a canned <see cref="HttpMessageHandler"/>: the error
/// envelope <c>code</c> → <see cref="GameError"/> mapping (server message shown verbatim), wire parsing
/// (decimal-string coordinates, string enums), the transparent <c>X-Admin-Secret</c> header, and the
/// client-side validation that short-circuits before any round-trip.
/// </summary>
public class HttpGameApiTests
{
    // A valid seed: 1250 zero-bytes, base64-encoded (100×100 all-dead grid — allowed).
    private static readonly string ValidSeed = Convert.ToBase64String(new byte[1250]);

    private static CreateGameRequest ValidCreate() =>
        new(ValidSeed, new Cell(10, 20), "B3/S23", 100.0, AutoStart: false);

    /// <summary>The one client-owned string, shown when no usable error envelope arrives.</summary>
    private const string TransportFallback = "Couldn't reach the server. Please try again.";

    /// <summary>Serializes an error envelope body exactly as the backend would (camelCase JSON).</summary>
    private static string Envelope(string code, string message, params FieldError[] errors) =>
        JsonSerializer.Serialize(
            new ErrorEnvelope(code, message, errors),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static (HttpGameApi api, StubHandler handler, FakeAdminSecretStore store) NewApi()
    {
        var handler = new StubHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://backend.test/") };
        var store = new FakeAdminSecretStore();
        return (new HttpGameApi(http, store), handler, store);
    }

    [Fact]
    public async Task Given_a_valid_create_request_When_the_backend_succeeds_Then_the_CreatedGame_is_parsed_and_the_origin_is_sent_as_decimal_strings()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.Created, """
            {"adminSecret":"the-secret","status":"Created","generation":0,"tickRate":5,"rule":"B3/S23",
             "hubUrl":"/hubs/game","snapshotUrl":"/snapshot"}
            """);

        var result = await api.CreateGame(ValidCreate());

        Assert.True(result.IsSuccess);
        Assert.Equal("the-secret", result.Value.Secret);
        Assert.Equal(GameStatus.Created, result.Value.Status);
        Assert.Equal(0, result.Value.Generation);
        Assert.Equal(5.0, result.Value.TickRate);
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
    public async Task Given_a_2xx_with_a_null_body_When_creating_a_game_Then_it_folds_into_Transport_instead_of_throwing()
    {
        var (api, handler, _) = NewApi();
        // A success status whose JSON body deserializes to null is a broken contract, not a domain
        // outcome: it must fold into Transport (with the client fallback), never throw an NRE.
        handler.Respond(HttpStatusCode.Created, "null");

        var result = await api.CreateGame(ValidCreate());

        var transport = Assert.IsType<GameError.Transport>(result.Error);
        Assert.Equal(TransportFallback, transport.Message);
    }

    [Fact]
    public async Task Given_a_2xx_with_a_null_body_When_a_control_call_succeeds_Then_it_folds_into_Transport_instead_of_throwing()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.OK, "null");

        var result = await api.Start();

        var transport = Assert.IsType<GameError.Transport>(result.Error);
        Assert.Equal(TransportFallback, transport.Message);
    }

    [Fact]
    public async Task Given_a_2xx_with_a_null_body_When_fetching_a_snapshot_Then_it_folds_into_Transport_instead_of_throwing()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.OK, "null");

        var result = await api.GetSnapshot();

        var transport = Assert.IsType<GameError.Transport>(result.Error);
        Assert.Equal(TransportFallback, transport.Message);
    }

    [Fact]
    public async Task Given_a_GAME_ALREADY_EXISTS_envelope_When_creating_a_game_Then_it_maps_to_AlreadyExists_with_the_server_message()
    {
        var (api, handler, _) = NewApi();
        const string message = "A game already exists. Only one game can run at a time.";
        handler.Respond(HttpStatusCode.Conflict, Envelope(ErrorCodes.GameAlreadyExists, message));

        var result = await api.CreateGame(ValidCreate());

        var error = Assert.IsType<GameError.AlreadyExists>(result.Error);
        Assert.Equal(message, error.Message);
    }

    [Fact]
    public async Task Given_a_VALIDATION_FAILED_envelope_When_creating_a_game_Then_it_maps_to_ValidationRejected_with_field_errors()
    {
        var (api, handler, _) = NewApi();
        const string message = "Some of the values you provided aren't valid.";
        handler.Respond(HttpStatusCode.BadRequest, Envelope(
            ErrorCodes.ValidationFailed, message,
            new FieldError("tickRate", "The tick rate must be between 60 and 250 generations per second.")));

        var result = await api.CreateGame(ValidCreate());

        var rejected = Assert.IsType<GameError.ValidationRejected>(result.Error);
        Assert.Equal(message, rejected.Message);
        Assert.Contains(rejected.Errors, e => e.Field == "tickRate");
    }

    [Theory]
    [InlineData("not-base64!!", "B3/S23", 5.0)]      // bad seed
    [InlineData("", "B3/S23", 5.0)]                    // empty seed — the IsValidSeed null/empty guard
    [InlineData(null, "B0/S23", 5.0)]                 // B0 rejected
    [InlineData(null, "B3/S23", 0.0)]                 // tick-rate below range
    [InlineData(null, "B3/S23", 250.1)]               // tick-rate above range
    [InlineData(null, "B3/S23", double.NaN)]          // NaN tick-rate rejected
    [InlineData(null, "B33/S23", 5.0)]                // repeated digit in a group
    public async Task Given_an_invalid_create_request_When_creating_a_game_Then_it_short_circuits_without_calling_the_backend(
        string? seed, string rule, double tickRate)
    {
        var (api, handler, _) = NewApi();
        var request = new CreateGameRequest(seed ?? ValidSeed, new Cell(0, 0), rule, tickRate, AutoStart: true);

        var result = await api.CreateGame(request);

        Assert.IsType<GameError.ValidationRejected>(result.Error);
        Assert.Equal(0, handler.CallCount); // never hit the wire
    }

    [Theory]
    [InlineData("resume", "/resume")]
    [InlineData("step", "/step")]
    public async Task Given_a_control_verb_When_it_is_invoked_Then_it_hits_its_own_route(string _, string expectedPath)
    {
        var (api, handler, _2) = NewApi();
        handler.Respond(HttpStatusCode.OK, """{"status":"Running","generation":1}""");

        // Cover the resume/step delegates specifically (start/stop/pause are covered elsewhere).
        var result = expectedPath == "/resume" ? await api.Resume() : await api.Step();

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedPath, handler.LastPath);
    }

    [Fact]
    public async Task Given_a_cancelled_token_When_fetching_a_snapshot_Then_the_cancellation_propagates_rather_than_a_transport_error()
    {
        var (api, _, _2) = NewApi();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Cancellation via the caller's token must surface as OperationCanceledException, never be
        // swallowed into a GameError.Transport result.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetSnapshot(cts.Token));
    }

    [Fact]
    public async Task Given_a_stored_admin_secret_When_a_control_call_succeeds_Then_the_outcome_is_parsed_and_the_admin_secret_header_is_attached()
    {
        var (api, handler, store) = NewApi();
        await store.Set("secret-123");
        handler.Respond(HttpStatusCode.OK, """{"status":"Running","generation":42}""");

        var result = await api.Start();

        Assert.True(result.IsSuccess);
        Assert.Equal(GameStatus.Running, result.Value.Status);
        Assert.Equal(42, result.Value.Generation);
        Assert.Equal("/start", handler.LastPath);
        Assert.Equal("secret-123", handler.LastAdminSecret);
    }

    [Fact]
    public async Task Given_no_stored_admin_secret_When_a_control_call_is_made_Then_no_admin_header_is_sent()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.Forbidden, Envelope(ErrorCodes.InvalidAdminSecret, "Your admin access isn't valid."));

        var result = await api.Pause();

        Assert.IsType<GameError.Forbidden>(result.Error);
        Assert.Null(handler.LastAdminSecret);
    }

    [Theory]
    [InlineData(ErrorCodes.GameNotFound, typeof(GameError.NoGame))]
    [InlineData(ErrorCodes.InvalidAdminSecret, typeof(GameError.Forbidden))]
    [InlineData(ErrorCodes.InvalidStateForVerb, typeof(GameError.InvalidState))]
    [InlineData(ErrorCodes.GameAlreadyExists, typeof(GameError.AlreadyExists))]
    [InlineData(ErrorCodes.ValidationFailed, typeof(GameError.ValidationRejected))]
    [InlineData(ErrorCodes.InternalError, typeof(GameError.Transport))]
    [InlineData(ErrorCodes.MalformedRequestBody, typeof(GameError.Transport))]
    public async Task Given_an_error_envelope_code_When_a_control_call_fails_Then_it_maps_to_the_matching_error_and_surfaces_the_server_message(string code, Type expectedError)
    {
        var (api, handler, _) = NewApi();
        // The client branches on the envelope code, not the HTTP status — a fixed non-2xx status here.
        handler.Respond(HttpStatusCode.BadRequest, Envelope(code, "the server's message"));

        var result = await api.Stop();

        Assert.IsType(expectedError, result.Error);
        Assert.Equal("the server's message", result.Error.Message);
    }

    [Fact]
    public async Task Given_an_unknown_error_code_When_a_control_call_fails_Then_it_falls_back_to_Transport_but_shows_the_server_message()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.BadRequest, Envelope("SOME_FUTURE_CODE", "a message from a newer server"));

        var result = await api.Stop();

        var transport = Assert.IsType<GameError.Transport>(result.Error);
        Assert.Equal("a message from a newer server", transport.Message);
    }

    [Theory]
    [InlineData("")]                 // no body at all
    [InlineData("not json")]         // unparseable body
    [InlineData("\"a bare string\"")] // valid JSON but not the envelope
    public async Task Given_an_absent_or_unparseable_body_When_a_control_call_fails_Then_it_falls_back_to_the_client_transport_string(string body)
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.BadRequest, body);

        var result = await api.Stop();

        var transport = Assert.IsType<GameError.Transport>(result.Error);
        Assert.Equal(TransportFallback, transport.Message);
    }

    [Fact]
    public async Task Given_an_envelope_with_an_empty_message_When_a_control_call_fails_Then_it_falls_back_to_the_client_transport_string()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.NotFound, Envelope(ErrorCodes.GameNotFound, ""));

        var result = await api.Stop();

        var transport = Assert.IsType<GameError.Transport>(result.Error);
        Assert.Equal(TransportFallback, transport.Message);
    }

    [Fact]
    public async Task Given_a_successful_snapshot_response_When_fetching_a_snapshot_Then_the_decimal_string_cells_are_parsed()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.OK, """
            {"gen":7,"status":"Paused","tickRate":2.5,
             "cells":[{"x":"18446744073709551615","y":"0"},{"x":"1","y":"2"}]}
            """);

        var result = await api.GetSnapshot();

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value.Gen);
        Assert.Equal(GameStatus.Paused, result.Value.Status);
        Assert.Equal(2.5, result.Value.TickRate);
        Assert.Contains(new Cell(ulong.MaxValue, 0), result.Value.Cells);
        Assert.Contains(new Cell(1, 2), result.Value.Cells);
    }

    [Fact]
    public async Task Given_a_GAME_NOT_FOUND_envelope_When_fetching_a_snapshot_Then_it_maps_to_NoGame_with_the_server_message()
    {
        var (api, handler, _) = NewApi();
        handler.Respond(HttpStatusCode.NotFound, Envelope(ErrorCodes.GameNotFound, "There's no game right now."));

        var result = await api.GetSnapshot();

        var error = Assert.IsType<GameError.NoGame>(result.Error);
        Assert.Equal("There's no game right now.", error.Message);
    }

    [Fact]
    public async Task Given_a_network_failure_When_fetching_a_snapshot_Then_it_maps_to_Transport_with_the_client_fallback_string()
    {
        var (api, handler, _) = NewApi();
        handler.Throw(new HttpRequestException("connection refused"));

        var result = await api.GetSnapshot();

        var transport = Assert.IsType<GameError.Transport>(result.Error);
        // A genuine network failure has no body, so the one client-owned string is shown.
        Assert.Equal(TransportFallback, transport.Message);
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
