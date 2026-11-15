using BettingAI.Data;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Services;

// Keeps Bet.ClosingOdds up to date: the last price our bookmaker showed for
// the same selection before kickoff. Two ways it gets fed:
//   - for free, every time a cycle already fetched a league's odds (any
//     still-pending bet of that league gets its latest price recorded),
//   - on demand via /api/refresh-closing-odds, meant to be called shortly
//     before kickoffs (costs 2 credits per league that has a pending bet
//     starting soon).
public class ClosingLineService
{
    private readonly BettingContext _db;
    private readonly OddsApiService _odds;

    public ClosingLineService(BettingContext db, OddsApiService odds)
    {
        _db = db;
        _odds = odds;
    }

    // Returns how many bets got a fresher closing price. Does NOT save.
    public async Task<int> ApplyAsync(IEnumerable<OddsEvent> events, CancellationToken ct)
    {
        var byId = events.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        if (byId.Count == 0) return 0;

        var now = DateTime.UtcNow;
        var ids = byId.Keys.ToList();
        var pending = await _db.Bets
            .Where(b => b.Result == "PENDING" && b.OddsEventId != null && ids.Contains(b.OddsEventId))
            .ToListAsync(ct);

        var updated = 0;
        foreach (var bet in pending)
        {
            if (bet.MatchUtcDate != null && bet.MatchUtcDate <= now) continue; // already kicked off - keep the last pre-match price
            var ev = byId[bet.OddsEventId!];
            var price = ValueBetFinder.PriceFor(ev, bet.Bookmaker ?? OddsApiService.BetBookmaker, bet.BetType, bet.Selection);
            if (price == null) continue;
            bet.ClosingOdds = price;
            bet.ClosingOddsAt = now;
            updated++;
        }
        return updated;
    }

    // Fetches odds only for leagues with a pending bet kicking off within
    // the next `withinHours` hours, then records their current price.
    public async Task<(int leagues, int updated)> RefreshAsync(int withinHours, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var horizon = now.AddHours(withinHours);
        var competitions = await _db.Bets
            .Where(b => b.Result == "PENDING" && b.OddsEventId != null && b.CompetitionCode != null
                        && b.MatchUtcDate > now && b.MatchUtcDate <= horizon)
            .Select(b => b.CompetitionCode!)
            .Distinct()
            .ToListAsync(ct);

        var updated = 0;
        foreach (var code in competitions)
        {
            var events = await _odds.GetLeagueOddsAsync(code, ct);
            updated += await ApplyAsync(events, ct);
        }
        await _db.SaveChangesAsync(ct);
        return (competitions.Count, updated);
    }
}
