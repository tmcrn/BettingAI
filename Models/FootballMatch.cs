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

    // football-data.org's competition code (e.g. "FL1" for Ligue 1) and
    // round number - not used by the real betting flow (which spans all 5
    // supported leagues at once), but needed to scope a request to one
    // specific league/matchday, e.g. the "pronos" exact-score feature.
    public string? CompetitionCode { get; set; }
    public int? Matchday { get; set; }
}