using System.Globalization;
using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Core.Engine.Planning;
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
builder.Services.AddScoped<CraftingSession>();
builder.Services.AddScoped<PlannerState>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

Console.WriteLine($"POE2 Crafting Simulator started. Data: {dataFolder} ({gameData.Bases.Count} bases, {gameData.Mods.Count} mods)");

app.Run();
