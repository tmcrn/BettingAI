using BettingAI.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class GetPortfolioResponse
{
    public decimal TotalStaked { get; set; }
    public decimal TotalWinnings { get; set; }

    // TotalWinnings alone is the gross payout from WIN tickets only, always
    // positive - it says nothing about whether the user is actually up or
    // down overall once every stake (including on LOSS tickets) is counted.
    // This is the real profit/loss: TotalWinnings - TotalStaked, negative
    // when down. Equivalent to CurrentBalance minus the starting bankroll,
    // computed here instead so the frontend doesn't need to know that
    // starting amount just to show this.
    public decimal NetProfit { get; set; }
    public decimal CurrentBalance { get; set; }
    public int TotalBets { get; set; }
    public int WonBets { get; set; }
    public int LostBets { get; set; }
    public int PendingBets { get; set; }
    public double WinRate { get; set; }

    // Return on investment: NetProfit as a % of TotalStaked, e.g. 12.5 for
    // +12.5%. Unlike NetProfit alone (raw euros), this says whether the
    // strategy is actually efficient per euro risked, not just ahead in
    // absolute terms - a bigger bankroll always shows a bigger NetProfit
    // even at the same true performance. 0 while nothing's been staked yet.
    public double Roi { get; set; }

    // Mean odds across every bet/combo ever placed (CombinedOdds for a
    // combo, Odds for a single - null only for a handful of very old single
    // bets saved before Odds existed). Null if there's nothing to average.
    public decimal? AverageOdds { get; set; }

    public decimal AverageStake { get; set; }

    // Longest run of consecutive WIN (or consecutive LOSS) among settled
    // bets/combos, most-recent-first - "PENDING" ones don't break or
    // extend it, they're just skipped. CurrentStreakResult is null (and
    // Count 0) until at least one bet has ever been settled.
    public int CurrentStreakCount { get; set; }
    public string? CurrentStreakResult { get; set; }

    // How many bets/combos match the current ResultFilter in total, before
    // Limit truncates the list - lets the dashboard's "Voir plus" button
    // know whether there's anything left to load.
    public int TotalMatchingBets { get; set; }

    public List<BetHistoryItem> RecentBets { get; set; } = new();
}

public class BetHistoryItem
{
    public int Id { get; set; }
    public string? MatchId { get; set; }
    public string? Match { get; set; }
    public string? BetType { get; set; }
    public string? Selection { get; set; }
    public decimal Stake { get; set; }
    public string? Result { get; set; }
    public decimal? Winnings { get; set; }
    public decimal Confidence { get; set; }
    public string? Reasoning { get; set; }
    public decimal? Odds { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsCombo { get; set; }
    public List<ComboLegItem>? Legs { get; set; }

    // The real final score for this match, set once at settlement. Null
    // while PENDING. Only meaningful for a standalone (non-combo) bet -
    // a combo can span several matches, so its own score lives per-leg
    // on ComboLegItem instead.
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    // Live match minute - see the comment on Bet.Minute. Only meaningful
    // while PENDING, same as HomeScore/AwayScore above.
    public int? Minute { get; set; }

    // Real kickoff time, not to be confused with CreatedAt (when the AI
    // placed the bet) - only meaningful for a standalone bet, same reason
    // as HomeScore/AwayScore above (a combo can span several matches).
    public DateTime? MatchUtcDate { get; set; }

    // football-data.org's competition code (e.g. "FL1"), used client-side
    // to show a small flag next to the match name.
    public string? CompetitionCode { get; set; }

    // Club logo URLs - null for rows saved before this existed, or when
    // the API had none for that team.
    public string? HomeTeamCrest { get; set; }
    public string? AwayTeamCrest { get; set; }
}

public class ComboLegItem
{
    public int Id { get; set; }
    public string? Match { get; set; }
    public string? BetType { get; set; }
    public string? Selection { get; set; }
    public decimal Odds { get; set; }
    public string? Result { get; set; }

    // The real final score for this leg's own match, set once at
    // settlement. Null while PENDING.
    public int? HomeScore { get; set; }
    public int? AwayScore { get; set; }

    // See the comment on BetHistoryItem.Minute.
    public int? Minute { get; set; }

    // Real kickoff time for this leg's own match.
    public DateTime? MatchUtcDate { get; set; }

    // See the comment on BetHistoryItem.CompetitionCode.
    public string? CompetitionCode { get; set; }

