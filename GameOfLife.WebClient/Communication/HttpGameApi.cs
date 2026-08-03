namespace GameOfLife.WebClient.Communication;

/// <summary>
/// The real <see cref="IGameApi"/> — a typed <see cref="HttpClient"/> over the backend REST contract.
/// It confines the wire DTOs (decimal-string coordinates, string enums) to this boundary and hands the
/// seam only parsed domain types. Non-2xx responses that carry UX meaning become <see cref="GameError"/>
/// values (never exceptions): the shared <see cref="ErrorEnvelope"/> is deserialized from the body and
/// its machine-readable <c>code</c> selects the arm (<see cref="ErrorCodes.GameNotFound"/>→<see cref="GameError.NoGame"/>,
/// <see cref="ErrorCodes.InvalidAdminSecret"/>→<see cref="GameError.Forbidden"/>,
/// <see cref="ErrorCodes.InvalidStateForVerb"/>→<see cref="GameError.InvalidState"/>,
/// <see cref="ErrorCodes.GameAlreadyExists"/>→<see cref="GameError.AlreadyExists"/>,
/// <see cref="ErrorCodes.ValidationFailed"/>→<see cref="GameError.ValidationRejected"/>), carrying the server's
/// message verbatim. An unknown/absent code, a network failure, or an unparseable body becomes
/// <see cref="GameError.Transport"/>. The admin secret is attached transparently as <c>X-Admin-Secret</c> on every
/// control verb from <see cref="IAdminSecretStore"/>, so component code never handles it.
/// </summary>
public sealed class HttpGameApi : IGameApi
{
    private readonly HttpClient _http;
    private readonly IAdminSecretStore _secretStore;

    public HttpGameApi(HttpClient http, IAdminSecretStore secretStore)
    {
        _http = http;
        _secretStore = secretStore;
    }

