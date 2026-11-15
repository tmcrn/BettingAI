using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BettingAI.Data;
using BettingAI.Models;
using BettingAI.Services;
using FastEndpoints;
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

    // Decision log: odds fetched, matches skipped and why, bets kept/cut.
    public string? AnalysisUsed { get; set; }

    // One line per match: model vs market for its best option - lets you
    // check every decision (including the "no bet") by hand.
    public string? StatsAnalysis { get; set; }
}

// Pro-style value betting. Robert no longer "feels" a bet from the stats:
//   1. real odds for every match are fetched BEFORE deciding (The Odds API:
//      Winamax = the price we get, Pinnacle = the sharp market reference),
//   2. a Poisson / Dixon-Coles model turns each team's attack/defence into
//      a probability for every outcome (1X2, over/under 1.5/2.5/3.5),
//   3. that probability is blended with the market's own (margin removed),
//   4. a bet is only placed when probability × odds − 1 ≥ the value
//      threshold - at most one bet per match, singles only (no combos: the
//      bookmaker margin multiplies with every leg),
//   5. the stake is a fraction of Kelly, capped per bet and per cycle,
//   6. Gemini never decides: it only writes the explanation and flags any
//      contextual risk it knows of.
// Closing prices are recorded afterwards (see ClosingLineService) so CLV
// and calibration tell us within weeks whether the edge is real.
public class DecideBetsEndpoint : Endpoint<DecideBetsRequest, DecideBetsResponse>
{
    private readonly BettingContext _context;
    private readonly HttpClient _httpClient;
    private readonly DiscordNotificationService _discord;
    private readonly FootballDataService _footballDataService;
    private readonly OddsApiService _oddsApi;
    private readonly ClosingLineService _closingLine;

    public DecideBetsEndpoint(BettingContext context, HttpClient httpClient, DiscordNotificationService discord,
        FootballDataService footballDataService, OddsApiService oddsApi, ClosingLineService closingLine)
    {
        _context = context;
        _httpClient = httpClient;
        _discord = discord;
        _footballDataService = footballDataService;
        _oddsApi = oddsApi;
        _closingLine = closingLine;
    }

    public override void Configure()
    {
        Post("/api/decide-bets");
        AllowAnonymous();
    }

    private sealed record Pick(FootballMatch Match, OddsEvent Event, PoissonModel Model, ValueCandidate Candidate,
        TeamStats Home, TeamStats Away)
    {
        public decimal Stake { get; set; }
    }

