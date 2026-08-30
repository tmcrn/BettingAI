using BettingAI.Models;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace BettingAI.Services;

public class FootballDataService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    // Shared across instances (this service is registered per-request via
    // AddHttpClient) so repeated calls - our own 30min cron cycle chief among
    // them - don't burn through the free API quota for data that hasn't
    // changed. Footballdata.io's free plan is 2,000 requests/MONTH, so this
    // still needs to be measured in hours, not minutes. Static + lock since
    // multiple requests can hit this concurrently.
    private static readonly Dictionary<string, (DateTime FetchedAt, string Content)> _dateCache = new();
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    public FootballDataService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["FootballData:ApiKey"] ?? "";
        _baseUrl = configuration["FootballData:BaseUrl"]?.TrimEnd('/') ?? "https://footballdata.io/api/v1";
    }

    private void SetupHeaders()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
    }

    // Fetches (or reuses a cached copy of) the raw fixtures payload for one
    // date. On an API failure (rate limit included), falls back to a stale
    // cached copy rather than giving up entirely.
    private async Task<JsonDocument?> GetFixturesByDateAsync(string date)
    {
        lock (_cacheLock)
        {
            if (_dateCache.TryGetValue(date, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            {
                return JsonDocument.Parse(cached.Content);
            }
        }

        SetupHeaders();

        var response = await _httpClient.GetAsync($"{_baseUrl}/fixtures?date={date}");

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Footballdata.io API Error: {response.StatusCode}");

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

        // Surface quota usage so we notice getting close to the monthly cap
        // before it silently starts failing again.
        try
        {
            var meta = JsonDocument.Parse(content).RootElement.GetProperty("meta");
            if (meta.TryGetProperty("requests_remaining", out var remaining) &&
                meta.TryGetProperty("requests_limit", out var limit))
            {
                Console.WriteLine($"  📊 Footballdata.io quota: {remaining.GetInt32()}/{limit.GetInt32()} requests remaining");
            }
        }
        catch { /* meta shape not guaranteed, quota logging is best-effort */ }

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
            var dates = new[] { now.ToString("yyyy-MM-dd"), now.AddDays(1).ToString("yyyy-MM-dd") };

            foreach (var date in dates)
            {
                var doc = await GetFixturesByDateAsync(date);
                if (doc == null) continue;

                if (!doc.RootElement.TryGetProperty("data", out var dataObj)) continue;
                if (!dataObj.TryGetProperty("matches", out var matchesArray)) continue;

                foreach (var fixture in matchesArray.EnumerateArray())
                {
                    var isFinished = fixture.GetProperty("status").GetString() == "complete";
                    if (isFinished) continue; // SKIP if match already finished

                    var matchTime = DateTimeOffset.FromUnixTimeSeconds(fixture.GetProperty("date_unix").GetInt64()).UtcDateTime;

                    // SKIP if match is outside 24h window
                    if (matchTime < now || matchTime > windowEnd) continue;

                    // The free plan is already capped server-side to 5 leagues
                    // (meta.league_limit), so no client-side league filter needed here.
                    matches.Add(new FootballMatch
                    {
                        Id = fixture.GetProperty("match_id").GetInt64().ToString(),
                        HomeTeam = fixture.GetProperty("home_team").GetProperty("team_name").GetString(),
                        AwayTeam = fixture.GetProperty("away_team").GetProperty("team_name").GetString(),
                        UtcDate = matchTime,
                        Status = "SCHEDULED",
                        HomeScore = fixture.GetProperty("score").GetProperty("home").GetInt32(),
                        AwayScore = fixture.GetProperty("score").GetProperty("away").GetInt32()
                    });
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
            var dates = new[] { referenceDate.ToString("yyyy-MM-dd"), referenceDate.AddDays(1).ToString("yyyy-MM-dd") };

            foreach (var date in dates)
            {
                var doc = await GetFixturesByDateAsync(date);
                if (doc == null) continue;

                if (!doc.RootElement.TryGetProperty("data", out var dataObj)) continue;
                if (!dataObj.TryGetProperty("matches", out var matchesArray)) continue;

                foreach (var fixture in matchesArray.EnumerateArray())
                {
                    var id = fixture.GetProperty("match_id").GetInt64().ToString();
                    if (id != matchId) continue;

                    var isFinished = fixture.GetProperty("status").GetString() == "complete";
                    var homeScore = fixture.GetProperty("score").GetProperty("home").GetInt32();
                    var awayScore = fixture.GetProperty("score").GetProperty("away").GetInt32();

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
