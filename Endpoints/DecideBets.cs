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
    // The actual per-match stats text handed to the model (ATTACKING EDGE,
    // FORM EDGE, MOMENTUM EDGE, common-opponent note...) - AnalysisUsed above
    // is only the save-loop's debug log (rejections, save counts), it never
    // carried the underlying numbers, so there was no way to check via curl
    // whether a given signal actually made it into the prompt for a specific
    // match without reading server logs.
    public string? StatsAnalysis { get; set; }
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
                    //
                    // ATTACKING EDGE used to compare raw xG head-to-head (home.xG vs
                    // away.xG), ignoring defense entirely - confirmed live on Toulouse
                    // (xG 1.35, xGA 1.35) vs Lille (xG 1.54, xGA 1.07): the 0.19 xG gap
                    // rounded down to EVEN, hiding Lille's much stronger defense (xGA
                    // 1.07 vs 1.35), the clearer signal of the two and the one the real
                    // market agreed with. Now it's each team's own attack against the
                    // OTHER team's defense - the actual matchup, not attack vs attack.
                    var homeExpected = (analysis.HomeXG + analysis.AwayXGA) / 2m;
                    var awayExpected = (analysis.AwayXG + analysis.HomeXGA) / 2m;
                    var xgEdge = homeExpected - awayExpected > 0.2m ? "HOME"
                        : awayExpected - homeExpected > 0.2m ? "AWAY" : "EVEN";
                    var formEdge = analysis.HomeFormLast5 - analysis.AwayFormLast5 > 0.3m ? "HOME"
                        : analysis.AwayFormLast5 - analysis.HomeFormLast5 > 0.3m ? "AWAY" : "EVEN";

                    // Margin-of-victory-weighted recent form + shared-opponent
                    // reasoning (per the user's own heuristic: "Monaco vient
                    // d'écraser Marseille qui a elle-même écrasé Strasbourg
                    // 4-0..."), on top of the plain win/draw/loss FORM EDGE
                    // above. Informational only for now - not a hard
                    // guardrail like ATTACKING EDGE, since a shared opponent
                    // is a noisier signal (different matchday, different
                    // context) than our own xG/xGA numbers.
                    var homeResults = await _context.TeamRecentResults
                        .Where(r => r.TeamName == match.HomeTeam)
                        .ToListAsync(ct);
                    var awayResults = await _context.TeamRecentResults
                        .Where(r => r.TeamName == match.AwayTeam)
                        .ToListAsync(ct);
                    var momentum = FormMomentumAnalyzer.Compute(match.HomeTeam ?? "", match.AwayTeam ?? "", homeResults, awayResults);

                    analysisPerMatch[match.Id ?? "unknown"] =
                        $"Home xG: {analysis.HomeXG} | Away xG: {analysis.AwayXG}\n" +
                        $"Home xGA (goals conceded): {analysis.HomeXGA} | Away xGA: {analysis.AwayXGA}\n" +
                        $"Home expected scoring vs this defense: {homeExpected:0.00} | Away expected scoring vs this defense: {awayExpected:0.00} => ATTACKING EDGE: {xgEdge}\n" +
                        $"Home form (last 5): {analysis.HomeFormLast5} | Away form (last 5): {analysis.AwayFormLast5} => FORM EDGE: {formEdge}\n" +
                        $"Home momentum (recent results, weighted by margin and recency): {momentum.HomeMomentum:0.00} | Away momentum: {momentum.AwayMomentum:0.00} => MOMENTUM EDGE: {momentum.Edge}\n" +
                        (momentum.CommonOpponentNote != null ? $"{momentum.CommonOpponentNote}\n" : "") +
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

        var currentTime = DateTime.UtcNow;

        var debugLog = new List<string>();
        var rawResponses = new List<string>();
        var savedBets = new List<Bet>();
        var savedCombos = new List<BetCombo>();

        // Same dedup/floor tracking as before, now shared ACROSS the per-match
        // loop below (a bet saved while evaluating match #2 must still be
        // visible when evaluating match #5, and the balance floor has to
        // accumulate across the whole cycle, not reset per match).
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
        // implausibly deep into the red. Bets already accepted this cycle
        // stay accepted.
        const decimal hardBalanceFloor = -20m;
        var projectedBalance = req.CurrentBalance;

        // Handles one Ollama call's worth of proposed bets end-to-end (retry,
        // JSON extraction, per-bet dedup/floor checks, saving). A local
        // function (not a separate method) so it can share existingKeys/
        // projectedBalance/savedBets/savedCombos/debugLog with the loop below
        // by closure, the same running state a single call used to mutate
        // directly - now reused for however many focused calls each match
        // gets (see the OUTCOME/GOALS split below). Returns what THIS call
        // specifically saved (a subset of savedBets/savedCombos) so the loop
        // below can merge an OUTCOME bet and a GOALS bet on the same match
        // into one combo ticket after the fact.
        async Task<(List<Bet> newBets, List<BetCombo> newCombos)> ProcessPromptAsync(string prompt, string focusLabel)
        {
            var newBets = new List<Bet>();
            var newCombos = new List<BetCombo>();

            string? responseText;
            try
            {
                responseText = await CallOllamaWithRetryAsync(prompt, focusLabel, debugLog, rawResponses, ct);
            }
            catch (Exception ex)
            {
                debugLog.Add($"[{focusLabel}] ERROR: {ex.Message}");
                return (newBets, newCombos);
            }

            if (responseText == null) return (newBets, newCombos); // no JSON array found even after a retry - already logged

            List<BetDecision>? betsArray;
            try
            {
                int start = responseText.IndexOf('[');
                int end = responseText.LastIndexOf(']');
                if (start < 0 || end <= start) return (newBets, newCombos);

                var jsonStr = responseText.Substring(start, end - start + 1);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                betsArray = JsonSerializer.Deserialize<List<BetDecision>>(jsonStr, options);
            }
            catch (Exception parseEx)
            {
                debugLog.Add($"[{focusLabel}] PARSE ERROR: {parseEx.Message}");
                return (newBets, newCombos);
            }

            if (betsArray == null || betsArray.Count == 0) return (newBets, newCombos);

            foreach (var bet in betsArray)
            {
                if (bet.Type == "COMBO")
                {
                    if (bet.Legs == null || bet.Legs.Count < 2)
                    {
                        debugLog.Add($"[{focusLabel}] COMBO REJECTED: fewer than 2 legs");
                        continue; // never falls through to the single-bet path with a null MatchId
                    }

                    if (projectedBalance - bet.Stake < hardBalanceFloor)
                    {
                        debugLog.Add($"[{focusLabel}] COMBO REJECTED: would push projected balance below {hardBalanceFloor}€ this cycle");
                        continue;
                    }

                    var combo = TryBuildCombo(bet, req.Matches, resolvedOdds, existingKeys, debugLog);
                    if (combo != null)
                    {
                        _context.BetCombos.Add(combo);
                        savedCombos.Add(combo);
                        newCombos.Add(combo);
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
                    debugLog.Add($"[{focusLabel}] BET REJECTED: no matchId at all ({bet.HomeTeam} vs {bet.AwayTeam})");
                    continue;
                }

                var effectiveMatchId = sourceMatch?.RealMatchId ?? bet.MatchId!;
                var dedupKey = (effectiveMatchId, bet.Type ?? "", bet.Selection);
                if (existingKeys.Contains(dedupKey))
                {
                    debugLog.Add($"[{focusLabel}] BET REJECTED: duplicate of an existing PENDING bet " +
                        $"({bet.Type}{(bet.Selection != null ? $" [{bet.Selection}]" : "")})");
                    continue;
                }

                // The hard "don't bet against your own edge" code guardrail that
                // used to sit here has been removed on purpose: the AI is meant to
                // be free to bet on any match with any type, including one that
                // contradicts ATTACKING EDGE, because part of what it's supposed to
                // learn from is being wrong sometimes - blocking that outright was
                // second-guessing a pick rather than catching a structural bug.
                // ATTACKING EDGE is still computed and shown to it as context in the
                // prompt; it's just no longer enforced in code. Duplicate-bet
                // prevention (above) and the balance floor (below) stay - those guard
                // against real structural/integrity issues, not against risk-taking.

                if (projectedBalance - bet.Stake < hardBalanceFloor)
                {
                    debugLog.Add($"[{focusLabel}] BET REJECTED: would push projected balance below {hardBalanceFloor}€ this cycle");
                    continue;
                }

                existingKeys.Add(dedupKey);
                projectedBalance -= bet.Stake;

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
                    Stake = bet.Stake,
                    Confidence = bet.Confidence ?? 0,
                    // Confirmed live: Mistral sometimes returns otherwise-valid JSON
                    // with "reasoning": null - the bet itself is fine (real type,
                    // stake, confidence), it just skipped writing the sentence. Never
                    // silently show a blank field on the dashboard for that - say
                    // plainly that none was given rather than leaving it looking like
                    // a rendering bug or, worse, inventing one after the fact.
                    Reasoning = string.IsNullOrWhiteSpace(bet.Reasoning)
                        ? "(l'IA n'a fourni aucune justification pour ce pari)"
                        : bet.Reasoning,
                    Result = "PENDING",
                    MatchUtcDate = sourceMatch?.UtcDate,
                    Odds = realOdds
                };
                _context.Bets.Add(dbBet);
                savedBets.Add(dbBet);
                newBets.Add(dbBet);
            }

            return (newBets, newCombos);
        }

        // If an OUTCOME call and a GOALS call on the same match each
        // independently produced exactly one single bet, the user wants
        // those shown as ONE ticket rather than two separate PENDING rows -
        // the classic "HOME_WIN + OVER_GOALS" same-match combo the AI itself
        // used to be able to propose before OUTCOME/GOALS were split into
        // separate calls (a combo needs both legs visible in the same
        // prompt, which splitting deliberately gave up - see the comment
        // above the call site). Rather than asking the model to do the
        // merging, this reassembles it in code: un-save the two standalone
        // Bet rows (harmless - nothing has hit the database yet, SaveChanges
        // only runs once at the very end) and replace them with a single
        // BetCombo carrying both as legs. If the GOALS call already built
        // its OWN 2-leg combo (two goal-total types), the outcome bet is
        // added as a third leg to that combo instead of building a new one.
        // Any other shape (only one side produced something, or the GOALS
        // side stayed empty) is left exactly as already saved.
        void MergeOutcomeAndGoalsIntoCombo(
            string shortMatchId,
            List<Bet> outcomeBets, List<BetCombo> outcomeCombos,
            List<Bet> goalsBets, List<BetCombo> goalsCombos)
        {
            if (outcomeCombos.Count > 0) return; // OUTCOME never produces its own combo, but guard anyway
            if (outcomeBets.Count != 1) return; // nothing to merge, or ambiguous - leave as-is

            var outcomeBet = outcomeBets[0];

            if (goalsCombos.Count == 1)
            {
                // GOALS already built its own 2-leg combo - fold the outcome
                // bet into it as a third leg instead of leaving it standalone.
                var combo = goalsCombos[0];
                _context.Bets.Remove(outcomeBet);
                savedBets.Remove(outcomeBet);

                // resolvedOdds is keyed by the short per-request match id (e.g. "0"),
                // not the real match id stored on the saved Bet - use shortMatchId,
                // not outcomeBet.MatchId, or this always misses and falls back to
                // the flat 2x default even when real odds exist.
                var outcomeOdds = OneXTwoFamilyTypes.Contains(outcomeBet.BetType ?? "") &&
                    resolvedOdds.TryGetValue(shortMatchId, out var o1) ? ResolveLegOdds(outcomeBet.BetType, o1) : null;
                var effectiveOutcomeOdds = outcomeOdds ?? DefaultLegOdds;

                combo.Legs.Add(new ComboLeg
                {
                    MatchId = outcomeBet.MatchId,
                    HomeTeam = outcomeBet.HomeTeam,
                    AwayTeam = outcomeBet.AwayTeam,
                    BetType = outcomeBet.BetType,
                    Selection = outcomeBet.Selection,
                    Odds = effectiveOutcomeOdds,
                    MatchUtcDate = outcomeBet.MatchUtcDate,
                    Result = "PENDING"
                });
                combo.CombinedOdds *= effectiveOutcomeOdds;
                combo.Confidence *= outcomeBet.Confidence;
                combo.Reasoning = $"{outcomeBet.Reasoning} | {combo.Reasoning}";
                // The combo's stake doesn't grow just because it picked up a
                // third leg - refund what was provisionally deducted for the
                // now-folded-in outcome bet so projectedBalance still matches
                // what's actually staked (combo.Stake alone) for the rest of
                // this cycle's remaining matches.
                projectedBalance += outcomeBet.Stake;
                debugLog.Add($"MERGED: OUTCOME bet folded into the GOALS combo for {outcomeBet.HomeTeam} vs {outcomeBet.AwayTeam}");
                return;
            }

            if (goalsBets.Count != 1) return; // GOALS produced nothing, or something ambiguous - leave outcomeBet standalone

            var goalsBet = goalsBets[0];

            // Both single bets on the same match - merge into a new 2-leg combo.
            _context.Bets.Remove(outcomeBet);
            _context.Bets.Remove(goalsBet);
            savedBets.Remove(outcomeBet);
            savedBets.Remove(goalsBet);

            // Same fix as above: resolvedOdds is keyed by the short per-request
            // match id, not the real match id saved on the Bet entities.
            decimal? LegOdds(Bet b) => OneXTwoFamilyTypes.Contains(b.BetType ?? "") &&
                resolvedOdds.TryGetValue(shortMatchId, out var o) ? ResolveLegOdds(b.BetType, o) : null;
            var outcomeLegOdds = LegOdds(outcomeBet) ?? DefaultLegOdds;
            var goalsLegOdds = LegOdds(goalsBet) ?? DefaultLegOdds;

            // Combined risk is higher than either leg alone - the smaller of
            // the two independently-decided stakes, same conservative
            // instinct as the "keep stakes small" rule the AI itself follows
            // when it builds a combo directly.
            var mergedCombo = new BetCombo
            {
                Stake = Math.Min(outcomeBet.Stake, goalsBet.Stake),
                Confidence = outcomeBet.Confidence * goalsBet.Confidence,
                Reasoning = $"{outcomeBet.Reasoning} | {goalsBet.Reasoning}",
                CombinedOdds = outcomeLegOdds * goalsLegOdds,
                Result = "PENDING",
                Legs = new List<ComboLeg>
                {
                    new ComboLeg
                    {
                        MatchId = outcomeBet.MatchId, HomeTeam = outcomeBet.HomeTeam, AwayTeam = outcomeBet.AwayTeam,
                        BetType = outcomeBet.BetType, Selection = outcomeBet.Selection, Odds = outcomeLegOdds,
                        MatchUtcDate = outcomeBet.MatchUtcDate, Result = "PENDING"
                    },
                    new ComboLeg
                    {
                        MatchId = goalsBet.MatchId, HomeTeam = goalsBet.HomeTeam, AwayTeam = goalsBet.AwayTeam,
                        BetType = goalsBet.BetType, Selection = goalsBet.Selection, Odds = goalsLegOdds,
                        MatchUtcDate = goalsBet.MatchUtcDate, Result = "PENDING"
                    }
                }
            };

            _context.BetCombos.Add(mergedCombo);
            savedCombos.Add(mergedCombo);
            // The two original stakes were both already deducted individually;
            // only mergedCombo.Stake is actually being risked now, so refund
            // the difference for the rest of this cycle's remaining matches.
            projectedBalance += outcomeBet.Stake + goalsBet.Stake - mergedCombo.Stake;
            debugLog.Add($"MERGED: OUTCOME + GOALS combined into one combo ticket for {outcomeBet.HomeTeam} vs {outcomeBet.AwayTeam}");
        }

        // TWO focused Ollama calls per match (OUTCOME, then GOALS) instead of
        // one call covering every bet type - confirmed live that even with a
        // "MANDATORY CHECK" instruction, Mistral kept defaulting to only a
        // who-wins pick and never once reached for OVER_GOALS/UNDER_GOALS/
        // BOTH_TEAMS_SCORE/HOME_OVER_GOALS/AWAY_OVER_GOALS across 8 matches in
        // a row - a buried bullet point in a long list is easy to skip. Giving
        // goal-total markets their own dedicated prompt, where they're the
        // ONLY thing being asked about, makes them impossible to skip past.
        // This also means the balance shown to the GOALS call already
        // reflects whatever the OUTCOME call just staked on the SAME match,
        // not just across different matches.
        //
        // This replaces the earlier "one call PER MATCH" design (itself a fix
        // for cross-match contamination - see BuildPromptHeader) - multi-match
        // combos were already dropped then; same-match combos are still
        // possible but now only within a single call's own bet-type family
        // (two goal-market legs, e.g. OVER_GOALS + BOTH_TEAMS_SCORE) since a
        // combo needs both legs visible in the same prompt.
        foreach (var match in req.Matches)
        {
            if (match.Id == null) continue;
            var shortMatchId = match.Id;

            var matchLabel = $"{match.HomeTeam} vs {match.AwayTeam}";
            analysisPerMatch.TryGetValue(shortMatchId, out var matchAnalysis);
            oddsPerMatch.TryGetValue(shortMatchId, out var matchOdds);
            var effectiveAnalysis = matchAnalysis ?? "Pas de données statistiques disponibles pour ce match.";
            var effectiveOdds = matchOdds ?? "Pas de cotes réelles disponibles pour ce match.";

            // Recomputed from projectedBalance (not the original req.CurrentBalance)
            // before EACH of the two calls below - confirmed live gap: with several
            // matches evaluated in sequence, later matches were still being shown
            // the SAME starting balance even after earlier ones in this exact cycle
            // had already committed stakes against it. A floor keeps this from
            // collapsing to near-zero stakes while temporarily negative purely from
            // other bets still being PENDING (they may still win and bring it back up).
            var (lowStake, medStake, highStake) = StakeTiers(projectedBalance);
            var header = BuildPromptHeader(currentTime, projectedBalance, learningNotebook, match, effectiveAnalysis, effectiveOdds);
            var outcomePrompt = BuildOutcomePrompt(header, match, lowStake, medStake, highStake);
            var (outcomeBets, outcomeCombos) = await ProcessPromptAsync(outcomePrompt, $"{matchLabel} / OUTCOME");

            (lowStake, medStake, highStake) = StakeTiers(projectedBalance);
            header = BuildPromptHeader(currentTime, projectedBalance, learningNotebook, match, effectiveAnalysis, effectiveOdds);
            var goalsPrompt = BuildGoalsPrompt(header, match, lowStake, medStake, highStake);
            var (goalsBets, goalsCombos) = await ProcessPromptAsync(goalsPrompt, $"{matchLabel} / GOALS");

            // User asked for this explicitly: when both calls land a bet on
            // the same match, show it as one combo ticket rather than two
            // separate PENDING rows - see MergeOutcomeAndGoalsIntoCombo above.
            MergeOutcomeAndGoalsIntoCombo(shortMatchId, outcomeBets, outcomeCombos, goalsBets, goalsCombos);
        }

        await _context.SaveChangesAsync(ct);

        // Report what was actually PERSISTED, not the AI's raw proposal -
        // rejections along the way (duplicate of an existing PENDING bet,
        // projected balance floor, unresolvable match id...) never make it
        // into savedBets/savedCombos, so this can't misreport a rejected bet
        // as placed.
        var bets = savedBets.Select(b => new BetDecision
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

        if (savedBets.Count > 0 || savedCombos.Count > 0)
        {
            await _httpClient.PostAsJsonAsync(
                "http://localhost:5255/api/update-learning",
                new { betId = 0, result = "PENDING" },
                cancellationToken: ct
            );
        }

        await Send.OkAsync(new DecideBetsResponse
        {
            Bets = bets,
            AiThinking = string.Join("\n---\n", rawResponses),
            AnalysisUsed = string.Join(" | ", debugLog),
            StatsAnalysis = analysisInfo
        });
    }

    // Stake sizing is tied to the real portfolio balance (already net of
    // every other PENDING bet's stake, including ones just placed earlier in
    // this very cycle) rather than fixed euro amounts, so it naturally
    // shrinks when a lot is already committed and grows when the bankroll is
    // healthy. A floor keeps this from collapsing to near-zero stakes while
    // temporarily negative purely from other bets still being PENDING (they
    // may still win and bring it back up).
    private static (decimal low, decimal med, decimal high) StakeTiers(decimal balance)
    {
        var effectiveBankroll = Math.Max(balance, 2m);
        return (
            Math.Round(effectiveBankroll * 0.05m, 2),
            Math.Round(effectiveBankroll * 0.10m, 2),
            Math.Round(effectiveBankroll * 0.15m, 2)
        );
    }

    // Calls Ollama for a single prompt, retrying once if the response
    // contains no JSON array at all. Mistral occasionally derails completely
    // and returns prose instead of the requested JSON (e.g. "Understood,
    // here's a summary of the rules...") - confirmed live. That used to be
    // swallowed silently with zero indication of why; now a snippet of the
    // actual response text is logged when it fails so this is diagnosable
    // from AnalysisUsed directly instead of requiring journalctl archaeology.
    // Returns the extracted, trimmed response text once it contains a JSON
    // array, or null if both attempts failed to produce one.
    private static async Task<string?> CallOllamaWithRetryAsync(
        string prompt, string focusLabel, List<string> debugLog, List<string> rawResponses, CancellationToken ct)
    {
        const int maxAttempts = 2;
        // Confirmed live: Ollama got OOM-killed mid-cycle by the WSL memory
        // cap (its own prompt-cache growing across many distinct per-match
        // prompts), and took ~8s to restart - every call to it during that
        // window failed with "Connection refused", silently losing that
        // match's decision for the rest of the cycle since a bare connection
        // failure wasn't retried at all. Wait it out and retry instead of
        // giving up on the first refused connection.
        const int maxConnectionRetries = 3;
        var connectionRetryDelay = TimeSpan.FromSeconds(10);
        var client = new HttpClient();
        var responseText = "";

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            HttpResponseMessage response;
            for (var connAttempt = 1; ; connAttempt++)
            {
                try
                {
                    response = await client.PostAsJsonAsync(
                        "http://localhost:11434/api/generate",
                        new { model = "mistral", prompt = prompt, stream = false },
                        cancellationToken: ct
                    );
                    break;
                }
                catch (HttpRequestException ex) when (connAttempt < maxConnectionRetries)
                {
                    debugLog.Add($"[{focusLabel}] CONNECTION ERROR (retry {connAttempt}/{maxConnectionRetries}): {ex.Message}");
                    await Task.Delay(connectionRetryDelay, ct);
                }
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            rawResponses.Add(jsonResponse);
            debugLog.Add($"[{focusLabel}] GOT RESPONSE (attempt {attempt}/{maxAttempts})");

            var doc = JsonDocument.Parse(jsonResponse);
            responseText = doc.RootElement.GetProperty("response").GetString() ?? "";
            debugLog.Add($"[{focusLabel}] RAW LENGTH: {responseText.Length}");

            responseText = responseText.Trim();
            responseText = System.Text.RegularExpressions.Regex.Unescape(responseText);
            responseText = responseText.Replace("\\n", "").Replace("  ", "");

            if (responseText.Contains('[') && responseText.LastIndexOf(']') > responseText.IndexOf('['))
            {
                return responseText;
            }

            var snippet = responseText.Length > 200 ? responseText.Substring(0, 200) + "..." : responseText;
            debugLog.Add($"[{focusLabel}] NO JSON ARRAY IN RESPONSE (attempt {attempt}/{maxAttempts}): \"{snippet}\"");
        }

        return null;
    }

    // Shared preamble for both the OUTCOME and GOALS prompts below - CURRENT
    // TIME, critical JSON-only instruction, French reasoning instruction,
    // portfolio balance, learning notebook, the match's own analysis/odds,
    // and the ATTACKING EDGE/FORM EDGE/MOMENTUM EDGE explanation. Everything
    // that differs between the two (which bet types, which worked examples)
    // is appended by the caller.
    private static string BuildPromptHeader(
        DateTime currentTime, decimal currentBalance, string learningNotebook,
        FootballMatch match, string matchAnalysis, string matchOdds)
    {
        return $@"CURRENT TIME: {currentTime:yyyy-MM-dd HH:mm:ss} UTC

⚠️ CRITICAL INSTRUCTION: You MUST respond ONLY with a valid JSON array. No explanations, no text before or after. Output starts with [ and ends with ]. An EMPTY array [] is a perfectly valid, complete answer if this match doesn't clear any threshold below - never respond with prose, a summary of these rules, or anything else instead of JSON.

⚠️ ""reasoning"" FIELD: NEVER null, NEVER empty - always a full FRENCH sentence, as a natural, human sentence a person would actually say, not a dump of the raw labels. Never just restate ""ATTACKING EDGE: AWAY, FORM EDGE: EVEN"" verbatim; translate what that means into plain French. Ground it in the real numbers for THIS match, just say it like a person explaining their pick, not a machine echoing variable names. Every bet you propose MUST have this field filled in - a bet with no reasoning is an incomplete answer.

CURRENT PORTFOLIO BALANCE: {currentBalance:F2}€ - this already accounts for every euro staked on your other currently-pending bets (including any placed earlier in this same cycle), it's what's actually available right now. Size your stake off of it.

You are an expert AI sports betting system that learns from experience. You are being asked to evaluate exactly ONE match right now - there is no other match in this decision, focus entirely on it.

{learningNotebook}

MATCH TO EVALUATE: {match.HomeTeam} vs {match.AwayTeam} [ID: {match.Id}]

MATCH ANALYSIS:
{matchAnalysis}

REAL 1X2 ODDS FROM BOOKMAKERS (for context only - these affect payout size on a winning bet, nothing else. No odds listed is perfectly fine to bet on from stats alone; do not skip it and do not treat a listed odds number as a signal about who is favored):
{matchOdds}

The analysis above already tells you the ATTACKING EDGE and FORM EDGE (HOME, AWAY, or EVEN) - the result of comparing both teams' numbers for you. ATTACKING EDGE weighs each team's own attack against the OTHER team's defense (""expected scoring vs this defense""), not just raw xG head-to-head - use it directly instead of re-deriving your own from the raw xG/xGA lines above. If ATTACKING EDGE says AWAY, the away team is the one expected to do more damage against this specific opponent, full stop. This applies to ANY bet type that leans on one team's attack, not just who-wins markets: never say a team has the attacking edge, or bet on that team's own goals (HOME_OVER_GOALS/AWAY_OVER_GOALS), when ATTACKING EDGE names the OTHER side - if you want to go against the edges, you need a specific stated reason (H2H, missing key players, fatigue) in your reasoning, not a restated version of the number that contradicts your own pick.

MOMENTUM EDGE is a third, complementary signal: unlike FORM EDGE (a flat win/draw/loss average), it weighs recent results by how BIG the win/loss was and how recent it was - a team that just crushed someone 4-0 has more momentum than one that scraped a 1-0. When an ""Adversaire commun récent"" line is present, both teams have recently played the same third team - read it like you would by hand (e.g. ""Monaco a battu Marseille 2-0, Strasbourg a perdu contre Marseille 4-0"" => Monaco is the side showing more strength against a common measuring stick). Treat MOMENTUM EDGE and the common-opponent note as supporting context that can reinforce ATTACKING EDGE or add real weight to a DRAW/upset pick when it clearly disagrees with it - it's a real signal, not just decoration, but it's noisier than ATTACKING EDGE, so it doesn't override the hard rule above.";
    }

    // OUTCOME-only prompt: who-wins / draw / double-chance markets. Kept
    // completely separate from the GOALS prompt below (see the comment above
    // the call site) so neither family of bet types has to compete for
    // attention with the other inside one long list.
    private static string BuildOutcomePrompt(
        string header, FootballMatch match, decimal lowStake, decimal medStake, decimal highStake)
    {
        return header + $@"

BET TYPES YOU CAN USE - decide purely from the xG/form/stats data above. Odds (when listed) are NOT a signal to weigh and are NOT required to bet - they only affect the payout of a bet that wins, nothing more. You are allowed to take real risks when the stats back it up - these confidence bars are deliberately low, lean toward betting when this match gives you a real read rather than skipping it.
1. HOME_WIN / AWAY_WIN: which side the stats (xG, xGA, form, H2H) favor, if confidence > 0.45
2. DRAW: if the two teams look closely matched on stats and confidence > 0.35
3. HOME_WIN_OR_DRAW / AWAY_WIN_OR_DRAW (double chance): if confidence > 0.50 for the double outcome

This call is ONLY about who wins - goal totals (OVER_GOALS, BOTH_TEAMS_SCORE, etc.) are handled in a separate call for this same match, do not mention them here.

Stake, sized off your CURRENT balance above (not a fixed amount): low confidence (0.35-0.5) ≈ {lowStake}€, medium (0.5-0.65) ≈ {medStake}€, high (0.65+) ≈ {highStake}€. If the balance is low or negative right now, stay smaller and more selective.

RESPONSE FORMAT - ONLY JSON ARRAY, NO TEXT. Zero entries ([]) if this match doesn't clear any threshold above; otherwise exactly one entry (never more than one - HOME_WIN, AWAY_WIN, DRAW, HOME_WIN_OR_DRAW and AWAY_WIN_OR_DRAW can never coexist on the same match, so pick the single best one). matchId in your response must always be exactly ""{match.Id}"" - never invent or borrow a different one.

[
  {{
    ""matchId"": ""{match.Id}"",
    ""homeTeam"": ""Équipe Domicile"",
    ""awayTeam"": ""Équipe Extérieur"",
    ""type"": ""HOME_WIN"",
    ""selection"": null,
    ""stake"": 1.0,
    ""confidence"": 0.68,
    ""reasoning"": ""Phrase en français expliquant le pari à partir des vrais chiffres ci-dessus""
  }}
]

REMEMBER: Start with [ immediately. No preamble. No markdown. Just JSON. [] is a valid, complete, correct answer.";
    }

    // GOALS-only prompt: total-goals markets. Split out on its own because,
    // even with a "MANDATORY CHECK" instruction inside the combined prompt,
    // Mistral kept defaulting to only a who-wins pick and never once reached
    // for a goals-total type across 8 real matches in a row - confirmed
    // live. Making this the ONLY thing asked about in its own call removes
    // the option to skip past it.
    private static string BuildGoalsPrompt(
        string header, FootballMatch match, decimal lowStake, decimal medStake, decimal highStake)
    {
        return header + $@"

BET TYPES YOU CAN USE - this call is ONLY about total goals, not who wins (that's handled in a separate call for this same match). Decide purely from the xG/xGA numbers above - they are ALWAYS enough to run every comparison below, so actually run them instead of returning an empty array out of habit:
1. OVER_GOALS (selection = line, e.g. ""2.5""): if combined xG (home xG + away xG) > line and confidence > 0.45
2. UNDER_GOALS (selection = line): if combined xG < line and confidence > 0.45
3. BOTH_TEAMS_SCORE: if both teams' own xG are each > 1.0 (both sides look likely to score) and confidence > 0.45
4. HOME_OVER_GOALS / AWAY_OVER_GOALS (selection = line, e.g. ""1.5""): if that team's OWN xG > line and confidence > 0.45 - this must agree with ATTACKING EDGE (don't bet a team's own goals against what ATTACKING EDGE says)

Compute combined xG and compare it to 2.5 right now using the Home xG and Away xG numbers in MATCH ANALYSIS above - if it clears the line, that's OVER_GOALS; if it's clearly under, that's UNDER_GOALS. Do the same per-team comparison against 1.5 for HOME_OVER_GOALS/AWAY_OVER_GOALS. These are real, valid bets just like a who-wins pick - propose one whenever the numbers support it, don't leave the array empty just because you're unsure which single type to prefer.

Stake, sized off your CURRENT balance above (not a fixed amount): low confidence (0.35-0.5) ≈ {lowStake}€, medium (0.5-0.65) ≈ {medStake}€, high (0.65+) ≈ {highStake}€. If the balance is low or negative right now, stay smaller and more selective.

SAME-MATCH COMBO (optional): you can combine two DIFFERENT goal-total types on this match into one combo (e.g. OVER_GOALS + BOTH_TEAMS_SCORE) when both are genuinely supported by the stats - this pays better combined than either leg alone. Never combine a type with itself.

RESPONSE FORMAT - ONLY JSON ARRAY, NO TEXT. Zero entries ([]) only if NONE of the 4 types above clear their threshold; otherwise one entry (a single bet) or, for a combo, one COMBO object with 2 legs. matchId must always be exactly ""{match.Id}"" - never invent or borrow a different one.

A single bet looks like:
[
  {{
    ""matchId"": ""{match.Id}"",
    ""homeTeam"": ""Équipe Domicile"",
    ""awayTeam"": ""Équipe Extérieur"",
    ""type"": ""OVER_GOALS"",
    ""selection"": ""2.5"",
    ""stake"": 1.0,
    ""confidence"": 0.55,
    ""reasoning"": ""Phrase en français expliquant le pari à partir des vrais chiffres ci-dessus""
  }}
]

A same-match combo looks like:
[
  {{
    ""type"": ""COMBO"",
    ""stake"": 0.5,
    ""confidence"": 0.45,
    ""reasoning"": ""Phrase en français expliquant pourquoi les deux résultats sont combinés"",
    ""legs"": [
      {{ ""matchId"": ""{match.Id}"", ""type"": ""OVER_GOALS"", ""selection"": ""2.5"" }},
      {{ ""matchId"": ""{match.Id}"", ""type"": ""BOTH_TEAMS_SCORE"" }}
    ]
  }}
]

REMEMBER: Start with [ immediately. No preamble. No markdown. Just JSON.";
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

            // The hard "don't bet against ATTACKING EDGE" combo-leg guardrail
            // that used to sit here has been removed on purpose, same as the
            // single-bet one above - the AI is free to combine any leg type
            // on any match, edge-contradicting or not.

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
