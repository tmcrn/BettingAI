namespace BettingAI.Models;

// A combined bet (multi) across 2+ different matches - wins only if every
// leg wins. Restricted to leg types with real scraped 1X2 odds (HOME_WIN,
// AWAY_WIN, DRAW, HOME_WIN_OR_DRAW, AWAY_WIN_OR_DRAW) since that's the only
// market we have verified pricing for - never fabricate combined odds.
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