    public override async Task HandleAsync(DecideBetsRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        var matches = req.Matches ?? new List<FootballMatch>();
        var log = new List<string>();
        var statsLines = new List<string>();

        if (!OddsApiService.IsConfigured)
        {
            await Send.OkAsync(new DecideBetsResponse
            {
                AnalysisUsed = "NO ODDS SOURCE: ajoute THE_ODDS_API_KEY dans .env (clé gratuite sur the-odds-api.com) - " +
                               "sans cotes réelles, aucun pari n'est possible avec la méthode value bet"
            });
            return;
        }

        // ── 1. Real odds, one call per league present in this cycle ──
        var eventsByCompetition = new Dictionary<string, List<OddsEvent>>();
        foreach (var code in matches.Select(m => m.CompetitionCode).Where(c => c != null).Distinct())
        {
            try
            {
                eventsByCompetition[code!] = await _oddsApi.GetLeagueOddsAsync(code!, ct);
            }
            catch (Exception ex)
            {
                log.Add($"ODDS ERROR {code}: {ex.Message}");
                eventsByCompetition[code!] = new();
            }
        }
        if (OddsApiService.LastQuotaInfo != null) log.Add($"Quota The Odds API: {OddsApiService.LastQuotaInfo}");

        // Free CLV update for already-placed bets of these leagues.
        var closingUpdated = await _closingLine.ApplyAsync(eventsByCompetition.Values.SelectMany(e => e), ct);
        if (closingUpdated > 0) log.Add($"CLOSING ODDS updated on {closingUpdated} pending bet(s)");

        // ── 2. Model inputs ──
        var allStats = await _context.TeamStats.ToListAsync(ct);
        var statsByName = allStats
            .Where(s => s.TeamName != null)
            .GroupBy(s => s.TeamName!)
            .ToDictionary(g => g.Key, g => g.First());
        var withGoals = allStats.Where(s => s.xG > 0).ToList();
        var averageGoals = withGoals.Count > 0 ? (double)withGoals.Average(s => s.xG) : 1.35;

        var matchesWithPendingBet = (await _context.Bets.Where(b => b.Result == "PENDING").Select(b => b.MatchId).ToListAsync(ct))
            .Concat(await _context.ComboLegs.Where(l => l.Result == "PENDING").Select(l => l.MatchId).ToListAsync(ct))
            .Where(id => id != null)
            .ToHashSet();

        var bankroll = BettingStrategy.Bankroll ?? req.CurrentBalance;
        log.Add($"Bankroll {bankroll:0.00}€ ({(BettingStrategy.Bankroll != null ? "BETTING_BANKROLL" : "solde de l'app")}), " +
                $"seuil value {BettingStrategy.MinEdge:P0}, Kelly ×{BettingStrategy.KellyFraction}, poids modèle {BettingStrategy.ModelWeight}");

        // ── 3. Best value bet per match ──
        var picks = new List<Pick>();
        foreach (var match in matches)
        {
            var label = $"{match.HomeTeam} vs {match.AwayTeam}";
            var realId = match.RealMatchId ?? match.Id;
            if (realId != null && matchesWithPendingBet.Contains(realId))
            {
                statsLines.Add($"{label}: déjà un pari en attente sur ce match");
                continue;
            }

            if (!statsByName.TryGetValue(match.HomeTeam ?? "", out var homeStats) ||
                !statsByName.TryGetValue(match.AwayTeam ?? "", out var awayStats))
            {
                statsLines.Add($"{label}: pas de stats pour une des deux équipes (promue / hors championnat suivi) - ignoré");
                continue;
            }

            var events = match.CompetitionCode != null && eventsByCompetition.TryGetValue(match.CompetitionCode, out var evs) ? evs : new();
            var ev = OddsApiService.FindEvent(match, events);
            if (ev == null)
            {
                statsLines.Add($"{label}: aucune cote trouvée chez The Odds API - ignoré");
                continue;
            }

            var model = PoissonModel.FromTeamStats(homeStats, awayStats, averageGoals)!;
            var candidates = ValueBetFinder.AllCandidates(model, ev, OddsApiService.BetBookmaker, OddsApiService.SharpBookmaker);
            if (candidates.Count == 0)
            {
                statsLines.Add($"{label}: {OddsApiService.BetBookmaker} ne cote pas ce match - ignoré");
                continue;
            }

            var best = candidates.OrderByDescending(c => c.ExpectedValue).First();
            var modelSummary = $"λ {model.LambdaHome:0.00}-{model.LambdaAway:0.00} | 1N2 modèle {model.HomeWin:P0}/{model.Draw:P0}/{model.AwayWin:P0}";
            var bestSummary = $"meilleure option {best.Label} @ {best.Odds:0.00} (modèle {best.PoissonProbability:P0}, marché {best.MarketProbability:P0}{(best.MarketFromSharp ? "" : " sans Pinnacle")}, EV {best.ExpectedValue:+0.0%;-0.0%})";

            if (!ValueBetFinder.IsValue(best))
            {
                var valueInRange = candidates.Where(ValueBetFinder.IsValue).OrderByDescending(c => c.ExpectedValue).FirstOrDefault();
                if (valueInRange == null)
                {
                    statsLines.Add($"{label}: {modelSummary} | {bestSummary} => pas de value");
                    continue;
                }
                best = valueInRange;
            }

            var stake = BettingStrategy.KellyStake(best.FinalProbability, best.Odds, bankroll);
            if (stake <= 0)
            {
                statsLines.Add($"{label}: {modelSummary} | {bestSummary} => value mais mise Kelly < {BettingStrategy.MinStake}€");
                continue;
            }

            statsLines.Add($"{label}: {modelSummary} | {bestSummary} => ✅ {stake:0.00}€");
            picks.Add(new Pick(match, ev, model, best, homeStats, awayStats) { Stake = stake });
        }

        // ── 4. Cycle exposure cap: best edges first ──
        var remaining = Math.Floor(bankroll * BettingStrategy.MaxCycleExposureShare * 10) / 10;
        var kept = new List<Pick>();
        foreach (var pick in picks.OrderByDescending(p => p.Candidate.ExpectedValue))
        {
            var stake = Math.Min(pick.Stake, remaining);
            if (stake < BettingStrategy.MinStake)
            {
                log.Add($"CUT (plafond du cycle atteint): {pick.Match.HomeTeam} vs {pick.Match.AwayTeam} {pick.Candidate.Label}");
                continue;
            }
            pick.Stake = stake;
            remaining -= stake;
            kept.Add(pick);
        }

        // ── 5. Explanation by Gemini (never changes the decision) + save ──
        var standingsCache = new Dictionary<string, List<StandingEntry>>();
        var aiOutputs = new List<string>();
        var savedBets = new List<Bet>();
        foreach (var pick in kept)
        {
            var (reasoning, aiRaw) = await ExplainAsync(pick, standingsCache, ct);
            if (aiRaw != null) aiOutputs.Add(aiRaw);

            var c = pick.Candidate;
            var bet = new Bet
            {
                MatchId = pick.Match.RealMatchId ?? pick.Match.Id!,
                HomeTeam = pick.Match.HomeTeam,
                AwayTeam = pick.Match.AwayTeam,
                HomeTeamShort = pick.Match.HomeTeamShort,
                AwayTeamShort = pick.Match.AwayTeamShort,
                HomeTeamCrest = pick.Match.HomeTeamCrest,
                AwayTeamCrest = pick.Match.AwayTeamCrest,
                BetType = c.BetType,
                Selection = c.Selection,
                Stake = pick.Stake,
                Confidence = Math.Round(c.FinalProbability, 2),
                Reasoning = reasoning,
                Result = "PENDING",
                MatchUtcDate = pick.Match.UtcDate,
                CompetitionCode = pick.Match.CompetitionCode,
                Odds = c.Odds,
                PoissonProbability = Math.Round(c.PoissonProbability, 4),
                MarketProbability = Math.Round(c.MarketProbability, 4),
                ModelProbability = Math.Round(c.FinalProbability, 4),
                ExpectedValue = Math.Round(c.ExpectedValue, 4),
                Bookmaker = OddsApiService.BetBookmaker,
                OddsEventId = pick.Event.Id,
                ClosingOdds = c.Odds,
                ClosingOddsAt = DateTime.UtcNow
            };
            _context.Bets.Add(bet);
            savedBets.Add(bet);
        }

        await _context.SaveChangesAsync(ct);
        log.Add($"SAVED {savedBets.Count} bets ({picks.Count} value bets trouvés sur {matches.Count} matchs)");

        foreach (var bet in savedBets)
        {
            await _discord.NotifyBetPlacedAsync(bet);
        }

        if (savedBets.Count > 0)
        {
            try
            {
                var updateLearningRequest = new HttpRequestMessage(System.Net.Http.HttpMethod.Post, "http://localhost:5255/api/update-learning")
                {
                    Content = JsonContent.Create(new { betId = 0, result = "PENDING" })
                };
                OwnerAuth.AttachSelfCallToken(updateLearningRequest);
                await _httpClient.SendAsync(updateLearningRequest, ct);
            }
            catch (Exception ex)
            {
                log.Add($"update-learning failed: {ex.Message}");
            }
        }

        await Send.OkAsync(new DecideBetsResponse
        {
            Bets = savedBets.Select(b => new BetDecision
            {
                MatchId = b.MatchId,
                HomeTeam = b.HomeTeam,
                AwayTeam = b.AwayTeam,
                Type = b.BetType,
                Selection = b.Selection,
                Stake = b.Stake,
                Confidence = b.Confidence,
                Reasoning = b.Reasoning
            }).ToList(),
            AiThinking = string.Join("\n---\n", aiOutputs),
            AnalysisUsed = string.Join(" | ", log),
            StatsAnalysis = string.Join("\n", statsLines)
        });
    }

