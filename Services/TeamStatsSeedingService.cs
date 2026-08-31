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
//  - football-data.org (SeedFromRealMatchesAsync): current + 2 previous
//    seasons (~2 years), refreshed automatically every day by
//    TeamStatsRefreshBackgroundService. This is the primary source - it's
//    also what every live team-name lookup elsewhere (AnalyzeMatch,
//    upcoming matches) is keyed against, so a team seeded from here is
//    guaranteed to be found again under the exact same name.
//  - openfootball/football.json on GitHub (SeedFromOpenFootballAsync): an
//    alternative/manual multi-season backfill, public domain, no API key.
//    Not used by the automatic daily refresh - its team names don't always
//    match football-data.org's spelling exactly, which can silently make a
//    team's stats unreachable from AnalyzeMatch's exact-name lookup. Useful
//    as a one-off if football-data.org is ever unavailable, but
//    football-data.org's own multi-season window above is the safer default.
//    Whichever ran most recently wins (each clears the table first).
public class TeamStatsSeedingService
{
    // Current season + the 2 previous ones (~2 years). Early in a season (or
    // for a team with few matches so far), a short window is too small a
    // sample and lets one lucky/unlucky result swing a team's average hard
    // (e.g. a single high-scoring away match inflating a team's away xG) -
    // more matches smooths that out. All from football-data.org, the same
    // source AnalyzeMatch's team-name lookups depend on elsewhere, so there's
    // no risk of a differently-spelled team name (as openfootball's JSON
    // sometimes has) silently failing to match and dropping a team's stats.
    private const int DaysBack = 760;
    private const int FormWindow = 5;

    // How many of each team's most recent matches to keep as individual
    // TeamRecentResult rows (opponent + score), for the margin-of-victory
    // and common-opponent reasoning in FormMomentumAnalyzer. Wider than
    // FormWindow (5) so a common opponent has more chance of showing up in
    // both teams' recent history even if it wasn't in either side's last 5.
    private const int RecentResultsWindow = 10;

    private static readonly string[] OpenFootballLeagueCodes = { "en.1", "es.1", "it.1", "de.1", "fr.1" };

    // A seed run now makes dozens of paced requests over several minutes -
    // two overlapping runs (e.g. a manual call fired right after the daily
    // background refresh started, or a re-run right after a Ctrl-C'd one
    // that keeps running server-side) would double up on football-data.org's
    // 10 req/min cap and both come back empty. Shared across instances
    // (this service is scoped per-request) so any seed method blocks any
    // other rather than racing it.
    private static readonly SemaphoreSlim _seedLock = new(1, 1);

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
        if (!await _seedLock.WaitAsync(0, ct))
        {
            return (false, "Un seeding TeamStats est déjà en cours (refresh auto ou appel précédent) - réessaie dans quelques minutes", 0);
        }

