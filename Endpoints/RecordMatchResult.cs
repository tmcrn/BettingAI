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

    public RecordMatchResultEndpoint(BettingContext context, BetSettlementService settlementService)
    {
        _context = context;
        _settlementService = settlementService;
    }

    public override void Configure()
    {
        Post("/api/record-result");
        AllowAnonymous();
    }

    public override async Task HandleAsync(RecordMatchResultRequest req, CancellationToken ct)
    {
        var bets = await _context.Bets
            .Where(b => b.MatchId == req.MatchId && b.Result == "PENDING")
            .ToListAsync(cancellationToken: ct);

        decimal totalWinnings = 0;

        foreach (var bet in bets)
        {
            var won = BetSettlementService.DetermineOutcome(bet.BetType, req.Result ?? "", req.HomeScore, req.AwayScore);

            if (won)
            {
                bet.Result = "WIN";
                bet.Winnings = bet.Stake * 2;  // Cotes simples : x2
                totalWinnings += bet.Winnings.Value;
            }
            else
            {
                bet.Result = "LOSS";
                bet.Winnings = 0;
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