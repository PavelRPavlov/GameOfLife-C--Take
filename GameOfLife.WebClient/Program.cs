using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using GameOfLife.WebClient;
using GameOfLife.WebClient.Communication;

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

// The integration seam (wayfinder #12/#23): a single GameStore over the real REST + localStorage
// implementations. IGameStream (the SignalR transport, #25) is registered by that ticket; until then
// nothing resolves GameStore, so the incomplete graph is inert.
builder.Services.AddSingleton<IAdminSecretStore, LocalStorageAdminSecretStore>();
builder.Services.AddSingleton<IGameApi>(sp => new HttpGameApi(
    new HttpClient { BaseAddress = new Uri(backendBaseAddress) },
    sp.GetRequiredService<IAdminSecretStore>()));
builder.Services.AddSingleton<GameStore>();

await builder.Build().RunAsync();
