namespace BettingAI.Models;

public class ComboLeg
{
    public int Id { get; set; }
    public int BetComboId { get; set; }
    public BetCombo? BetCombo { get; set; }

    public string? MatchId { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
    public string? BetType { get; set; } // HOME_WIN, AWAY_WIN, DRAW, HOME_WIN_OR_DRAW, AWAY_WIN_OR_DRAW
    public decimal Odds { get; set; } // resolved real odds for this leg's selection
    public DateTime? MatchUtcDate { get; set; }
    public string Result { get; set; } = "PENDING"; // PENDING, WIN, LOSS
}