        try
        {
            var diag = new FetchDiagnostics();
            var matches = await _footballDataService.GetFinishedMatchesAsync(DaysBack, ct, diag);
            if (matches.Count == 0)
            {
                return (false,
                    $"Aucun match terminé trouvé sur la période ({diag.ChunksAttempted} chunks essayés, " +
                    $"{diag.ChunksNullDoc} sans réponse API, {diag.ChunksNoMatchesKey} sans clé 'matches', " +
                    $"{diag.ChunksThrew} exceptions, {diag.RawMatchesSeen} matchs bruts vus toutes compétitions confondues)",
                    0);
            }

            var generic = matches
                .Select(m => (m.HomeTeam, m.AwayTeam, m.HomeScore, m.AwayScore, m.UtcDate))
                .ToList();

            return await AggregateAndSaveAsync(generic, $"{matches.Count} matchs réels ({DaysBack} derniers jours, football-data.org)", ct);
        }
        finally
        {
            _seedLock.Release();
        }
    }

    // One-time deeper backfill from openfootball's public-domain JSON
    // dataset, across multiple seasons. Season format is "YYYY-YY", e.g.
    // "2026-27". Defaults to the current season plus the 2 previous ones -
    // early in a season (or for teams with few matches so far) a single
    // season is too small a sample and can make one lucky/unlucky result
    // swing a team's averages hard (e.g. a single high-scoring away match
    // inflating a team's away xG). More matches smooths that out, at the
    // cost of reflecting last season's squad rather than only this one's -
    // an honest tradeoff, not fabricated data either way.
    // `seasonsParam` can be a single season ("2025-26") or a comma-separated
    // list ("2026-27,2025-26,2024-25") to override the default 3-season window.
    public async Task<(bool Success, string Message, int TeamsUpdated)> SeedFromOpenFootballAsync(string? seasonsParam = null, CancellationToken ct = default)
    {
        if (!await _seedLock.WaitAsync(0, ct))
        {
            return (false, "Un seeding TeamStats est déjà en cours (refresh auto ou appel précédent) - réessaie dans quelques minutes", 0);
        }

        try
        {
            var seasons = string.IsNullOrWhiteSpace(seasonsParam)
                ? DefaultSeasons()
                : seasonsParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            var allMatches = new List<(string Team1, string Team2, int Score1, int Score2, DateTime Date)>();

            foreach (var season in seasons)
            {
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
            }

            if (allMatches.Count == 0)
            {
                return (false, $"Aucun match joué trouvé pour les saisons {string.Join(", ", seasons)}", 0);
            }

            return await AggregateAndSaveAsync(
                allMatches,
                $"{allMatches.Count} matchs réels (saisons {string.Join(", ", seasons)}, openfootball.json)",
                ct);
        }
        finally
        {
            _seedLock.Release();
        }
    }

    private static List<string> DefaultSeasons()
    {
        var now = DateTime.UtcNow;
        var currentStartYear = now.Month >= 7 ? now.Year : now.Year - 1;
        return new List<string>
        {
            $"{currentStartYear}-{(currentStartYear + 1) % 100:D2}",
            $"{currentStartYear - 1}-{currentStartYear % 100:D2}",
            $"{currentStartYear - 2}-{(currentStartYear - 1) % 100:D2}"
        };
    }


    private async Task<(bool Success, string Message, int TeamsUpdated)> AggregateAndSaveAsync(
        List<(string Team1, string Team2, int Score1, int Score2, DateTime Date)> matches,
        string sourceLabel,
        CancellationToken ct)
    {
        // Every prior match a team played, most recent first, across both
        // "team1" (home) and "team2" (away) fixtures. Opponent+IsHome kept
        // alongside the score (not just aggregated away) so it can also
        // be persisted per-match into TeamRecentResults below.
        var perTeam = new Dictionary<string, List<(DateTime Date, string Opponent, int GoalsFor, int GoalsAgainst, bool IsHome)>>();

        void Record(string team, DateTime date, string opponent, int goalsFor, int goalsAgainst, bool isHome)
        {
            if (!perTeam.TryGetValue(team, out var list))
            {
                list = new List<(DateTime, string, int, int, bool)>();
                perTeam[team] = list;
            }
            list.Add((date, opponent, goalsFor, goalsAgainst, isHome));
        }

        foreach (var m in matches)
        {
            Record(m.Team1, m.Date, m.Team2, m.Score1, m.Score2, isHome: true);
            Record(m.Team2, m.Date, m.Team1, m.Score2, m.Score1, isHome: false);
        }

        // Old/stale rows (test-seed fakes, or a previous run from the other
        // source) would otherwise sit alongside these under different name
        // strings and never get matched by AnalyzeMatch again.
        _context.TeamStats.RemoveRange(_context.TeamStats);
        _context.TeamRecentResults.RemoveRange(_context.TeamRecentResults);

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

            foreach (var g in games.Take(RecentResultsWindow))
            {
                _context.TeamRecentResults.Add(new TeamRecentResult
                {
                    TeamName = team,
                    OpponentName = g.Opponent,
                    MatchDate = g.Date,
                    GoalsFor = g.GoalsFor,
                    GoalsAgainst = g.GoalsAgainst,
                    IsHome = g.IsHome
                });
            }

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
