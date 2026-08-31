namespace BettingAI.Models;

// One real finished match, kept from a single team's point of view (goals
// scored/conceded are always THIS team's, regardless of home/away). This is
// the raw per-match data TeamStats.FormLast5 already aggregates away into a
// single win/draw/loss average - kept here separately so DecideBets can look
// past that average and reason about SPECIFIC recent results: margin of
// victory (a 4-0 win says more than a 1-0 win) and shared opponents between
// the two teams about to play (e.g. both recently played the same third
// team, so how each did against them is a transitive form signal).
// Rebuilt from scratch by TeamStatsSeedingService every seed run, same as
// TeamStats - only the last few matches per team are kept (see
// TeamStatsSeedingService.RecentResultsWindow), it's not a full match history.
public class TeamRecentResult
{
    public int Id { get; set; }
    public string TeamName { get; set; } = "";
    public string OpponentName { get; set; } = "";
    public DateTime MatchDate { get; set; }
    public int GoalsFor { get; set; }
    public int GoalsAgainst { get; set; }
    public bool IsHome { get; set; }
}
