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
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<FootballDataService>();
builder.Services.AddHttpClient<DiscordNotificationService>();

// 🎯 Auto-corrige les paris PENDING toutes les 15min (vérifie le score réel,
// marque WIN/LOSS, met à jour le LearningNotebook) - c'est ce qui permet à
// l'IA de réellement apprendre de ses résultats, sans dépendre d'un cron externe.
builder.Services.AddHostedService<AutoSettlementBackgroundService>();

// 🤖 Décide de nouveaux paris toutes les 45min, sur les matchs qui démarrent
// dans l'heure qui suit - remplace la dépendance à un cron externe.
builder.Services.AddHostedService<AutoDecideBetsBackgroundService>();

// ⚡ FASTENDPOINTS
builder.Services.AddFastEndpoints();

var app = builder.Build();

// 🌐 MIDDLEWARE
app.UseStaticFiles();
app.UseFastEndpoints();

Console.WriteLine("✅ BettingAI API started");
Console.WriteLine("🤖 Auto-bets managed by CRON script");

app.Run();