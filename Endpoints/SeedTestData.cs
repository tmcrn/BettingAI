using BettingAI.Data;
using BettingAI.Models;
using FastEndpoints;

namespace BettingAI.Endpoints;

public class SeedTestDataEndpoint : EndpointWithoutRequest
{
    private readonly BettingContext _context;

    public SeedTestDataEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Post("/api/seed-test-data");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        // Clear existing
        _context.TeamStats.RemoveRange(_context.TeamStats);
        _context.MatchContexts.RemoveRange(_context.MatchContexts);
        await _context.SaveChangesAsync(ct);

        // Seed teams
        var teams = new[]
        {
            new TeamStats
            {
                TeamName = "Monaco",
                LeagueId = 1,
                xG = 1.85m,
                xA = 1.2m,
                ShotsOnTarget = 5,
                TotalShots = 12,
                ConversionRate = 0.25m,
                PossessionAvg = 0.55m,
                xGA = 1.3m,
                ShotsConceded = 8,
                CleanSheets = 3,
                DefenseRating = 0.75m,
                Wins = 4,
                Draws = 1,
                Losses = 1,
                FormLast5 = 1.8m,  // Excellente forme
                IsHomeMatch = true,
                DaysSinceLastMatch = 3,
                KeyInjuries = "None",
                FatigueIndex = 0.3m,
                ConsecutiveMatches = 1,
                LastUpdated = DateTime.UtcNow
            },
            new TeamStats
            {
                TeamName = "Marseille",
                LeagueId = 1,
                xG = 1.45m,
                xA = 0.95m,
                ShotsOnTarget = 4,
                TotalShots = 10,
                ConversionRate = 0.20m,
                PossessionAvg = 0.45m,
                xGA = 1.7m,  // Défense plus faible
                ShotsConceded = 11,
                CleanSheets = 1,
                DefenseRating = 0.65m,
                Wins = 2,
                Draws = 2,
                Losses = 2,
                FormLast5 = 1.2m,  // Forme moyenne
                IsHomeMatch = false,
                DaysSinceLastMatch = 4,
                KeyInjuries = "Payet doubtful",
                FatigueIndex = 0.5m,
                ConsecutiveMatches = 2,
                LastUpdated = DateTime.UtcNow
            },
            new TeamStats
            {
                TeamName = "PSG",
                LeagueId = 1,
                xG = 2.15m,
                xA = 1.5m,
                ShotsOnTarget = 6,
                TotalShots = 14,
                ConversionRate = 0.30m,
                PossessionAvg = 0.62m,
                xGA = 1.1m,
                ShotsConceded = 7,
                CleanSheets = 4,
                DefenseRating = 0.85m,
                Wins = 5,
                Draws = 0,
                Losses = 1,
                FormLast5 = 2.0m,
                IsHomeMatch = true,
                DaysSinceLastMatch = 2,
                KeyInjuries = "None",
                FatigueIndex = 0.4m,
                ConsecutiveMatches = 1,
                LastUpdated = DateTime.UtcNow
            },
            new TeamStats
            {
                TeamName = "Nice",
                LeagueId = 1,
                xG = 1.35m,
                xA = 0.85m,
                ShotsOnTarget = 3,
                TotalShots = 9,
                ConversionRate = 0.18m,
                PossessionAvg = 0.40m,
                xGA = 1.9m,
                ShotsConceded = 13,
                CleanSheets = 0,
                DefenseRating = 0.55m,
                Wins = 1,
                Draws = 1,
                Losses = 4,
                FormLast5 = 0.8m,
                IsHomeMatch = false,
                DaysSinceLastMatch = 5,
                KeyInjuries = "Lemina out",
                FatigueIndex = 0.7m,
                ConsecutiveMatches = 3,
                LastUpdated = DateTime.UtcNow
            }
        };

        _context.TeamStats.AddRange(teams);

        // Seed match context
        var contexts = new[]
        {
            new MatchContext
            {
                MatchId = "1",
                HomeTeam = "Monaco",
                AwayTeam = "Marseille",
                MatchDate = DateTime.UtcNow.AddDays(1),
                HomeLineup = "[\"Nubel\", \"Henrichs\", \"Salisu\"]",
                AwayLineup = "[\"Lopez\", \"Murillo\", \"Balerdi\"]",
                HomeMissingPlayers = "None",
                AwayMissingPlayers = "Payet (doubtful)",
                HomeWinsH2H = 3,
                DrawsH2H = 2,
                AwayWinsH2H = 1,
                AvgGoalsH2H = 2.5m,
                Competition = "Ligue 1",
                Weather = "Clear, 18°C",
                Altitude = 0,
                IsEuropeanMatch = false,
                IsDerby = false,
                ExpectedScore = 3.15m,  // xG combinés
                HomeExpectedWinProbability = 0.68m
            },
            new MatchContext
            {
                MatchId = "2",
                HomeTeam = "PSG",
                AwayTeam = "Nice",
                MatchDate = DateTime.UtcNow.AddDays(1),
                HomeLineup = "[\"Donnarumma\", \"Hakimi\", \"Marquinhos\"]",
                AwayLineup = "[\"Bulka\", \"Lotomba\", \"Dante\"]",
                HomeMissingPlayers = "None",
                AwayMissingPlayers = "Lemina (out)",
                HomeWinsH2H = 8,
                DrawsH2H = 1,
                AwayWinsH2H = 0,
                AvgGoalsH2H = 3.2m,
                Competition = "Ligue 1",
                Weather = "Cloudy, 16°C",
                Altitude = 0,
                IsEuropeanMatch = false,
                IsDerby = false,
                ExpectedScore = 3.5m,
                HomeExpectedWinProbability = 0.82m
            }
        };

        _context.MatchContexts.AddRange(contexts);

        await _context.SaveChangesAsync(ct);

        await Send.OkAsync();
    }
}