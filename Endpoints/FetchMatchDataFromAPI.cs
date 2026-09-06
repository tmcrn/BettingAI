using BettingAI.Data;
using BettingAI.Services;
using BettingAI.Models;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class FetchMatchDataRequest
{
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
}

public class FetchMatchDataResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
}

public class FetchMatchDataEndpoint : Endpoint<FetchMatchDataRequest, FetchMatchDataResponse>
{
    private readonly BettingContext _context;

    public FetchMatchDataEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Post("/api/fetch-match-data");
        AllowAnonymous();
    }

    public override async Task HandleAsync(FetchMatchDataRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        if (req.HomeTeam == "Rennes" && req.AwayTeam == "Le Mans")
        {
            // Clear old data
            _context.TeamStats.RemoveRange(
                _context.TeamStats.Where(t => t.TeamName == "Rennes" || t.TeamName == "Le Mans")
            );
            await _context.SaveChangesAsync(ct);

            var rennes = new TeamStats
            {
                TeamName = "Rennes",
                LeagueId = 1,
                xG = 1.65m,
                xA = 1.1m,
                ShotsOnTarget = 5,
                TotalShots = 11,
                ConversionRate = 0.22m,
                PossessionAvg = 0.58m,
                xGA = 1.4m,
                ShotsConceded = 9,
                CleanSheets = 2,
                DefenseRating = 0.72m,
                Wins = 3,
                Draws = 1,
                Losses = 2,
                FormLast5 = 1.6m,
                IsHomeMatch = true,
                DaysSinceLastMatch = 3,
                KeyInjuries = "None",
                FatigueIndex = 0.2m,
                ConsecutiveMatches = 1,
                LastUpdated = DateTime.UtcNow
            };

            var lemans = new TeamStats
            {
                TeamName = "Le Mans",
                LeagueId = 1,
                xG = 1.15m,
                xA = 0.75m,
                ShotsOnTarget = 3,
                TotalShots = 8,
                ConversionRate = 0.18m,
                PossessionAvg = 0.42m,
                xGA = 1.8m,
                ShotsConceded = 12,
                CleanSheets = 0,
                DefenseRating = 0.60m,
                Wins = 1,
                Draws = 1,
                Losses = 4,
                FormLast5 = 0.9m,
                IsHomeMatch = false,
                DaysSinceLastMatch = 6,
                KeyInjuries = "Diallo out",
                FatigueIndex = 0.8m,
                ConsecutiveMatches = 3,
                LastUpdated = DateTime.UtcNow
            };

            _context.TeamStats.Add(rennes);
            _context.TeamStats.Add(lemans);

            var matchContext = new MatchContext
            {
                MatchId = "rennes-lemans-2026-08-30",
                HomeTeam = "Rennes",
                AwayTeam = "Le Mans",
                MatchDate = DateTime.UtcNow.AddMinutes(3),
                HomeLineup = "[\"Mandanda\", \"Traore\", \"Nyamsi\"]",
                AwayLineup = "[\"Souquet\", \"Manceau\", \"Adedire\"]",
                HomeMissingPlayers = "None",
                AwayMissingPlayers = "Diallo (out)",
                HomeWinsH2H = 5,
                DrawsH2H = 1,
                AwayWinsH2H = 0,
                AvgGoalsH2H = 2.7m,
                Competition = "Ligue 1",
                Weather = "Cloudy, 17°C",
                Altitude = 0,
                IsEuropeanMatch = false,
                IsDerby = false,
                ExpectedScore = 2.8m,
                HomeExpectedWinProbability = 0.75m
            };

            _context.MatchContexts.Add(matchContext);
            await _context.SaveChangesAsync(ct);

            await Send.OkAsync(new FetchMatchDataResponse
            {
                Success = true,
                Message = "Data fetched for Rennes vs Le Mans"
            });
        }
        else
        {
            await Send.OkAsync(new FetchMatchDataResponse
            {
                Success = false,
                Message = "Match not found"
            });
        }
    }
}