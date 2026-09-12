using BettingAI.Data;
using BettingAI.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 📦 SERVICES
builder.Services.AddDbContext<BettingContext>(options =>
    options.UseSqlite("Data Source=betting.db"));  // ← FIX: Ajoute SQLite

builder.Services.AddScoped<BetSettlementService>();
builder.Services.AddScoped<TeamStatsSeedingService>();
builder.Services.AddScoped<WinPredictionService>();
builder.Services.AddScoped<OddsLearningService>();
builder.Services.AddSingleton<CycleStatusService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<FootballDataService>();
builder.Services.AddHttpClient<DiscordNotificationService>();

// 🎯 Le règlement des paris PENDING (vérifie le score réel, marque WIN/LOSS,
// met à jour le LearningNotebook) ne tourne plus en continu toutes les 15min
// (AutoSettlementBackgroundService, désormais non enregistré) - l'utilisateur
// veut tout regroupé sur le cycle unique de 8h, pas de vérifications éparpillées
// dans la journée. AutoDecideBetsEndpoint appelle déjà /api/settle-pending-bets
// en tout premier avant de décider les nouveaux paris, donc le règlement se
// fait bien une fois par jour, dans le même cycle que les décisions.

// 🤖 Le cycle automatique de 8h (heure de Paris) n'est plus enregistré -
// l'utilisateur veut déclencher lui-même chaque cycle manuellement
// (bouton "Forcer un cycle IA" du dashboard, ou POST /api/auto-decide-bets)
// plutôt que de le laisser partir tout seul. AutoDecideBetsBackgroundService
// reste dans le code, juste non branché, au cas où l'automatique redevienne
// souhaité plus tard.
// builder.Services.AddHostedService<AutoDecideBetsBackgroundService>();

// 📊 Recalcule TeamStats une fois par jour à partir des vrais résultats
// (forme, xG-proxy, fatigue) - remplace les données de test fabriquées.
builder.Services.AddHostedService<TeamStatsRefreshBackgroundService>();

// ⚡ FASTENDPOINTS
builder.Services.AddFastEndpoints();

var app = builder.Build();

// 🗄️ Applique automatiquement toute migration EF en attente au démarrage.
// Son absence a mordu une fois pour de vrai: la migration AddSelectionToComboLeg
// était bien dans le code déployé mais n'a jamais touché le vrai betting.db,
// donc toute tentative de sauvegarde de pari (même un pari simple, dans la
// même requête groupée) plantait silencieusement avec "no such column:
// c.Selection" - sans jamais atteindre un point où l'erreur remontait
// clairement ailleurs que dans analysisUsed. Plus besoin d'un `dotnet ef
// database update` manuel après chaque déploiement.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<BettingContext>();
    db.Database.Migrate();
}

// 🌐 MIDDLEWARE
// UseDefaultFiles (must run BEFORE UseStaticFiles) makes "/" itself serve
// wwwroot/index.html - without it, only the exact "/index.html" path
// worked, which happened to go unnoticed locally (an already-bookmarked
// URL/home-screen icon keeps whatever path it was saved with) but broke
// the bare link through a tunnel (a fresh visitor lands on "/" with
// nothing to show).
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseFastEndpoints();

Console.WriteLine("✅ Robert API started");
Console.WriteLine("🤖 Auto-bets managed by CRON script");
// Printed once at boot so switching OLLAMA_MODEL is directly visible in the
// console right away, instead of only discoverable by digging through a
// cycle's raw JSON response afterwards.
Console.WriteLine($"🧠 Ollama model: {Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "mistral"}");
// Same reasoning, for the separate multimodal model the screenshot-odds-
// import feature calls (OllamaVisionService) - a text-only model like the
// one above can't see images at all, so this one needs its own tag pulled
// locally ("ollama pull qwen2.5vl") before that feature works.
Console.WriteLine($"👁️ Ollama vision model: {Environment.GetEnvironmentVariable("OLLAMA_VISION_MODEL") ?? "qwen2.5vl"}");
// Where Ollama itself is actually called - defaults to this same machine,
// but can point at another one on the network (e.g. over Tailscale) so
// this app + its SQLite DB stay on a light always-on box while the heavy
// inference runs elsewhere. Printed so a misconfigured/unreachable address
// shows up here first, not as a mysterious connection error mid-cycle.
Console.WriteLine($"🌐 Ollama base URL: {Environment.GetEnvironmentVariable("OLLAMA_BASE_URL") ?? "http://localhost:11434"}");

app.Run();