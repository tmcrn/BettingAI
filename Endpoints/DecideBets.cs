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
                var analysis = await analysisResp.Content.ReadFromJsonAsync<AnalyzeMatchResponse>(cancellationToken: ct);

                if (analysis != null)
                {
                    // Pre-compute the edge ourselves instead of handing the model raw
                    // numbers to compare - Mistral 7B has repeatedly gotten this
                    // arithmetic wrong (e.g. betting HOME_WIN while its own reasoning
                    // stated away xG was double home xG). Spelling out "favors AWAY"
                    // in plain text removes that failure mode entirely.
                    var xgEdge = analysis.HomeXG - analysis.AwayXG > 0.2m ? "HOME"
                        : analysis.AwayXG - analysis.HomeXG > 0.2m ? "AWAY" : "EVEN";
                    var formEdge = analysis.HomeFormLast5 - analysis.AwayFormLast5 > 0.3m ? "HOME"
                        : analysis.AwayFormLast5 - analysis.HomeFormLast5 > 0.3m ? "AWAY" : "EVEN";

                    analysisPerMatch[match.Id ?? "unknown"] =
                        $"Home xG: {analysis.HomeXG} | Away xG: {analysis.AwayXG} => ATTACKING EDGE: {xgEdge}\n" +
                        $"Home xGA (goals conceded): {analysis.HomeXGA} | Away xGA: {analysis.AwayXGA}\n" +
                        $"Home form (last 5): {analysis.HomeFormLast5} | Away form (last 5): {analysis.AwayFormLast5} => FORM EDGE: {formEdge}\n" +
                        $"H2H wins - Home: {analysis.HomeWinsH2H} | Away: {analysis.AwayWinsH2H}\n" +
                        $"Key factors: {analysis.AnalysisSummary}";
                }

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

        // Stake sizing is tied to the real portfolio balance (already net of
        // every other PENDING bet's stake - see AutoDecideBets) rather than
        // fixed euro amounts, so it naturally shrinks when a lot is already
        // committed and grows when the bankroll is healthy. A floor keeps
        // this from collapsing to near-zero stakes while the balance is
        // temporarily negative purely from other bets still being PENDING
        // (they may still win and bring it back up).
        var effectiveBankroll = Math.Max(req.CurrentBalance, 2m);
        var lowStake = Math.Round(effectiveBankroll * 0.05m, 2);
        var medStake = Math.Round(effectiveBankroll * 0.10m, 2);
        var highStake = Math.Round(effectiveBankroll * 0.15m, 2);

        // 🤖 PROMPT INTELLIGENT - AVEC COTES RÉELLES UNIQUEMENT
        var prompt = $@"CURRENT TIME: {currentTime:yyyy-MM-dd HH:mm:ss} UTC

⚠️ CRITICAL INSTRUCTION: You MUST respond ONLY with valid JSON array. No explanations, no text before or after. Output starts with [ and ends with ]. Any text outside JSON will break parsing.

CURRENT PORTFOLIO BALANCE: {req.CurrentBalance:F2}€ - this already accounts for every euro staked on your OTHER currently-pending bets, it's what's actually available right now, not your original bankroll. Size every stake below off of it.

You are an expert AI sports betting system that learns from experience and diversifies betting types.

" + learningNotebook + @"

DETAILED MATCH ANALYSIS WITH COMPOSITIONS:
" + analysisInfo + @"

REAL 1X2 ODDS FROM BOOKMAKERS (for context only - these affect payout size on a winning bet, nothing else. A match with no odds listed is perfectly fine to bet on from stats alone; do not skip it and do not treat a listed odds number as a signal about who is favored):
" + oddsInfo + @"

AVAILABLE MATCHES:
" + matchsInfo + @"

Each match's analysis above already tells you the ATTACKING EDGE and FORM EDGE (HOME, AWAY, or EVEN) - this is the result of comparing both teams' numbers for you. Use it directly instead of re-deriving it: if ATTACKING EDGE says AWAY, the away team is the one with the higher xG, full stop. This applies to ANY bet type that leans on one team's attack, not just who-wins markets: never say a team has the attacking edge, or bet on that team's own goals (HOME_OVER_GOALS/AWAY_OVER_GOALS), when ATTACKING EDGE names the OTHER side - if you want to go against the edges, you need a specific stated reason (H2H, missing key players, fatigue) in your reasoning, not a restated version of the number that contradicts your own pick.

