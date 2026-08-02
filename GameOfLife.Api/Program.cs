using GameOfLife.Api.Composition;
using GameOfLife.Api.Configuration;
using GameOfLife.Api.Game;

var builder = WebApplication.CreateBuilder(args);

// Service configuration: backend settings bind (and validate at startup) from appsettings; the game
// kernel registers its own runtime (host, broadcast loop, SignalR); the API surface registers the
// REST/OpenAPI/CORS edge.
builder.Services
    .AddGameOptions(builder.Configuration)
    .AddGame()
    .AddGameApiSurface(builder.Configuration);

var app = builder.Build();

// Request pipeline (development OpenAPI document, CORS).
app.UseGameApiPipeline();

// Endpoint mapping — one step of the configuration; every route lives in the central registrar.
app.MapGameEndpoints();

app.Run();

// Exposed so the in-memory test host (WebApplicationFactory<Program>) can reference the entry point.
public partial class Program;
