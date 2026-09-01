namespace BettingAI.Models;

// Learned average of REAL odds (Sofascore scrape or hand-entered by the
// user) seen for each bet type - one row per BetType. Used as the fallback
// estimate for a combo leg/bet with no real odds resolved at decision time,
// instead of a flat guess that was wildly off for some types (a heavy
// favorite at 1.07 shown/paid as 2.00). Never fabricated: SampleCount is
// always available so a caller can gate on it (see OddsLearningService.
// MinSample) rather than trusting a 1-sample average as if it meant
// something.
public class LearnedOddsStats
{
    public int Id { get; set; }
    public string BetType { get; set; } = "";
    public decimal AverageOdds { get; set; } = 0m;
    public int SampleCount { get; set; } = 0;
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}
