using BettingAI.Data;
using BettingAI.Models;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Services;

// Closes the loop the AI needs to actually learn: finds bets still marked
// PENDING whose match has had time to finish, fetches the real final score,
// marks each bet WIN/LOSS, and refreshes the LearningNotebook stats that
// feed back into the next Mistral prompt.
public class BetSettlementService
{
    private static readonly TimeSpan SettlementBuffer = TimeSpan.FromHours(2);

    private readonly BettingContext _context;
    private readonly FootballDataService _footballDataService;

    public BetSettlementService(BettingContext context, FootballDataService footballDataService)
    {
        _context = context;
        _footballDataService = footballDataService;
    }

    public async Task<int> SettlePendingBetsAsync(CancellationToken ct = default)
    {
        var pendingBets = await _context.Bets
            .Where(b => b.Result == "PENDING")
            .ToListAsync(ct);

        if (pendingBets.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var settledCount = 0;

        foreach (var group in pendingBets.GroupBy(b => b.MatchId))
        {
            var matchId = group.Key;
            if (string.IsNullOrEmpty(matchId)) continue;

            // Don't check too early - give the match time to actually finish
            var referenceDate = group.First().MatchUtcDate ?? group.First().CreatedAt;
            if (referenceDate + SettlementBuffer > now) continue;

            var status = await _footballDataService.GetMatchStatusAsync(matchId, referenceDate);
            if (status == null || !status.Finished) continue;

            var result = status.HomeScore > status.AwayScore ? "HOME_WIN"
                : status.HomeScore < status.AwayScore ? "AWAY_WIN"
                : "DRAW";

            foreach (var bet in group)
            {
                var won = DetermineOutcome(bet.BetType, result, status.HomeScore, status.AwayScore);
                bet.Result = won ? "WIN" : "LOSS";
                bet.Winnings = won ? bet.Stake * 2 : 0;
                settledCount++;
            }
        }

        if (settledCount > 0)
        {
            await _context.SaveChangesAsync(ct);
            await RefreshLearningNotebookAsync(ct);
        }

        return settledCount;
    }

    public static bool DetermineOutcome(string? betType, string result, int homeScore, int awayScore)
    {
        return betType switch
        {
            "HOME_WIN" => result == "HOME_WIN",
            "AWAY_WIN" => result == "AWAY_WIN",
            "DRAW" => result == "DRAW",
            "BOTH_TEAMS_SCORE" => homeScore > 0 && awayScore > 0,
            "OVER_GOALS" => (homeScore + awayScore) > 2.5m,
            "UNDER_GOALS" => (homeScore + awayScore) < 2.5m,
            _ => false
        };
    }

    public async Task RefreshLearningNotebookAsync(CancellationToken ct = default)
    {
        var notebook = await _context.LearningNotebook
            .OrderByDescending(n => n.LastUpdated)
            .FirstOrDefaultAsync(ct);

        if (notebook == null)
        {
            notebook = new LearningNotebook { CreatedAt = DateTime.UtcNow };
            _context.LearningNotebook.Add(notebook);
        }

        var bets = await _context.Bets.ToListAsync(ct);

        notebook.LastUpdated = DateTime.UtcNow;
        notebook.TotalBets = bets.Count;
        notebook.WonBets = bets.Count(b => b.Result == "WIN");
        notebook.WinRate = bets.Count > 0 ? (decimal)notebook.WonBets / bets.Count : 0;
        notebook.AverageConfidence = bets.Count > 0 ? bets.Average(b => b.Confidence) : 0;

        await _context.SaveChangesAsync(ct);
    }
}
