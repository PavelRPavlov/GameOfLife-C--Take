using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using GameOfLife.Core;

namespace GameOfLife.WebClient.Communication;

/// <summary>
/// The real <see cref="IGameApi"/> — a typed <see cref="HttpClient"/> over the backend REST contract.
/// It confines the wire DTOs (decimal-string coordinates, string enums) to this boundary and hands the
/// seam only parsed domain types. Non-2xx responses that carry UX meaning become <see cref="GameError"/>
/// values (never exceptions): <c>404</c>→<see cref="GameError.NoGame"/>, <c>403</c>→<see cref="GameError.Forbidden"/>,
/// <c>409</c>→<see cref="GameError.AlreadyExists"/> (create) or <see cref="GameError.InvalidState"/> (control),
/// <c>400</c>→<see cref="GameError.ValidationRejected"/>; a network failure / <c>5xx</c> / unforeseen status becomes
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

    public async Task<Result<CreatedGame, GameError>> CreateGameAsync(CreateGameRequest request, CancellationToken ct = default)
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
                return new CreatedGame(dto!.AdminSecret, dto.Status, dto.Generation, dto.TickRate, dto.Rule);
            },
            status => status switch
            {
                HttpStatusCode.Conflict => GameError.AlreadyExists.Instance,
                _ => null, // 400 (ValidationRejected) and network/5xx handled by SendAsync's shared paths
            },
            ct);
    }

    public Task<Result<ControlOutcome, GameError>> StartAsync(CancellationToken ct = default) => ControlAsync("start", ct);
    public Task<Result<ControlOutcome, GameError>> StopAsync(CancellationToken ct = default) => ControlAsync("stop", ct);
    public Task<Result<ControlOutcome, GameError>> PauseAsync(CancellationToken ct = default) => ControlAsync("pause", ct);
    public Task<Result<ControlOutcome, GameError>> ResumeAsync(CancellationToken ct = default) => ControlAsync("resume", ct);
    public Task<Result<ControlOutcome, GameError>> StepAsync(CancellationToken ct = default) => ControlAsync("step", ct);

    private Task<Result<ControlOutcome, GameError>> ControlAsync(string verb, CancellationToken ct) =>
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
                return new ControlOutcome(dto!.Status, dto.Generation);
            },
            status => status switch
            {
                HttpStatusCode.NotFound => GameError.NoGame.Instance,
                HttpStatusCode.Forbidden => GameError.Forbidden.Instance,
                HttpStatusCode.Conflict => GameError.InvalidState.Instance,
                _ => null,
            },
            ct);

    public Task<Result<Snapshot, GameError>> GetSnapshotAsync(CancellationToken ct = default) =>
        SendAsync(
            () => new HttpRequestMessage(HttpMethod.Get, "snapshot"),
            async (response, token) =>
            {
                var dto = await response.Content.ReadFromJsonAsync<SnapshotResponseDto>(WireJson.Options, token);
                var cells = dto!.Cells.Select(ToCell).ToList();
                return new Snapshot(dto.Gen, dto.Status, dto.TickRate, cells);
            },
            status => status switch
            {
                HttpStatusCode.NotFound => GameError.NoGame.Instance,
                _ => null,
            },
            ct);

    /// <summary>
    /// The shared request pipeline: build the message, send it, and fold the response into a
    /// <see cref="Result{T, GameError}"/>. Success (2xx) parses the body; a mapped non-2xx becomes its
    /// <see cref="GameError"/> value; a <c>400</c> becomes <see cref="GameError.ValidationRejected"/>;
    /// anything else — plus a thrown <see cref="HttpRequestException"/> or a non-cancellation timeout —
    /// becomes <see cref="GameError.Transport"/>. A cancellation via <paramref name="ct"/> propagates.
    /// </summary>
    private async Task<Result<T, GameError>> SendAsync<T>(
        Func<HttpRequestMessage> buildRequest,
        Func<HttpResponseMessage, CancellationToken, Task<T>> parseSuccess,
        Func<HttpStatusCode, GameError?> mapStatus,
        CancellationToken ct)
    {
        try
        {
            using var request = buildRequest();
            using var response = await _http.SendAsync(request, ct);

            if (response.IsSuccessStatusCode)
                return Result<T, GameError>.Ok(await parseSuccess(response, ct));

            if (mapStatus(response.StatusCode) is { } mapped)
                return Result<T, GameError>.Err(mapped);

            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                var details = await response.Content.ReadAsStringAsync(ct);
                return Result<T, GameError>.Err(new GameError.ValidationRejected(details));
            }

            return Result<T, GameError>.Err(
                new GameError.Transport($"Unexpected status {(int)response.StatusCode} {response.StatusCode}."));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or NotSupportedException)
        {
            return Result<T, GameError>.Err(new GameError.Transport(ex.Message));
        }
    }

    private static Cell ToCell(CellDto dto) =>
        new(
            ulong.Parse(dto.X, NumberStyles.None, CultureInfo.InvariantCulture),
            ulong.Parse(dto.Y, NumberStyles.None, CultureInfo.InvariantCulture));

    // ---- Client-side validation (mirrors the backend's contract) ----

    /// <summary>Returns a <see cref="GameError.ValidationRejected"/> if the request is malformed, else null.</summary>
    private static GameError? Validate(CreateGameRequest request)
    {
        if (!IsValidSeed(request.Seed))
            return new GameError.ValidationRejected("Seed must be base64 that decodes to exactly 1250 bytes (100×100 bits).");
        // The B/S rule grammar (unique digits per group, no B0) is owned by the shared kernel.
        if (!Rule.TryParse(request.Rule, out _))
            return new GameError.ValidationRejected("Rule must match B[0-8]*/S[0-8]* with unique digits per group and no B0.");
        if (request.TickRate is < MinTickRate or > MaxTickRate || double.IsNaN(request.TickRate))
            return new GameError.ValidationRejected($"TickRate must be within {MinTickRate}..{MaxTickRate} generations per second.");
        return null;
    }

    private const double MinTickRate = 0.1;
    private const double MaxTickRate = 60.0;

    /// <summary>The seed is 100×100 bits = 1250 bytes, base64-encoded.</summary>
    private const int SeedByteLength = 1250;

    private static bool IsValidSeed(string? seed)
    {
        if (string.IsNullOrEmpty(seed))
            return false;

        var buffer = new byte[SeedByteLength];
        // TryFromBase64String fails if the decoded length exceeds the buffer, so an over-long seed is rejected too.
        return Convert.TryFromBase64String(seed, buffer, out var written) && written == SeedByteLength;
    }
}
