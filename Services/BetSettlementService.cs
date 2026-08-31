using BettingAI.Data;
using BettingAI.Models;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Services;

// Closes the loop the AI needs to actually learn: finds bets (and combo
// legs) still marked PENDING whose match has had time to finish, fetches
// the real final score, marks each WIN/LOSS, resolves any combo whose legs
// are now all known, and refreshes the LearningNotebook stats that feed
// back into the next Mistral prompt.
public class BetSettlementService
{
    private static readonly TimeSpan SettlementBuffer = TimeSpan.FromHours(2);

    private readonly BettingContext _context;
    private readonly FootballDataService _footballDataService;
    private readonly DiscordNotificationService _discord;

    public BetSettlementService(BettingContext context, FootballDataService footballDataService, DiscordNotificationService discord)
    {
        _context = context;
        _footballDataService = footballDataService;
        _discord = discord;
    }

    public async Task<int> SettlePendingBetsAsync(CancellationToken ct = default)
    {
        var settledCount = 0;
        settledCount += await SettleSingleBetsAsync(ct);
        settledCount += await SettleComboLegsAsync(ct);

        if (settledCount > 0)
        {
            await RefreshLearningNotebookAsync(ct);
        }

        return settledCount;
    }

    private async Task<int> SettleSingleBetsAsync(CancellationToken ct)
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

            var referenceDate = group.First().MatchUtcDate ?? group.First().CreatedAt;
            if (referenceDate + SettlementBuffer > now) continue;

            var status = await _footballDataService.GetMatchStatusAsync(matchId, referenceDate);
            if (status == null || !status.Finished) continue;

            var result = status.HomeScore > status.AwayScore ? "HOME_WIN"
                : status.HomeScore < status.AwayScore ? "AWAY_WIN"
                : "DRAW";

            foreach (var bet in group)
            {
                var won = DetermineOutcome(bet.BetType, bet.Selection, result, status.HomeScore, status.AwayScore);
                bet.Result = won ? "WIN" : "LOSS";
                // Real odds (when Sofascore had them at decision time) price the payout for
                // real; otherwise fall back to a flat 2x - odds never influenced whether this
                // bet was placed, only how much it pays out now that it won.
                bet.Winnings = won ? bet.Stake * (bet.Odds ?? 2m) : 0;
                settledCount++;

                if (won)
                    await _discord.NotifyBetWonAsync(bet);
                else
                    await _discord.NotifyBetLostAsync(bet);
            }
        }

        if (settledCount > 0)
        {
            await _context.SaveChangesAsync(ct);
        }

        return settledCount;
    }

    private async Task<int> SettleComboLegsAsync(CancellationToken ct)
    {
        var pendingLegs = await _context.ComboLegs
            .Include(l => l.BetCombo)
            .ThenInclude(c => c!.Legs)
            .Where(l => l.Result == "PENDING")
            .ToListAsync(ct);

        if (pendingLegs.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var settledLegCount = 0;
        var affectedCombos = new HashSet<int>();

        foreach (var group in pendingLegs.GroupBy(l => l.MatchId))
        {
            var matchId = group.Key;
            if (string.IsNullOrEmpty(matchId)) continue;

            var referenceDate = group.First().MatchUtcDate ?? DateTime.UtcNow.AddHours(-3);
            if (referenceDate + SettlementBuffer > now) continue;

            var status = await _footballDataService.GetMatchStatusAsync(matchId, referenceDate);
            if (status == null || !status.Finished) continue;

            var result = status.HomeScore > status.AwayScore ? "HOME_WIN"
                : status.HomeScore < status.AwayScore ? "AWAY_WIN"
                : "DRAW";

            foreach (var leg in group)
            {
                var won = DetermineOutcome(leg.BetType, null, result, status.HomeScore, status.AwayScore);
                leg.Result = won ? "WIN" : "LOSS";
                settledLegCount++;
                affectedCombos.Add(leg.BetComboId);
            }
        }

        if (settledLegCount == 0) return 0;

        await _context.SaveChangesAsync(ct);

        // Now finalize any combo whose outcome is now determined: LOSS as
        // soon as any leg loses (no need to wait for the rest), WIN only
        // once every leg has won.
        foreach (var comboId in affectedCombos)
        {
            var combo = await _context.BetCombos
                .Include(c => c.Legs)
                .FirstOrDefaultAsync(c => c.Id == comboId, ct);
            if (combo == null || combo.Result != "PENDING") continue;

            if (combo.Legs.Any(l => l.Result == "LOSS"))
            {
                combo.Result = "LOSS";
                combo.Winnings = 0;
                await _context.SaveChangesAsync(ct);
                await _discord.NotifyComboLostAsync(combo);
            }
            else if (combo.Legs.All(l => l.Result == "WIN"))
            {
                combo.Result = "WIN";
                combo.Winnings = combo.Stake * combo.CombinedOdds;
                await _context.SaveChangesAsync(ct);
                await _discord.NotifyComboWonAsync(combo);
            }
            // else: some legs still PENDING and none lost yet - stays PENDING
        }

        return settledLegCount;
    }

    public static bool DetermineOutcome(string? betType, string? selection, string result, int homeScore, int awayScore)
    {
        decimal ParseLine(decimal fallback) =>
            decimal.TryParse(selection, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var line)
                ? line
                : fallback;

        return betType switch
        {
            "HOME_WIN" => result == "HOME_WIN",
            "AWAY_WIN" => result == "AWAY_WIN",
            "DRAW" => result == "DRAW",
            "HOME_WIN_OR_DRAW" => result == "HOME_WIN" || result == "DRAW",
            "AWAY_WIN_OR_DRAW" => result == "AWAY_WIN" || result == "DRAW",
            "BOTH_TEAMS_SCORE" => homeScore > 0 && awayScore > 0,
            "OVER_GOALS" => (homeScore + awayScore) > ParseLine(2.5m),
            "UNDER_GOALS" => (homeScore + awayScore) < ParseLine(2.5m),
            "HOME_OVER_GOALS" => homeScore > ParseLine(1.5m),
            "AWAY_OVER_GOALS" => awayScore > ParseLine(1.5m),
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