    // See the comment on BetHistoryItem.HomeTeamCrest.
    public string? HomeTeamCrest { get; set; }
    public string? AwayTeamCrest { get; set; }
}

public class GetPortfolioRequest
{
    // Optional: "WIN" | "LOSS" | "PENDING" - narrows RecentBets to just that
    // status. The portfolio-wide stats above (TotalBets, WonBets, ...) are
    // never affected by this - only which tickets are listed.
    [QueryParam]
    public string? ResultFilter { get; set; }

    // Optional: how many RecentBets to return, most-recent-first (after
    // ResultFilter narrows the set). Defaults to 15 - the dashboard's
    // "Voir plus" button just re-fetches with a bigger Limit each time
    // rather than a real cursor, which is fine at personal scale and means
    // every earlier ticket is still there (in the same order) whenever the
    // list re-renders. Clamped to GetPortfolioEndpoint.MaxRecentBets so
    // nothing pathological can be requested.
    [QueryParam]
    public int? Limit { get; set; }

    // Optional: a football-data.org competition code (e.g. "FL1") - narrows
    // RecentBets to tickets in that league, combined with ResultFilter
    // rather than replacing it. A single bet matches on its own
    // CompetitionCode; a combo matches if ANY of its legs does (a combo's
    // legs never actually mix leagues in practice, but this checks properly
    // rather than assuming it).
    [QueryParam]
    public string? League { get; set; }
}

public class GetPortfolioEndpoint : Endpoint<GetPortfolioRequest, GetPortfolioResponse>
{
    // Default page size for RecentBets ("Voir plus" starts from here and
    // asks for 15 more each time) and the sanity ceiling on Request.Limit -
    // still a cap, not unlimited, since this is a personal-scale dashboard,
    // not a paginated table.
    private const int DefaultRecentBetsLimit = 15;
    private const int MaxRecentBets = 500;

    private readonly BettingContext _context;

    // Prefers the display-only short name (e.g. "Union Berlin") over the
    // long official one (e.g. "1. FC Union Berlin") football-data.org
    // otherwise returns - short name is null for rows saved before this
    // existed, or when the API had none for that team.
    private static string TeamName(string? shortName, string? fullName) => shortName ?? fullName ?? "?";

    public GetPortfolioEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Get("/api/portfolio");
        AllowAnonymous();
    }

