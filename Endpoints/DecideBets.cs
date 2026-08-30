using BettingAI.Models;
using BettingAI.Data;
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
    private readonly BettingContext _context;
    private readonly HttpClient _httpClient;

    public DecideBetsEndpoint(BettingContext context, HttpClient httpClient)
    {
        _context = context;
        _httpClient = httpClient;
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

        // ⭐ RÉCUPÈRE LES COTES (AJOUTE ICI)
        var oddsPerMatch = new Dictionary<string, string>();
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
            }
            catch { }
        }

        var oddsInfo = string.Join("\n\n", oddsPerMatch.Select(kv =>
            $"Match {kv.Key} Odds: {kv.Value}"));

        var currentTime = DateTime.UtcNow;


        // 🤖 PROMPT INTELLIGENT - AVEC COTES
        var prompt = $@"CURRENT TIME: {currentTime:yyyy-MM-dd HH:mm:ss} UTC
        
        ⚠️ CRITICAL INSTRUCTION: You MUST respond ONLY with valid JSON array. No explanations, no text before or after. Output starts with [ and ends with ]. Any text outside JSON will break parsing.

You are an expert AI sports betting system that learns from experience and diversifies betting types.

" + learningNotebook + @"

DETAILED MATCH ANALYSIS WITH COMPOSITIONS:
" + analysisInfo + @"

REAL-TIME ODDS FROM BOOKMAKERS:
" + oddsInfo + @"

AVAILABLE BETS TODAY:
" + matchsInfo + @"

FLEXIBLE DECISION RULES:
1. HOME_WIN/AWAY_WIN: Only if confidence > 0.65 AND odds > 1.8
2. BOTH_TEAMS_SCORE: If xGA > 1.5 and confidence > 0.55 AND odds available
3. OVER_GOALS 2.5: If combined xG > 2.5 and confidence > 0.55 AND odds available
4. UNDER_GOALS 2.5: If combined xG < 2.2 and confidence > 0.55 AND odds available
5. Check VALUE: Expected Value = (Confidence * Odds) - 1 must be positive
6. Always check compositions 30min before match

CALCULATE VALUE:
- For BOTH_TEAMS_SCORE at odds 1.85 with confidence 0.58: EV = (0.58 * 1.85) - 1 = 0.073 = +7.3% value ✓
- Only recommend if EV is positive (profit expected)

DIVERSIFY: Propose 2-3 different bet types per match if conditions are met and have positive EV.
Vary stakes: risky bets (EV 5-10%) = 0.8€, medium (10-20%) = 1.0€, safe (20%+) = 1.5€

RESPONSE FORMAT - ONLY JSON ARRAY, NO TEXT:
[
  {
    ""matchId"": ""0"",
    ""homeTeam"": ""Rennes"",
    ""awayTeam"": ""Le Mans"",
    ""type"": ""BOTH_TEAMS_SCORE"",
    ""selection"": null,
    ""stake"": 0.8,
    ""confidence"": 0.58,
    ""reasoning"": ""xGA 1.8 > 1.5, EV +7.3% at odds 1.85""
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
                        foreach (var bet in betsArray)
                        {
                            var dbBet = new Bet
                            {
                                MatchId = bet.MatchId,
                                HomeTeam = bet.HomeTeam,
                                AwayTeam = bet.AwayTeam,
                                BetType = bet.Type,
                                Selection = bet.Selection,
                                Stake = bet.Stake,
                                Confidence = bet.Confidence ?? 0,
                                Reasoning = bet.Reasoning,
                                Result = "PENDING"
                            };
                            _context.Bets.Add(dbBet);
                        }
                        await _context.SaveChangesAsync(ct);
                        bets = betsArray;
                        debugLog.Add($"SAVED {bets.Count} bets");

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
}