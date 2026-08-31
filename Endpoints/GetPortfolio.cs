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
    public decimal Stake { get; set; }
    public string? Result { get; set; }
    public decimal? Winnings { get; set; }
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
                Stake = b.Stake,
                Result = b.Result,
                Winnings = b.Winnings
            }))
            .Concat(combos.Select(c => (CreatedAt: c.CreatedAt, Item: new BetHistoryItem
            {
                Id = c.Id,
                MatchId = null,
                Match = $"Combiné {c.Legs.Count} matchs (" + string.Join(", ", c.Legs.Select(l => $"{l.HomeTeam} vs {l.AwayTeam}")) + ")",
                BetType = "COMBO",
                Stake = c.Stake,
                Result = c.Result,
                Winnings = c.Winnings
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
