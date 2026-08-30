using System.Text.Json;

namespace BettingAI.Services;

public class OddsScraperService
{
    private readonly HttpClient _httpClient;

    public OddsScraperService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    public async Task<Dictionary<string, decimal>?> GetSofascoreOdds(string homeTeam, string awayTeam)
    {
        try
        {
            Console.WriteLine($"🔍 Searching Sofascore for: {homeTeam} vs {awayTeam}");

            var matchId = await FindMatchId(homeTeam, awayTeam);

            if (matchId == null)
            {
                Console.WriteLine("❌ Match not found on Sofascore");
                return null;
            }

            Console.WriteLine($"✓ Found match ID: {matchId}");

            var odds = await GetMatchOdds(matchId);

            if (odds == null || odds.Count == 0)
            {
                Console.WriteLine("❌ No odds found");
                return null;
            }

            Console.WriteLine($"✓ Sofascore odds for {homeTeam} vs {awayTeam}:");
            Console.WriteLine($"  HomeWin: {odds["homeWin"]}");
            Console.WriteLine($"  Draw: {odds["draw"]}");
            Console.WriteLine($"  AwayWin: {odds["awayWin"]}");

            return odds;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Scrape error: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    private async Task<string?> FindMatchId(string homeTeam, string awayTeam)
    {
        try
        {
            // ✅ Encode correctement les espaces
            var encodedQuery = Uri.EscapeDataString($"{homeTeam} {awayTeam}");
            var searchUrl = $"https://api.sofascore.com/api/v1/search/all?q={encodedQuery}";

            Console.WriteLine($"  Calling: {searchUrl}");

            await Task.Delay(500);

            var response = await _httpClient.GetAsync(searchUrl);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"  ❌ API returned {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);

            if (!doc.RootElement.TryGetProperty("results", out var resultsElement))
            {
                Console.WriteLine("  ❌ No 'results' in response");
                return null;
            }

            foreach (var result in resultsElement.EnumerateArray())
            {
                if (!result.TryGetProperty("entity", out var entity)) continue;

                var name = entity.GetProperty("name").GetString() ?? "";

                bool hasHome = ContainsTeamName(name, homeTeam);
                bool hasAway = ContainsTeamName(name, awayTeam);

                if (hasHome && hasAway && !name.Contains("U19") && !name.Contains("Women"))
                {
                    var id = entity.GetProperty("id").GetInt64().ToString();
                    Console.WriteLine($"  ✓ MATCH FOUND: {name} (ID: {id})");
                    return id;
                }
            }

            Console.WriteLine("  ❌ No matching event found");
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Search error: {ex.Message}");
            return null;
        }
    }

    // Sofascore's event name doesn't always match the football-data team name
    // exactly (e.g. "Roma" vs "AS Roma"), so fall back to the team's most
    // distinctive word instead of requiring an exact substring match.
    private static bool ContainsTeamName(string haystack, string? teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName)) return false;

        if (haystack.Contains(teamName, StringComparison.OrdinalIgnoreCase)) return true;

        var significantWord = teamName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .OrderByDescending(w => w.Length)
            .FirstOrDefault();

        return significantWord != null && significantWord.Length > 3 &&
            haystack.Contains(significantWord, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Dictionary<string, decimal>?> GetMatchOdds(string matchId)
    {
        try
        {
            var oddsUrl = $"https://api.sofascore.com/api/v1/event/{matchId}/odds";
            Console.WriteLine($"  Calling: {oddsUrl}");

            await Task.Delay(500);

            var response = await _httpClient.GetAsync(oddsUrl);
            Console.WriteLine($"  Status: {response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"  ⚠️ No odds available yet (match pas approché)");
                return GenerateDefaultOdds();
            }

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"  Response length: {content.Length}");

            var doc = JsonDocument.Parse(content);

            var odds = new Dictionary<string, decimal>();

            if (!doc.RootElement.TryGetProperty("odds", out var oddsElement))
            {
                Console.WriteLine("  ❌ No 'odds' property");
                return GenerateDefaultOdds();
            }

            foreach (var bookmaker in oddsElement.EnumerateArray())
            {
                var name = bookmaker.GetProperty("bookmakerName").GetString();
                Console.WriteLine($"    Bookmaker: {name}");

                if (name == "1xBet" || name == "Bet365" || name == "Betfair")
                {
                    try
                    {
                        var markets = bookmaker.GetProperty("markets").EnumerateArray().FirstOrDefault();

                        odds["homeWin"] = markets.GetProperty("mainLine").GetProperty("homeWin").GetProperty("odds").GetDecimal();
                        odds["draw"] = markets.GetProperty("mainLine").GetProperty("draw").GetProperty("odds").GetDecimal();
                        odds["awayWin"] = markets.GetProperty("mainLine").GetProperty("awayWin").GetProperty("odds").GetDecimal();

                        Console.WriteLine($"    ✓ Found odds from {name}");
                        return odds;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"    ❌ Error parsing {name}: {e.Message}");
                    }
                }
            }

            return odds.Count > 0 ? odds : GenerateDefaultOdds();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  ❌ Odds error: {ex.Message}");
            return GenerateDefaultOdds();
        }
    }

    private Dictionary<string, decimal> GenerateDefaultOdds()
    {
        Console.WriteLine("  📌 Using default odds");
        return new Dictionary<string, decimal>
        {
            { "homeWin", 2.0m },
            { "draw", 3.0m },
            { "awayWin", 3.5m }
        };
    }
}