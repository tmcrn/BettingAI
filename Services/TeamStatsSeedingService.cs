using BettingAI.Data;
using BettingAI.Models;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Services;

// Populates TeamStats with numbers derived from real finished matches
// instead of the old hardcoded test data (Monaco/Marseille/PSG/Nice with
// made-up xG values). football-data.org's free tier has no real xG/shots/
// possession data, so xG/xGA here are honest proxies (goals scored/conceded
// average) rather than fabricated advanced stats - everything else that
// can't be derived from real results (ShotsOnTarget, PossessionAvg,
// ConversionRate, DefenseRating, KeyInjuries...) is left at its default
// rather than invented.
public class TeamStatsSeedingService
{
    private const int DaysBack = 45; // ~6 matchdays for a weekly league - enough for 5-game form
    private const int FormWindow = 5;

    private readonly BettingContext _context;
    private readonly FootballDataService _footballDataService;

    public TeamStatsSeedingService(BettingContext context, FootballDataService footballDataService)
    {
        _context = context;
        _footballDataService = footballDataService;
    }

    public async Task<(bool Success, string Message, int TeamsUpdated)> SeedFromRealMatchesAsync(CancellationToken ct = default)
    {
        var matches = await _footballDataService.GetFinishedMatchesAsync(DaysBack);
        if (matches.Count == 0)
        {
            return (false, "Aucun match terminé trouvé sur la période", 0);
        }

        // Every prior match a team played, most recent first, across both
        // home and away fixtures.
        var perTeam = new Dictionary<string, List<(DateTime Date, int GoalsFor, int GoalsAgainst)>>();

        void Record(string team, DateTime date, int goalsFor, int goalsAgainst)
        {
            if (!perTeam.TryGetValue(team, out var list))
            {
                list = new List<(DateTime, int, int)>();
                perTeam[team] = list;
            }
            list.Add((date, goalsFor, goalsAgainst));
        }

        foreach (var m in matches)
        {
            Record(m.HomeTeam, m.UtcDate, m.HomeScore, m.AwayScore);
            Record(m.AwayTeam, m.UtcDate, m.AwayScore, m.HomeScore);
        }

        // Old test-seed rows (Monaco/Marseille/PSG/Nice with fabricated
        // stats) would otherwise sit alongside these under different name
        // strings and never get matched by AnalyzeMatch again - clear them
        // so nothing stale/fake lingers.
        _context.TeamStats.RemoveRange(_context.TeamStats);

        var now = DateTime.UtcNow;

        foreach (var (team, gamesUnsorted) in perTeam)
        {
            var games = gamesUnsorted.OrderByDescending(g => g.Date).ToList();
            var last5 = games.Take(FormWindow).ToList();

            var wins = games.Count(g => g.GoalsFor > g.GoalsAgainst);
            var draws = games.Count(g => g.GoalsFor == g.GoalsAgainst);
            var losses = games.Count(g => g.GoalsFor < g.GoalsAgainst);
            var cleanSheets = games.Count(g => g.GoalsAgainst == 0);

            var xG = Math.Round((decimal)games.Average(g => g.GoalsFor), 2);
            var xGA = Math.Round((decimal)games.Average(g => g.GoalsAgainst), 2);

            // 2/1/0 scale (win/draw/loss) - matches the thresholds already
            // used in AnalyzeMatch's GenerateAnalysis (>1.7 = excellent,
            // <1.3 = average).
            var formPoints = last5.Sum(g => g.GoalsFor > g.GoalsAgainst ? 2 : g.GoalsFor == g.GoalsAgainst ? 1 : 0);
            var formLast5 = last5.Count > 0 ? Math.Round((decimal)formPoints / last5.Count, 2) : 0;

            var daysSinceLastMatch = (int)(now - games[0].Date).TotalDays;
            var matchesLast7Days = games.Count(g => (now - g.Date).TotalDays <= 7);
            var fatigueIndex = Math.Round(Math.Min(1m, matchesLast7Days / 3m), 2);

            _context.TeamStats.Add(new TeamStats
            {
                TeamName = team,
                LastUpdated = now,
                xG = xG,
                xGA = xGA,
                Wins = wins,
                Draws = draws,
                Losses = losses,
                FormLast5 = formLast5,
                CleanSheets = cleanSheets,
                DaysSinceLastMatch = daysSinceLastMatch,
                ConsecutiveMatches = matchesLast7Days,
                FatigueIndex = fatigueIndex
                // xA, ShotsOnTarget, TotalShots, ConversionRate, PossessionAvg,
                // ShotsConceded, DefenseRating, KeyInjuries: no real data
                // source for these - left at default rather than fabricated.
            });
        }

        await _context.SaveChangesAsync(ct);

        return (true, $"{perTeam.Count} équipes mises à jour à partir de {matches.Count} matchs réels ({DaysBack} derniers jours)", perTeam.Count);
    }
}
