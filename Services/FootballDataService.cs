using BettingAI.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace BettingAI.Services;

public class FootballDataService
{
    private readonly HttpClient _httpClient;
    
    // Supported leagues: Ligue 1 (53), La Liga (61), Serie A (135), Bundesliga (78), Premier League (39), MLS (253)
    private static readonly List<int> SupportedLeagueIds = new() { 53, 61, 135, 78, 39, 253 };
    private static readonly Dictionary<int, string> LeagueNames = new()
    {
        { 53, "Ligue 1" },
        { 61, "La Liga" },
        { 135, "Serie A" },
        { 78, "Bundesliga" },
        { 39, "Premier League" },
        { 253, "MLS" }
    };

    public FootballDataService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<List<FootballMatch>> GetUpcomingMatchesAsync()
    {
        try
        {
            var apiKey = "1c30d5045emsh23b3584f2aa6cd3p17ed3ejsn6b9d95be77f3";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-rapidapi-host", "free-api-live-football-data.p.rapidapi.com");
            _httpClient.DefaultRequestHeaders.Add("x-rapidapi-key", apiKey);

            var response = await _httpClient.GetAsync(
                "https://free-api-live-football-data.p.rapidapi.com/football-current-live"
            );

            var content = await response.Content.ReadAsStringAsync();

            Console.WriteLine("===== API RESPONSE =====");
            Console.WriteLine(content);
            Console.WriteLine("===== END =====");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Free API Error: {response.StatusCode}");
                return new List<FootballMatch>();
            }

            var doc = JsonDocument.Parse(content);
            var matches = new List<FootballMatch>();
            var now = DateTime.UtcNow;

            if (doc.RootElement.TryGetProperty("response", out var respObj))
            {
                Console.WriteLine($"✓ Found 'response' object");

                if (respObj.TryGetProperty("live", out var liveArray))
                {
                    Console.WriteLine($"✓ Found 'live' array with {liveArray.GetArrayLength()} items");

                    foreach (var fixture in liveArray.EnumerateArray())
                    {
                        var leagueId = fixture.GetProperty("leagueId").GetInt32();
                        var homeTeam = fixture.GetProperty("home").GetProperty("name").GetString();
                        var awayTeam = fixture.GetProperty("away").GetProperty("name").GetString();

                        var leagueName = LeagueNames.ContainsKey(leagueId) ? LeagueNames[leagueId] : $"League {leagueId}";
                        Console.WriteLine($"  {leagueName} ({leagueId}): {homeTeam} vs {awayTeam}");

                        if (!SupportedLeagueIds.Contains(leagueId)) continue;

                        // 🔥 GET EXACT UTC TIME
                        var utcTimeStr = fixture.GetProperty("status")
                            .GetProperty("utcTime").GetString();
                        var matchTime = DateTime.Parse(utcTimeStr ?? DateTime.Now.ToString());

                        // 🔥 GET MATCH STATUS
                        var liveTimeShort = fixture.GetProperty("status")
                            .GetProperty("liveTime").GetProperty("short").GetString();

                        // 🔥 ONLY BET IF MATCH NOT STARTED OR WITHIN 30 MIN
                        var timeUntilKickoff = matchTime - now;
                        var isMatchStarted = fixture.GetProperty("status")
                            .GetProperty("started").GetBoolean();

                        Console.WriteLine($"    UTC: {matchTime:HH:mm:ss}");
                        Console.WriteLine($"    Status: {liveTimeShort}");
                        Console.WriteLine($"    Time until kickoff: {timeUntilKickoff.TotalMinutes:F0} min");

                        // ⚠️ SKIP if match already started
                        if (isMatchStarted)
                        {
                            Console.WriteLine($"    ❌ SKIP: Match already started");
                            continue;
                        }

                        matches.Add(new FootballMatch
                        {
                            Id = fixture.GetProperty("id").GetInt32().ToString(),
                            HomeTeam = homeTeam,
                            AwayTeam = awayTeam,
                            UtcDate = matchTime,
                            Status = liveTimeShort ?? "SCHEDULED",
                            HomeScore = fixture.GetProperty("home").GetProperty("score").GetInt32(),
                            AwayScore = fixture.GetProperty("away").GetProperty("score").GetInt32()
                        });
                    }

                    Console.WriteLine($"✓ Filtered to {matches.Count} valid matches from {SupportedLeagueIds.Count} supported leagues");
                }
            }

            return matches.OrderBy(m => m.UtcDate).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Erreur: {ex.Message}");
            return new List<FootballMatch>();
        }
    }
}