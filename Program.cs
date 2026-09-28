using BettingAI.Data;
using BettingAI.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

// Must run BEFORE WebApplication.CreateBuilder(args) below - that's the
// moment ASP.NET Core's configuration system snapshots the process's
// environment variables, so anything .env sets after that point would be
// too late for FootballData__ApiKey/Discord__WebhookUrl (read via
// IConfiguration) to pick up, though GEMINI_API_KEY/OWNER_TOKEN (read
// directly via Environment.GetEnvironmentVariable at first use) wouldn't
// actually care about the ordering - kept first regardless, for one
// obvious rule instead of two.
LoadDotEnv();

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
// Printed once at boot - not the key itself (never log a secret), just
// whether one is actually configured, since a missing GEMINI_API_KEY is
// the #1 way this fails and it's much easier to notice here than as a
// mysterious error mid-cycle. Same idea as the old OLLAMA_MODEL line this
// replaces (GeminiService now handles every AI call - decisions, pronos,
// screenshot OCR - that used to be split across a local Ollama text model
// and a separate vision model).
Console.WriteLine(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GEMINI_API_KEY"))
    ? "❌ GEMINI_API_KEY non configurée - crée une clé gratuite sur https://aistudio.google.com/apikey"
    : $"🧠 Gemini model: {Environment.GetEnvironmentVariable("GEMINI_MODEL") ?? "gemini-flash-latest"}");

app.Run();

// Reads a plain KEY=value .env file (if present) into this process's own
// environment variables - lets every secret this app needs (GEMINI_API_KEY,
// FootballData__ApiKey, Discord__WebhookUrl, OWNER_TOKEN, ...) live in one
// plain-text file inside the project (see .env.example for the full list)
// instead of scattered across shell profiles/systemd units/launch scripts
// that are easy to lose track of - exactly the "je sais jamais où c'est"
// problem this replaces. .env itself is gitignored, never committed.
//
// Looks in the current working directory first (where `dotnet run`/the
// published exe is actually launched from) and falls back to the build
// output folder, since those differ during local development. A variable
// already present in the real OS environment is left alone - lets a real
// deploy still set secrets the usual way instead, if ever preferred over
// this file.
static void LoadDotEnv()
{
    var path = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory }
        .Select(dir => Path.Combine(dir, ".env"))
        .FirstOrDefault(File.Exists);
    if (path == null) return;

    foreach (var line in File.ReadAllLines(path))
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;

        var separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex <= 0) continue;

        var key = trimmed[..separatorIndex].Trim();
        var value = trimmed[(separatorIndex + 1)..].Trim().Trim('"');
        if (Environment.GetEnvironmentVariable(key) == null)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
    }
}
