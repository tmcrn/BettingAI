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
    public string Result { get; set; } = "PENDING"; // PENDING, WIN, LOSS
    public decimal? Winnings { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<ComboLeg> Legs { get; set; } = new();
}
