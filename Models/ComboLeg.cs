namespace BettingAI.Models;

public class ComboLeg
{
    public int Id { get; set; }
    public int BetComboId { get; set; }
    public BetCombo? BetCombo { get; set; }

    public string? MatchId { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
    public string? BetType { get; set; } // any bet type, incl. non-1X2 (goal lines etc.)
    public string? Selection { get; set; } // the line for goal-based types, e.g. "2.5" - null for 1X2-family types
    public decimal Odds { get; set; } // resolved real odds for this leg's selection
    public DateTime? MatchUtcDate { get; set; }
    public string Result { get; set; } = "PENDING"; // PENDING, WIN, LOSS
}
