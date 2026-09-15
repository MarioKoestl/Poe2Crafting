using System.Globalization;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Engine.Planning;
using POE2Crafting.Core.Market;
using POE2Crafting.Web;
using POE2Crafting.Web.Services;

// the UI is English; numbers in markup (widths, input values) must not depend on the server's regional settings
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Game data: loaded once from the data/ folder next to the exe (or configured path)
var dataFolder = builder.Configuration["DataFolder"] ?? Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data");
dataFolder = Path.GetFullPath(dataFolder);
if (!Directory.Exists(dataFolder))
    throw new DirectoryNotFoundException($"Data folder not found: {dataFolder}. Copy the data/ folder next to the application or set DataFolder in appsettings.json.");

var gameData = GameData.Load(dataFolder);
var engine = new CraftingEngine(gameData);
// data, mod pool and engine are read-only after loading and shared by all sessions
builder.Services.AddSingleton(gameData);
builder.Services.AddSingleton(engine.Pool);
builder.Services.AddSingleton(engine);
// crafting projects: saved automatically next to the data folder (or configured path)
var projectsFolder = Path.GetFullPath(builder.Configuration["ProjectsFolder"] ?? Path.Combine(dataFolder, "..", "projects"));
builder.Services.AddSingleton(sp => new ProjectStore(projectsFolder, gameData, sp.GetRequiredService<ILogger<ProjectStore>>()));
// guides saved from simulator histories: next to the projects (or configured path)
var savedGuidesFolder = Path.GetFullPath(builder.Configuration["SavedGuidesFolder"] ?? Path.Combine(dataFolder, "..", "saved-guides"));
builder.Services.AddSingleton(sp => new GuideCatalog(savedGuidesFolder, new GuideLibrary(engine), engine, sp.GetRequiredService<ILogger<GuideCatalog>>()));
// Currency Exchange prices: hourly digests of GGG's public API, cached next to the projects (or configured path), polled in the background
var marketSettings = builder.Configuration.GetSection("Market").Get<MarketSettings>() ?? new MarketSettings();
var marketCacheFolder = Path.GetFullPath(builder.Configuration["MarketCacheFolder"] ?? Path.Combine(dataFolder, "..", "market-cache"));
builder.Services.AddHttpClient(MarketDataService.HttpClientName, http =>
{
    http.BaseAddress = new Uri("https://web.poecdn.com/api/currency-exchange/poe2/");
    http.DefaultRequestHeaders.UserAgent.ParseAdd("POE2CraftingSimulator/1.0 (+https://github.com/MarioKoestl/Poe2Crafting)");
    http.Timeout = TimeSpan.FromSeconds(90);
});
builder.Services.AddSingleton(new ExchangeCatalog(gameData));
builder.Services.AddSingleton(sp => new MarketDataService(sp.GetRequiredService<IHttpClientFactory>(), marketSettings, marketCacheFolder,
    sp.GetRequiredService<ILogger<MarketDataService>>()));
builder.Services.AddHostedService(sp => sp.GetRequiredService<MarketDataService>());
builder.Services.AddScoped<CraftingSession>();
builder.Services.AddScoped<PlannerState>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

Console.WriteLine($"POE2 Crafting Simulator started. Data: {dataFolder} ({gameData.Bases.Count} bases, {gameData.Mods.Count} mods)");

app.Run();
