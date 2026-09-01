using BettingAI.Data;
using BettingAI.Models;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Services;

// Learns a real average odds per bet type from actual odds seen (Sofascore
// scrape resolved at decision time, or hand-entered by the user via
// SetOddsEndpoint/ManualSettle) - replaces the old flat "2x" guess used for
// combo legs with no real odds resolved, the same transparent, sample-gated
// pattern as WinPredictionService: a plain running average per bet type,
// never trusted until MinSample real observations exist.
public class OddsLearningService
{
    public const int MinSample = 5;

    private readonly BettingContext _context;

    public OddsLearningService(BettingContext context)
    {
        _context = context;
    }

    // One real odds observation for a bet type - nudges its running average.
    // Silently ignored for a null/empty type or an odds value that can't be
    // real (< 1.0) rather than corrupting the average with garbage input.
    public async Task RecordRealOddsAsync(string? betType, decimal odds, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(betType) || odds < 1m) return;

        var stat = await _context.LearnedOddsStats.FirstOrDefaultAsync(s => s.BetType == betType, ct);
        if (stat == null)
        {
            stat = new LearnedOddsStats { BetType = betType, AverageOdds = odds, SampleCount = 1, LastUpdated = DateTime.UtcNow };
            _context.LearnedOddsStats.Add(stat);
        }
        else
        {
            stat.AverageOdds += (odds - stat.AverageOdds) / (stat.SampleCount + 1);
            stat.SampleCount += 1;
            stat.LastUpdated = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(ct);
    }

    // All learned stats keyed by bet type, fetched once per decide-bets
    // request rather than one query per leg - the table stays tiny (one row
    // per bet type, at most a dozen or so).
    public async Task<Dictionary<string, (decimal Average, int SampleCount)>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await _context.LearnedOddsStats.ToListAsync(ct);
        return rows.ToDictionary(r => r.BetType, r => (r.AverageOdds, r.SampleCount));
    }
}
