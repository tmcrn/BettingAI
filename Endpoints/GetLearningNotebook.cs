using BettingAI.Data;
using BettingAI.Models;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BettingAI.Endpoints;

public class GetLearningNotebookResponse
{
    public string? FormattedLearning { get; set; }
}

public class GetLearningNotebookEndpoint : EndpointWithoutRequest<GetLearningNotebookResponse>
{
    // Below this many settled (WIN/LOSS) bets, any specific-sounding
    // percentage or recommendation is statistical noise dressed up as a
    // finding - a handful of results can swing 0% to 100% on nothing more
    // than luck. Below the bar, say plainly that there isn't enough data yet
    // rather than stating a confident-looking "pattern" from 2-3 bets.
    private const int MinSampleForPattern = 3;
    private const int MinSampleForStrategy = 8;

    private readonly BettingContext _context;

    public GetLearningNotebookEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Get("/api/learning-notebook");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var bets = await _context.Bets.ToListAsync(cancellationToken: ct);
        var notebook = await _context.LearningNotebook.OrderByDescending(n => n.LastUpdated).FirstOrDefaultAsync(cancellationToken: ct);

        var wonBets = bets.Where(b => b.Result == "WIN").ToList();
        var lostBets = bets.Where(b => b.Result == "LOSS").ToList();
        var settledBets = wonBets.Concat(lostBets).ToList();

        // Patterns de victoire - only stated once there's a real sample,
        // never off a single lucky bet.
        var winPatterns = new List<string>();
        if (wonBets.Count >= MinSampleForPattern)
        {
            var highConfidenceWins = wonBets.Count(b => b.Confidence > 0.7m);
            winPatterns.Add($"✓ Confiance > 0.7: {(highConfidenceWins * 100 / Math.Max(1, wonBets.Count))}% des victoires (sur {wonBets.Count} paris gagnés)");

            var homeWinAttempts = bets.Count(b => b.BetType == "HOME_WIN");
            if (homeWinAttempts >= MinSampleForPattern)
            {
                var homeWins = wonBets.Count(b => b.BetType == "HOME_WIN");
                winPatterns.Add($"✓ HOME_WIN: {(homeWins * 100 / homeWinAttempts)}% de réussite (sur {homeWinAttempts} paris)");
            }
        }

        // Patterns de défaite - same discipline. The "Éviter DRAW" line used
        // to be hardcoded regardless of the actual drawLosses count computed
        // right above it (that count was calculated and then thrown away) -
        // now it only appears when DRAW bets have actually underperformed.
        var lossPatterns = new List<string>();
        if (lostBets.Count >= MinSampleForPattern)
        {
            var lowConfidenceLosses = lostBets.Count(b => b.Confidence < 0.6m);
            lossPatterns.Add($"✗ Confiance < 0.6: {(lowConfidenceLosses * 100 / Math.Max(1, lostBets.Count))}% des pertes (sur {lostBets.Count} paris perdus)");

            var drawAttempts = bets.Count(b => b.BetType == "DRAW");
            if (drawAttempts >= MinSampleForPattern)
            {
                var drawLosses = lostBets.Count(b => b.BetType == "DRAW");
                var drawLossRate = drawLosses * 100 / drawAttempts;
                if (drawLossRate >= 60)
                {
                    lossPatterns.Add($"✗ DRAW: {drawLossRate}% de pertes (sur {drawAttempts} paris) - à éviter jusqu'à nouvel ordre");
                }
            }
        }

