using BettingAI.Data;
using BettingAI.Models;
using BettingAI.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using System.Text.Json;

namespace BettingAI.Endpoints;

// "Pronos" feature - separate from the real betting flow on purpose. Family
// score-guessing pools ask for an exact scoreline per match, not a bet type/
// stake/confidence, and don't touch Bets/BetCombos or the bankroll at all;
// this never writes to the database, it only reads TeamStats/matches and
// asks Ollama for a plausible score per match.
public class PredictExactScoresRequest
{
    // football-data.org competition code - defaults to Ligue 1. See
    // FootballDataService.SupportedCompetitionCodes for the other 4.
    [QueryParam]
    public string? Competition { get; set; }

    // How many days ahead to look - a domestic round completes within a
    // week for these leagues, so 7 covers "this matchday" without needing
    // to filter by the API's own matchday number (which can be uneven
    // across postponed/rescheduled fixtures).
    [QueryParam]
    public int? WindowDays { get; set; }
}

public class ScorePrediction
{
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
    public DateTime UtcDate { get; set; }
    public int? Matchday { get; set; }
    public int? PredictedHomeScore { get; set; }
    public int? PredictedAwayScore { get; set; }
    public string? Reasoning { get; set; }
}

public class PredictExactScoresResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public List<ScorePrediction> Predictions { get; set; } = new();
    // Ready to paste straight into a group chat.
    public string? FormattedText { get; set; }
}

public class PredictExactScoresEndpoint : Endpoint<PredictExactScoresRequest, PredictExactScoresResponse>
{
    private const string DefaultCompetition = "FL1"; // Ligue 1

    private static readonly string OllamaModel = Environment.GetEnvironmentVariable("OLLAMA_MODEL") ?? "mistral";

    private readonly FootballDataService _footballData;
    private readonly BettingContext _context;

    public PredictExactScoresEndpoint(FootballDataService footballData, BettingContext context)
    {
        _footballData = footballData;
        _context = context;
    }

    public override void Configure()
    {
        Get("/api/predict-scores");
        AllowAnonymous();
    }

    public override async Task HandleAsync(PredictExactScoresRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        var competition = string.IsNullOrWhiteSpace(req.Competition) ? DefaultCompetition : req.Competition;
        var windowHours = (req.WindowDays ?? 7) * 24;

        var matches = await _footballData.GetUpcomingMatchesAsync(windowHours, 0, competition);
        if (matches.Count == 0)
        {
            await Send.OkAsync(new PredictExactScoresResponse
            {
                Success = false,
                Message = $"Aucun match trouvé pour '{competition}' dans les {req.WindowDays ?? 7} prochains jours."
            });
            return;
        }

        // TeamStats keyed by name - one query for every team in this batch,
        // not one round-trip per match.
        var teamNames = matches.SelectMany(m => new[] { m.HomeTeam, m.AwayTeam }).Where(n => n != null).Distinct().ToList();
        var statsByTeam = await _context.TeamStats
            .Where(t => t.TeamName != null && teamNames.Contains(t.TeamName))
            .ToDictionaryAsync(t => t.TeamName!, t => t, ct);

        var matchLines = matches.Select((m, i) =>
        {
            var home = statsByTeam.GetValueOrDefault(m.HomeTeam ?? "");
            var away = statsByTeam.GetValueOrDefault(m.AwayTeam ?? "");
            return $"{i}: {m.HomeTeam} (domicile) vs {m.AwayTeam} (extérieur)\n" +
                $"   {m.HomeTeam} - xG: {home?.xG.ToString() ?? "?"} | xGA: {home?.xGA.ToString() ?? "?"} | forme (5 derniers): {home?.FormLast5.ToString() ?? "?"}\n" +
                $"   {m.AwayTeam} - xG: {away?.xG.ToString() ?? "?"} | xGA: {away?.xGA.ToString() ?? "?"} | forme (5 derniers): {away?.FormLast5.ToString() ?? "?"}";
        }).ToList();

        var prompt = $@"Tu es un expert en pronostics de football. Voici {matches.Count} matchs à venir avec leurs vraies statistiques (xG = buts attendus en attaque, xGA = buts attendus encaissés en défense, forme = points moyens sur les 5 derniers matchs).

Pour CHAQUE match ci-dessous, prédis un score exact plausible (ex: 2-1), cohérent avec ces chiffres - pas un score au hasard. Réponds UNIQUEMENT avec un tableau JSON, rien avant, rien après, un objet par match dans le MÊME ORDRE que la liste, avec index correspondant:
[{{""index"": 0, ""homeScore"": 2, ""awayScore"": 1, ""reasoning"": ""phrase courte""}}, ...]

Matchs:
{string.Join("\n\n", matchLines)}";

        var responseText = await CallOllamaForScoresAsync(prompt, ct);

        var predictions = matches.Select(m => new ScorePrediction
        {
            HomeTeam = m.HomeTeam,
            AwayTeam = m.AwayTeam,
            UtcDate = m.UtcDate,
            Matchday = m.Matchday
        }).ToList();

        if (responseText == null)
        {
            await Send.OkAsync(new PredictExactScoresResponse
            {
                Success = false,
                Message = "Ollama n'a pas répondu (est-il démarré ? bouton 'Ollama' du dashboard) ou n'a pas produit de JSON exploitable après 2 tentatives.",
                Predictions = predictions
            });
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseText);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("index", out var idxEl) || !idxEl.TryGetInt32(out var idx)) continue;
                if (idx < 0 || idx >= predictions.Count) continue;

