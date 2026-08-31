using BettingAI.Data;
using BettingAI.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 📦 SERVICES
builder.Services.AddDbContext<BettingContext>(options =>
    options.UseSqlite("Data Source=betting.db"));  // ← FIX: Ajoute SQLite

builder.Services.AddScoped<OddsScraperService>();
builder.Services.AddScoped<BetSettlementService>();
builder.Services.AddScoped<TeamStatsSeedingService>();
builder.Services.AddSingleton<CycleStatusService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<FootballDataService>();
builder.Services.AddHttpClient<DiscordNotificationService>();

// 🎯 Auto-corrige les paris PENDING toutes les 15min (vérifie le score réel,
// marque WIN/LOSS, met à jour le LearningNotebook) - c'est ce qui permet à
// l'IA de réellement apprendre de ses résultats, sans dépendre d'un cron externe.
builder.Services.AddHostedService<AutoSettlementBackgroundService>();

// 🤖 Décide des paris du jour une fois par jour (8h heure de Paris), sur tous
// les matchs restants de la journée - remplace la dépendance à un cron externe.
builder.Services.AddHostedService<AutoDecideBetsBackgroundService>();

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
app.UseStaticFiles();
app.UseFastEndpoints();

Console.WriteLine("✅ BettingAI API started");
Console.WriteLine("🤖 Auto-bets managed by CRON script");

app.Run();