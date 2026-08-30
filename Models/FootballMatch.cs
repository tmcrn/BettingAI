namespace BettingAI.Models;

public class FootballMatch
{
    public string? Id { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }
    public DateTime UtcDate { get; set; }
    public string? Status { get; set; }
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    // The real football-data API id. AutoDecideBets replaces Id with a local
    // array index so the AI can reliably echo it back, so this field carries
    // the true id through to DecideBets for storage/result lookup.
    public string? RealMatchId { get; set; }
}