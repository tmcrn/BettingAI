using BettingAI.Models;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Text.Json;

namespace BettingAI.Services;

public class FootballDataService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;

    // The 5 major leagues, as football-data.org competition codes. The
    // /v4/matches endpoint's `competitions` filter wants numeric competition
    // ids (undocumented here, and not worth guessing wrong again like the
    // Sofascore team-name matching did) - so instead we don't filter
    // server-side at all and match each fixture's competition.code
    // client-side, which the docs do confirm as string codes like "PL".
    private static readonly HashSet<string> SupportedCompetitionCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "PL", "PD", "SA", "BL1", "FL1" // Premier League, La Liga, Serie A, Bundesliga, Ligue 1
    };

    // Shared across instances (this service is registered per-request via
    // AddHttpClient). football-data.org's free tier has no hard monthly cap,
    // just 10 requests/minute, so a short cache is enough here - mainly to
    // avoid tripping that limit during back-to-back manual tests or an
    // overlapping cron + settlement cycle.
    private static readonly Dictionary<string, (DateTime FetchedAt, string Content)> _cache = new();
    private static readonly object _cacheLock = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public FootballDataService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["FootballData:ApiKey"] ?? "";
        _baseUrl = configuration["FootballData:BaseUrl"]?.TrimEnd('/') ?? "https://api.football-data.org/v4";
    }

    private void SetupHeaders()
    {
        _httpClient.DefaultRequestHeaders.Clear();
        _httpClient.DefaultRequestHeaders.Add("X-Auth-Token", _apiKey);
    }

    // Fetches (or reuses a cached copy of) a GET request's raw body. On an
    // API failure (rate limit included), falls back to a stale cached copy
    // rather than giving up entirely.
    private async Task<JsonDocument?> GetAsync(string url, CancellationToken ct = default)
    {
        lock (_cacheLock)
        {
            if (_cache.TryGetValue(url, out var cached) && DateTime.UtcNow - cached.FetchedAt < CacheTtl)
            {
                return JsonDocument.Parse(cached.Content);
            }
        }

        SetupHeaders();

        var response = await _httpClient.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"football-data.org API error: {response.StatusCode} for {url}");

            lock (_cacheLock)
            {
                if (_cache.TryGetValue(url, out var stale))
                {
                    Console.WriteLine($"  ⚠️ Using stale cached data ({DateTime.UtcNow - stale.FetchedAt:g} old) due to API error");
                    return JsonDocument.Parse(stale.Content);
                }
            }

            return null;
        }

        var content = await response.Content.ReadAsStringAsync();

        lock (_cacheLock)
        {
            _cache[url] = (DateTime.UtcNow, content);
        }

        return JsonDocument.Parse(content);
    }

    private static DateTime ParseUtcDate(string utcDateStr) =>
        DateTime.Parse(utcDateStr, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    private static int GetScoreOrZero(JsonElement fullTime, string side) =>
        fullTime.TryGetProperty(side, out var val) && val.ValueKind != JsonValueKind.Null ? val.GetInt32() : 0;

    // minHours lets a caller ask for a slice that doesn't start at "now" -
    // e.g. minHours=72, windowHours=96 for "matches between 72h and 96h
    // from now" (used to test the AI on a later slice without re-touching
    // matches an earlier call already bet on). Defaults to 0 (the
    // original "from now" behavior).
    public async Task<List<FootballMatch>> GetUpcomingMatchesAsync(int windowHours = 24, int minHours = 0)
    {
        try
        {
            var matches = new List<FootballMatch>();
            var now = DateTime.UtcNow;
            var windowStart = now.AddHours(minHours);
            var windowEnd = now.AddHours(windowHours);

            // dateTo appears to behave as an EXCLUSIVE bound (confirmed live:
            // dateFrom=today&dateTo=tomorrow only returned today's matches).
            // Scale the fetch span with windowHours (+1 day buffer for that
            // exclusive bound) instead of a fixed +2 days - that fixed cap
            // silently dropped every match beyond 48h out regardless of what
            // windowHours actually asked for (confirmed live: windowHours=80
            // still only ever saw matches within the next 2 days). Still
            // capped at 9 days total span - this call has no `competitions`
            // filter, which the API caps at 10 days max ("Specified period
            // must not exceed 10 days").
            var requestedEnd = now.AddHours(windowHours).AddDays(1);
            var maxSpanEnd = now.AddDays(9);
            var dateToDate = requestedEnd < maxSpanEnd ? requestedEnd : maxSpanEnd;

            var dateFrom = now.ToString("yyyy-MM-dd");
            var dateTo = dateToDate.ToString("yyyy-MM-dd");
            var url = $"{_baseUrl}/matches?dateFrom={dateFrom}&dateTo={dateTo}";

            var doc = await GetAsync(url);
            if (doc == null) return matches;

            if (!doc.RootElement.TryGetProperty("matches", out var matchesArray)) return matches;

            foreach (var fixture in matchesArray.EnumerateArray())
            {
                var competitionCode = fixture.GetProperty("competition").TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
                if (competitionCode == null || !SupportedCompetitionCodes.Contains(competitionCode)) continue;

                var status = fixture.GetProperty("status").GetString();
                if (status == "FINISHED" || status == "CANCELLED" || status == "POSTPONED") continue;

                var matchTime = ParseUtcDate(fixture.GetProperty("utcDate").GetString()!);

                // SKIP if match is outside [windowStart, windowEnd] - windowStart is
                // "now" unless minHours shifts it later for a specific slice.
                if (matchTime < windowStart || matchTime > windowEnd) continue;

                var fullTime = fixture.GetProperty("score").GetProperty("fullTime");

                matches.Add(new FootballMatch
                {
                    Id = fixture.GetProperty("id").GetInt32().ToString(),
                    HomeTeam = fixture.GetProperty("homeTeam").GetProperty("name").GetString(),
                    AwayTeam = fixture.GetProperty("awayTeam").GetProperty("name").GetString(),
                    UtcDate = matchTime,
                    Status = "SCHEDULED",
                    HomeScore = GetScoreOrZero(fullTime, "home"),
                    AwayScore = GetScoreOrZero(fullTime, "away")
                });
            }

            return matches.OrderBy(m => m.UtcDate).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            return new List<FootballMatch>();
        }
    }

    // Fetches finished matches from the last N days across our 5 leagues.
    // Confirmed live: /v4/matches without a competitions filter caps the
    // dateFrom/dateTo span at 10 days ("Specified period must not exceed 10
    // days") - so daysBack > 10 is split into consecutive <=10-day chunks,
    // each its own request (cheap: football-data.org allows 10/min and a
    // 45-day seed is only 5 chunks). Used to seed TeamStats with real
    // derived numbers instead of the old hardcoded test data.
    public async Task<List<FinishedMatch>> GetFinishedMatchesAsync(int daysBack, CancellationToken ct = default, FetchDiagnostics? diag = null)
    {
        var results = new List<FinishedMatch>();
        var today = DateTime.UtcNow.Date;
        var earliestDate = today.AddDays(-daysBack);
        const int chunkSpanDays = 8; // 9-day inclusive window per request, safely under the 10-day cap

        // daysBack can now span multiple seasons (see TeamStatsSeedingService.DaysBack),
        // which means dozens of chunked requests in one run - the free tier caps at
        // 10 requests/minute, so pace them out instead of firing them back to back
        // (a burst would just start getting rate-limited partway through and silently
        // lose the tail of the window). ct is threaded through so a client that gives
        // up on the request (e.g. a curl someone Ctrl-C'd) actually stops this loop
        // instead of continuing to burn requests in the background - a second call
        // started right after would otherwise race the still-running first one and
        // both would get rate-limited into returning nothing.
        var isFirstChunk = true;

        for (var chunkFrom = earliestDate; chunkFrom <= today; chunkFrom = chunkFrom.AddDays(chunkSpanDays + 1))
        {
            ct.ThrowIfCancellationRequested();

            if (!isFirstChunk)
            {
                await Task.Delay(TimeSpan.FromSeconds(6.5), ct);
            }
            isFirstChunk = false;

            var chunkTo = chunkFrom.AddDays(chunkSpanDays);
            if (chunkTo > today) chunkTo = today;

            var dateFrom = chunkFrom.ToString("yyyy-MM-dd");
            var dateTo = chunkTo.AddDays(1).ToString("yyyy-MM-dd"); // exclusive bound - includes chunkTo itself

            if (diag != null) diag.ChunksAttempted++;

            try
            {
                var doc = await GetAsync($"{_baseUrl}/matches?dateFrom={dateFrom}&dateTo={dateTo}", ct);
                if (doc == null)
                {
                    if (diag != null) diag.ChunksNullDoc++;
                    continue;
                }
                if (!doc.RootElement.TryGetProperty("matches", out var matchesArray))
                {
                    // A 200 OK with no "matches" key is unexpected (quota message,
                    // different error shape, etc.) - GetAsync only logs on a non-2xx
                    // status, so without this we'd silently skip every chunk with
                    // zero visibility into why, which is exactly what happened.
                    if (diag != null) diag.ChunksNoMatchesKey++;
                    Console.WriteLine($"football-data.org: 200 OK but no 'matches' key for {dateFrom}..{dateTo} - body: {doc.RootElement}");
                    continue;
                }

                if (diag != null) diag.RawMatchesSeen += matchesArray.GetArrayLength();

                foreach (var fixture in matchesArray.EnumerateArray())
                {
                    var competitionCode = fixture.GetProperty("competition").TryGetProperty("code", out var codeEl) ? codeEl.GetString() : null;
                    if (competitionCode == null || !SupportedCompetitionCodes.Contains(competitionCode)) continue;

                    var status = fixture.GetProperty("status").GetString();
                    if (status != "FINISHED") continue;

                    var fullTime = fixture.GetProperty("score").GetProperty("fullTime");
                    if (!fullTime.TryGetProperty("home", out var homeEl) || homeEl.ValueKind == JsonValueKind.Null) continue;
                    if (!fullTime.TryGetProperty("away", out var awayEl) || awayEl.ValueKind == JsonValueKind.Null) continue;

                    results.Add(new FinishedMatch
                    {
                        HomeTeam = fixture.GetProperty("homeTeam").GetProperty("name").GetString() ?? "",
                        AwayTeam = fixture.GetProperty("awayTeam").GetProperty("name").GetString() ?? "",
                        HomeScore = homeEl.GetInt32(),
                        AwayScore = awayEl.GetInt32(),
                        UtcDate = ParseUtcDate(fixture.GetProperty("utcDate").GetString()!)
                    });
                }
            }
            catch (Exception ex)
            {
                if (diag != null) diag.ChunksThrew++;
                Console.WriteLine($"Error fetching finished matches for chunk {dateFrom}..{dateTo}: {ex.Message}");
            }
        }

        return results;
    }

    // Looks up the real final result of a match by its real API id. Used to
    // settle PENDING bets once a match should be over. referenceDate isn't
    // needed for the lookup itself (football-data.org has a direct
    // by-id endpoint) but is kept for interface compatibility.
    public async Task<MatchStatus?> GetMatchStatusAsync(string matchId, DateTime referenceDate)
    {
        try
        {
            var doc = await GetAsync($"{_baseUrl}/matches/{matchId}");
            if (doc == null) return null;

            var status = doc.RootElement.GetProperty("status").GetString();
            var fullTime = doc.RootElement.GetProperty("score").GetProperty("fullTime");

            return new MatchStatus
            {
                Finished = status == "FINISHED",
                HomeScore = GetScoreOrZero(fullTime, "home"),
                AwayScore = GetScoreOrZero(fullTime, "away")
            };
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

public class FinishedMatch
{
    public string HomeTeam { get; set; } = "";
    public string AwayTeam { get; set; } = "";
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
    public DateTime UtcDate { get; set; }
}

// Optional counters passed into GetFinishedMatchesAsync so a caller can
// report exactly where a 0-match result came from (API errors, a 200 with
// no "matches" key, exceptions, or the raw match count before our 5-league
// filter) directly in its own response - without needing to correlate a
// journalctl tail, which can be delayed by stdout buffering or filtered by
// too narrow a grep. This loop is sequential (not parallelized), so plain
// ints are fine, no locking needed.
public class FetchDiagnostics
{
    public int ChunksAttempted;
    public int ChunksNullDoc;
    public int ChunksNoMatchesKey;
    public int ChunksThrew;
    public int RawMatchesSeen;
}