    public override async Task HandleAsync(GetPortfolioRequest req, CancellationToken ct)
    {
        var bets = await _context.Bets.ToListAsync(cancellationToken: ct);
        var combos = await _context.BetCombos.Include(c => c.Legs).ToListAsync(cancellationToken: ct);

        var totalStaked = bets.Sum(b => b.Stake) + combos.Sum(c => c.Stake);
        var totalWinnings = bets.Where(b => b.Result == "WIN").Sum(b => b.Winnings ?? 0)
            + combos.Where(c => c.Result == "WIN").Sum(c => c.Winnings ?? 0);
        var currentBalance = 10 + totalWinnings - totalStaked;

        var wonCount = bets.Count(b => b.Result == "WIN") + combos.Count(c => c.Result == "WIN");
        var lostCount = bets.Count(b => b.Result == "LOSS") + combos.Count(c => c.Result == "LOSS");
        var pendingCount = bets.Count(b => b.Result == "PENDING") + combos.Count(c => c.Result == "PENDING");
        var totalCount = bets.Count + combos.Count;
        var winRate = totalCount > 0 ? (double)wonCount / totalCount : 0;

        var netProfit = totalWinnings - totalStaked;
        var roi = totalStaked > 0 ? (double)(netProfit / totalStaked) * 100 : 0;
        var averageStake = totalCount > 0 ? totalStaked / totalCount : 0;

        var allOdds = bets.Select(b => b.Odds).Where(o => o.HasValue).Select(o => o!.Value)
            .Concat(combos.Select(c => c.CombinedOdds))
            .ToList();
        var averageOdds = allOdds.Count > 0 ? allOdds.Average() : (decimal?)null;

        // Walk every settled bet/combo most-recent-first and count how many
        // in a row share the very first (i.e. most recent) result - PENDING
        // ones are excluded up front so they neither break nor extend it.
        var settledChronological = bets.Select(b => (CreatedAt: b.CreatedAt, Result: b.Result))
            .Concat(combos.Select(c => (CreatedAt: c.CreatedAt, Result: (string?)c.Result)))
            .Where(x => x.Result == "WIN" || x.Result == "LOSS")
            .OrderByDescending(x => x.CreatedAt)
            .ToList();
        var streakResult = settledChronological.Count > 0 ? settledChronological[0].Result : null;
        var streakCount = settledChronological.TakeWhile(x => x.Result == streakResult).Count();

        var recentBets = bets
            .Select(b => (CreatedAt: b.CreatedAt, Item: new BetHistoryItem
            {
                Id = b.Id,
                MatchId = b.MatchId,
                Match = $"{TeamName(b.HomeTeamShort, b.HomeTeam)} vs {TeamName(b.AwayTeamShort, b.AwayTeam)}",
                BetType = b.BetType,
                Selection = b.Selection,
                Stake = b.Stake,
                Result = b.Result,
                Winnings = b.Winnings,
                Confidence = b.Confidence,
                Reasoning = b.Reasoning,
                Odds = b.Odds,
                CreatedAt = b.CreatedAt,
                IsCombo = false,
                HomeScore = b.HomeScore,
                AwayScore = b.AwayScore,
                Minute = b.Minute,
                MatchUtcDate = b.MatchUtcDate,
                CompetitionCode = b.CompetitionCode,
                HomeTeamCrest = b.HomeTeamCrest,
                AwayTeamCrest = b.AwayTeamCrest
            }))
            .Concat(combos.Select(c => (CreatedAt: c.CreatedAt, Item: new BetHistoryItem
            {
                Id = c.Id,
                MatchId = null,
                // "Combiné N matchs" used Legs.Count directly, which counts
                // legs, not distinct matches - wrong now that a same-match
                // combo (multiple legs on ONE match, e.g. HOME_WIN +
                // OVER_GOALS) is common again since the OUTCOME/GOALS merge.
                // Count distinct match ids: a same-match combo names that one
                // match instead of claiming "2 matchs" for something that's
                // actually one.
                Match = c.Legs.Select(l => l.MatchId).Distinct().Count() == 1
                    ? $"Combiné - {TeamName(c.Legs.First().HomeTeamShort, c.Legs.First().HomeTeam)} vs {TeamName(c.Legs.First().AwayTeamShort, c.Legs.First().AwayTeam)}"
                    : $"Combiné {c.Legs.Select(l => l.MatchId).Distinct().Count()} matchs",
                BetType = "COMBO",
                Stake = c.Stake,
                Result = c.Result,
                Winnings = c.Winnings,
                Confidence = c.Confidence,
                Reasoning = c.Reasoning,
                Odds = c.CombinedOdds,
                CreatedAt = c.CreatedAt,
                IsCombo = true,
                Legs = c.Legs.Select(l => new ComboLegItem
                {
                    Id = l.Id,
                    Match = $"{TeamName(l.HomeTeamShort, l.HomeTeam)} vs {TeamName(l.AwayTeamShort, l.AwayTeam)}",
                    BetType = l.BetType,
                    Selection = l.Selection,
                    Odds = l.Odds,
                    Result = l.Result,
                    HomeScore = l.HomeScore,
                    AwayScore = l.AwayScore,
                    Minute = l.Minute,
                    MatchUtcDate = l.MatchUtcDate,
                    CompetitionCode = l.CompetitionCode,
                    HomeTeamCrest = l.HomeTeamCrest,
                    AwayTeamCrest = l.AwayTeamCrest
                }).ToList()
            })))
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => x.Item)
            .Where(item => req.ResultFilter == null || item.Result == req.ResultFilter)
            .Where(item => req.League == null
                || item.CompetitionCode == req.League
                || (item.Legs?.Any(l => l.CompetitionCode == req.League) ?? false))
            .ToList();

        var limit = Math.Clamp(req.Limit ?? DefaultRecentBetsLimit, 1, MaxRecentBets);
        var totalMatchingBets = recentBets.Count;
        recentBets = recentBets.Take(limit).ToList();

        await Send.OkAsync(new GetPortfolioResponse
        {
            TotalStaked = totalStaked,
            TotalWinnings = totalWinnings,
            NetProfit = netProfit,
            CurrentBalance = currentBalance,
            TotalBets = totalCount,
            WonBets = wonCount,
            LostBets = lostCount,
            PendingBets = pendingCount,
            WinRate = winRate,
            Roi = roi,
            AverageOdds = averageOdds,
            AverageStake = averageStake,
            CurrentStreakCount = streakCount,
            CurrentStreakResult = streakResult,
            TotalMatchingBets = totalMatchingBets,
            RecentBets = recentBets
        });
    }
}
