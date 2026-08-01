using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameOfLife.Api.Contracts;
using GameOfLife.Api.Hosting;
using GameOfLife.Api.Hubs;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

// Enums cross the wire as strings ("Running", "Created", ...) on both REST and SignalR.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSignalR()
    .AddJsonProtocol(options =>
        options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<GameHost>();
builder.Services.AddHostedService<BroadcastLoopService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

// POST /game — create + seed + configure; issues the one-time admin secret; no auth.
app.MapPost("/game", async (HttpContext context, GameHost host) =>
{
    CreateGameRequest? request;
    try
    {
        // ReadFromJsonAsync honours [JsonUnmappedMemberHandling(Disallow)]: unknown properties,
        // an empty body, and malformed JSON all surface as JsonException → 400.
        request = await context.Request.ReadFromJsonAsync<CreateGameRequest>();
    }
    catch (JsonException)
    {
        return Results.BadRequest("Request body is not valid JSON or contains unknown properties.");
    }

    if (request is null)
        return Results.BadRequest("Request body is required.");

    var validationResults = new List<ValidationResult>();
    if (!Validator.TryValidateObject(request, new ValidationContext(request), validationResults, validateAllProperties: true))
        return Results.ValidationProblem(ToErrorDictionary(validationResults));

    // Validation passed (stateless) before any game is created — a bad request never claims the slot.
    var session = await host.TryCreateAsync(request.ToParameters());
    if (session is null)
        return Results.Conflict("A game already exists.");

    var response = new CreateGameResponse(
        AdminSecret: session.AdminSecret.ToString(),
        Status: session.Status,
        Generation: session.Current.Number,
        TickRate: session.TickRate,
        Rule: session.Rule.ToString(),
        HubUrl: GameHost.HubUrl,
        SnapshotUrl: GameHost.SnapshotUrl);

    return Results.Created(GameHost.SnapshotUrl, response);
});

// Control verbs — X-Admin-Secret gated; bodyless 404/403/409 in existence → auth → state order.
app.MapPost("/start", (HttpContext context, GameHost host) => Control(host.StartAsync, context));
app.MapPost("/stop", (HttpContext context, GameHost host) => Control(host.StopAsync, context));
app.MapPost("/pause", (HttpContext context, GameHost host) => Control(host.PauseAsync, context));
app.MapPost("/resume", (HttpContext context, GameHost host) => Control(host.ResumeAsync, context));
app.MapPost("/step", (HttpContext context, GameHost host) => Control(host.StepAsync, context));

// GET /snapshot — view-only, no secret; the full live set at the last broadcast-aligned generation.
app.MapGet("/snapshot", async (GameHost host) =>
{
    var snapshot = await host.GetSnapshotAsync();
    return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
});

app.MapHub<GameHub>(GameHost.HubUrl);

app.Run();

// Runs a control verb and maps its outcome to the shared HTTP response contract.
static async Task<IResult> Control(Func<string?, Task<ControlOutcome>> verb, HttpContext context)
{
    var secret = context.Request.Headers["X-Admin-Secret"].ToString();
    var outcome = await verb(secret);
    return outcome.Result switch
    {
        ControlResult.Ok => Results.Ok(new ControlResponse(outcome.Status, outcome.Generation)),
        ControlResult.NoGame => Results.NotFound(),
        ControlResult.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
        ControlResult.InvalidState => Results.Conflict(),
        _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
    };
}

static Dictionary<string, string[]> ToErrorDictionary(IEnumerable<ValidationResult> results)
{
    return results
        .SelectMany(r => (r.MemberNames.Any() ? r.MemberNames : ["_"])
            .Select(member => (member, message: r.ErrorMessage ?? "Invalid value.")))
        .GroupBy(x => x.member, x => x.message)
        .ToDictionary(g => g.Key, g => g.ToArray());
}

// Exposed so the in-memory test host (WebApplicationFactory<Program>) can reference the entry point.
public partial class Program;
