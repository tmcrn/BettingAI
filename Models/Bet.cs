namespace BettingAI.Models;

public class Bet
{
    public int Id { get; set; }
    public string? MatchId { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
    public string? BetType { get; set; }  // PLAYER_SCORER, HOME_WIN, etc.
    public string? Selection { get; set; } // Nom joueur ou "2.5"
    public decimal Stake { get; set; }
    public decimal Confidence { get; set; }
    public string? Reasoning { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public decimal? Odds { get; set; }
    public string? Result { get; set; }  // "WIN", "LOSS", "PENDING"
    public decimal? Winnings { get; set; }
    public DateTime? MatchUtcDate { get; set; }  // Kickoff time - used to know when to check the real result
}