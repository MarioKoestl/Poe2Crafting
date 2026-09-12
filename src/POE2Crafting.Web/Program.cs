using POE2Crafting.Core.Data;
using POE2Crafting.Core.Engine;
using POE2Crafting.Web;
using POE2Crafting.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Game data: loaded once from the data/ folder next to the exe (or configured path)
var dataFolder = builder.Configuration["DataFolder"] ?? Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data");
dataFolder = Path.GetFullPath(dataFolder);
if (!Directory.Exists(dataFolder))
    throw new DirectoryNotFoundException($"Data folder not found: {dataFolder}. Copy the data/ folder next to the application or set DataFolder in appsettings.json.");

var gameData = GameData.Load(dataFolder);
builder.Services.AddSingleton(gameData);
builder.Services.AddSingleton(new ModPool(gameData));
builder.Services.AddScoped<CraftingSession>();

var app = builder.Build();

app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

Console.WriteLine($"POE2 Crafting Simulator started. Data: {dataFolder} ({gameData.Bases.Count} bases, {gameData.Mods.Count} mods)");

app.Run();