        // STRATÉGIE RECOMMANDÉE - used to be 4 hardcoded lines, always
        // identical regardless of actual results, fed to the AI every cycle
        // as if it were learned guidance. Now genuinely derived from settled
        // results: a confidence-threshold comparison and the best/worst
        // performing bet types, each only stated with a real sample behind
        // it, with the sample size always shown so it's never mistaken for
        // more certainty than it has.
        var strategyLines = new List<string>();
        if (settledBets.Count < MinSampleForStrategy)
        {
            strategyLines.Add($"Pas encore assez de paris réglés pour une stratégie fiable ({settledBets.Count}/{MinSampleForStrategy} minimum) - continuer à parier normalement, la stratégie se construira au fil des résultats réels.");
        }
        else
        {
            var sortedByConf = settledBets.OrderBy(b => b.Confidence).ToList();
            var medianConf = sortedByConf[sortedByConf.Count / 2].Confidence;
            var aboveMedian = settledBets.Where(b => b.Confidence >= medianConf).ToList();
            var belowMedian = settledBets.Where(b => b.Confidence < medianConf).ToList();
            if (aboveMedian.Count >= MinSampleForPattern && belowMedian.Count >= MinSampleForPattern)
            {
                var aboveRate = aboveMedian.Count(b => b.Result == "WIN") * 100m / aboveMedian.Count;
                var belowRate = belowMedian.Count(b => b.Result == "WIN") * 100m / belowMedian.Count;
                if (Math.Abs(aboveRate - belowRate) >= 10m)
                {
                    strategyLines.Add(aboveRate > belowRate
                        ? $"Privilégier confiance ≥ {medianConf:0.00} ({aboveRate:0}% de réussite sur {aboveMedian.Count} paris, contre {belowRate:0}% en dessous sur {belowMedian.Count})"
                        : $"La confiance affichée n'est pas fiable pour l'instant (≥ {medianConf:0.00}: {aboveRate:0}% sur {aboveMedian.Count} vs < {medianConf:0.00}: {belowRate:0}% sur {belowMedian.Count}) - rester sélectif sur d'autres critères que la confiance seule");
                }
            }

            var byType = settledBets
                .Where(b => b.BetType != null)
                .GroupBy(b => b.BetType!)
                .Select(g => new { Type = g.Key, N = g.Count(), WinRate = g.Count(b => b.Result == "WIN") * 100m / g.Count() })
                .Where(t => t.N >= MinSampleForPattern)
                .OrderByDescending(t => t.WinRate)
                .ToList();

            if (byType.Count > 0)
            {
                var best = byType[0];
                strategyLines.Add($"Meilleur type jusqu'ici: {best.Type} ({best.WinRate:0}% de réussite sur {best.N} paris)");

                var worst = byType[^1];
                if (byType.Count > 1 && worst.WinRate < 40m)
                {
                    strategyLines.Add($"À surveiller: {worst.Type} ({worst.WinRate:0}% de réussite sur {worst.N} paris) - performance faible jusqu'ici");
                }
            }

            if (strategyLines.Count == 0)
            {
                strategyLines.Add($"{settledBets.Count} paris réglés mais aucun sous-groupe encore assez large pour une recommandation spécifique - continuer à observer.");
            }
        }

        // MODÈLE STATISTIQUE APPRIS - the actual learned model (WinPredictionService),
        // distinct from the text-based patterns above. Reports its real internal
        // state honestly: all-zero weights and SampleCount 0 mean it hasn't learned
        // anything yet, so say that plainly instead of dressing up sigmoid(0)=50%
        // as a real prediction.
        var modelWeights = await _context.LearnedModelWeights.FirstOrDefaultAsync(cancellationToken: ct);
        string modelLine;
        if (modelWeights == null || modelWeights.SampleCount == 0)
        {
            modelLine = "Pas encore de paris réglés utilisés pour l'entraînement - poids à zéro, aucune prédiction fiable pour l'instant.";
        }
        else
        {
            modelLine = $"Entraîné sur {modelWeights.SampleCount} résultat(s) réel(s) - " +
                $"biais={modelWeights.Bias:0.###}, poids[edge={modelWeights.WeightEdgeAlignment:0.###}, " +
                $"forme={modelWeights.WeightFormAlignment:0.###}, momentum={modelWeights.WeightMomentumAlignment:0.###}, " +
                $"confiance IA={modelWeights.WeightConfidence:0.###}] - dernière mise à jour {modelWeights.LastUpdated:yyyy-MM-dd HH:mm}";
        }

        var learning = $@"🧠 LEARNING NOTEBOOK - Mémoire IA

📊 STATISTIQUES GLOBALES:
- Total paris: {bets.Count}
- Gagnés: {wonBets.Count} ({(bets.Count > 0 ? (wonBets.Count * 100 / bets.Count) : 0)}%)
- Perdus: {lostBets.Count}
- Confiance moyenne: {(bets.Count > 0 ? bets.Average(b => b.Confidence) : 0):F2}

✅ PATTERNS GAGNANTS:
{string.Join("\n", winPatterns.Count > 0 ? winPatterns : new List<string> { "À découvrir..." })}

❌ PATTERNS À ÉVITER:
{string.Join("\n", lossPatterns.Count > 0 ? lossPatterns : new List<string> { "À découvrir..." })}

🎯 STRATÉGIE RECOMMANDÉE:
{string.Join("\n", strategyLines.Select(l => $"- {l}"))}

🤖 MODÈLE STATISTIQUE APPRIS (WinPredictionService):
{modelLine}

📈 DERNIÈRE MISE À JOUR: {(notebook?.LastUpdated ?? DateTime.UtcNow):yyyy-MM-dd HH:mm}
";

        await Send.OkAsync(new GetLearningNotebookResponse
        {
            FormattedLearning = learning
        });
    }
}