    public async Task<Result<CreatedGame, GameError>> CreateGame(CreateGameRequest request, CancellationToken ct = default)
    {
        // Client-side guard: the seam validates seed/rule/tick-rate before the round-trip, so a backend
        // ValidationRejected is a programming error rather than an expected outcome (see GameError).
        if (Validate(request) is { } invalid)
            return Result<CreatedGame, GameError>.Err(invalid);

        var body = new CreateGameRequestDto(
            request.Seed,
            new CellDto(
                request.Origin.X.ToString(CultureInfo.InvariantCulture),
                request.Origin.Y.ToString(CultureInfo.InvariantCulture)),
            request.Rule,
            request.TickRate,
            request.AutoStart);

        return await SendAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "game")
            {
                Content = JsonContent.Create(body, options: WireJson.Options),
            },
            async (response, token) =>
            {
                var dto = await response.Content.ReadFromJsonAsync<CreateGameResponseDto>(WireJson.Options, token);
                // A 2xx with an empty/null JSON body is a broken contract, not a domain outcome → Transport.
                if (dto is null)
                    return Result<CreatedGame, GameError>.Err(new GameError.Transport(TransportFallbackMessage));
                return Result<CreatedGame, GameError>.Ok(
                    new CreatedGame(dto.AdminSecret, dto.Status, dto.Generation, dto.TickRate, dto.Rule));
            },
            ct);
    }

    public Task<Result<ControlOutcome, GameError>> Start(CancellationToken ct = default) => Control("start", ct);
    public Task<Result<ControlOutcome, GameError>> Stop(CancellationToken ct = default) => Control("stop", ct);
    public Task<Result<ControlOutcome, GameError>> Pause(CancellationToken ct = default) => Control("pause", ct);
    public Task<Result<ControlOutcome, GameError>> Resume(CancellationToken ct = default) => Control("resume", ct);
    public Task<Result<ControlOutcome, GameError>> Step(CancellationToken ct = default) => Control("step", ct);

    private Task<Result<ControlOutcome, GameError>> Control(string verb, CancellationToken ct) =>
        SendAsync(
            () =>
            {
                var message = new HttpRequestMessage(HttpMethod.Post, verb);
                // Attach the capability transparently; its absence yields a 403 the store then clears.
                if (_secretStore.Current is { } secret)
                    message.Headers.Add("X-Admin-Secret", secret);
                return message;
            },
            async (response, token) =>
            {
                var dto = await response.Content.ReadFromJsonAsync<ControlResponseDto>(WireJson.Options, token);
                // A 2xx with an empty/null JSON body is a broken contract, not a domain outcome → Transport.
                if (dto is null)
                    return Result<ControlOutcome, GameError>.Err(new GameError.Transport(TransportFallbackMessage));
                return Result<ControlOutcome, GameError>.Ok(new ControlOutcome(dto.Status, dto.Generation));
            },
            ct);

    public Task<Result<Snapshot, GameError>> GetSnapshot(CancellationToken ct = default) =>
        SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "snapshot"),
            async (response, token) =>
            {
                var dto = await response.Content.ReadFromJsonAsync<SnapshotResponseDto>(WireJson.Options, token);
                // A 2xx with an empty/null JSON body is a broken contract, not a domain outcome → Transport.
                if (dto is null)
                    return Result<Snapshot, GameError>.Err(new GameError.Transport(TransportFallbackMessage));
                var cells = dto.Cells.Select(c => c.ToDomain()).ToList();
                return Result<Snapshot, GameError>.Ok(new Snapshot(dto.Gen, dto.Status, dto.TickRate, cells));
            },
            ct);

    /// <summary>
    /// The client-owned fallback shown when no usable error envelope arrived: a genuine network failure
    /// (no body), an unparseable body, or an envelope with an empty message. The one string the client
    /// authors — every other message is the server's, shown verbatim.
    /// </summary>
    internal const string TransportFallbackMessage = "Couldn't reach the server. Please try again.";

    /// <summary>
    /// The shared request pipeline: build the message, send it, and fold the response into a
    /// <see cref="Result{T, GameError}"/>. Success (2xx) parses the body — <paramref name="parseSuccess"/>
    /// returns its own <see cref="Result{T, GameError}"/> so a null/empty body (a broken 2xx contract)
    /// folds into <see cref="GameError.Transport"/> rather than dereferencing null. A non-2xx deserializes
    /// the shared <see cref="ErrorEnvelope"/> and branches on its <c>code</c> into a <see cref="GameError"/>
    /// arm (carrying the server's message verbatim). An unknown code falls back to
    /// <see cref="GameError.Transport"/> still showing the server message; an absent/unparseable
    /// envelope, an empty message, a thrown <see cref="HttpRequestException"/>, or a non-cancellation
    /// timeout becomes <see cref="GameError.Transport"/> with <see cref="TransportFallbackMessage"/>.
    /// A cancellation via <paramref name="ct"/> propagates.
    /// </summary>
    private async Task<Result<T, GameError>> SendAsync<T>(
        Func<HttpRequestMessage> buildRequest,
        Func<HttpResponseMessage, CancellationToken, Task<Result<T, GameError>>> parseSuccess,
        CancellationToken ct)
    {
        try
        {
            using var request = buildRequest();
            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
                return await parseSuccess(response, ct);

            var envelope = await ReadEnvelope(response, ct);
            return Result<T, GameError>.Err(ToGameError(envelope));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or NotSupportedException)
        {
            return Result<T, GameError>.Err(new GameError.Transport(TransportFallbackMessage));
        }
    }

    /// <summary>Reads the error envelope from a non-2xx body, or null if the body isn't a usable envelope.</summary>
    private static async Task<ErrorEnvelope?> ReadEnvelope(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ErrorEnvelope>(WireJson.Options, ct);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // A body that isn't the envelope (empty, HTML, a bare string) → no usable code.
            return null;
        }
    }

    /// <summary>
    /// Maps an error envelope to a <see cref="GameError"/> arm by its <c>code</c>. An absent envelope, a
    /// missing code, or an empty message all fall back to <see cref="GameError.Transport"/> with the
    /// client string; an unknown code with a message falls back to <see cref="GameError.Transport"/>
    /// showing that server message verbatim.
    /// </summary>
    private static GameError ToGameError(ErrorEnvelope? envelope)
    {
        if (envelope is null || string.IsNullOrEmpty(envelope.Code) || string.IsNullOrEmpty(envelope.Message))
            return new GameError.Transport(TransportFallbackMessage);

        var message = envelope.Message;
        return envelope.Code switch
        {
            ErrorCodes.GameNotFound => new GameError.NoGame(message),
            ErrorCodes.InvalidAdminSecret => new GameError.Forbidden(message),
            ErrorCodes.InvalidStateForVerb => new GameError.InvalidState(message),
            ErrorCodes.GameAlreadyExists => new GameError.AlreadyExists(message),
            ErrorCodes.ValidationFailed => new GameError.ValidationRejected(message, envelope.Errors ?? []),
            // INTERNAL_ERROR, MALFORMED_REQUEST_BODY, and any unknown code → Transport, server message shown.
            _ => new GameError.Transport(message),
        };
    }

    // ---- Client-side validation (mirrors the backend's contract) ----

    /// <summary>
    /// Returns a <see cref="GameError.ValidationRejected"/> if the request is malformed, else null. A
    /// pre-send guard (not the surfaced contract): its messages mirror the backend's field copy so a
    /// short-circuit reads the same as a server rejection. Carries no per-field breakdown.
    /// </summary>
    private static GameError? Validate(CreateGameRequest request)
    {
        if (!IsValidSeed(request.Seed))
            return new GameError.ValidationRejected("The starting grid isn't in the expected format. Please regenerate it and try again.", []);
        // The B/S rule grammar (unique digits per group, no B0) is owned by the shared kernel.
        if (!Rule.TryParse(request.Rule, out _))
            return new GameError.ValidationRejected("That rule isn't valid. Use a birth/survival rule like \"B3/S23\" — birth on 0 neighbours isn't allowed.", []);
        if (request.TickRate is < MinTickRate or > MaxTickRate || double.IsNaN(request.TickRate))
            return new GameError.ValidationRejected("The tick rate must be between 60 and 250 generations per second.", []);
        return null;
    }

    // Mirrors the backend's CreateGameRequest.Min/MaxTickRate: the sim is capped so every generation is
    // delivered to observers (see the API's advance-driven broadcast pump).
    private const double MinTickRate = 60.0;
    private const double MaxTickRate = 250.0;

    /// <summary>The seed is 100×100 bits = 1250 bytes, base64-encoded — the single source of truth is
    /// <see cref="SeedBoard.ByteLength"/> (the seeding domain that produces the packing).</summary>
    private const int SeedByteLength = SeedBoard.ByteLength;

    private static bool IsValidSeed(string? seed)
    {
        if (string.IsNullOrEmpty(seed))
            return false;

        var buffer = new byte[SeedByteLength];
        // TryFromBase64String fails if the decoded length exceeds the buffer, so an over-long seed is rejected too.
        return Convert.TryFromBase64String(seed, buffer, out var written) && written == SeedByteLength;
    }
}
