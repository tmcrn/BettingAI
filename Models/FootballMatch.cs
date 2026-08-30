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
}