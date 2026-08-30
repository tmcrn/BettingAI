namespace BettingAI.Models;

public enum BetType
{
    PLAYER_SCORER,           // Quel joueur marque
    PLAYER_ASSIST,           // Quel joueur fait l'assist
    BOTH_TEAMS_SCORE,        // Les 2 équipes marquent
    HOME_WIN,                // Victoire domicile
    AWAY_WIN,                // Victoire extérieur
    DRAW,                    // Match nul
    HOME_WIN_OR_DRAW,        // Domicile gagne ou nul
    AWAY_WIN_OR_DRAW,        // Extérieur gagne ou nul
    OVER_GOALS,              // Plus de X buts au total
    UNDER_GOALS,             // Moins de X buts au total
    HOME_OVER_GOALS,         // Plus de X buts domicile
    AWAY_OVER_GOALS          // Plus de X buts extérieur
}

public class BetDecision
{
    public string? MatchId { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
    public string? Type { get; set; }  // ← Change en string
    public string? Selection { get; set; }
    public decimal Stake { get; set; }
    public decimal? Confidence { get; set; }
    public string? Reasoning { get; set; }
}

public class DecideBetsResponse
{
    public List<BetDecision> Bets { get; set; } = new();
    public string? AiThinking { get; set; }
}