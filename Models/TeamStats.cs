namespace BettingAI.Models;

public class TeamStats
{
    public int Id { get; set; }
    public string? TeamName { get; set; }
    public int LeagueId { get; set; }
    public DateTime LastUpdated { get; set; }

    // Offensifs
    public decimal xG { get; set; }  // Expected Goals moyenne
    public decimal xA { get; set; }  // Expected Assists
    public int ShotsOnTarget { get; set; }
    public int TotalShots { get; set; }
    public decimal ConversionRate { get; set; }
    public decimal PossessionAvg { get; set; }

    // Défensifs
    public decimal xGA { get; set; }  // Expected Goals Against
    public int ShotsConceded { get; set; }
    public int CleanSheets { get; set; }
    public decimal DefenseRating { get; set; }

    // Forme
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public decimal FormLast5 { get; set; }  // Moyenne points derniers 5

    // Contexte
    public bool IsHomeMatch { get; set; }
    public int DaysSinceLastMatch { get; set; }
    public string? KeyInjuries { get; set; }  // JSON
    public decimal FatigueIndex { get; set; }  // 0-1
    public int ConsecutiveMatches { get; set; }  // Matchs cette semaine
}