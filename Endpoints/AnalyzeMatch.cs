using BettingAI.Data;
using BettingAI.Models;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class AnalyzeMatchRequest
{
    public string? MatchId { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
}

public class AnalyzeMatchResponse
{
    public string? MatchId { get; set; }
    public string? Matchup { get; set; }

    // Offensif
    public decimal HomeXG { get; set; }
    public decimal AwayXG { get; set; }

    // Défensif
    public decimal HomeXGA { get; set; }
    public decimal AwayXGA { get; set; }

    // Forme
    public decimal HomeFormLast5 { get; set; }
    public decimal AwayFormLast5 { get; set; }

    // H2H
    public int HomeWinsH2H { get; set; }
    public int AwayWinsH2H { get; set; }

    // Contexte
    public string? KeyFactors { get; set; }  // Injuries, fatigue, etc
    public decimal PredictedWinProbHome { get; set; }
    public string? AnalysisSummary { get; set; }
}

public class AnalyzeMatchEndpoint : Endpoint<AnalyzeMatchRequest, AnalyzeMatchResponse>
{
    private readonly BettingContext _context;

    public AnalyzeMatchEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Post("/api/analyze-match");
        AllowAnonymous();
    }

    public override async Task HandleAsync(AnalyzeMatchRequest req, CancellationToken ct)
    {
        // Récupère stats des équipes
        var homeStats = await _context.TeamStats
            .FirstOrDefaultAsync(t => t.TeamName == req.HomeTeam, cancellationToken: ct);
        var awayStats = await _context.TeamStats
            .FirstOrDefaultAsync(t => t.TeamName == req.AwayTeam, cancellationToken: ct);

        // Récupère contexte du match
        var matchContext = await _context.MatchContexts
            .FirstOrDefaultAsync(m => m.MatchId == req.MatchId, cancellationToken: ct);

        var response = new AnalyzeMatchResponse
        {
            MatchId = req.MatchId,
            Matchup = $"{req.HomeTeam} vs {req.AwayTeam}",
            HomeXG = homeStats?.xG ?? 0,
            AwayXG = awayStats?.xG ?? 0,
            HomeXGA = homeStats?.xGA ?? 0,
            AwayXGA = awayStats?.xGA ?? 0,
            HomeFormLast5 = homeStats?.FormLast5 ?? 0,
            AwayFormLast5 = awayStats?.FormLast5 ?? 0,
            HomeWinsH2H = matchContext?.HomeWinsH2H ?? 0,
            AwayWinsH2H = matchContext?.AwayWinsH2H ?? 0,
            KeyFactors = matchContext?.HomeMissingPlayers ?? "Aucune info",
            PredictedWinProbHome = CalculateWinProbability(homeStats, awayStats),
            AnalysisSummary = GenerateAnalysis(homeStats, awayStats, matchContext)
        };

        await Send.OkAsync(response);
    }

    private decimal CalculateWinProbability(TeamStats? home, TeamStats? away)
    {
        if (home == null || away == null) return 0.5m;

        // Formule simple : xG difference + forme + H2H
        var xgDiff = home.xG - away.xG;
        var formDiff = home.FormLast5 - away.FormLast5;

        var probability = 0.5m + (xgDiff * 0.1m) + (formDiff * 0.05m);

        return Math.Min(0.95m, Math.Max(0.05m, probability));
    }

    // Symmetric home-vs-away comparison. The old version only ever checked
    // "is home's attack strong" / "is away's defense weak" in isolation, so
    // a match where the AWAY team's attack was clearly the stronger one
    // (e.g. away xG 4.0 vs home xG 2.0) produced a summary that never said
    // so - nothing here ever pointed at the away team's attack or the
    // home team's defense. That one-sidedness is exactly what led the AI to
    // bet HOME_WIN on a match its own xG numbers favored the away side on.
    // Every factor below explicitly names which side it favors.
    private string GenerateAnalysis(TeamStats? home, TeamStats? away, MatchContext? context)
    {
        if (home == null || away == null) return "Données insuffisantes";

        var factors = new List<string>();

        // Attaque (xG) - qui marque le plus
        var xgDiff = home.xG - away.xG;
        if (xgDiff > 0.3m) factors.Add($"✓ Attaque domicile supérieure ({home.xG} xG vs {away.xG})");
        else if (xgDiff < -0.3m) factors.Add($"✓ Attaque visiteur supérieure ({away.xG} xG vs {home.xG})");
        else factors.Add($"= Attaques comparables ({home.xG} xG vs {away.xG})");

        // Défense (xGA) - qui encaisse le moins (plus bas = meilleur)
        var xgaDiff = home.xGA - away.xGA;
        if (xgaDiff < -0.3m) factors.Add($"✓ Défense domicile supérieure ({home.xGA} xGA vs {away.xGA})");
        else if (xgaDiff > 0.3m) factors.Add($"✓ Défense visiteur supérieure ({away.xGA} xGA vs {home.xGA})");

        // Forme (5 derniers matchs)
        var formDiff = home.FormLast5 - away.FormLast5;
        if (formDiff > 0.4m) factors.Add($"✓ Meilleure forme domicile ({home.FormLast5} vs {away.FormLast5})");
        else if (formDiff < -0.4m) factors.Add($"✓ Meilleure forme visiteur ({away.FormLast5} vs {home.FormLast5})");

        // H2H
        if (context != null)
        {
            if (context.HomeWinsH2H > context.AwayWinsH2H + 1) factors.Add("✓ Domicile dominant en H2H");
            else if (context.AwayWinsH2H > context.HomeWinsH2H + 1) factors.Add("✓ Visiteur dominant en H2H");
        }

        // Fatigue
        if (home.FatigueIndex > 0.6m) factors.Add("⚠ Fatigue domicile élevée");
        if (away.ConsecutiveMatches > 2) factors.Add("⚠ Visiteur fatigué (3+ matchs)");

        return factors.Count > 0 ? string.Join(" | ", factors) : "Contexte équilibré";
    }
}