BET TYPES YOU CAN USE - decide purely from the xG/form/stats data above. Odds (when listed) are NOT a signal to weigh and are NOT required to bet - they only affect the payout of a bet that wins, nothing more. Do not avoid a bet just because a match has no odds listed, and do not let a big/small odds number talk you out of a pick your stats support. You are allowed to take real risks when the stats back it up - these confidence bars are deliberately low, lean toward betting when a match gives you a real read rather than skipping it.
1. HOME_WIN / AWAY_WIN: which side your stats (xG, xGA, form, H2H) favor, if confidence > 0.45
2. DRAW: if the two teams look closely matched on stats and confidence > 0.35
3. HOME_WIN_OR_DRAW / AWAY_WIN_OR_DRAW (double chance): if confidence > 0.50 for the double outcome
4. BOTH_TEAMS_SCORE: if xGA (both teams) > 1.5 and confidence > 0.45
5. OVER_GOALS (selection = line, e.g. ""2.5""): if combined xG > line and confidence > 0.45
6. UNDER_GOALS (selection = line): if combined xG < line and confidence > 0.45
7. HOME_OVER_GOALS / AWAY_OVER_GOALS (selection = line, e.g. ""1.5""): if that team's OWN xG > line and confidence > 0.45 - this must agree with ATTACKING EDGE (e.g. don't pick HOME_OVER_GOALS when ATTACKING EDGE says AWAY)

Vary stakes by conviction, sized off your CURRENT balance above (not a fixed amount): low confidence (0.35-0.5) ≈ {lowStake}€, medium (0.5-0.65) ≈ {medStake}€, high (0.65+) ≈ {highStake}€. If the balance is low or negative right now (a lot already committed to other pending bets), stay smaller and more selective rather than piling on.

NOT AVAILABLE - do not use, no data source exists for these: PLAYER_SCORER, PLAYER_ASSIST.

COMBO BETS (paris combinés) - an actual tool to reach for, not just a technical option: when you look at this batch of matches and find 2-4 where the stats genuinely support a pick each, combining them into one combo is a legitimate way to swing for a much bigger payout than any single bet could give you - that's the whole point of a combo, and you should propose one whenever you believe the compounded risk is worth what it pays if it lands. Don't hold back on a combo just because the combined odds/probability is low; that's expected and fine, it's still a real risk you can choose to take. Mix ANY of the bet types above freely across legs - not restricted to 1X2 types.

Legs don't have to be on different matches - a SAME-MATCH combo (multiple legs on one match) is a classic move when a single match strongly supports more than one angle: e.g. HOME_WIN + that same team's HOME_OVER_GOALS line (""they win AND they score more than X"") usually pays noticeably better combined than either leg alone. The only hard rule: never put two ""who wins"" legs (HOME_WIN, AWAY_WIN, DRAW, HOME_WIN_OR_DRAW, AWAY_WIN_OR_DRAW) on the same match together - they're either contradictory or redundant with each other. Everything else can be combined with itself across matches or on the same one.

Format:
{
  ""type"": ""COMBO"",
  ""stake"": 0.5,
  ""confidence"": 0.45,
  ""reasoning"": ""..."",
  ""legs"": [
    { ""matchId"": ""0"", ""type"": ""HOME_WIN"" },
    { ""matchId"": ""0"", ""type"": ""HOME_OVER_GOALS"", ""selection"": ""2.5"" }
  ]
}
Combo confidence is the product of each leg's individual confidence - keep stakes small (0.3-0.6€) since combined risk is much higher. You can still place separate single bets on other matches in the same batch alongside a combo.

EVALUATE EVERY MATCH INDEPENDENTLY: go through each match in AVAILABLE MATCHES one at a time and judge it entirely on its own - whether match #1 got a bet has zero bearing on whether match #2, #3, etc. also deserve one. Do NOT stop scanning after finding one good bet elsewhere in the list; do NOT treat this as ""pick the single best match of the batch"". If three separate matches each clear the confidence bar for some bet type, that's three bets, not one. The only valid reason to skip a specific match is that MATCH failing every threshold above on its own stats - never because another match already got picked.