    // Asks Gemini to EXPLAIN a bet the maths already decided, and to flag
    // any contextual risk it knows of. Falls back to a plain numeric
    // explanation if Gemini is unavailable - the bet stands either way.
    private async Task<(string reasoning, string? raw)> ExplainAsync(Pick pick, Dictionary<string, List<StandingEntry>> standingsCache, CancellationToken ct)
    {
        var c = pick.Candidate;
        var m = pick.Match;
        var numbers = $"📊 Modèle {c.PoissonProbability:P0} · marché {c.MarketProbability:P0} → {c.FinalProbability:P0} · cote {c.Odds:0.00} · value {c.ExpectedValue:+0.0%}";
        var fallback = $"{c.Label} : notre modèle (attaque/défense, loi de Poisson) estime cette issue plus probable que ce que la cote implique. {numbers}";

        string standings = "";
        try
        {
            if (m.CompetitionCode != null)
            {
                if (!standingsCache.TryGetValue(m.CompetitionCode, out var table))
                {
                    table = await _footballDataService.GetStandingsAsync(m.CompetitionCode);
                    standingsCache[m.CompetitionCode] = table;
                }
                var h = table.FirstOrDefault(s => s.TeamName == m.HomeTeam);
                var a = table.FirstOrDefault(s => s.TeamName == m.AwayTeam);
                if (h != null && a != null)
                    standings = $"Classement : {m.HomeTeam} {h.Position}e ({h.Points} pts), {m.AwayTeam} {a.Position}e ({a.Points} pts)\n";
            }
        }
        catch { /* standings are optional context */ }

        var (scoreH, scoreA) = pick.Model.MostLikelyScore();
        var prompt = new StringBuilder()
            .AppendLine("Tu es l'analyste d'un parieur professionnel. La décision est DÉJÀ prise par un modèle mathématique, tu ne la changes pas.")
            .AppendLine("Ton rôle : l'expliquer simplement en français, et signaler un risque contextuel concret si tu en connais un.")
            .AppendLine()
            .AppendLine($"Match : {m.HomeTeam} (domicile) vs {m.AwayTeam} - {m.CompetitionCode}, coup d'envoi {m.UtcDate:yyyy-MM-dd HH:mm} UTC")
            .AppendLine($"Pari choisi : {c.Label} à la cote {c.Odds.ToString("0.00", CultureInfo.InvariantCulture)}")
            .AppendLine($"Buts marqués/encaissés par match - {m.HomeTeam} : {pick.Home.xG}/{pick.Home.xGA}, forme {pick.Home.FormLast5}/2, {pick.Home.DaysSinceLastMatch} j de repos")
            .AppendLine($"Buts marqués/encaissés par match - {m.AwayTeam} : {pick.Away.xG}/{pick.Away.xGA}, forme {pick.Away.FormLast5}/2, {pick.Away.DaysSinceLastMatch} j de repos")
            .Append(standings)
            .AppendLine($"Buts attendus (modèle) : {pick.Model.LambdaHome:0.00} - {pick.Model.LambdaAway:0.00}, score le plus probable {scoreH}-{scoreA}")
            .AppendLine($"Probabilité modèle {c.PoissonProbability:P1}, probabilité du marché (Pinnacle sans marge) {c.MarketProbability:P1}, retenue {c.FinalProbability:P1}, espérance de gain {c.ExpectedValue:P1}")
            .AppendLine()
            .AppendLine("Tes connaissances peuvent être datées : n'invente aucune blessure ni actualité. Ne signale un risque que si tu es sûr qu'il est réel et pertinent (match sans enjeu, rotation probable avant un match européen, derby, etc.).")
            .AppendLine("Réponds UNIQUEMENT en JSON : {\"reasoning\": \"2 phrases max, en français, pourquoi ce pari a de la valeur\", \"warning\": \"risque concret en une phrase, ou null\"}")
            .ToString();

        try
        {
            var raw = await GeminiService.AskAsync(prompt, ct);
            var start = raw.IndexOf('{');
            var end = raw.LastIndexOf('}');
            if (start < 0 || end <= start) return (fallback, raw);
            using var doc = JsonDocument.Parse(raw[start..(end + 1)]);
            var reasoning = doc.RootElement.TryGetProperty("reasoning", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            var warning = doc.RootElement.TryGetProperty("warning", out var w) && w.ValueKind == JsonValueKind.String ? w.GetString() : null;
            if (string.IsNullOrWhiteSpace(reasoning)) return (fallback, raw);
            var text = $"{reasoning.Trim()} {numbers}";
            if (!string.IsNullOrWhiteSpace(warning) && !warning.Trim().Equals("null", StringComparison.OrdinalIgnoreCase))
                text += $" ⚠️ {warning.Trim()}";
            return (text, raw);
        }
        catch (Exception ex)
        {
            return (fallback, $"Gemini indisponible: {ex.Message}");
        }
    }
}
