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

        var totalStaked = bets.Sum(b => b.Stake);
        var totalWinnings = bets.Where(b => b.Result == "WIN").Sum(b => b.Winnings ?? 0);
        var currentBalance = 10 + totalWinnings - totalStaked;
        var wonBets = bets.Count(b => b.Result == "WIN");
        var lostBets = bets.Count(b => b.Result == "LOSS");
        var pendingBets = bets.Count(b => b.Result == "PENDING");
        var winRate = bets.Count > 0 ? (double)wonBets / bets.Count : 0;

        var recentBets = bets.OrderByDescending(b => b.CreatedAt)
            .Take(10)
            .Select(b => new BetHistoryItem
            {
                Id = b.Id,
                MatchId = b.MatchId,
                Match = $"{b.HomeTeam} vs {b.AwayTeam}",
                BetType = b.BetType,
                Stake = b.Stake,
                Result = b.Result,
                Winnings = b.Winnings
            })
            .ToList();

        await Send.OkAsync(new GetPortfolioResponse
        {
            TotalStaked = totalStaked,
            TotalWinnings = totalWinnings,
            CurrentBalance = currentBalance,
            TotalBets = bets.Count,
            WonBets = wonBets,
            LostBets = lostBets,
            PendingBets = pendingBets,
            WinRate = winRate,
            RecentBets = recentBets
        });
    }
}