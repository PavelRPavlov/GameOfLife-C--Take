var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The backend is a separate host from the Wasm static origin (see wwwroot/appsettings.json); it needs
// CORS to allow this origin. Falls back to the API's default dev URL when no config is present.
var backendBaseAddress = builder.Configuration["BackendBaseAddress"] ?? "http://localhost:5092/";

// Default client for the app's own static assets (the framework template's HttpClient).
builder.Services.AddScoped(sp => new HttpClient
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
});

// The integration seam (wayfinder #12/#23/#24/#25): a single GameStore over the real REST + SignalR +
// localStorage implementations. With IGameStream now registered the DI graph is complete and GameStore
// resolves; the pages (#17/#18/#19) consume it.
builder.Services.AddSingleton<IAdminSecretStore, LocalStorageAdminSecretStore>();
builder.Services.AddSingleton<IGameApi>(sp => new HttpGameApi(
    new HttpClient { BaseAddress = new Uri(backendBaseAddress) },
    sp.GetRequiredService<IAdminSecretStore>()));
// The SignalR hub is a sibling route on the same backend origin (see GameHost.HubUrl = "/hubs/game").
builder.Services.AddSingleton<IGameStream>(_ => new SignalRGameStream(
    new Uri(new Uri(backendBaseAddress), "hubs/game").ToString()));
builder.Services.AddSingleton<GameStore>();
// The app-shell connection state machine (Connecting…→Ready→Reconnecting…→Disconnected/Retry) that
// MainLayout drives on first render. App-lifetime like the shell itself; wraps the single GameStore.
builder.Services.AddSingleton<ShellConnection>();

await builder.Build().RunAsync();
