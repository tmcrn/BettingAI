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
    // The AI decides every bet type purely from TeamStats/xG/form - never
    // gated on whether real odds exist. Real odds, when Sofascore has them,
    // are used ONLY to compute the actual payout on settlement (a realistic
    // portfolio number) - never to block or steer the decision itself.
    // These are the types priceable from real scraped 1X2 odds (home/draw/away);
    // everything else falls back to a flat 2x payout multiplier.
    private static readonly HashSet<string> OneXTwoFamilyTypes = new()
    {
        "HOME_WIN", "AWAY_WIN", "DRAW", "HOME_WIN_OR_DRAW", "AWAY_WIN_OR_DRAW"
    };

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

REAL 1X2 ODDS FROM BOOKMAKERS (for context only - these affect payout size on a winning bet, nothing else. A match with no odds listed is perfectly fine to bet on from stats alone; do not skip it and do not treat a listed odds number as a signal about who is favored):
" + oddsInfo + @"

AVAILABLE MATCHES:
" + matchsInfo + @"

BET TYPES YOU CAN USE - decide purely from the xG/form/stats data above. Odds (when listed) are NOT a signal to weigh and are NOT required to bet - they only affect the payout of a bet that wins, nothing more. Do not avoid a bet just because a match has no odds listed, and do not let a big/small odds number talk you out of a pick your stats support. You are allowed to take real risks when the stats back it up.
1. HOME_WIN / AWAY_WIN: which side your stats (xG, xGA, form, H2H) favor, if confidence > 0.55
2. DRAW: if the two teams look closely matched on stats and confidence > 0.40
3. HOME_WIN_OR_DRAW / AWAY_WIN_OR_DRAW (double chance): if confidence > 0.60 for the double outcome
4. BOTH_TEAMS_SCORE: if xGA (both teams) > 1.5 and confidence > 0.55
5. OVER_GOALS (selection = line, e.g. ""2.5""): if combined xG > line and confidence > 0.55
6. UNDER_GOALS (selection = line): if combined xG < line and confidence > 0.55
7. HOME_OVER_GOALS / AWAY_OVER_GOALS (selection = line, e.g. ""1.5""): if that team's xG > line and confidence > 0.55

Vary stakes by conviction: low confidence (0.4-0.55) = 0.5€, medium (0.55-0.7) = 1.0€, high (0.7+) = 1.5€.

NOT AVAILABLE - do not use, no data source exists for these: PLAYER_SCORER, PLAYER_ASSIST.

COMBO BETS (paris combinés): you may propose a combo across 2-4 DIFFERENT matches, mixing ANY of the bet types above freely based on stats - not restricted to 1X2 types. Format:
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

DIVERSIFY: Propose different bet types across matches when the stats support them. Don't force a bet on every match - it's fine to skip a match entirely if nothing qualifies.

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
    ""reasoning"": ""Home xG 1.9 vs away xG 0.8 over last 5 matches, strong home form""
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

                            // Odds are never a gate on the decision - the AI already decided
                            // purely from stats above. If real 1X2 odds happen to exist for a
                            // priceable type, resolve them ONLY so settlement can pay out a
                            // realistic amount; otherwise BetSettlementService falls back to a
                            // flat 2x multiplier. Missing odds never blocks or caps the bet.
                            decimal? realOdds = null;
                            if (bet.MatchId != null && OneXTwoFamilyTypes.Contains(bet.Type ?? "") &&
                                resolvedOdds.TryGetValue(bet.MatchId, out var o))
                            {
                                realOdds = ResolveLegOdds(bet.Type, o);
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

    // Builds a BetCombo from the AI's proposal. Legs can be any bet type -
    // the decision itself is stats-driven, not odds-gated. When a leg's
    // real 1X2 odds are resolvable it's priced for real; otherwise it falls
    // back to a flat 2x multiplier for that leg, same as a single bet with
    // no real odds. A combo is only rejected for structural reasons
    // (duplicate match, unresolvable match id), never for lacking odds.
    private const decimal DefaultLegOdds = 2m;

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
            if (legDecision.MatchId == null || legDecision.Type == null)
            {
                debugLog.Add("COMBO REJECTED: leg missing matchId or type");
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

            decimal? legOdds = null;
            if (OneXTwoFamilyTypes.Contains(legDecision.Type) &&
                resolvedOdds.TryGetValue(legDecision.MatchId, out var oddsTuple))
            {
                legOdds = ResolveLegOdds(legDecision.Type, oddsTuple);
            }
            var effectiveLegOdds = legOdds ?? DefaultLegOdds;

            combinedOdds *= effectiveLegOdds;

            legs.Add(new ComboLeg
            {
                MatchId = sourceMatch.RealMatchId,
                HomeTeam = sourceMatch.HomeTeam,
                AwayTeam = sourceMatch.AwayTeam,
                BetType = legDecision.Type,
                Odds = effectiveLegOdds,
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
