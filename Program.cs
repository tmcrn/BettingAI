using BettingAI.Data;
using BettingAI.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 📦 SERVICES
builder.Services.AddDbContext<BettingContext>(options =>
    options.UseSqlite("Data Source=betting.db"));  // ← FIX: Ajoute SQLite

builder.Services.AddScoped<OddsScraperService>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<FootballDataService>();

// ⚡ FASTENDPOINTS
builder.Services.AddFastEndpoints();

var app = builder.Build();

// 🌐 MIDDLEWARE
app.UseStaticFiles();
app.UseFastEndpoints();

Console.WriteLine("✅ BettingAI API started");
Console.WriteLine("🤖 Auto-bets managed by CRON script");

app.Run();