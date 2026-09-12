namespace BettingAI.Models;

// A combined bet (multi) across 2+ different matches - wins only if every
// leg wins. Legs can be any bet type; a leg without resolvable real 1X2
// odds falls back to a flat 2x multiplier (see DecideBets.TryBuildCombo) -
// the decision is stats-driven, odds only price the payout.
public class BetCombo
{
    public int Id { get; set; }
    public decimal Stake { get; set; }
    public decimal Confidence { get; set; }
    public string? Reasoning { get; set; }
    public decimal CombinedOdds { get; set; }

    // True once the user has typed in the real "cote totale" seen on their
    // own bookmaker (see SetOddsEndpoint's "combo" kind) instead of relying
    // on the computed estimate (ComboOddsCalculator - a plain product for
    // cross-match legs, or a calibrated same-match correction otherwise,
    // itself only ever an approximation of the bookmaker's real pricing).
    // Once true, adding/editing a leg's own odds no longer overwrites
    // CombinedOdds - the user's real number is trusted over any formula.
    public bool CombinedOddsIsManual { get; set; } = false;

    public string Result { get; set; } = "PENDING"; // PENDING, WIN, LOSS
    public decimal? Winnings { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ComboLeg> Legs { get; set; } = new();
}
