namespace BettingAI.Models;

public class MatchContext
{
    public int Id { get; set; }
    public string? MatchId { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
    public DateTime MatchDate { get; set; }

    // Compositions
    public string? HomeLineup { get; set; }  // JSON players
    public string? AwayLineup { get; set; }
    public string? HomeMissingPlayers { get; set; }  // Star players absent?
    public string? AwayMissingPlayers { get; set; }

    // H2H (Historique)
    public int HomeWinsH2H { get; set; }
    public int DrawsH2H { get; set; }
    public int AwayWinsH2H { get; set; }
    public decimal AvgGoalsH2H { get; set; }

    // Contexte match
    public string? Competition { get; set; }  // Ligue 1, CL, etc.
    public string? Weather { get; set; }
    public int Altitude { get; set; }  // Peut affecter jeu
    public bool IsEuropeanMatch { get; set; }  // Fatigue semaine?
    public bool IsDerby { get; set; }

    // Expected metrics
    public decimal ExpectedScore { get; set; }  // xG combinés
    public decimal HomeExpectedWinProbability { get; set; }
}