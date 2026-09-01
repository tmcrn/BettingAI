using BettingAI.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class GetPortfolioResponse
{
    public decimal TotalStaked { get; set; }
    public decimal TotalWinnings { get; set; }
    public decimal CurrentBalance { get; set; }
    public int TotalBets { get; set; }
    public int WonBets { get; set; }
    public int LostBets { get; set; }
    public int PendingBets { get; set; }
    public double WinRate { get; set; }
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
}

public class ComboLegItem
{
    public int Id { get; set; }
    public string? Match { get; set; }
    public string? BetType { get; set; }
    public string? Selection { get; set; }
    public decimal Odds { get; set; }
    public string? Result { get; set; }
}

public class GetPortfolioEndpoint : EndpointWithoutRequest<GetPortfolioResponse>
{
    private readonly BettingContext _context;

    public GetPortfolioEndpoint(BettingContext context)
    {
        _context = context;
    }

    public override void Configure()
    {
        Get("/api/portfolio");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
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

        var recentBets = bets
            .Select(b => (CreatedAt: b.CreatedAt, Item: new BetHistoryItem
            {
                Id = b.Id,
                MatchId = b.MatchId,
                Match = $"{b.HomeTeam} vs {b.AwayTeam}",
                BetType = b.BetType,
                Selection = b.Selection,
                Stake = b.Stake,
                Result = b.Result,
                Winnings = b.Winnings,
                Confidence = b.Confidence,
                Reasoning = b.Reasoning,
                Odds = b.Odds,
                CreatedAt = b.CreatedAt,
                IsCombo = false
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
                    ? $"Combiné - {c.Legs.First().HomeTeam} vs {c.Legs.First().AwayTeam}"
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
                    Match = $"{l.HomeTeam} vs {l.AwayTeam}",
                    BetType = l.BetType,
                    Selection = l.Selection,
                    Odds = l.Odds,
                    Result = l.Result
                }).ToList()
            })))
            .OrderByDescending(x => x.CreatedAt)
            .Take(10)
            .Select(x => x.Item)
            .ToList();

        await Send.OkAsync(new GetPortfolioResponse
        {
            TotalStaked = totalStaked,
            TotalWinnings = totalWinnings,
            CurrentBalance = currentBalance,
            TotalBets = totalCount,
            WonBets = wonCount,
            LostBets = lostCount,
            PendingBets = pendingCount,
            WinRate = winRate,
            RecentBets = recentBets
        });
    }
}
