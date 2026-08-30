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

            var matches = new List<FootballMatch>();
            var now = DateTime.UtcNow;

            // Fetch matches for today + tomorrow (24h window)
            var dates = new[] { now.ToString("yyyyMMdd"), now.AddDays(1).ToString("yyyyMMdd") };

            foreach (var date in dates)
            {
                var response = await _httpClient.GetAsync(
                    $"https://free-api-live-football-data.p.rapidapi.com/football-get-matches-by-date?date={date}"
                );

                var content = await response.Content.ReadAsStringAsync();

                Console.WriteLine($"===== API RESPONSE ({date}) =====");
                Console.WriteLine(content.Substring(0, Math.Min(200, content.Length)));
                Console.WriteLine("===== END =====");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Free API Error: {response.StatusCode}");
                    continue;
                }

                var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("response", out var respObj))
                {
                    Console.WriteLine($"✓ Found 'response' object for {date}");

                    if (respObj.TryGetProperty("matches", out var matchesArray))
                    {
                        Console.WriteLine($"✓ Found 'matches' array with {matchesArray.GetArrayLength()} items");

                        foreach (var fixture in matchesArray.EnumerateArray())
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
                            var statusObj = fixture.GetProperty("status");
                            var isFinished = statusObj.GetProperty("finished").GetBoolean();

                            Console.WriteLine($"    UTC: {matchTime:HH:mm:ss}");
                            Console.WriteLine($"    Status: {(isFinished ? "FINISHED" : "SCHEDULED")}");

                            // ⚠️ SKIP if match already finished
                            if (isFinished)
                            {
                                Console.WriteLine($"    ❌ SKIP: Match finished");
                                continue;
                            }

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

                        Console.WriteLine($"✓ Processed {date}: found matches from supported leagues");
                    }
                }
            }

            Console.WriteLine($"✓ Total valid matches from 24h window: {matches.Count}");
            return matches.OrderBy(m => m.UtcDate).ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Erreur: {ex.Message}");
            return new List<FootballMatch>();
        }
    }
}
