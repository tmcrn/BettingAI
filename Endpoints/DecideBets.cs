using BettingAI.Models;
using BettingAI.Data;
using BettingAI.Services;
using FastEndpoints;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class DecideBetsRequest
{
    public List<FootballMatch>? Matches { get; set; }
    public decimal CurrentBalance { get; set; } = 10;
    public string? BettingHistory { get; set; }
}

public class DecideBetsResponse
{
    public List<BetDecision> Bets { get; set; } = new();
    public string? AiThinking { get; set; }
    public string? AnalysisUsed { get; set; }
}

public class DecideBetsEndpoint : Endpoint<DecideBetsRequest, DecideBetsResponse>
{
    // Leg/bet types priced from real scraped 1X2 odds (home/draw/away).
    // Combos are restricted to these - never fabricate combined odds for a
    // market we can't price.
    private static readonly HashSet<string> RealOddsTypes = new()
    {
        "HOME_WIN", "AWAY_WIN", "DRAW", "HOME_WIN_OR_DRAW", "AWAY_WIN_OR_DRAW"
    };

    // Goal-based markets we have no real market odds for (Sofascore's scraped
    // mainLine only covers 1X2). Usable for single bets on xG/stats
    // confidence alone, but stake-capped since there's no verified edge.
    private static readonly HashSet<string> StatsOnlyTypes = new()
    {
        "BOTH_TEAMS_SCORE", "OVER_GOALS", "UNDER_GOALS", "HOME_OVER_GOALS", "AWAY_OVER_GOALS"
    };

    private const decimal StatsOnlyStakeCap = 0.5m;

    private readonly BettingContext _context;
    private readonly HttpClient _httpClient;
    private readonly DiscordNotificationService _discord;

    public DecideBetsEndpoint(BettingContext context, HttpClient httpClient, DiscordNotificationService discord)
    {
        _context = context;
        _httpClient = httpClient;
        _discord = discord;
    }

    public override void Configure()
    {
        Post("/api/decide-bets");
        AllowAnonymous();
    }

