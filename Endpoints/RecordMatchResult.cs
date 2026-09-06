using BettingAI.Data;
using BettingAI.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace BettingAI.Endpoints;

public class RecordMatchResultRequest
{
    public string? MatchId { get; set; }
    public string? Result { get; set; }  // "HOME_WIN", "AWAY_WIN", "DRAW"
    public int HomeScore { get; set; }
    public int AwayScore { get; set; }
}

public class RecordMatchResultResponse
{
    public int BetsUpdated { get; set; }
    public decimal TotalWinnings { get; set; }
}

public class RecordMatchResultEndpoint : Endpoint<RecordMatchResultRequest, RecordMatchResultResponse>
{
    private readonly BettingContext _context;
    private readonly BetSettlementService _settlementService;
    private readonly DiscordNotificationService _discord;

    public RecordMatchResultEndpoint(BettingContext context, BetSettlementService settlementService, DiscordNotificationService discord)
    {
        _context = context;
        _settlementService = settlementService;
        _discord = discord;
    }

    public override void Configure()
    {
        Post("/api/record-result");
        AllowAnonymous();
    }

    public override async Task HandleAsync(RecordMatchResultRequest req, CancellationToken ct)
    {
        if (!OwnerAuth.IsAuthorized(HttpContext))
        {
            HttpContext.Response.StatusCode = 403;
            return;
        }

        var bets = await _context.Bets
            .Where(b => b.MatchId == req.MatchId && b.Result == "PENDING")
            .ToListAsync(cancellationToken: ct);

        decimal totalWinnings = 0;

        foreach (var bet in bets)
        {
            var won = BetSettlementService.DetermineOutcome(bet.BetType, bet.Selection, req.Result ?? "", req.HomeScore, req.AwayScore);

            if (won)
            {
                bet.Result = "WIN";
                bet.Winnings = bet.Stake * (bet.Odds ?? BetSettlementService.EstimateOddsFromConfidence(bet.Confidence));
                totalWinnings += bet.Winnings.Value;
                await _discord.NotifyBetWonAsync(bet);
            }
            else
            {
                bet.Result = "LOSS";
                bet.Winnings = 0;
                await _discord.NotifyBetLostAsync(bet);
            }
        }

        await _context.SaveChangesAsync(ct);

        if (bets.Count > 0)
        {
            await _settlementService.RefreshLearningNotebookAsync(ct);
        }

        await Send.OkAsync(new RecordMatchResultResponse
        {
            BetsUpdated = bets.Count,
            TotalWinnings = totalWinnings
        });
    }
}