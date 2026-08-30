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

        // Patterns de victoire
        var winPatterns = new List<string>();
        if (wonBets.Count > 0)
        {
            var highConfidenceWins = wonBets.Where(b => b.Confidence > 0.7m).Count();
            winPatterns.Add($"✓ Confiance > 0.7: {(highConfidenceWins * 100 / Math.Max(1, wonBets.Count))}% victoires");

            var homeWins = wonBets.Count(b => b.BetType == "HOME_WIN");
            winPatterns.Add($"✓ HOME_WIN: {(homeWins * 100 / Math.Max(1, bets.Count(b => b.BetType == "HOME_WIN")))}% de réussite");
        }

        // Patterns de défaite
        var lossPatterns = new List<string>();
        if (lostBets.Count > 0)
        {
            var lowConfidenceLosses = lostBets.Where(b => b.Confidence < 0.6m).Count();
            lossPatterns.Add($"✗ Confiance < 0.6: {(lowConfidenceLosses * 100 / Math.Max(1, lostBets.Count))}% pertes");

            var drawLosses = lostBets.Count(b => b.BetType == "DRAW");
            lossPatterns.Add($"✗ Éviter DRAW sur petites équipes");
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
- Parier que si confiance > 0.65
- Favoriser HOME_WIN avec xG > 2.0
- Éviter DRAW sur petites équipes
- Vérifier compositions 30min avant

📈 DERNIÈRE MISE À JOUR: {(notebook?.LastUpdated ?? DateTime.UtcNow):yyyy-MM-dd HH:mm}
";

        await Send.OkAsync(new GetLearningNotebookResponse
        {
            FormattedLearning = learning
        });
    }
}