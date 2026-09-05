namespace BettingAI.Models;

public class FootballMatch
{
    public string? Id { get; set; }
    public string? HomeTeam { get; set; }
    public string? AwayTeam { get; set; }

    // football-data.org's "shortName" for each team (e.g. "Union Berlin"
    // instead of the official "1. FC Union Berlin") - display only, never
    // used for TeamStats/TeamRecentResult lookups (those stay keyed on the
    // long HomeTeam/AwayTeam above, which is what football-data.org's other
    // endpoints also return as "name" - switching the join key itself to
    // shortName would silently break every stats lookup for a team whose
    // short name differs). Null when the API didn't provide one.
    public string? HomeTeamShort { get; set; }
    public string? AwayTeamShort { get; set; }

    // football-data.org's "crest" URL for each team's logo - display only,
    // same reasoning as HomeTeamShort above.
    public string? HomeTeamCrest { get; set; }
    public string? AwayTeamCrest { get; set; }
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