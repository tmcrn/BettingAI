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

    // If football-data.org still hasn't given us a real result (or the bet
    // still has no real odds) this long after kickoff, auto-settlement has
    // had its chance - stop waiting on it and surface the bet for manual
    // entry instead of leaving it PENDING forever.
    public static readonly TimeSpan ManualReviewThreshold = TimeSpan.FromHours(3);

    // Payout fallback for a bet with no real market odds. A flat 2x was
    // wildly off in practice (a real HOME_WIN market at 1.07 for a heavy
    // favorite got shown/paid out as if it were 2.00, roughly double the
    // real payout) - deriving it from the AI's own confidence is a much
    // closer estimate (high confidence => the AI itself thinks this is a
    // strong favorite => low odds) using only data we already have, no
    // external lookup. Still just an estimate, not a real market price -
    // callers should keep marking it as such wherever it's shown.
    public static decimal EstimateOddsFromConfidence(decimal confidence) =>
        confidence > 0 ? Math.Round(1m / confidence, 2) : 2m;

    private readonly BettingContext _context;
    private readonly FootballDataService _footballDataService;
    private readonly DiscordNotificationService _discord;
    private readonly WinPredictionService _winPrediction;
    private readonly OddsLearningService _oddsLearning;

    public BetSettlementService(BettingContext context, FootballDataService footballDataService, DiscordNotificationService discord, WinPredictionService winPrediction, OddsLearningService oddsLearning)
    {
        _oddsLearning = oddsLearning;
        _context = context;
        _footballDataService = footballDataService;
        _discord = discord;
        _winPrediction = winPrediction;
    }

    // The actual "reward" step: one online gradient-descent update per
    // settled bet/leg, called from every place Result gets set to WIN/LOSS
    // (auto-settlement and manual entry alike). Silently skipped when the
    // decision-time features weren't persisted (bets placed before this
    // existed, or the match's edges weren't resolvable at decision time) -
    // training on a fabricated/zeroed feature would teach the model
    // something false, so it's better to just not train on that one point.
    private async Task TrainModelAsync(decimal? edge, decimal? form, decimal? momentum, decimal? confidence, bool won, CancellationToken ct)
    {
        if (edge == null || form == null || momentum == null || confidence == null) return;

        var features = new WinPredictionService.Features(edge.Value, form.Value, momentum.Value, confidence.Value);
        await _winPrediction.UpdateAsync(features, won, ct);
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
                bet.HomeScore = status.HomeScore;
                bet.AwayScore = status.AwayScore;
                // Real odds (when Sofascore had them at decision time) price the payout for
                // real; otherwise estimate from the AI's own confidence - odds never
                // influenced whether this bet was placed, only how much it pays out now.
                bet.Winnings = won ? bet.Stake * (bet.Odds ?? EstimateOddsFromConfidence(bet.Confidence)) : 0;
                await TrainModelAsync(bet.EdgeAlignmentFeature, bet.FormAlignmentFeature, bet.MomentumAlignmentFeature, bet.Confidence, won, ct);
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
                var won = DetermineOutcome(leg.BetType, leg.Selection, result, status.HomeScore, status.AwayScore);
                leg.Result = won ? "WIN" : "LOSS";
                leg.HomeScore = status.HomeScore;
                leg.AwayScore = status.AwayScore;
                await TrainModelAsync(leg.EdgeAlignmentFeature, leg.FormAlignmentFeature, leg.MomentumAlignmentFeature, leg.Confidence, won, ct);
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
            if (combo == null) continue;

            await FinalizeComboIfDeterminedAsync(combo, ct);
        }

        return settledLegCount;
    }

    // LOSS as soon as any leg loses (no need to wait for the rest), WIN only
    // once every leg has won, otherwise stays PENDING. Shared between the
    // automatic settlement loop and manual leg entry.
    private async Task<bool> FinalizeComboIfDeterminedAsync(BetCombo combo, CancellationToken ct)
    {
        if (combo.Result != "PENDING") return false;

        if (combo.Legs.Any(l => l.Result == "LOSS"))
        {
            combo.Result = "LOSS";
            combo.Winnings = 0;
            await _context.SaveChangesAsync(ct);
            await _discord.NotifyComboLostAsync(combo);
            return true;
        }

        if (combo.Legs.All(l => l.Result == "WIN"))
        {
            combo.Result = "WIN";
            combo.Winnings = combo.Stake * combo.CombinedOdds;
            await _context.SaveChangesAsync(ct);
            await _discord.NotifyComboWonAsync(combo);
            return true;
        }

        return false; // some legs still PENDING and none lost yet
    }

    // Bets/legs whose match kicked off more than ManualReviewThreshold ago
    // and are still PENDING - auto-settlement (football-data.org lookup)
    // has had its chance and hasn't resolved them, so surface them for
    // manual score/odds entry instead of leaving them stuck forever.
    public async Task<List<ManualReviewItem>> GetItemsNeedingManualReviewAsync(CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - ManualReviewThreshold;

        var bets = await _context.Bets
            .Where(b => b.Result == "PENDING" && b.MatchUtcDate != null && b.MatchUtcDate < cutoff)
            .ToListAsync(ct);

        var legs = await _context.ComboLegs
            .Where(l => l.Result == "PENDING" && l.MatchUtcDate != null && l.MatchUtcDate < cutoff)
            .ToListAsync(ct);

        var items = bets.Select(b => new ManualReviewItem
        {
            Kind = "bet",
            Id = b.Id,
            Match = $"{b.HomeTeam} vs {b.AwayTeam}",
            BetType = b.BetType,
            Selection = b.Selection,
            MatchUtcDate = b.MatchUtcDate,
            HasRealOdds = b.Odds != null
        })
        .Concat(legs.Select(l => new ManualReviewItem
        {
            Kind = "leg",
            Id = l.Id,
            Match = $"{l.HomeTeam} vs {l.AwayTeam}",
            BetType = l.BetType,
            Selection = null,
            MatchUtcDate = l.MatchUtcDate,
            HasRealOdds = true // legs always carry a value (real or the flat 2x fallback)
        }))
        .OrderBy(i => i.MatchUtcDate)
        .ToList();

        return items;
    }

    // Manual entry for a single Bet stuck past ManualReviewThreshold. The
    // user reports the real final score (and optionally the real odds they
    // saw, if none were resolved automatically) instead of the system
    // guessing or leaving it PENDING forever.
    public async Task<(bool Success, string Message)> ManualSettleBetAsync(int betId, int homeScore, int awayScore, decimal? realOdds, CancellationToken ct = default)
    {
        var bet = await _context.Bets.FirstOrDefaultAsync(b => b.Id == betId, ct);
        if (bet == null) return (false, "Pari introuvable");
        if (bet.Result != "PENDING") return (false, "Ce pari est déjà réglé");

        var result = homeScore > awayScore ? "HOME_WIN" : homeScore < awayScore ? "AWAY_WIN" : "DRAW";
        var won = DetermineOutcome(bet.BetType, bet.Selection, result, homeScore, awayScore);

        if (realOdds.HasValue)
        {
            bet.Odds = realOdds;
            // Real odds the user reported at settlement - same training
            // signal as SetOddsEndpoint, for OddsLearningService.
            await _oddsLearning.RecordRealOddsAsync(bet.BetType, realOdds.Value, ct);
        }
        bet.Result = won ? "WIN" : "LOSS";
        bet.HomeScore = homeScore;
        bet.AwayScore = awayScore;
        bet.Winnings = won ? bet.Stake * (bet.Odds ?? EstimateOddsFromConfidence(bet.Confidence)) : 0;
        await TrainModelAsync(bet.EdgeAlignmentFeature, bet.FormAlignmentFeature, bet.MomentumAlignmentFeature, bet.Confidence, won, ct);

        await _context.SaveChangesAsync(ct);
        await RefreshLearningNotebookAsync(ct);

        if (won) await _discord.NotifyBetWonAsync(bet);
        else await _discord.NotifyBetLostAsync(bet);

        return (true, $"Pari #{betId} réglé manuellement : {(won ? "GAGNÉ" : "PERDU")}");
    }

    // Same idea for one leg of a combo - settling it may in turn finalize
    // the whole combo (via FinalizeComboIfDeterminedAsync) if that was the
    // last leg still PENDING, or leave it PENDING if others still are.
    public async Task<(bool Success, string Message)> ManualSettleLegAsync(int legId, int homeScore, int awayScore, decimal? realOdds, CancellationToken ct = default)
    {
        var leg = await _context.ComboLegs
            .Include(l => l.BetCombo)
            .ThenInclude(c => c!.Legs)
            .FirstOrDefaultAsync(l => l.Id == legId, ct);
        if (leg == null) return (false, "Jambe de combiné introuvable");
        if (leg.Result != "PENDING") return (false, "Cette jambe est déjà réglée");
        if (leg.BetCombo == null) return (false, "Combiné parent introuvable");

        var result = homeScore > awayScore ? "HOME_WIN" : homeScore < awayScore ? "AWAY_WIN" : "DRAW";
        var won = DetermineOutcome(leg.BetType, leg.Selection, result, homeScore, awayScore);

        if (realOdds.HasValue)
        {
            leg.Odds = realOdds.Value;
            await _oddsLearning.RecordRealOddsAsync(leg.BetType, realOdds.Value, ct);
        }
        leg.Result = won ? "WIN" : "LOSS";
        leg.HomeScore = homeScore;
        leg.AwayScore = awayScore;
        await TrainModelAsync(leg.EdgeAlignmentFeature, leg.FormAlignmentFeature, leg.MomentumAlignmentFeature, leg.Confidence, won, ct);

        await _context.SaveChangesAsync(ct);

        var comboFinalized = await FinalizeComboIfDeterminedAsync(leg.BetCombo, ct);
        if (comboFinalized) await RefreshLearningNotebookAsync(ct);

        return (true, $"Jambe #{legId} réglée manuellement : {(won ? "GAGNÉE" : "PERDUE")}" +
            (comboFinalized ? $" - combiné #{leg.BetComboId} finalisé ({leg.BetCombo.Result})" : ""));
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

public class ManualReviewItem
{
    public string Kind { get; set; } = ""; // "bet" | "leg"
    public int Id { get; set; }
    public string? Match { get; set; }
    public string? BetType { get; set; }
    public string? Selection { get; set; }
    public DateTime? MatchUtcDate { get; set; }
    public bool HasRealOdds { get; set; }
}