    public override async Task HandleAsync(DecideBetsRequest req, CancellationToken ct)
    {
        if (req.Matches == null || req.Matches.Count == 0)
        {
            req.Matches = new List<FootballMatch>();
        }

        var matchsInfo = string.Join("\n", req.Matches.Select(m =>
            $"- {m.HomeTeam} vs {m.AwayTeam} [ID: {m.Id}]"));

        // 🧠 RÉCUPÈRE LEARNING NOTEBOOK
        var learningResponse = await _httpClient.GetAsync("http://localhost:5255/api/learning-notebook", ct);
        var learningData = await learningResponse.Content.ReadAsStringAsync();
        var learningDoc = JsonDocument.Parse(learningData);
        var learningNotebook = learningDoc.RootElement.GetProperty("formattedLearning").GetString() ?? "";

        // 📊 RÉCUPÈRE ANALYSES DÉTAILLÉES + COMPOSITIONS
        var analysisPerMatch = new Dictionary<string, string>();
        foreach (var match in req.Matches)
        {
            try
            {
                var analysisRequest = new { matchId = match.Id, homeTeam = match.HomeTeam, awayTeam = match.AwayTeam };
                var analysisResp = await _httpClient.PostAsJsonAsync(
                    "http://localhost:5255/api/analyze-match",
                    analysisRequest,
                    cancellationToken: ct
                );
                var analysisText = await analysisResp.Content.ReadAsStringAsync();
                analysisPerMatch[match.Id ?? "unknown"] = analysisText;

                // Récupère aussi les contextes (compos)
                var context = await _context.MatchContexts
                    .FirstOrDefaultAsync(mc => mc.MatchId == match.Id, cancellationToken: ct);
                if (context != null)
                {
                    analysisPerMatch[match.Id + "_context"] =
                        $"Home Lineup: {context.HomeLineup}\nAway Lineup: {context.AwayLineup}\nMissing Home: {context.HomeMissingPlayers}\nMissing Away: {context.AwayMissingPlayers}";
                }
            }
            catch { }
        }

        var analysisInfo = string.Join("\n\n", analysisPerMatch.Select(kv =>
            $"{kv.Key}: {kv.Value}"));

        // ⭐ RÉCUPÈRE LES VRAIES COTES 1X2 (les seules qu'on a réellement)
        var oddsPerMatch = new Dictionary<string, string>();
        var resolvedOdds = new Dictionary<string, (decimal home, decimal draw, decimal away)>();
        foreach (var match in req.Matches)
        {
            try
            {
                var oddsResp = await _httpClient.PostAsJsonAsync(
                    "http://localhost:5255/api/fetch-odds",
                    new { homeTeam = match.HomeTeam, awayTeam = match.AwayTeam },
                    cancellationToken: ct
                );
                var oddsText = await oddsResp.Content.ReadAsStringAsync();
                oddsPerMatch[match.Id ?? "unknown"] = oddsText;

                var parsed = ParseOneXTwoOdds(oddsText);
                if (parsed != null && match.Id != null)
                {
                    resolvedOdds[match.Id] = parsed.Value;
                }
            }
            catch { }
        }

        var oddsInfo = string.Join("\n\n", oddsPerMatch.Select(kv =>
            $"Match {kv.Key} Odds: {kv.Value}"));

        var currentTime = DateTime.UtcNow;

        // 🤖 PROMPT INTELLIGENT - AVEC COTES RÉELLES UNIQUEMENT
        var prompt = $@"CURRENT TIME: {currentTime:yyyy-MM-dd HH:mm:ss} UTC

⚠️ CRITICAL INSTRUCTION: You MUST respond ONLY with valid JSON array. No explanations, no text before or after. Output starts with [ and ends with ]. Any text outside JSON will break parsing.

You are an expert AI sports betting system that learns from experience and diversifies betting types.

" + learningNotebook + @"

DETAILED MATCH ANALYSIS WITH COMPOSITIONS:
" + analysisInfo + @"

REAL 1X2 ODDS FROM BOOKMAKERS (the only market with verified real pricing - if a match has no odds listed here, it means real odds aren't published yet and you MUST NOT bet on it):
" + oddsInfo + @"

AVAILABLE MATCHES:
" + matchsInfo + @"

BET TYPES YOU CAN USE:

Priced with REAL odds above - calculate real EV, bet with confidence:
1. HOME_WIN / AWAY_WIN: only if confidence > 0.65 AND real odds > 1.8
2. DRAW: only if confidence > 0.40 AND real draw odds > 3.0 (draws are hard to predict, be conservative)
3. HOME_WIN_OR_DRAW / AWAY_WIN_OR_DRAW (double chance): combined implied odds = 1 / (1/mainOdds + 1/drawOdds). Use when confidence > 0.70 for the double outcome.
4. Check VALUE: Expected Value = (Confidence * Odds) - 1 must be positive. Only recommend if EV is positive.
5. Vary stakes by EV: risky (EV 5-10%) = 0.8€, medium (10-20%) = 1.0€, safe (20%+) = 1.5€

NOT priced (no real market odds available from our data source) - use ONLY the xG/stats data above, confidence-only, NO fabricated odds or EV claim, stake capped at 0.5€:
6. BOTH_TEAMS_SCORE: if xGA (both teams) > 1.5 and confidence > 0.55
7. OVER_GOALS (selection = line, e.g. ""2.5""): if combined xG > line and confidence > 0.55
8. UNDER_GOALS (selection = line): if combined xG < line and confidence > 0.55
9. HOME_OVER_GOALS / AWAY_OVER_GOALS (selection = line, e.g. ""1.5""): if that team's xG > line and confidence > 0.55

NOT AVAILABLE - do not use, no data source exists for these: PLAYER_SCORER, PLAYER_ASSIST.

COMBO BETS (paris combinés): you may propose a combo across 2-4 DIFFERENT matches, using ONLY the real-odds types above (HOME_WIN, AWAY_WIN, DRAW, HOME_WIN_OR_DRAW, AWAY_WIN_OR_DRAW) since only those can be priced for real - never combine stats-only types into a combo. Format:
{
  ""type"": ""COMBO"",
  ""stake"": 0.5,
  ""confidence"": 0.45,
  ""reasoning"": ""..."",
  ""legs"": [
    { ""matchId"": ""0"", ""type"": ""HOME_WIN"" },
    { ""matchId"": ""1"", ""type"": ""AWAY_WIN_OR_DRAW"" }
  ]
}
Combo confidence is the product of each leg's individual confidence - keep stakes small (0.3-0.6€) since combined risk is much higher.

DIVERSIFY: Propose different bet types across matches when conditions are met and have positive EV. Don't force a bet on every match - it's fine to skip a match entirely if nothing qualifies.

RESPONSE FORMAT - ONLY JSON ARRAY, NO TEXT:
[
  {
    ""matchId"": ""0"",
    ""homeTeam"": ""Rennes"",
    ""awayTeam"": ""Le Mans"",
    ""type"": ""HOME_WIN"",
    ""selection"": null,
    ""stake"": 1.0,
    ""confidence"": 0.68,
    ""reasoning"": ""Real odds 1.9, EV +6.9%""
  }
]

REMEMBER: Start with [ immediately. No preamble. No markdown. Just JSON.";

        var jsonResponse = "";
        var bets = new List<BetDecision>();
        var debugLog = new List<string>();

        try
        {
            var client = new HttpClient();
            var response = await client.PostAsJsonAsync(
                "http://localhost:11434/api/generate",
                new { model = "mistral", prompt = prompt, stream = false },
                cancellationToken: ct
            );

            jsonResponse = await response.Content.ReadAsStringAsync();
            debugLog.Add($"GOT RESPONSE");

            try
            {
                var doc = JsonDocument.Parse(jsonResponse);
                var responseText = doc.RootElement.GetProperty("response").GetString() ?? "";

                debugLog.Add($"RAW LENGTH: {responseText.Length}");

                responseText = responseText.Trim();
                responseText = System.Text.RegularExpressions.Regex.Unescape(responseText);
                responseText = responseText.Replace("\\n", "").Replace("  ", "");

                int start = responseText.IndexOf('[');
                int end = responseText.LastIndexOf(']');

                if (start >= 0 && end > start)
                {
                    var jsonStr = responseText.Substring(start, end - start + 1);
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var betsArray = JsonSerializer.Deserialize<List<BetDecision>>(jsonStr, options);

                    if (betsArray != null && betsArray.Count > 0)
                    {
                        var savedBets = new List<Bet>();
                        var savedCombos = new List<BetCombo>();

                        foreach (var bet in betsArray)
                        {
                            if (bet.Type == "COMBO")
                            {
                                if (bet.Legs == null || bet.Legs.Count < 2)
                                {
                                    debugLog.Add("COMBO REJECTED: fewer than 2 legs");
                                    continue; // never falls through to the single-bet path with a null MatchId
                                }

                                var combo = TryBuildCombo(bet, req.Matches, resolvedOdds, debugLog);
                                if (combo != null)
                                {
                                    _context.BetCombos.Add(combo);
                                    savedCombos.Add(combo);
                                }
                                continue;
                            }

                            // The AI echoes back the index-based id we sent it. Resolve it against
                            // the original request to recover the real match id + kickoff time,
                            // otherwise settlement can never look the match back up later.
                            var sourceMatch = req.Matches.FirstOrDefault(m => m.Id == bet.MatchId);
                            if (sourceMatch?.RealMatchId == null)
                            {
                                Console.WriteLine($"⚠️ Could not resolve real match id for AI matchId='{bet.MatchId}' " +
                                    $"({bet.HomeTeam} vs {bet.AwayTeam}) - this bet may never auto-settle");
                            }

                            // MatchId is a required column - a bet we truly can't attach to any
                            // match would fail the whole batch's SaveChanges, so drop it instead.
                            if (sourceMatch?.RealMatchId == null && bet.MatchId == null)
                            {
                                debugLog.Add($"BET REJECTED: no matchId at all ({bet.HomeTeam} vs {bet.AwayTeam})");
                                continue;
                            }

                            var stake = bet.Stake;
                            decimal? realOdds = null;

                            if (RealOddsTypes.Contains(bet.Type ?? "") && bet.MatchId != null && resolvedOdds.TryGetValue(bet.MatchId, out var o))
                            {
                                realOdds = ResolveLegOdds(bet.Type, o);
                            }
                            else if (StatsOnlyTypes.Contains(bet.Type ?? ""))
                            {
                                // No real market odds exist for this type - cap exposure regardless
                                // of what the AI proposed as a safety net.
                                stake = Math.Min(stake, StatsOnlyStakeCap);
                            }

                            var dbBet = new Bet
                            {
                                MatchId = sourceMatch?.RealMatchId ?? bet.MatchId,
                                HomeTeam = bet.HomeTeam,
                                AwayTeam = bet.AwayTeam,
                                BetType = bet.Type,
                                Selection = bet.Selection,
                                Stake = stake,
                                Confidence = bet.Confidence ?? 0,
                                Reasoning = bet.Reasoning,
                                Result = "PENDING",
                                MatchUtcDate = sourceMatch?.UtcDate,
                                Odds = realOdds
                            };
                            _context.Bets.Add(dbBet);
                            savedBets.Add(dbBet);
                        }
                        await _context.SaveChangesAsync(ct);
                        bets = betsArray;
                        debugLog.Add($"SAVED {savedBets.Count} bets, {savedCombos.Count} combos");

                        foreach (var savedBet in savedBets)
                        {
                            await _discord.NotifyBetPlacedAsync(savedBet);
                        }
                        foreach (var savedCombo in savedCombos)
                        {
                            await _discord.NotifyComboPlacedAsync(savedCombo);
                        }

                        // Update learning
                        await _httpClient.PostAsJsonAsync(
                            "http://localhost:5255/api/update-learning",
                            new { betId = 0, result = "PENDING" },
                            cancellationToken: ct
                        );
                    }
                }
            }
            catch (Exception parseEx)
            {
                debugLog.Add($"PARSE ERROR: {parseEx.Message}");
            }
        }
        catch (Exception ex)
        {
            debugLog.Add($"ERROR: {ex.Message}");
        }

        await Send.OkAsync(new DecideBetsResponse
        {
            Bets = bets,
            AiThinking = jsonResponse,
            AnalysisUsed = string.Join(" | ", debugLog)
        });
    }

    private static (decimal home, decimal draw, decimal away)? ParseOneXTwoOdds(string? oddsJson)
    {
        if (string.IsNullOrWhiteSpace(oddsJson)) return null;
        try
        {
            using var doc = JsonDocument.Parse(oddsJson);
            if (!doc.RootElement.TryGetProperty("success", out var successEl) || !successEl.GetBoolean()) return null;
            if (!doc.RootElement.TryGetProperty("odds", out var odds)) return null;

            return (
                odds.GetProperty("homeWin").GetDecimal(),
                odds.GetProperty("draw").GetDecimal(),
                odds.GetProperty("awayWin").GetDecimal()
            );
        }
        catch
        {
            return null;
        }
    }

    private static decimal? ResolveLegOdds(string? betType, (decimal home, decimal draw, decimal away) odds)
    {
        return betType switch
        {
            "HOME_WIN" => odds.home,
            "AWAY_WIN" => odds.away,
            "DRAW" => odds.draw,
            "HOME_WIN_OR_DRAW" => 1m / (1m / odds.home + 1m / odds.draw),
            "AWAY_WIN_OR_DRAW" => 1m / (1m / odds.away + 1m / odds.draw),
            _ => null
        };
    }

    // Builds a BetCombo from the AI's proposal, resolving each leg against
    // real scraped odds. Returns null (rejecting the whole combo) rather
    // than fabricating a leg's odds when it can't be verified - same
    // principle as single bets: no priced market, no bet.
    private static BetCombo? TryBuildCombo(
        BetDecision bet,
        List<FootballMatch> matches,
        Dictionary<string, (decimal home, decimal draw, decimal away)> resolvedOdds,
        List<string> debugLog)
    {
        var legs = new List<ComboLeg>();
        var seenMatchIds = new HashSet<string>();
        var combinedOdds = 1m;

        foreach (var legDecision in bet.Legs!)
        {
            if (legDecision.MatchId == null || legDecision.Type == null || !RealOddsTypes.Contains(legDecision.Type))
            {
                debugLog.Add($"COMBO REJECTED: leg has unsupported type '{legDecision.Type}'");
                return null;
            }

            if (!seenMatchIds.Add(legDecision.MatchId))
            {
                debugLog.Add("COMBO REJECTED: two legs on the same match");
                return null;
            }

            var sourceMatch = matches.FirstOrDefault(m => m.Id == legDecision.MatchId);
            if (sourceMatch?.RealMatchId == null)
            {
                debugLog.Add($"COMBO REJECTED: could not resolve real match id for leg matchId='{legDecision.MatchId}'");
                return null;
            }

            if (!resolvedOdds.TryGetValue(legDecision.MatchId, out var oddsTuple))
            {
                debugLog.Add($"COMBO REJECTED: no real odds available for leg matchId='{legDecision.MatchId}'");
                return null;
            }

            var legOdds = ResolveLegOdds(legDecision.Type, oddsTuple);
            if (legOdds == null)
            {
                debugLog.Add($"COMBO REJECTED: could not price leg type '{legDecision.Type}'");
                return null;
            }

            combinedOdds *= legOdds.Value;

            legs.Add(new ComboLeg
            {
                MatchId = sourceMatch.RealMatchId,
                HomeTeam = sourceMatch.HomeTeam,
                AwayTeam = sourceMatch.AwayTeam,
                BetType = legDecision.Type,
                Odds = legOdds.Value,
                MatchUtcDate = sourceMatch.UtcDate,
                Result = "PENDING"
            });
        }

        return new BetCombo
        {
            Stake = bet.Stake,
            Confidence = bet.Confidence ?? 0,
            Reasoning = bet.Reasoning,
            CombinedOdds = combinedOdds,
            Result = "PENDING",
            Legs = legs
        };
    }
}
