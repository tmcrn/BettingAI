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

public class AutoDecideBetsRequest
{
    [QueryParam]
    public int? WindowHours { get; set; }
}

public class AutoDecideBetsEndpoint : Endpoint<AutoDecideBetsRequest, AutoDecideResponse>
{
    private readonly HttpClient _httpClient;
    private readonly OddsScraperService _scraper;
    private readonly BettingContext _db;
    private readonly DiscordNotificationService _discord;
    private readonly CycleStatusService _cycleStatus;

    public AutoDecideBetsEndpoint(HttpClient httpClient, OddsScraperService scraper, BettingContext db, DiscordNotificationService discord, CycleStatusService cycleStatus)
    {
        _httpClient = httpClient;
        _scraper = scraper;
        _db = db;
        _discord = discord;
        _cycleStatus = cycleStatus;
    }

    public override void Configure()
    {
        Post("/api/auto-decide-bets");
        AllowAnonymous();
    }

    public override async Task HandleAsync(AutoDecideBetsRequest req, CancellationToken ct)
    {
        try
        {
            Console.WriteLine("🤖 AUTO-DECIDE-BETS STARTED");

            // 1️⃣ Récupère les prochains matchs
            var windowHours = req.WindowHours ?? 24;
            var upcomingMatches = await GetUpcomingMatches(windowHours);
            if (upcomingMatches == null || upcomingMatches.Count == 0)
            {
                // No Discord notification here on purpose (an empty window is the
                // normal case most cycles, outside match hours - notifying every
                // time would be spam) - but that means this is the ONE outcome
                // with zero external visibility, which reads as "the cron is
                // broken" from the outside. CycleStatusService/GetCycleStatus
                // exists so that can be checked on demand instead of guessed at.
                _cycleStatus.Record("no_matches", 0, 0, $"Aucun match dans les {windowHours}h qui suivent");
                await Send.OkAsync(new AutoDecideResponse
                {
                    Success = false,
                    Message = "No upcoming matches found"
                });
                return;
            }

            Console.WriteLine($"✓ Found {upcomingMatches.Count} upcoming matches");

            // 2️⃣ Pour chaque match, tente de scraper les cotes - mais garde le
            // match même sans cotes réelles: DecideBets sait encore proposer
            // des paris "stats seules" (BTTS, over/under) dessus à partir de
            // TeamStats, sans avoir besoin de cotes 1X2. Exclure ces matchs
            // ici les aurait empêché d'atteindre l'IA pour ce type de pari.
            var matchesWithOdds = new List<dynamic>();
            var matchesWithRealOdds = 0;
            foreach (var match in upcomingMatches.Take(5)) // Limite à 5 matchs par cycle (assez pour permettre des combinés)
            {
                Console.WriteLine($"  Scraping odds for: {match.HomeTeam} vs {match.AwayTeam}");

                var odds = await _scraper.GetSofascoreOdds(match.HomeTeam, match.AwayTeam);
                if (odds != null) matchesWithRealOdds++;

                matchesWithOdds.Add(new
                {
                    match.Id,
                    match.HomeTeam,
                    match.AwayTeam,
                    match.UtcDate,
                    odds // null si pas encore publiées - DecideBets gère ce cas
                });
                await Task.Delay(1000); // Rate limit respectueux
            }

            Console.WriteLine($"✓ {matchesWithOdds.Count} matchs à analyser ({matchesWithRealOdds} avec cotes réelles)");

            // 3️⃣ Appelle decide-bets avec les matchs trouvés
            // Same formula as GetPortfolio: 10 + gains réglés - TOUTES les mises
            // (y compris les paris encore PENDING). L'ancien calcul ne comptait
            // que les gains réglés, donc l'IA voyait toujours ~10€ de marge même
            // avec des dizaines d'euros déjà engagés sur des paris en attente.
            var settledWinnings = (_db.Bets.Where(b => b.Result != "PENDING").Sum(b => b.Winnings) ?? 0)
                + (_db.BetCombos.Where(c => c.Result != "PENDING").Sum(c => c.Winnings) ?? 0);
            var totalStaked = _db.Bets.Sum(b => b.Stake) + _db.BetCombos.Sum(c => c.Stake);
            var balance = 10 + settledWinnings - totalStaked;

            // 🔧 UTILISER LES INDICES comme IDs
            var decideBetsPayload = new
            {
                currentBalance = balance,
                matches = matchesWithOdds.Select((m, index) => new  // ← index!
                {
                    id = index.ToString(),  // ← "0", "1", "2"... pour que l'IA le retrouve de façon fiable
                    realMatchId = (string)m.Id,  // ← vrai ID, conservé pour le règlement automatique
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
                    var betType = bet.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;

                    if (betType == "COMBO")
                    {
                        // Combos don't carry a top-level matchId (they span several
                        // matches via "legs") - already saved by decide-bets, just
                        // summarize them differently here.
                        bets.Add(new BetData
                        {
                            Match = "Combiné",
                            BetType = "COMBO",
                            Stake = bet.TryGetProperty("stake", out var s) ? s.GetDecimal() : 0,
                            Confidence = bet.TryGetProperty("confidence", out var c) ? c.GetDecimal() : 0,
                            Reasoning = bet.TryGetProperty("reasoning", out var r) ? r.GetString() : null
                        });
                        continue;
                    }

                    // 🔧 Matcher par INDEX
                    var matchIdStr = bet.TryGetProperty("matchId", out var matchIdEl) ? matchIdEl.GetString() : null;
                    if (int.TryParse(matchIdStr, out var matchIndex) && matchIndex < matchesWithOdds.Count)
                    {
                        var matchData = matchesWithOdds[matchIndex];  // ← Direct access par index

                        bets.Add(new BetData
                        {
                            Match = $"{matchData.HomeTeam} vs {matchData.AwayTeam}",
                            BetType = betType,
                            Stake = bet.GetProperty("stake").GetDecimal(),
                            Confidence = bet.GetProperty("confidence").GetDecimal(),
                            Reasoning = bet.GetProperty("reasoning").GetString()
                        });
                    }
                }
            }

            if (bets.Count == 0)
            {
                var reason = matchesWithRealOdds > 0
                    ? "Matchs analysés (dont certains avec cotes réelles), mais aucun pari ne remplissait les critères de l'IA"
                    : "Matchs analysés sur stats seules (aucune cote réelle publiée pour l'instant), mais aucun pari ne remplissait les critères de l'IA";
                await _discord.NotifyNoActionAsync(reason, upcomingMatches.Count, matchesWithRealOdds);
                _cycleStatus.Record("no_bets", upcomingMatches.Count, 0, reason);
            }
            else
            {
                _cycleStatus.Record("bets_placed", upcomingMatches.Count, bets.Count, $"{bets.Count} pari(s) placé(s)");
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
            _cycleStatus.Record("error", 0, 0, ex.Message);
            await Send.OkAsync(new AutoDecideResponse
            {
                Success = false,
                Message = ex.Message
            });
        }
    }

    private async Task<List<dynamic>?> GetUpcomingMatches(int windowHours)
    {
        try
        {
            // Appelle l'endpoint interne
            var response = await _httpClient.GetAsync($"http://localhost:5255/api/matches/upcoming?windowHours={windowHours}");

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