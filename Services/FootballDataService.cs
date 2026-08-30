using BettingAI.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace BettingAI.Services;

public class FootballDataService
{
    private readonly HttpClient _httpClient;

    // Supported leagues: Ligue 1 (53), Ligue 2 (110), La Liga (87), Serie A (55), Bundesliga (54), Premier League (39)
    private static readonly List<int> SupportedLeagueIds = new() { 53, 110, 87, 55, 54, 39 };
    private static readonly Dictionary<int, string> LeagueNames = new()
    {
        { 53, "Ligue 1" },
        { 110, "Ligue 2" },
        { 87, "La Liga" },
        { 55, "Serie A" },
        { 54, "Bundesliga" },
        { 39, "Premier League" }
    };

    private const string ApiKey = "1c30d5045emsh23b3584f2aa6cd3p17ed3ejsn6b9d95be77f3";

    // Shared across instances (this service is registered per-request via
    // AddHttpClient) so repeated calls - e.g. our own 30min cron cycle, or
    // several manual tests in a row - don't burn through the free API quota
    // for data that hasn't changed. Static + lock since multiple requests
    // can hit this concurrently.
    private static readonly Dictionary<string, (DateTime FetchedAt, string Content)> _dateCache = new();
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    public FootballDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private void SetupHeaders()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("x-rapidapi-host", "free-api-live-football-data.p.rapidapi.com");
        _httpClient.DefaultRequestHeaders.Add("x-rapidapi-key", ApiKey);
    }

    // Fetches (or reuses a cached copy of) the raw matches-by-date payload
    // for one date. On an API failure (rate limit included), falls back to
    // a stale cached copy rather than giving up entirely.
    private async Task<JsonDocument?> GetMatchesByDateAsync(string date)
    {
        lock (_cacheLock)
        {
            if (_dateCache.TryGetValue(date, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            {
                return JsonDocument.Parse(cached.Content);
            }
        }

        SetupHeaders();

        var response = await _httpClient.GetAsync(
            $"https://free-api-live-football-data.p.rapidapi.com/football-get-matches-by-date?date={date}"
        );

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Free API Error: {response.StatusCode}");

            lock (_cacheLock)
            {
                if (_dateCache.TryGetValue(date, out var stale))
                {
                    Console.WriteLine($"  ⚠️ Using stale cached data for {date} ({DateTime.UtcNow - stale.FetchedAt:g} old) due to API error");
                    return JsonDocument.Parse(stale.Content);
                }
            }

            return null;
        }

        var content = await response.Content.ReadAsStringAsync();

        lock (_cacheLock)
        {
            _dateCache[date] = (DateTime.UtcNow, content);
        }

        return JsonDocument.Parse(content);
    }

    public async Task<List<FootballMatch>> GetUpcomingMatchesAsync()
    {
        try
        {
            var matches = new List<FootballMatch>();
            var now = DateTime.UtcNow;
            var windowEnd = now.AddHours(24);

            // Fetch matches for today + tomorrow (24h window)
            var dates = new[] { now.ToString("yyyyMMdd"), now.AddDays(1).ToString("yyyyMMdd") };

            foreach (var date in dates)
            {
                var doc = await GetMatchesByDateAsync(date);
                if (doc == null) continue;

                if (doc.RootElement.TryGetProperty("response", out var respObj))
                {
                    if (respObj.TryGetProperty("matches", out var matchesArray))
                    {
                        foreach (var fixture in matchesArray.EnumerateArray())
                        {
                            var leagueId = fixture.GetProperty("leagueId").GetInt32();
                            var homeTeam = fixture.GetProperty("home").GetProperty("name").GetString();
                            var awayTeam = fixture.GetProperty("away").GetProperty("name").GetString();

                            if (!SupportedLeagueIds.Contains(leagueId)) continue;

                            // Get exact UTC time
                            var utcTimeStr = fixture.GetProperty("status").GetProperty("utcTime").GetString();
                            var matchTime = DateTime.Parse(utcTimeStr ?? DateTime.Now.ToString());

                            // Get match status
                            var statusObj = fixture.GetProperty("status");
                            var isFinished = statusObj.GetProperty("finished").GetBoolean();

                            // SKIP if match already finished
                            if (isFinished) continue;

                            // SKIP if match is outside 24h window
                            if (matchTime < now || matchTime > windowEnd) continue;

                            matches.Add(new FootballMatch
                            {
                                Id = fixture.GetProperty("id").GetInt32().ToString(),
                                HomeTeam = homeTeam,
                                AwayTeam = awayTeam,
                                UtcDate = matchTime,
                                Status = "SCHEDULED",
                                HomeScore = fixture.GetProperty("home").GetProperty("score").GetInt32(),
                                AwayScore = fixture.GetProperty("away").GetProperty("score").GetInt32()
                            });
                        }
                    }
                }
            }

            return matches.OrderBy(m => m.UtcDate).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return new List<FootballMatch>();
        }
    }

    // Looks up the real final result of a match by its real API id, searching
    // around the match's kickoff date. Used to settle PENDING bets once a
    // match should be over.
    public async Task<MatchStatus?> GetMatchStatusAsync(string matchId, DateTime referenceDate)
    {
        try
        {
            var dates = new[] { referenceDate.ToString("yyyyMMdd"), referenceDate.AddDays(1).ToString("yyyyMMdd") };

            foreach (var date in dates)
            {
                var doc = await GetMatchesByDateAsync(date);
                if (doc == null) continue;

                if (!doc.RootElement.TryGetProperty("response", out var respObj)) continue;
                if (!respObj.TryGetProperty("matches", out var matchesArray)) continue;

                foreach (var fixture in matchesArray.EnumerateArray())
                {
                    var id = fixture.GetProperty("id").GetInt32().ToString();
                    if (id != matchId) continue;

                    var isFinished = fixture.GetProperty("status").GetProperty("finished").GetBoolean();
                    var homeScore = fixture.GetProperty("home").GetProperty("score").GetInt32();
                    var awayScore = fixture.GetProperty("away").GetProperty("score").GetInt32();

                    return new MatchStatus
                    {
                        Finished = isFinished,
                        HomeScore = homeScore,
                        AwayScore = awayScore
                    };
                }
            }

            // Not found (too early, wrong date, or API doesn't carry it anymore)
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching match status for {matchId}: {ex.Message}");
            return null;
        }
    }
}

public class MatchStatus
{
    public bool Finished { get; set; }
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
}
