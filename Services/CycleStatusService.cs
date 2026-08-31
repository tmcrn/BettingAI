namespace BettingAI.Services;

// Tracks the outcome of the last auto-decide-bets cycle so it can be
// checked on demand (GetCycleStatus endpoint) instead of digging through
// journalctl. The 45-min cron/1h window combo legitimately finds nothing
// most cycles outside match hours, and that case sends no Discord
// notification at all (by design, to avoid spamming an empty result) -
// which reads as "the cron is broken" from the outside with no other way
// to tell it apart from an actually-stuck service. Singleton: a single
// BettingAI process, in-memory is enough, no need to persist across restarts.
public class CycleStatusService
{
    public DateTime? LastRunAt { get; private set; }
    public string? LastOutcome { get; private set; } // "no_matches" | "no_bets" | "bets_placed" | "error"
    public int MatchesFound { get; private set; }
    public int BetsPlaced { get; private set; }
    public string? LastMessage { get; private set; }

    public void Record(string outcome, int matchesFound, int betsPlaced, string? message)
    {
        LastRunAt = DateTime.UtcNow;
        LastOutcome = outcome;
        MatchesFound = matchesFound;
        BetsPlaced = betsPlaced;
        LastMessage = message;
    }
}