                predictions[idx].PredictedHomeScore = item.TryGetProperty("homeScore", out var h) && h.TryGetInt32(out var hs) ? hs : null;
                predictions[idx].PredictedAwayScore = item.TryGetProperty("awayScore", out var a) && a.TryGetInt32(out var aws) ? aws : null;
                predictions[idx].Reasoning = item.TryGetProperty("reasoning", out var r) ? r.GetString() : null;
            }
        }
        catch (JsonException)
        {
            await Send.OkAsync(new PredictExactScoresResponse
            {
                Success = false,
                Message = "Réponse d'Ollama pas exploitable (JSON malformé) - réessaie.",
                Predictions = predictions
            });
            return;
        }

        var matchdayLabel = predictions.Select(p => p.Matchday).Distinct().Count() == 1 && predictions[0].Matchday.HasValue
            ? $" - Journée {predictions[0].Matchday}"
            : "";

        var formatted = $"📋 Pronos Robert - {CompetitionLabel(competition)}{matchdayLabel}\n\n" +
            string.Join("\n", predictions.Select(p =>
                p.PredictedHomeScore.HasValue && p.PredictedAwayScore.HasValue
                    ? $"{p.HomeTeam} {p.PredictedHomeScore} - {p.PredictedAwayScore} {p.AwayTeam}"
                    : $"{p.HomeTeam} vs {p.AwayTeam} - (pas de prono)"));

        await Send.OkAsync(new PredictExactScoresResponse
        {
            Success = true,
            Message = $"{predictions.Count(p => p.PredictedHomeScore.HasValue)}/{predictions.Count} pronos générés.",
            Predictions = predictions,
            FormattedText = formatted
        });
    }

    private static string CompetitionLabel(string code) => code.ToUpperInvariant() switch
    {
        "FL1" => "Ligue 1",
        "PL" => "Premier League",
        "PD" => "La Liga",
        "SA" => "Serie A",
        "BL1" => "Bundesliga",
        _ => code
    };

    // Simplified sibling of DecideBets' CallOllamaWithRetryAsync - same
    // "retry once if no JSON array, retry the connection if Ollama is
    // mid-restart" discipline, but this feature never touches Bets/the
    // bankroll so it stays fully self-contained rather than sharing that
    // method (kept private/static on a different endpoint class).
    private static async Task<string?> CallOllamaForScoresAsync(string prompt, CancellationToken ct)
    {
        const int maxAttempts = 2;
        const int maxConnectionRetries = 3;
        var connectionRetryDelay = TimeSpan.FromSeconds(10);
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            HttpResponseMessage response;
            for (var connAttempt = 1; ; connAttempt++)
            {
                try
                {
                    response = await client.PostAsJsonAsync(
                        "http://localhost:11434/api/generate",
                        new { model = OllamaModel, prompt, stream = false },
                        cancellationToken: ct
                    );
                    break;
                }
                catch (HttpRequestException) when (connAttempt < maxConnectionRetries)
                {
                    await Task.Delay(connectionRetryDelay, ct);
                }
                catch (HttpRequestException)
                {
                    return null;
                }
            }

            var jsonResponse = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(jsonResponse);
            var responseText = (doc.RootElement.GetProperty("response").GetString() ?? "").Trim();

            var start = responseText.IndexOf('[');
            var end = responseText.LastIndexOf(']');
            if (start >= 0 && end > start)
            {
                return responseText.Substring(start, end - start + 1);
            }
        }

        return null;
    }
}
