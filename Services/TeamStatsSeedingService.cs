using BettingAI.Data;
using BettingAI.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BettingAI.Services;

// Populates TeamStats with numbers derived from real finished matches
// instead of the old hardcoded test data (Monaco/Marseille/PSG/Nice with
// made-up xG values). football-data.org's free tier has no real xG/shots/
// possession data, so xG/xGA here are honest proxies (goals scored/conceded
// average) rather than fabricated advanced stats - everything else that
// can't be derived from real results (ShotsOnTarget, PossessionAvg,
// ConversionRate, DefenseRating, KeyInjuries...) is left at its default
// rather than invented.
//
// Two sources feed the same aggregation:
//  - football-data.org (SeedFromRealMatchesAsync): last 45 days, refreshed
//    automatically every day by TeamStatsRefreshBackgroundService.
//  - openfootball/football.json on GitHub (SeedFromOpenFootballAsync): a
//    one-time deeper backfill covering the whole season so far, public
//    domain, no API key. Whichever ran most recently wins (each clears the
//    table first) - the daily refresh naturally takes back over afterwards.
public class TeamStatsSeedingService
{
    private const int DaysBack = 45; // ~6 matchdays for a weekly league - enough for 5-game form
    private const int FormWindow = 5;

    private static readonly string[] OpenFootballLeagueCodes = { "en.1", "es.1", "it.1", "de.1", "fr.1" };

    private readonly BettingContext _context;
    private readonly FootballDataService _footballDataService;
    private readonly HttpClient _httpClient;

    public TeamStatsSeedingService(BettingContext context, FootballDataService footballDataService, HttpClient httpClient)
    {
        _context = context;
        _footballDataService = footballDataService;
        _httpClient = httpClient;
    }

    public async Task<(bool Success, string Message, int TeamsUpdated)> SeedFromRealMatchesAsync(CancellationToken ct = default)
    {
        var matches = await _footballDataService.GetFinishedMatchesAsync(DaysBack);
        if (matches.Count == 0)
        {
            return (false, "Aucun match terminé trouvé sur la période", 0);
        }

        var generic = matches
            .Select(m => (m.HomeTeam, m.AwayTeam, m.HomeScore, m.AwayScore, m.UtcDate))
            .ToList();

        return await AggregateAndSaveAsync(generic, $"{matches.Count} matchs réels ({DaysBack} derniers jours, football-data.org)", ct);
    }

    // One-time deeper backfill from the current season's public-domain JSON
    // dataset. Season format is "YYYY-YY", e.g. "2026-27" - defaults to the
    // season currently in progress (European season runs roughly Aug-May).
    public async Task<(bool Success, string Message, int TeamsUpdated)> SeedFromOpenFootballAsync(string? season = null, CancellationToken ct = default)
    {
        season ??= CurrentSeason();

        var allMatches = new List<(string Team1, string Team2, int Score1, int Score2, DateTime Date)>();

        foreach (var code in OpenFootballLeagueCodes)
        {
            try
            {
                var url = $"https://raw.githubusercontent.com/openfootball/football.json/master/{season}/{code}.json";
                var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"openfootball {code} ({season}): HTTP {response.StatusCode}");
                    continue;
                }

                var content = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(content);
                if (!doc.RootElement.TryGetProperty("matches", out var matchesArray)) continue;

                foreach (var m in matchesArray.EnumerateArray())
                {
                    // Not played yet - no "score" (or no "ft") at all for future fixtures.
                    if (!m.TryGetProperty("score", out var scoreEl)) continue;
                    if (!scoreEl.TryGetProperty("ft", out var ftEl) || ftEl.ValueKind != JsonValueKind.Array || ftEl.GetArrayLength() < 2) continue;

                    var dateStr = m.TryGetProperty("date", out var dateEl) ? dateEl.GetString() : null;
                    if (dateStr == null || !DateTime.TryParse(
                            dateStr,
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                            out var date))
                    {
                        continue;
                    }

                    allMatches.Add((
                        m.GetProperty("team1").GetString() ?? "",
                        m.GetProperty("team2").GetString() ?? "",
                        ftEl[0].GetInt32(),
                        ftEl[1].GetInt32(),
                        date
                    ));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"openfootball {code} ({season}) error: {ex.Message}");
            }
        }

        if (allMatches.Count == 0)
        {
            return (false, $"Aucun match joué trouvé pour la saison {season}", 0);
        }

        return await AggregateAndSaveAsync(allMatches, $"{allMatches.Count} matchs réels (saison {season}, openfootball.json)", ct);
    }

    private static string CurrentSeason()
    {
        var now = DateTime.UtcNow;
        var startYear = now.Month >= 7 ? now.Year : now.Year - 1;
        return $"{startYear}-{(startYear + 1) % 100:D2}";
    }

    private async Task<(bool Success, string Message, int TeamsUpdated)> AggregateAndSaveAsync(
        List<(string Team1, string Team2, int Score1, int Score2, DateTime Date)> matches,
        string sourceLabel,
        CancellationToken ct)
    {
        // Every prior match a team played, most recent first, across both
        // "team1" (home) and "team2" (away) fixtures.
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
            Record(m.Team1, m.Date, m.Score1, m.Score2);
            Record(m.Team2, m.Date, m.Score2, m.Score1);
        }

        // Old/stale rows (test-seed fakes, or a previous run from the other
        // source) would otherwise sit alongside these under different name
        // strings and never get matched by AnalyzeMatch again.
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

        return (true, $"{perTeam.Count} équipes mises à jour à partir de {sourceLabel}", perTeam.Count);
    }
}