DIVERSIFY: Propose different bet types across matches when the stats support them.

RESPONSE FORMAT - ONLY JSON ARRAY, NO TEXT. One array entry per match that clears its threshold - below is an example with TWO separate matches that both qualified, not a cap of one:
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
  },
  {
    ""matchId"": ""2"",
    ""homeTeam"": ""Marseille"",
    ""awayTeam"": ""Nice"",
    ""type"": ""OVER_GOALS"",
    ""selection"": ""2.5"",
    ""stake"": 0.7,
    ""confidence"": 0.5,
    ""reasoning"": ""Combined xG 3.1 across both teams, both sides attack-heavy""
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

                        // The AI re-evaluates the same still-upcoming matches every cron cycle
                        // (a match sits in the 1h window across several 45-min cycles as kickoff
                        // approaches) with no memory of what it already bet - it kept proposing
                        // the exact same match+type, stacking stakes on an identical position
                        // several times over. Track every (matchId, betType, selection) already
                        // PENDING - existing DB rows plus whatever gets saved in this batch - and
                        // reject an exact repeat rather than trust the prompt to self-police it.
                        var existingKeys = new HashSet<(string MatchId, string BetType, string? Selection)>(
                            (await _context.Bets.Where(b => b.Result == "PENDING").Select(b => new { b.MatchId, b.BetType, b.Selection }).ToListAsync(ct))
                                .Where(b => b.MatchId != null && b.BetType != null)
                                .Select(b => (b.MatchId!, b.BetType!, b.Selection))
                        );
                        existingKeys.UnionWith(
                            (await _context.ComboLegs.Where(l => l.Result == "PENDING").Select(l => new { l.MatchId, l.BetType, l.Selection }).ToListAsync(ct))
                                .Where(l => l.MatchId != null && l.BetType != null)
                                .Select(l => (l.MatchId!, l.BetType!, l.Selection))
                        );

                        // Runaway-batch safety net, not a risk-appetite cap: nothing here
                        // second-guesses a well-reasoned bet, it just stops accepting MORE
                        // new stakes once this single cycle would have driven the balance
                        // implausibly deep into the red (e.g. a malformed response with far
                        // too many bets). Bets already accepted this cycle stay accepted.
                        const decimal hardBalanceFloor = -20m;
                        var projectedBalance = req.CurrentBalance;

                        foreach (var bet in betsArray)
                        {
                            if (bet.Type == "COMBO")
                            {
                                if (bet.Legs == null || bet.Legs.Count < 2)
                                {
                                    debugLog.Add("COMBO REJECTED: fewer than 2 legs");
                                    continue; // never falls through to the single-bet path with a null MatchId
                                }

                                if (projectedBalance - bet.Stake < hardBalanceFloor)
                                {
                                    debugLog.Add($"COMBO REJECTED: would push projected balance below {hardBalanceFloor}€ this cycle");
                                    continue;
                                }

                                var combo = TryBuildCombo(bet, req.Matches, resolvedOdds, existingKeys, debugLog);
                                if (combo != null)
                                {
                                    _context.BetCombos.Add(combo);
                                    savedCombos.Add(combo);
                                    projectedBalance -= combo.Stake;
                                    foreach (var leg in combo.Legs)
                                        if (leg.MatchId != null && leg.BetType != null) existingKeys.Add((leg.MatchId, leg.BetType, leg.Selection));
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

                            var effectiveMatchId = sourceMatch?.RealMatchId ?? bet.MatchId!;
                            var dedupKey = (effectiveMatchId, bet.Type ?? "", bet.Selection);
                            if (existingKeys.Contains(dedupKey))
                            {
                                debugLog.Add($"BET REJECTED: duplicate of an existing PENDING bet " +
                                    $"({bet.HomeTeam} vs {bet.AwayTeam}, {bet.Type}" +
                                    $"{(bet.Selection != null ? $" [{bet.Selection}]" : "")})");
                                continue;
                            }

                            if (projectedBalance - bet.Stake < hardBalanceFloor)
                            {
                                debugLog.Add($"BET REJECTED: would push projected balance below {hardBalanceFloor}€ this cycle");
                                continue;
                            }

                            existingKeys.Add(dedupKey);
                            projectedBalance -= bet.Stake;

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
                                MatchId = effectiveMatchId,
                                // Prefer our own verified team names over the AI's echo, same
                                // principle as effectiveMatchId - confirmed live: the model
                                // wrote matchId "0" (correctly resolving to the real match) but
                                // paired it with a DIFFERENT match's team names, saving a bet
                                // attached to the right match yet displaying the wrong one.
                                HomeTeam = sourceMatch?.HomeTeam ?? bet.HomeTeam,
                                AwayTeam = sourceMatch?.AwayTeam ?? bet.AwayTeam,
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

                        // Report what was actually PERSISTED, not the AI's raw proposal -
                        // betsArray still includes anything rejected along the way
                        // (duplicate of an existing PENDING bet, projected balance floor,
                        // unresolvable match id...), so echoing it back made the response
                        // claim a bet was placed even when it had just been silently
                        // rejected as a duplicate.
                        bets = savedBets.Select(b => new BetDecision
                        {
                            MatchId = b.MatchId,
                            HomeTeam = b.HomeTeam,
                            AwayTeam = b.AwayTeam,
                            Type = b.BetType,
                            Selection = b.Selection,
                            Stake = b.Stake,
                            Confidence = b.Confidence,
                            Reasoning = b.Reasoning
                        })
                        .Concat(savedCombos.Select(c => new BetDecision
                        {
                            Type = "COMBO",
                            Stake = c.Stake,
                            Confidence = c.Confidence,
                            Reasoning = c.Reasoning
                        }))
                        .ToList();
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
        HashSet<(string MatchId, string BetType, string? Selection)> existingKeys,
        List<string> debugLog)
    {
        var legs = new List<ComboLeg>();
        // matchId -> leg types already added in this combo. Same-match combos
        // are allowed (e.g. HOME_WIN + that team's own OVER_GOALS line, a
        // classic same-game multi with better combined odds than either leg
        // alone) - only reject an exact repeat, or two "who wins" legs on the
        // same match, which are either contradictory (HOME_WIN + AWAY_WIN can
        // never both happen) or redundant (HOME_WIN + HOME_WIN_OR_DRAW, the
        // second already covers the first).
        var legsByMatch = new Dictionary<string, HashSet<string>>();
        var combinedOdds = 1m;

        foreach (var legDecision in bet.Legs!)
        {
            if (legDecision.MatchId == null || legDecision.Type == null)
            {
                debugLog.Add("COMBO REJECTED: leg missing matchId or type");
                return null;
            }

            if (!legsByMatch.TryGetValue(legDecision.MatchId, out var typesForMatch))
            {
                typesForMatch = new HashSet<string>();
                legsByMatch[legDecision.MatchId] = typesForMatch;
            }

            if (!typesForMatch.Add(legDecision.Type))
            {
                debugLog.Add($"COMBO REJECTED: same type '{legDecision.Type}' proposed twice for the same match");
                return null;
            }

            if (OneXTwoFamilyTypes.Contains(legDecision.Type) && typesForMatch.Count(t => OneXTwoFamilyTypes.Contains(t)) > 1)
            {
                debugLog.Add($"COMBO REJECTED: two '1X2-family' legs (who-wins markets) on the same match (matchId='{legDecision.MatchId}')");
                return null;
            }

            var sourceMatch = matches.FirstOrDefault(m => m.Id == legDecision.MatchId);
            if (sourceMatch?.RealMatchId == null)
            {
                debugLog.Add($"COMBO REJECTED: could not resolve real match id for leg matchId='{legDecision.MatchId}'");
                return null;
            }

            // Same guard as single bets: don't let a combo re-stake a match+type
            // (+selection, for goal-line types) that's already an existing
            // PENDING bet/leg (or another leg earlier in this same batch) -
            // reject the whole combo rather than silently drop just that leg,
            // since a partial combo isn't what the AI proposed.
            if (existingKeys.Contains((sourceMatch.RealMatchId, legDecision.Type, legDecision.Selection)))
            {
                debugLog.Add($"COMBO REJECTED: leg duplicates an existing PENDING bet " +
                    $"(matchId='{legDecision.MatchId}', type='{legDecision.Type}')");
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
                Selection = legDecision.Selection,
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
