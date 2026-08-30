using FastEndpoints;
using BettingAI.Services;
using BettingAI.Data;
using System.Text.Json;

namespace BettingAI.Endpoints;

public class AutoDecideResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<BetData>? Bets { get; set; }
}

public class BetData
{
    public string? Match { get; set; }
    public string? BetType { get; set; }
    public decimal Stake { get; set; }
    public decimal Confidence { get; set; }
    public string? Reasoning { get; set; }
}

public class AutoDecideBetsEndpoint : Endpoint<EmptyRequest, AutoDecideResponse>
{
    private readonly HttpClient _httpClient;
    private readonly OddsScraperService _scraper;
    private readonly BettingContext _db;

    public AutoDecideBetsEndpoint(HttpClient httpClient, OddsScraperService scraper, BettingContext db)
    {
        _httpClient = httpClient;
        _scraper = scraper;
        _db = db;
    }

    public override void Configure()
    {
        Post("/api/auto-decide-bets");
        AllowAnonymous();
    }

    public override async Task HandleAsync(EmptyRequest req, CancellationToken ct)
    {
        try
        {
            Console.WriteLine("🤖 AUTO-DECIDE-BETS STARTED");

            // 1️⃣ Récupère les prochains matchs
            var upcomingMatches = await GetUpcomingMatches();
            if (upcomingMatches == null || upcomingMatches.Count == 0)
            {
                await Send.OkAsync(new AutoDecideResponse
                {
                    Success = false,
                    Message = "No upcoming matches found"
                });
                return;
            }

            Console.WriteLine($"✓ Found {upcomingMatches.Count} upcoming matches");

            // 2️⃣ Pour chaque match, scrape les cotes
            var matchesWithOdds = new List<dynamic>();
            foreach (var match in upcomingMatches.Take(3)) // Limite à 3 pour ce test
            {
                Console.WriteLine($"  Scraping odds for: {match.HomeTeam} vs {match.AwayTeam}");

                var odds = await _scraper.GetSofascoreOdds(match.HomeTeam, match.AwayTeam);

                if (odds != null)
                {
                    matchesWithOdds.Add(new
                    {
                        match.Id,
                        match.HomeTeam,
                        match.AwayTeam,
                        match.UtcDate,
                        odds
                    });
                    await Task.Delay(1000); // Rate limit respectueux
                }
            }

            if (matchesWithOdds.Count == 0)
            {
                await Send.OkAsync(new AutoDecideResponse
                {
                    Success = false,
                    Message = "Could not scrape odds for any matches"
                });
                return;
            }

            Console.WriteLine($"✓ Scraped odds for {matchesWithOdds.Count} matches");

            // 3️⃣ Appelle decide-bets avec les matchs trouvés
            var balance = _db.Bets.Where(b => b.Result != "PENDING").Sum(b => b.Winnings) + 10;

            // 🔧 UTILISER LES INDICES comme IDs
            var decideBetsPayload = new
            {
                currentBalance = balance,
                matches = matchesWithOdds.Select((m, index) => new  // ← index!
                {
                    id = index.ToString(),  // ← "0", "1", "2"... au lieu du vrai ID
                    homeTeam = m.HomeTeam,
                    awayTeam = m.AwayTeam,
                    utcDate = m.UtcDate
                }).ToList(),
                bettingHistory = (object?)null
            };

            var response = await _httpClient.PostAsJsonAsync(
                "http://localhost:5255/api/decide-bets",
                decideBetsPayload,
                cancellationToken: ct
            );

            if (!response.IsSuccessStatusCode)
            {
                await Send.OkAsync(new AutoDecideResponse
                {
                    Success = false,
                    Message = "Failed to get AI decision"
                });
                return;
            }

            var content = await response.Content.ReadAsStringAsync();
            Console.WriteLine("🤖 AI DECISION:");
            Console.WriteLine(content);

            // 4️⃣ Parse et retourne les paris
            var doc = JsonDocument.Parse(content);
            var bets = new List<BetData>();

            if (doc.RootElement.TryGetProperty("bets", out var betsElement))
            {
                foreach (var bet in betsElement.EnumerateArray())
                {
                    // 🔧 Matcher par INDEX
                    var matchIdStr = bet.GetProperty("matchId").GetString();
                    if (int.TryParse(matchIdStr, out var matchIndex) && matchIndex < matchesWithOdds.Count)
                    {
                        var matchData = matchesWithOdds[matchIndex];  // ← Direct access par index

                        bets.Add(new BetData
                        {
                            Match = $"{matchData.HomeTeam} vs {matchData.AwayTeam}",
                            BetType = bet.GetProperty("type").GetString(),
                            Stake = bet.GetProperty("stake").GetDecimal(),
                            Confidence = bet.GetProperty("confidence").GetDecimal(),
                            Reasoning = bet.GetProperty("reasoning").GetString()
                        });
                    }
                }
            }

            await Send.OkAsync(new AutoDecideResponse
            {
                Success = true,
                Message = $"✅ IA a décidé {bets.Count} pari(s) automatiquement !",
                Bets = bets
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            await Send.OkAsync(new AutoDecideResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
    }

    private async Task<List<dynamic>?> GetUpcomingMatches()
    {
        try
        {
            // Appelle l'endpoint interne
            var response = await _httpClient.GetAsync("http://localhost:5255/api/matches/upcoming");

            if (!response.IsSuccessStatusCode) return null;

            var content = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(content);

            var matches = new List<dynamic>();
            foreach (var match in doc.RootElement.EnumerateArray())
            {
                matches.Add(new
                {
                    Id = match.GetProperty("id").GetString(),
                    HomeTeam = match.GetProperty("homeTeam").GetString(),
                    AwayTeam = match.GetProperty("awayTeam").GetString(),
                    UtcDate = match.GetProperty("utcDate").GetString()
                });
            }

            return matches;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching matches: {ex.Message}");
            return null;
        }
    }
}