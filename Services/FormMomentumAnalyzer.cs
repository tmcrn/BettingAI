using BettingAI.Models;

namespace BettingAI.Services;

// TeamStats.FormLast5 already gives a win/draw/loss average, but that treats
// a scraped 1-0 win the same as a 4-0 blowout, and it says nothing about how
// two teams that are about to play compare via teams they've BOTH recently
// played. This is the "Monaco vient d'écraser Marseille 2-0, Marseille a
// elle-même écrasé Strasbourg 4-0" reasoning the user does by hand - looks
// past the aggregated average at the individual recent results
// (TeamRecentResult, from TeamStatsSeedingService) to build:
//  - a margin-of-victory-weighted momentum score per team (recent results
//    count more, and how BIG a win/loss was counts, not just win/draw/loss)
//  - a plain-French note when both teams share a recent opponent, comparing
//    how each did against them
// Deliberately informational only for now (surfaced as prompt context, not a
// hard guardrail like ATTACKING EDGE) - a shared opponent is often from a
// different venue/matchday and is a much noisier signal than our own xG/xGA
// numbers, so it's not reliable enough on its own to hard-reject a bet on.
public static class FormMomentumAnalyzer
{
    private const int RecentWindow = 5;
    // Momentum difference beyond which we call it an edge rather than EVEN -
    // deliberately conservative since this is a secondary/noisier signal.
    private const decimal EdgeThreshold = 1.0m;

    public record MomentumResult(decimal HomeMomentum, decimal AwayMomentum, string Edge, string? CommonOpponentNote);

    public static MomentumResult Compute(
        string homeTeam,
        string awayTeam,
        List<TeamRecentResult> homeResults,
        List<TeamRecentResult> awayResults)
    {
        var homeMomentum = WeightedMomentum(homeResults);
        var awayMomentum = WeightedMomentum(awayResults);

        var diff = homeMomentum - awayMomentum;
        var edge = diff > EdgeThreshold ? "HOME" : diff < -EdgeThreshold ? "AWAY" : "EVEN";

        var note = BuildCommonOpponentNote(homeTeam, awayTeam, homeResults, awayResults);

        return new MomentumResult(homeMomentum, awayMomentum, edge, note);
    }

    // Recency-weighted, margin-of-victory-weighted goal difference over the
    // last few matches - a 4-0 win last week counts for more than a 1-0 win
    // three months ago. A single match's swing is capped so one true blowout
    // doesn't dominate the whole score.
    private static decimal WeightedMomentum(List<TeamRecentResult> results)
    {
        var recent = results.OrderByDescending(r => r.MatchDate).Take(RecentWindow).ToList();
        if (recent.Count == 0) return 0;

        decimal weightedTotal = 0;
        decimal weightSum = 0;
        for (var i = 0; i < recent.Count; i++)
        {
            var weight = RecentWindow - i; // most recent match weighs the most
            var margin = Math.Clamp(recent[i].GoalsFor - recent[i].GoalsAgainst, -4, 4);
            weightedTotal += weight * margin;
            weightSum += weight;
        }

        return Math.Round(weightedTotal / weightSum, 2);
    }

    // Finds the most recent opponent both teams have played, and describes
    // how each did against them - the transitive "A beat X who beat B"
    // signal. Only ever the single most recent shared opponent, to keep the
    // note short and avoid mixing signals from very different matchdays.
    private static string? BuildCommonOpponentNote(
        string homeTeam,
        string awayTeam,
        List<TeamRecentResult> homeResults,
        List<TeamRecentResult> awayResults)
    {
        var homeOpponents = homeResults.Select(r => r.OpponentName).ToHashSet();

        var sharedAwayResult = awayResults
            .Where(r => homeOpponents.Contains(r.OpponentName) && r.OpponentName != homeTeam && r.OpponentName != awayTeam)
            .OrderByDescending(r => r.MatchDate)
            .FirstOrDefault();
        if (sharedAwayResult == null) return null;

        var sharedHomeResult = homeResults
            .Where(r => r.OpponentName == sharedAwayResult.OpponentName)
            .OrderByDescending(r => r.MatchDate)
            .First();

        return $"Adversaire commun récent ({sharedAwayResult.OpponentName}): " +
            $"{Describe(homeTeam, sharedHomeResult)}, {Describe(awayTeam, sharedAwayResult)}";
    }

    private static string Describe(string team, TeamRecentResult r) => r.GoalsFor > r.GoalsAgainst
        ? $"{team} a battu {r.OpponentName} {r.GoalsFor}-{r.GoalsAgainst}"
        : r.GoalsFor < r.GoalsAgainst
            ? $"{team} a perdu contre {r.OpponentName} {r.GoalsAgainst}-{r.GoalsFor}"
            : $"{team} a fait match nul contre {r.OpponentName} {r.GoalsFor}-{r.GoalsFor}";
}
